using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Media;
using FastCraft3D.Geometry;
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

    public SceneRenderer(GroupModel3D root, Scene scene)
    {
        this.root = root;
        this.scene = scene;

        scene.Objects.CollectionChanged += OnCollectionChanged;
        foreach (var o in scene.Objects) Attach(o);
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
            Transform = MeshConverter.ToTransform(o.Transform),
            Material = MaterialFor(o)
        };

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
                visual.Material = MaterialFor(o);
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
    private static PhongMaterial MaterialFor(SceneObject o)
    {
        var colour = new SharpDX.Color4(o.Colour.X, o.Colour.Y, o.Colour.Z, 1f);

        float lift = o.IsSelected ? 0.22f : 0f;
        var diffuse = new SharpDX.Color4(
            Math.Min(colour.Red + lift, 1f),
            Math.Min(colour.Green + lift, 1f),
            Math.Min(colour.Blue + lift, 1f),
            1f);

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
        scene.Objects.CollectionChanged -= OnCollectionChanged;
        foreach (var o in visuals.Keys.ToList()) Detach(o);
    }
}
