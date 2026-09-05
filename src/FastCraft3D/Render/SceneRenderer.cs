using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Media;
using FastCraft3D.Geometry;
using FastCraft3D.Geometry.Engraving;
using FastCraft3D.Model;
using HelixToolkit.SharpDX.Core;
using HelixToolkit.Wpf.SharpDX;

namespace FastCraft3D.Render;

/// <summary>
/// Keeps the Direct3D scene graph in step with the editable scene.
///
/// Syncing imperatively rather than through an ItemsSource binding is deliberate: geometry
/// uploads are expensive, and this way a transform change moves an existing model instead of
/// rebuilding and re-uploading its vertex buffer.
/// </summary>
public sealed class SceneRenderer : IDisposable
{
    private static readonly Color OutlineColour = Colors.White;

    /// <summary>
    /// Above this, tracing the outline would cost more than it is worth on a selection click.
    /// Dense imports fall back to the brightened material alone.
    /// </summary>
    private const int OutlineTriangleLimit = 200_000;

    private readonly GroupModel3D root;
    private readonly Scene scene;
    private readonly Dictionary<SceneObject, MeshGeometryModel3D> visuals = new();
    private readonly Dictionary<SceneObject, LineGeometryModel3D> outlines = new();

    /// <summary>The face waiting to be engraved, drawn over the surface while it is picked.</summary>
    private bool wireframe;
    private bool xray;

    private MeshGeometryModel3D? faceHighlight;
    private LineGeometryModel3D? faceOutline;
    private MeshGeometryModel3D? facePreview;

    public SceneRenderer(GroupModel3D root, Scene scene)
    {
        this.root = root;
        this.scene = scene;

        scene.Objects.CollectionChanged += OnCollectionChanged;
        foreach (var o in scene.Objects) Attach(o);
    }

    /// <summary>
    /// Marks the face that is about to be engraved, or clears it when given null.
    ///
    /// Drawn as a tinted skin over the face rather than as a change to the object's own
    /// material: the point is to show which of several flat surfaces was picked, and repainting
    /// the whole object would show nothing at all. The patch is already in world space, so the
    /// highlight carries no transform.
    /// </summary>
    public void ShowFace(FacePatch? face, GrooveSet? preview = null, Mesh? overlay = null)
    {
        if (facePreview is not null)
        {
            root.Children.Remove(facePreview);
            facePreview.Dispose();
            facePreview = null;
        }

        if (faceHighlight is not null)
        {
            root.Children.Remove(faceHighlight);
            faceHighlight.Dispose();
            faceHighlight = null;
        }

        if (faceOutline is not null)
        {
            root.Children.Remove(faceOutline);
            faceOutline.Dispose();
            faceOutline = null;
        }

        if (face is null || face.Triangles.Count == 0) return;

        // Lifted clear of the surface it covers, or the two fight over every pixel.
        var lift = face.Normal * 0.05f;
        var skin = new Mesh();

        foreach (int t in face.Triangles)
        {
            skin.AddTriangle(
                face.Mesh.Positions[face.Mesh.Indices[t]] + lift,
                face.Mesh.Positions[face.Mesh.Indices[t + 1]] + lift,
                face.Mesh.Positions[face.Mesh.Indices[t + 2]] + lift);
        }

        faceHighlight = new MeshGeometryModel3D
        {
            Geometry = MeshConverter.ToGeometry(skin),
            Material = new PhongMaterial
            {
                DiffuseColor = new SharpDX.Color4(0.20f, 0.62f, 1f, 0.42f),
                AmbientColor = new SharpDX.Color4(0.10f, 0.30f, 0.50f, 1f),
                SpecularColor = new SharpDX.Color4(0, 0, 0, 1)
            },
            IsTransparent = true,
            IsHitTestVisible = false // clicking again must pick the face underneath, not this
        };
        root.Children.Add(faceHighlight);

        var builder = new LineBuilder();
        foreach (var (a, b) in face.Boundary)
        {
            var from = face.Mesh.Positions[a] + lift;
            var to = face.Mesh.Positions[b] + lift;
            builder.AddLine(
                new SharpDX.Vector3(from.X, from.Y, from.Z),
                new SharpDX.Vector3(to.X, to.Y, to.Z));
        }

        faceOutline = new LineGeometryModel3D
        {
            Geometry = builder.ToLineGeometry3D(),
            Color = Color.FromRgb(0x2E, 0x9B, 0xFF),
            Thickness = 2.2,
            IsHitTestVisible = false
        };
        root.Children.Add(faceOutline);

        ShowPreview(face, preview, overlay);
    }

    /// <summary>
    /// Lays the pattern over the highlighted face so the settings can be judged before anything
    /// is cut. The shapes come from the same code that builds the cutter, so this is not an
    /// impression of the result - it is the result, drawn flat.
    /// </summary>
    private void ShowPreview(FacePatch face, GrooveSet? preview, Mesh? overlay)
    {
        // Either a pattern to lay out, or a shape already built - lettering arrives as the
        // second, since its outlines are nothing like the rectangles a pattern is made of.
        var pattern = overlay ?? (preview is null || preview.IsEmpty
            ? new Mesh()
            : GrooveSolid.Surface(preview, face, 0.09f));

        if (pattern.TriangleCount == 0) return;

        facePreview = new MeshGeometryModel3D
        {
            Geometry = MeshConverter.ToGeometry(pattern),
            Material = new PhongMaterial
            {
                DiffuseColor = new SharpDX.Color4(0.05f, 0.13f, 0.24f, 0.92f),
                AmbientColor = new SharpDX.Color4(0.05f, 0.10f, 0.18f, 1f),
                SpecularColor = new SharpDX.Color4(0, 0, 0, 1)
            },
            // The strips are drawn on their own with no underside, so both faces have to show.
            CullMode = SharpDX.Direct3D11.CullMode.None,
            IsHitTestVisible = false
        };
        root.Children.Add(facePreview);
    }

    /// <summary>
    /// Draws every object's triangle edges over it. Useful for seeing how dense an import is,
    /// and for spotting where a boolean has left a mess.
    /// </summary>
    public bool Wireframe
    {
        get => wireframe;
        set
        {
            if (wireframe == value) return;
            wireframe = value;

            foreach (var (o, visual) in visuals) ApplyLook(o, visual);
        }
    }

    /// <summary>
    /// Fades everything that is not selected, so a part buried inside another can be seen and
    /// worked on. Only the unselected fade: the point is to look past them at what is selected.
    /// </summary>
    public bool Xray
    {
        get => xray;
        set
        {
            if (xray == value) return;
            xray = value;

            foreach (var (o, visual) in visuals) ApplyLook(o, visual);
        }
    }

    /// <summary>
    /// Puts the current view settings on one object's model. Everything that decides how an
    /// object looks goes through here, so a new object and a toggled setting cannot disagree.
    /// </summary>
    private void ApplyLook(SceneObject o, MeshGeometryModel3D visual)
    {
        visual.Material = MaterialFor(o);

        // The flag has to be set as well as the alpha: it is what puts the model through the
        // order-independent transparency pass rather than straight into the depth buffer.
        visual.IsTransparent = xray && !o.IsSelected;

        visual.RenderWireframe = wireframe;
        visual.WireframeColor = Color.FromArgb(0x99, 0x1E, 0x26, 0x30);
    }

    /// <summary>Maps a hit-tested model back to the object it represents.</summary>
    public SceneObject? Resolve(object? model)
    {
        foreach (var (sceneObject, visual) in visuals)
            if (ReferenceEquals(visual, model))
                return sceneObject;
        return null;
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
            foreach (SceneObject o in e.OldItems) Detach(o);

        if (e.NewItems is not null)
            foreach (SceneObject o in e.NewItems) Attach(o);

        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            foreach (var o in visuals.Keys.ToList()) Detach(o);
            foreach (var o in scene.Objects) Attach(o);
        }
    }

    private void Attach(SceneObject o)
    {
        if (visuals.ContainsKey(o)) return;

        var visual = new MeshGeometryModel3D
        {
            Geometry = MeshConverter.ToGeometry(o.Mesh),
            Transform = MeshConverter.ToTransform(o.Transform)
        };
        ApplyLook(o, visual);

        visuals[o] = visual;
        root.Children.Add(visual);
        o.PropertyChanged += OnObjectChanged;

        UpdateOutline(o);
    }

    private void Detach(SceneObject o)
    {
        o.PropertyChanged -= OnObjectChanged;
        RemoveOutline(o);

        if (!visuals.Remove(o, out var visual)) return;
        root.Children.Remove(visual);
        visual.Dispose();
    }

    private void OnObjectChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not SceneObject o || !visuals.TryGetValue(o, out var visual)) return;

        switch (e.PropertyName)
        {
            case nameof(SceneObject.Mesh):
                visual.Geometry = MeshConverter.ToGeometry(o.Mesh);
                RemoveOutline(o); // the old edges describe geometry that no longer exists
                UpdateOutline(o);
                break;

            case nameof(SceneObject.Transform):
                visual.Transform = MeshConverter.ToTransform(o.Transform);
                if (outlines.TryGetValue(o, out var line))
                    line.Transform = visual.Transform;
                break;

            case nameof(SceneObject.IsSelected):
            case nameof(SceneObject.Colour):
                ApplyLook(o, visual);
                UpdateOutline(o);
                break;
        }
    }

    /// <summary>
    /// Draws a white line along the shape's own edges while it is selected.
    ///
    /// The object keeps its own colour rather than being repainted: the palette already contains
    /// oranges and blues, so a colour swap alone left it genuinely unclear which object was
    /// selected. An outline traces the thing itself and cannot be confused with a paint job.
    /// </summary>
    private void UpdateOutline(SceneObject o)
    {
        if (!o.IsSelected)
        {
            RemoveOutline(o);
            return;
        }

        if (outlines.ContainsKey(o)) return;
        if (o.Mesh.TriangleCount > OutlineTriangleLimit) return;

        var edges = FeatureEdges.Build(o.Mesh);
        if (edges.Count == 0) return;

        var builder = new LineBuilder();
        foreach (var (a, b) in edges)
        {
            builder.AddLine(
                new SharpDX.Vector3(a.X, a.Y, a.Z),
                new SharpDX.Vector3(b.X, b.Y, b.Z));
        }

        var outline = new LineGeometryModel3D
        {
            Geometry = builder.ToLineGeometry3D(),
            Color = OutlineColour,
            Thickness = 1.4,
            Transform = MeshConverter.ToTransform(o.Transform),
            IsHitTestVisible = false // picking must still hit the solid underneath
        };

        outlines[o] = outline;
        root.Children.Add(outline);
    }

    private void RemoveOutline(SceneObject o)
    {
        if (!outlines.Remove(o, out var outline)) return;
        root.Children.Remove(outline);
        outline.Dispose();
    }

    /// <summary>
    /// Selected objects keep their colour and are lifted a little, so they read as picked even
    /// where the outline is edge-on to the camera.
    /// </summary>
    private PhongMaterial MaterialFor(SceneObject o)
    {
        var colour = new SharpDX.Color4(o.Colour.X, o.Colour.Y, o.Colour.Z, 1f);

        float lift = o.IsSelected ? 0.22f : 0f;
        var diffuse = new SharpDX.Color4(
            Math.Min(colour.Red + lift, 1f),
            Math.Min(colour.Green + lift, 1f),
            Math.Min(colour.Blue + lift, 1f),
            1f);

        // In x-ray the unselected go translucent. Transparency is ordered by the renderer's
        // own OIT pass, so parts behind parts still read correctly.
        if (xray && !o.IsSelected) diffuse.Alpha = 0.28f;

        return new PhongMaterial
        {
            DiffuseColor = diffuse,
            SpecularColor = new SharpDX.Color4(0.25f, 0.25f, 0.25f, 1f),
            SpecularShininess = 24f,
            AmbientColor = new SharpDX.Color4(diffuse.Red * 0.35f, diffuse.Green * 0.35f, diffuse.Blue * 0.35f, 1f)
        };
    }

    public void Dispose()
    {
        ShowFace(null);
        scene.Objects.CollectionChanged -= OnCollectionChanged;
        foreach (var o in visuals.Keys.ToList()) Detach(o);
    }
}
