using System.Collections.Specialized;
using System.ComponentModel;
using System.Numerics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
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
/// <summary>
/// What becomes of the half a split would throw away, while the plane is still being placed.
/// </summary>
public enum SplitOffcut
{
    /// <summary>Nothing is drawn differently. The plane alone says where the cut will fall.</summary>
    Shown,

    /// <summary>The half that goes is drawn through, so both what stays and what goes can be seen.</summary>
    Faded,

    /// <summary>Only what will be left is drawn, which is the closest thing to the finished part.</summary>
    Hidden
}

public sealed class SceneRenderer : IDisposable
{
    private static readonly Color OutlineColour = Colors.White;

    /// <summary>Matches the repair banner's border, so the same red means the same thing in both places.</summary>
    private static readonly Color DamagedOutlineColour = Color.FromRgb(0xD6, 0x45, 0x45);

    /// <summary>
    /// Above this, tracing the outline would cost more than it is worth on a selection click.
    /// Dense imports fall back to the brightened material alone.
    /// </summary>
    private const int OutlineTriangleLimit = 200_000;

    private readonly GroupModel3D root;
    private readonly Scene scene;
    private readonly Dictionary<SceneObject, MeshGeometryModel3D> visuals = new();
    private readonly Dictionary<SceneObject, LineGeometryModel3D> outlines = new();

    /// <summary>Each object's own shape, reflected through the plate, while Reflections is on.</summary>
    private readonly Dictionary<SceneObject, MeshGeometryModel3D> mirrors = new();
    private bool showReflections;

    /// <summary>Flips a world point through the plate at Z = 0, which is what a floor mirror does.</summary>
    private static readonly Matrix4x4 MirrorThroughPlate = Matrix4x4.CreateScale(1f, 1f, -1f);

    /// <summary>The faces that overhang, drawn over each object while the overhang view is on.</summary>
    private readonly Dictionary<SceneObject, MeshGeometryModel3D> overhangs = new();
    private readonly HashSet<SceneObject> overhangsStale = new();
    private readonly Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
    private bool overhangsQueued;
    private bool showOverhangs;
    private float overhangAngle = Overhangs.DefaultAngle;
    private Vector3 overhangColour = Palette.WarningSwatches[0].Colour;

    /// <summary>Stand-ins for the objects a split is being set up on: what stays, then what goes.</summary>
    private readonly Dictionary<SceneObject, List<MeshGeometryModel3D>> splitParts = new();

    /// <summary>The cut being previewed, kept so a moved or edited object can be redone.</summary>
    private (Vector3 Normal, float Offset, bool Ghost, bool Fill)? split;

    /// <summary>What a tool has in hand, while everything else stands aside. See Focus.</summary>
    private IReadOnlyList<SceneObject>? focus;

    private bool showOutlines = true;

    /// <summary>The face waiting to be engraved, drawn over the surface while it is picked.</summary>
    private bool wireframe;
    private bool xray;

    private MeshGeometryModel3D? faceHighlight;
    private LineGeometryModel3D? faceOutline;
    private MeshGeometryModel3D? facePreview;
    private MeshGeometryModel3D? connectorMarks;
    private MeshGeometryModel3D? restingShown;
    private MeshGeometryModel3D? restingHovered;

    /// <summary>Asks the viewport for a frame when the scene changes. See ViewportRepaint.</summary>
    private readonly ViewportRepaint? repaint;

    public SceneRenderer(GroupModel3D root, Scene scene, ViewportRepaint? repaint = null)
    {
        this.root = root;
        this.scene = scene;
        this.repaint = repaint;

        scene.Objects.CollectionChanged += OnCollectionChanged;
        foreach (var o in scene.Objects) Attach(o);
    }

    /// <summary>Asks for the scene to be drawn again. See ViewportRepaint for why it has to.</summary>
    private void Invalidate() => repaint?.Ask();

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
        Invalidate();

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

    private GroupModel3D? voronoiWeb;

    /// <summary>
    /// The web a Voronoi cut would leave, drawn on the model as lines while the numbers are being
    /// chosen.
    ///
    /// Lines rather than a solid, because a solid preview cannot be honest: it would have to be
    /// built on a coarser grid than the real thing, and a coarse grid cannot hold a strut of the
    /// width somebody is choosing - the preview would refuse to show the very thing being set.
    /// This is the exact edge the finished web will have, drawn straight onto the surface.
    /// </summary>
    public void ShowVoronoi(IReadOnlyList<(Vector3 From, Vector3 To)> lines)
    {
        Invalidate();

        if (voronoiWeb is not null)
        {
            root.Children.Remove(voronoiWeb);
            voronoiWeb.Dispose();
            voronoiWeb = null;
        }

        if (lines.Count == 0) return;

        var drawn = new LineBuilder();
        foreach (var (from, to) in lines)
            drawn.AddLine(new SharpDX.Vector3(from.X, from.Y, from.Z), new SharpDX.Vector3(to.X, to.Y, to.Z));

        var group = new GroupModel3D();
        group.Children.Add(new LineGeometryModel3D
        {
            Geometry = drawn.ToLineGeometry3D(),
            Color = Color.FromRgb(0xE8, 0x76, 0x2C),
            Thickness = 1.8,
            IsHitTestVisible = false
        });

        root.Children.Add(group);
        voronoiWeb = group;
    }

    private MeshGeometryModel3D? pivotMark;

    /// <summary>
    /// The pivot: a small bright sphere sitting on the model.
    ///
    /// While one is being picked it follows the pointer, so what would be taken is visible before
    /// the click rather than after it. Setting a pivot moves nothing on the plate - that is the
    /// whole point of it - so without this the tool looked as though it had done nothing at all.
    ///
    /// Drawn without shading and over everything, because a marker half inside the model it marks
    /// is no marker: this one sits on a shaft hole, which is the inside of a part by definition.
    /// </summary>
    public void ShowPivot(Vector3? at, float radius)
    {
        Invalidate();

        if (pivotMark is not null)
        {
            root.Children.Remove(pivotMark);
            pivotMark.Dispose();
            pivotMark = null;
        }

        if (at is not { } point) return;

        var ball = MeshTransform.Transformed(
            Primitives.Sphere(MathF.Max(radius, 0.05f), 16, 10), Matrix4x4.CreateTranslation(point));

        pivotMark = new MeshGeometryModel3D
        {
            Geometry = MeshConverter.ToGeometry(ball),
            Material = new PhongMaterial
            {
                DiffuseColor = new SharpDX.Color4(0.1f, 0.02f, 0.02f, 1f),
                EmissiveColor = new SharpDX.Color4(1f, 0.13f, 0.13f, 1f),
                AmbientColor = new SharpDX.Color4(0.3f, 0.04f, 0.04f, 1f),
                SpecularColor = new SharpDX.Color4(0.4f, 0.4f, 0.4f, 1f)
            },
            DepthBias = -2000,
            IsHitTestVisible = false
        };

        root.Children.Add(pivotMark);
    }

    /// <summary>
    /// Marks where the connectors will land on the cut, while the numbers are being set: a disc
    /// the width of each one, lying in the plane. Without them the panel asked for a diameter and
    /// a count with nothing on the model to say where any of it was going.
    /// </summary>
    public void ShowConnectorMarks(IReadOnlyList<Connectors.Mark> marks, Vector3 normal)
    {
        Invalidate();

        if (connectorMarks is not null)
        {
            root.Children.Remove(connectorMarks);
            connectorMarks.Dispose();
            connectorMarks = null;
        }

        if (marks.Count == 0 || normal.LengthSquared() < 1e-6f) return;

        var up = Vector3.Normalize(normal);
        var lying = MeshTransform.RotationBetween(Vector3.UnitZ, up);
        var discs = Mesh.Combine(marks.Select(mark => mark.Side.LengthSquared() > 1e-8f
            ? MeshTransform.Transformed(Tile(mark.Radius), Facing(mark.Side) * Matrix4x4.CreateTranslation(mark.At))
            : MeshTransform.Transformed(Primitives.Prism(mark.Radius, 0.5f, 28), lying * Matrix4x4.CreateTranslation(mark.At))));

        // A square peg marked as a square, turned as it will be cut.
        static Mesh Tile(float half) => MeshTransform.Transformed(
            Primitives.Prism(half * MathF.Sqrt(2f), 0.5f, 4), Matrix4x4.CreateRotationZ(MathF.PI / 4f));

        Matrix4x4 Facing(Vector3 side)
        {
            side = Vector3.Normalize(side - up * Vector3.Dot(side, up));
            var other = Vector3.Cross(up, side);
            return new Matrix4x4(
                side.X, side.Y, side.Z, 0f,
                other.X, other.Y, other.Z, 0f,
                up.X, up.Y, up.Z, 0f,
                0f, 0f, 0f, 1f);
        }

        if (discs.TriangleCount == 0) return;

        connectorMarks = new MeshGeometryModel3D
        {
            Geometry = MeshConverter.ToGeometry(discs),
            Material = new PhongMaterial
            {
                DiffuseColor = new SharpDX.Color4(0.18f, 0.60f, 1f, 0.75f),
                AmbientColor = new SharpDX.Color4(0.08f, 0.26f, 0.45f, 1f),
                SpecularColor = new SharpDX.Color4(0, 0, 0, 1)
            },
            IsTransparent = true,
            IsHitTestVisible = false
        };
        root.Children.Add(connectorMarks);
    }

    /// <summary>
    /// Marks the faces an object can be laid on, pale, with the one under the pointer in the
    /// highlight blue.
    ///
    /// Each is drawn shrunk towards its middle and a hair off the surface. Shrunk, two faces meeting
    /// at a shallow angle read as two patches rather than one sheet; lifted, a face does not fight
    /// the model's own for the same pixels.
    /// </summary>
    public void ShowRestingFaces(IReadOnlyList<RestingFace> faces, int hovered)
    {
        Invalidate();

        foreach (var shown in new[] { restingShown, restingHovered })
        {
            if (shown is null) continue;
            root.Children.Remove(shown);
            shown.Dispose();
        }
        restingShown = restingHovered = null;

        if (faces.Count == 0) return;

        var pale = new Mesh();
        var lit = new Mesh();
        for (int i = 0; i < faces.Count; i++)
        {
            var face = faces[i];
            var into = i == hovered ? lit : pale;
            var lift = face.Normal * 0.08f;
            for (int t = 0; t + 2 < face.Triangles.Count; t += 3)
                into.AddTriangle(Drawn(face.Triangles[t]), Drawn(face.Triangles[t + 1]), Drawn(face.Triangles[t + 2]));

            Vector3 Drawn(Vector3 corner) => face.Centre + (corner - face.Centre) * RestingFaces.DrawnShare + lift;
        }

        restingShown = Patches(pale, new SharpDX.Color4(1f, 1f, 1f, 0.5f));
        restingHovered = Patches(lit, new SharpDX.Color4(0.20f, 0.62f, 1f, 0.75f));

        MeshGeometryModel3D? Patches(Mesh mesh, SharpDX.Color4 colour)
        {
            if (mesh.TriangleCount == 0) return null;

            var model = new MeshGeometryModel3D
            {
                Geometry = MeshConverter.ToGeometry(mesh),
                Material = new PhongMaterial
                {
                    DiffuseColor = colour,
                    AmbientColor = new SharpDX.Color4(colour.Red * 0.5f, colour.Green * 0.5f, colour.Blue * 0.5f, 1f),
                    SpecularColor = new SharpDX.Color4(0, 0, 0, 1)
                },
                IsTransparent = true,
                CullMode = SharpDX.Direct3D11.CullMode.None,
                IsHitTestVisible = false
            };
            root.Children.Add(model);
            return model;
        }
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
    /// Draws the selection as the split would leave it: what stays solid, and what goes either
    /// faded or not at all.
    ///
    /// The triangles are cut here rather than by the boolean the split itself uses. That one
    /// takes seconds on a dense model, which is no use to a plane being dragged; this is one
    /// pass and leaves the cut open, which is all a preview needs.
    ///
    /// The card was asked to do it first, with clip planes, and it filled the cut face for free.
    /// It also cleared the stencil buffer once per model and rebound the render target, which
    /// took the transparent plane marker with it and left the whole thing looking broken.
    /// </summary>
    public void ShowSplit(IReadOnlyList<SceneObject> targets, Vector3 normal, float offset,
                          SplitKeep keep, SplitOffcut offcut, bool fill)
    {
        Invalidate();

        // Keeping both halves throws nothing away, so there is nothing to fade or hide.
        bool showing = offcut is not SplitOffcut.Shown
                       && keep is SplitKeep.Front or SplitKeep.Back
                       && targets.Count > 0;

        if (!showing)
        {
            ClearSplit();
            return;
        }

        // Held facing the half that survives, so everything below reads the same way round.
        split = keep is SplitKeep.Front
            ? (normal, offset, offcut is SplitOffcut.Faded, fill)
            : (-normal, -offset, offcut is SplitOffcut.Faded, fill);

        foreach (var stale in splitParts.Keys.Where(o => !targets.Contains(o)).ToList())
            Release(stale);

        foreach (var o in targets) Rebuild(o);
    }

    /// <summary>
    /// What a tool has in hand. Everything else stands aside until this is given null again.
    ///
    /// Two tools wanted the same thing for different reasons and each had grown its own way of
    /// asking, so this is the one of them. A split plane is aimed by eye, and on a scene of any
    /// depth the thing being cut is behind something else; a lithophane is judged by its picture,
    /// and whatever else is on the plate is read as part of it.
    ///
    /// What stands aside is not drawn - unless x-ray is on, which is the one setting that asks
    /// to see past the work rather than to be rid of what is behind it, so there it is drawn
    /// through instead. Either way it stops taking clicks, so a face picked for a plane lands on
    /// the part being cut and not on whatever happens to be in front of it.
    ///
    /// Only the drawing is affected: nothing is hidden in the scene itself, so the object list
    /// and the undo history are left alone.
    /// </summary>
    public void Focus(IReadOnlyList<SceneObject>? working)
    {
        // Kept as a copy. What it is usually given is the live selection, and holding that would
        // leave it comparing a list against itself and never noticing a change. Asked for on
        // every sketch stroke and every view setting, so it is worth not repeating the work.
        var next = working?.ToArray();
        if (Same(focus, next)) return;

        Invalidate();

        focus = next;

        foreach (var (o, visual) in visuals)
        {
            ShowOrHide(o, visual);
            ApplyLook(o, visual);
        }
    }

    private static bool Same(IReadOnlyList<SceneObject>? a, IReadOnlyList<SceneObject>? b) =>
        a is null ? b is null : b is not null && a.SequenceEqual(b);

    /// <summary>Whether a tool has something else in hand and this is not it.</summary>
    private bool StandsAside(SceneObject o) => focus is not null && !focus.Contains(o);

    /// <summary>
    /// Whether the object is drawn through rather than solid: in x-ray, everything that is not
    /// selected, and everything a tool has stood aside even when it is.
    /// </summary>
    private bool Faded(SceneObject o) => xray && (!o.IsSelected || StandsAside(o));

    /// <summary>
    /// Whether a selected object is traced in white.
    ///
    /// On a dense mesh every feature edge is an edge, so the outline covers the surface rather
    /// than bounding it - a lithophane comes back as a white haze of its own picture. Worth
    /// turning off there, which is what the View tab's Outline does.
    /// </summary>
    public bool ShowOutlines
    {
        get => showOutlines;
        set
        {
            if (showOutlines == value) return;

            Invalidate();
            showOutlines = value;

            foreach (var o in visuals.Keys.ToList()) UpdateOutline(o);
        }
    }

    /// <summary>
    /// Whether the plate reflects what stands on it, the way a glossy print bed does.
    ///
    /// Built as a second copy of each object's model, reflected through the plate and given a
    /// dimmer, part-see-through version of its material.
    /// </summary>
    public bool ShowReflections
    {
        get => showReflections;
        set
        {
            if (showReflections == value) return;

            Invalidate();
            showReflections = value;

            foreach (var o in visuals.Keys.ToList()) UpdateMirror(o);
        }
    }

    /// <summary>Adds, moves or removes one object's reflection, to match what it is doing now.</summary>
    private void UpdateMirror(SceneObject o)
    {
        if (!visuals.TryGetValue(o, out var visual)) return;

        if (!showReflections || visual.Visibility != Visibility.Visible)
        {
            RemoveMirror(o);
            return;
        }

        if (!mirrors.TryGetValue(o, out var mirror))
        {
            mirror = new MeshGeometryModel3D
            {
                // Flipping through the plate flips the winding too, and re-deriving it is not
                // worth it for a reflection that is never picked or measured against.
                CullMode = SharpDX.Direct3D11.CullMode.None,
                IsTransparent = true,
                IsHitTestVisible = false,

                // A hair further from the camera than the plate itself, the same way the plate
                // is held off the objects standing on it - otherwise the two fight for pixels
                // wherever a reflection meets the board it is reflected in.
                DepthBias = 10
            };
            mirrors[o] = mirror;
            root.Children.Add(mirror);
        }

        // Its own buffer rather than the visual's, so disposing one can never leave the other
        // holding a GPU resource that has already gone.
        mirror.Geometry = MeshConverter.ToGeometry(o.Mesh);
        mirror.Transform = MeshConverter.ToTransform(o.Transform * MirrorThroughPlate);
        mirror.Material = MirrorMaterial(o);
    }

    private void RemoveMirror(SceneObject o)
    {
        if (!mirrors.Remove(o, out var mirror)) return;
        root.Children.Remove(mirror);
        mirror.Dispose();
    }

    /// <summary>A dimmer, part-see-through version of the object's own colour.</summary>
    private static PhongMaterial MirrorMaterial(SceneObject o) => new()
    {
        DiffuseColor = new SharpDX.Color4(o.Colour.X, o.Colour.Y, o.Colour.Z, 0.22f),
        SpecularColor = new SharpDX.Color4(0, 0, 0, 1),
        AmbientColor = new SharpDX.Color4(o.Colour.X * 0.3f, o.Colour.Y * 0.3f, o.Colour.Z * 0.3f, 1f)
    };

    /// <summary>
    /// Whether an object is drawn at all. Three things take it off the plate: the user hiding
    /// it, a split standing its halves in for it, and a tool with something else in hand.
    ///
    /// The last of those is the one x-ray excuses: it is drawn through rather than taken away,
    /// since x-ray is a request to see past the work and not to be rid of what is behind it.
    /// Being hidden by hand is not excused - that was asked for outright.
    ///
    /// Standing aside takes the clicks with it either way, drawn or not. Otherwise a face picked
    /// for a split plane would land on the ghost of whatever is in front of the part being cut.
    /// </summary>
    private void ShowOrHide(SceneObject o, MeshGeometryModel3D visual)
    {
        bool aside = StandsAside(o);
        bool draw = !o.IsHidden
                    && !splitParts.ContainsKey(o)
                    && (!aside || xray);

        visual.Visibility = draw ? Visibility.Visible : Visibility.Collapsed;
        visual.IsHitTestVisible = !aside;

        if (overhangs.TryGetValue(o, out var shown)) shown.Visibility = visual.Visibility;

        // The outline is a model of its own rather than part of the solid, so left alone it
        // would hang in the air round an object that is no longer drawn.
        if (outlines.TryGetValue(o, out var line)) line.Visibility = visual.Visibility;

        // Likewise the reflection: nothing stood aside or hidden belongs on the plate either way up.
        UpdateMirror(o);
    }

    /// <summary>
    /// Shows the faces that would need support in red, over everything on the plate, or takes them
    /// away.
    ///
    /// Worked out in the background, a moment after whatever changed: a drag moves the object many
    /// times a second, and turning a dense scan into world space for every one of them would make
    /// the drag itself stutter. Several changes waiting are done once.
    /// </summary>
    public void ShowOverhangs(bool on, float angleDegrees, Vector3 colour)
    {
        Invalidate();

        showOverhangs = on;
        overhangAngle = angleDegrees;
        overhangColour = colour;

        foreach (var o in visuals.Keys) MarkOverhangs(o);
        if (!on) foreach (var o in overhangs.Keys.ToList()) RemoveOverhangs(o);
    }

    private void MarkOverhangs(SceneObject o)
    {
        overhangsStale.Add(o);
        if (overhangsQueued) return;

        overhangsQueued = true;
        dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(UpdateOverhangs));
    }

    private void UpdateOverhangs()
    {
        Invalidate();

        overhangsQueued = false;
        var stale = overhangsStale.ToList();
        overhangsStale.Clear();

        foreach (var o in stale)
        {
            RemoveOverhangs(o);
            if (!showOverhangs || !visuals.TryGetValue(o, out var visual)) continue;

            var faces = Overhangs.Faces(o.ToWorldMesh(), overhangAngle);
            if (faces.TriangleCount == 0) continue;

            var shown = new MeshGeometryModel3D
            {
                Geometry = MeshConverter.ToGeometry(faces),

                // An overhanging face is, by definition, tilted past the threshold - which more
                // often than not means it faces away from the light rather than towards it. Lit
                // the ordinary way, on diffuse and ambient alone, the colour picked for it read
                // as a dark, muddy version of itself on exactly the faces it is meant to flag.
                // Mostly emissive instead, so it reads as the colour chosen wherever it is on
                // the model; diffuse and ambient only add a little shading on top of that.
                Material = new PhongMaterial
                {
                    EmissiveColor = new SharpDX.Color4(overhangColour.X * 0.75f, overhangColour.Y * 0.75f, overhangColour.Z * 0.75f, 1f),
                    DiffuseColor = new SharpDX.Color4(overhangColour.X * 0.35f, overhangColour.Y * 0.35f, overhangColour.Z * 0.35f, 1f),
                    AmbientColor = new SharpDX.Color4(overhangColour.X * 0.2f, overhangColour.Y * 0.2f, overhangColour.Z * 0.2f, 1f),
                    SpecularColor = new SharpDX.Color4(0, 0, 0, 1)
                },
                CullMode = SharpDX.Direct3D11.CullMode.Back,
                DepthBias = -8,
                IsHitTestVisible = false,
                Visibility = visual.Visibility
            };

            overhangs[o] = shown;
            root.Children.Add(shown);
        }
    }

    private void RemoveOverhangs(SceneObject o)
    {
        if (!overhangs.Remove(o, out var shown)) return;
        root.Children.Remove(shown);
        shown.Dispose();
    }

    private GroupModel3D? sketchShown;

    /// <summary>
    /// Draws a sketch on the plate: the finished outlines in blue, what is being drawn in orange up
    /// to the pointer, and a dot at each point placed and at the pointer. Lifted a hair off the
    /// plate so the board does not cover it. Given no sketch, takes it away.
    /// </summary>
    public void ShowSketch(Geometry.Sketches.Sketch? sketch, Vector2? cursor, Geometry.Sketches.SketchTool tool)
    {
        Invalidate();

        if (sketchShown is not null)
        {
            root.Children.Remove(sketchShown);
            sketchShown.Dispose();
            sketchShown = null;
        }

        if (sketch is null) return;

        const float lift = 0.06f;
        static SharpDX.Vector3 On(Vector2 p) => new(p.X, p.Y, lift);

        var group = new GroupModel3D();

        if (sketch.Loops.Count > 0)
        {
            var done = new LineBuilder();
            foreach (var loop in sketch.Loops)
                for (int i = 0; i < loop.Count; i++)
                    done.AddLine(On(loop[i]), On(loop[(i + 1) % loop.Count]));

            group.Children.Add(new LineGeometryModel3D
            {
                Geometry = done.ToLineGeometry3D(),
                Color = Color.FromRgb(0x1F, 0x6F, 0xD1),
                Thickness = 2.2,
                IsHitTestVisible = false
            });
        }

        var pending = sketch.Pending(cursor, tool);
        if (pending.Count > 0)
        {
            var lines = new LineBuilder();
            foreach (var (a, b) in pending) lines.AddLine(On(a), On(b));

            group.Children.Add(new LineGeometryModel3D
            {
                Geometry = lines.ToLineGeometry3D(),
                Color = Color.FromRgb(0xE8, 0x76, 0x2C),
                Thickness = 2.2,
                IsHitTestVisible = false
            });
        }

        // The corners of the finished outlines that can be dragged, in the blue those outlines
        // are drawn in. The one being drawn has its own points marked in orange below.
        var grips = sketch.Handles().Where(h => h.Loop >= 0).Select(h => On(h.At)).ToList();
        if (grips.Count > 0)
        {
            group.Children.Add(new PointGeometryModel3D
            {
                Geometry = new PointGeometry3D { Positions = new Vector3Collection(grips) },
                Color = Color.FromRgb(0x1F, 0x6F, 0xD1),
                Size = new Size(7, 7),
                IsHitTestVisible = false
            });
        }

        var dots = sketch.Corners.Select(On).ToList();
        if (cursor is { } c) dots.Add(On(c));
        if (dots.Count > 0)
        {
            group.Children.Add(new PointGeometryModel3D
            {
                Geometry = new PointGeometry3D { Positions = new Vector3Collection(dots) },
                Color = Color.FromRgb(0xE8, 0x76, 0x2C),
                Size = new Size(7, 7),
                IsHitTestVisible = false
            });
        }

        root.Children.Add(group);
        sketchShown = group;
    }

    /// <summary>Puts the objects back the way they are drawn when no split is being set up.</summary>
    public void ClearSplit()
    {
        Invalidate();

        split = null;
        foreach (var o in splitParts.Keys.ToList()) Release(o);
    }

    /// <summary>Cuts one object again for the plane where it now is.</summary>
    private void Rebuild(SceneObject o)
    {
        if (split is not { } plane || !visuals.TryGetValue(o, out var visual)) return;

        var stays = PlaneClip.Keep(o.Mesh, o.Transform, plane.Normal, plane.Offset, plane.Fill);
        // The off cut is left open where it was cut. Its cap would sit exactly on top of the
        // kept piece's, one solid and one drawn through, and two surfaces in the same place
        // flicker between each other as the camera moves.
        var goes = plane.Ghost
            ? PlaneClip.Keep(o.Mesh, o.Transform, -plane.Normal, -plane.Offset, cap: false)
            : null;
        int wanted = goes is null ? 1 : 2;

        if (!splitParts.TryGetValue(o, out var parts) || parts.Count != wanted)
        {
            Drop(o);

            parts = [];
            for (int i = 0; i < wanted; i++)
            {
                var part = NewPart(o, ghost: i == 1);
                parts.Add(part);
                root.Children.Add(part);
            }

            splitParts[o] = parts;

            // The object itself stands down while its halves stand in for it, and its outline
            // with it: that traces the whole shape, including the half being taken off.
            ShowOrHide(o, visual);
            RemoveOutline(o);
        }

        Fill(parts[0], stays);
        if (goes is not null) Fill(parts[1], goes);

        static void Fill(MeshGeometryModel3D part, Mesh piece)
        {
            // A plane clear of the solid leaves one side with nothing in it, and an empty
            // geometry is not something the renderer will take.
            part.Visibility = piece.TriangleCount > 0 ? Visibility.Visible : Visibility.Collapsed;
            if (piece.TriangleCount > 0) part.Geometry = MeshConverter.ToGeometry(piece);
        }
    }

    private MeshGeometryModel3D NewPart(SceneObject o, bool ghost)
    {
        var part = new MeshGeometryModel3D
        {
            // The cut piece is already in world space, so it carries no transform of its own.
            IsTransparent = ghost,

            // Both faces are drawn. The cut is left open - capping it would mean triangulating
            // the cross section, which is the boolean's job and far too slow to do while a
            // plane is being dragged - so without this you would see straight through the
            // opening and out the far side of the shell.
            CullMode = SharpDX.Direct3D11.CullMode.None
        };

        Dress(o, part, ghost);
        return part;
    }

    /// <summary>Colours one stand-in. Split out so a colour or a view setting can redo it.</summary>
    private void Dress(SceneObject o, MeshGeometryModel3D part, bool ghost)
    {
        part.Material = ghost ? GhostMaterial(o) : MaterialFor(o);
        part.RenderWireframe = wireframe;
        part.WireframeColor = Color.FromArgb(0x99, 0x1E, 0x26, 0x30);
    }

    /// <summary>The half on its way out: the object's own colour, drawn through.</summary>
    private static PhongMaterial GhostMaterial(SceneObject o) => new()
    {
        DiffuseColor = new SharpDX.Color4(o.Colour.X, o.Colour.Y, o.Colour.Z, 0.26f),
        SpecularColor = new SharpDX.Color4(0, 0, 0, 1),
        AmbientColor = new SharpDX.Color4(o.Colour.X * 0.35f, o.Colour.Y * 0.35f, o.Colour.Z * 0.35f, 1f)
    };

    /// <summary>Takes the stand-ins away without putting the object itself back.</summary>
    private void Drop(SceneObject o)
    {
        if (!splitParts.Remove(o, out var parts)) return;

        foreach (var part in parts)
        {
            root.Children.Remove(part);
            part.Dispose();
        }
    }

    private void Release(SceneObject o)
    {
        if (!splitParts.ContainsKey(o)) return;

        Drop(o);

        if (visuals.TryGetValue(o, out var visual)) ShowOrHide(o, visual);
        UpdateOutline(o);
    }

    /// <summary>Puts a changed view setting or colour onto whatever the split is standing in with.</summary>
    private void RefreshSplitLook()
    {
        foreach (var (o, parts) in splitParts)
            for (int i = 0; i < parts.Count; i++)
                Dress(o, parts[i], ghost: i == 1);
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
            RefreshSplitLook();
            Invalidate();
        }
    }

    /// <summary>
    /// Fades everything that is not selected, so a part buried inside another can be seen and
    /// worked on. Only the unselected fade: the point is to look past them at what is selected.
    ///
    /// It also brings back what a tool has stood aside, drawn through. See Focus.
    /// </summary>
    public bool Xray
    {
        get => xray;
        set
        {
            if (xray == value) return;
            xray = value;

            // What is drawn and not only how: this is the exception that puts what a tool stood
            // aside back on the plate.
            foreach (var (o, visual) in visuals)
            {
                ShowOrHide(o, visual);
                ApplyLook(o, visual);
            }

            RefreshSplitLook();
            Invalidate();
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
        visual.IsTransparent = Faded(o);

        // Left on regardless of the Shadows setting: the viewport's own IsShadowMappingEnabled
        // is what actually turns the pass on, and unless RenderShadowMap on other objects'
        // materials is also true a part cannot fall in one another's shadow. Setting both here
        // means Shadows can be flipped on the fly rather than needing every object rebuilt.
        visual.IsThrowingShadow = true;
        ((PhongMaterial)visual.Material).RenderShadowMap = true;

        visual.RenderWireframe = wireframe;
        visual.WireframeColor = Color.FromArgb(0x99, 0x1E, 0x26, 0x30);
    }

    /// <summary>Maps a hit-tested model back to the object it represents.</summary>
    public SceneObject? Resolve(object? model)
    {
        foreach (var (sceneObject, visual) in visuals)
            if (ReferenceEquals(visual, model))
                return sceneObject;

        // While a split is being set up the object is not drawn - its two halves are - so a
        // click on either of them has to come back as the object. Picking a face to cut on
        // goes through here, and without this it stopped working the moment the preview was on.
        foreach (var (sceneObject, parts) in splitParts)
            if (parts.Any(part => ReferenceEquals(part, model)))
                return sceneObject;

        return null;
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        Invalidate();

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
        ShowOrHide(o, visual);
        root.Children.Add(visual);
        o.PropertyChanged += OnObjectChanged;
        if (showOverhangs) MarkOverhangs(o);

        UpdateOutline(o);
    }

    private void Detach(SceneObject o)
    {
        RemoveOverhangs(o);
        Release(o);
        o.PropertyChanged -= OnObjectChanged;
        RemoveOutline(o);
        RemoveMirror(o);

        if (!visuals.Remove(o, out var visual)) return;
        root.Children.Remove(visual);
        visual.Dispose();
    }

    private void OnObjectChanged(object? sender, PropertyChangedEventArgs e)
    {
        Invalidate();

        if (sender is not SceneObject o || !visuals.TryGetValue(o, out var visual)) return;

        switch (e.PropertyName)
        {
            case nameof(SceneObject.Mesh):
                visual.Geometry = MeshConverter.ToGeometry(o.Mesh);
                RemoveOutline(o); // the old edges describe geometry that no longer exists
                UpdateOutline(o);
                UpdateMirror(o); // a fresh buffer, so the reflection's borrowed reference is stale too
                break;

            case nameof(SceneObject.Transform):
                visual.Transform = MeshConverter.ToTransform(o.Transform);
                if (outlines.TryGetValue(o, out var line))
                    line.Transform = visual.Transform;
                if (mirrors.TryGetValue(o, out var mirror))
                    mirror.Transform = MeshConverter.ToTransform(o.Transform * MirrorThroughPlate);
                break;

            case nameof(SceneObject.IsHidden):
                ShowOrHide(o, visual);
                break;

            case nameof(SceneObject.IsSelected):
            case nameof(SceneObject.Colour):
                ApplyLook(o, visual);
                UpdateOutline(o);
                if (e.PropertyName == nameof(SceneObject.Colour) && mirrors.TryGetValue(o, out var tinted))
                    tinted.Material = MirrorMaterial(o);
                break;
        }

        if (showOverhangs && e.PropertyName is nameof(SceneObject.Mesh) or nameof(SceneObject.Transform))
            MarkOverhangs(o);

        if (!splitParts.ContainsKey(o)) return;

        // The stand-ins are cut in world space, so anything that moves or reshapes the object
        // means cutting it again rather than moving them with it.
        if (e.PropertyName is nameof(SceneObject.Mesh) or nameof(SceneObject.Transform)) Rebuild(o);
        else RefreshSplitLook();
    }

    /// <summary>
    /// Draws a line along the shape's own edges while it is selected, or always while it is
    /// broken - the same red as the repair banner, so an object nobody has clicked on yet is not
    /// left to guess at from the banner's word "one or more".
    ///
    /// The object keeps its own colour rather than being repainted: the palette already contains
    /// oranges and blues, so a colour swap alone left it genuinely unclear which object was
    /// meant. An outline traces the thing itself and cannot be confused with a paint job - and
    /// for a torn mesh, the boundary of the tear is itself a feature edge, so the red line runs
    /// right along the hole rather than just round the outside.
    /// </summary>
    private void UpdateOutline(SceneObject o)
    {
        // Nothing to trace: the split is showing two halves in the object's place, and an
        // outline of the whole shape round them would draw the half that is being taken off.
        if (splitParts.ContainsKey(o)) return;

        bool damaged = !o.Health.IsWatertight;

        if (!showOutlines || (!o.IsSelected && !damaged))
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
            Color = damaged ? DamagedOutlineColour : OutlineColour,
            Thickness = damaged ? 2.2 : 1.4,
            Transform = MeshConverter.ToTransform(o.Transform),
            IsHitTestVisible = false // picking must still hit the solid underneath
        };

        outlines[o] = outline;
        root.Children.Add(outline);

        if (visuals.TryGetValue(o, out var solid)) outline.Visibility = solid.Visibility;
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

        // In x-ray the unselected go translucent, and so does whatever a tool has stood aside.
        // Transparency is ordered by the renderer's own OIT pass, so parts behind parts still
        // read correctly.
        if (Faded(o)) diffuse.Alpha = 0.28f;

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
        ClearSplit();
        ShowFace(null);
        scene.Objects.CollectionChanged -= OnCollectionChanged;
        foreach (var o in visuals.Keys.ToList()) Detach(o);
    }
}
