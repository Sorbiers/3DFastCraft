using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using FastCraft3D.Geometry;
using FastCraft3D.Model;
using FastCraft3D.Model.Commands;
using HelixToolkit.Wpf.SharpDX;
using Axis = FastCraft3D.Geometry.Axis;
using Vector = System.Windows.Vector;
using WpfGeometry = System.Windows.Media.Geometry;
using Media3D = System.Windows.Media.Media3D;

namespace FastCraft3D.Render;

public enum GizmoMode
{
    Move,
    Rotate,
    Scale
}

/// <summary>
/// The on-screen manipulator: axis arrows for moving and resizing, rings for rotating.
///
/// Handles are 2D shapes on a canvas over the viewport, placed by projecting points of the
/// selection's bounding box into screen space - not 3D meshes. That keeps them a constant,
/// readable size at any zoom (a 3D arrow shrinks to nothing when you zoom away from a 5 mm
/// part), makes picking ordinary WPF hit-testing, and reduces every drag to 2D vector maths.
/// The canvas has no background, so a click that misses a handle falls through to the camera.
///
/// Shape geometry is always built with its top-left at the origin and then placed with
/// Canvas.Left/Top. A WPF Path offsets its geometry to the bounding box origin during layout,
/// so feeding it absolute screen coordinates would double-apply that offset.
/// </summary>
public sealed class GizmoController
{
    private const double AxisReferenceLength = 10.0; // mm, the ruler used to measure screen scale
    private const double HandleOffsetPixels = 22.0;  // clearance beyond the projected silhouette
    private const double RotationSnapDegrees = 15.0;
    private const double ArrowHalfLength = 21.0;
    private const double ArrowHalfHeight = 7.0;

    private static readonly Color XColour = Color.FromRgb(0xE8, 0x54, 0x62);
    private static readonly Color YColour = Color.FromRgb(0x35, 0xC7, 0x5A);
    private static readonly Color ZColour = Color.FromRgb(0x3B, 0x9C, 0xF0);
    private static readonly Color OutlineColour = Color.FromArgb(0xE0, 0xFF, 0xFF, 0xFF);

    private readonly Canvas layer;
    private readonly IScreenProjector projector;
    private readonly Scene scene;
    private readonly UndoStack undo;
    private readonly List<Handle> handles = new();

    private GizmoMode mode = GizmoMode.Move;
    private Rect screenBox;
    private bool screenBoxValid;
    private Point lastLayoutCentre;
    private Bounds lastLayoutBounds;
    private Handle? active;
    private Point dragStart;

    /// <summary>Where the pointer last was during a drag, so a key alone can re-run it.</summary>
    private Point lastScreen;
    private Vector3 dragCentre;
    /// <summary>The box the drag started against, so a long drag cannot chase its own tail.</summary>
    private Frame dragFrame;

    private List<SceneObject> dragObjects = [];
    private List<TransformState> dragBefore = [];

    /// <summary>
    /// How far this drag may go each way before contact, worked out once and then only clamped
    /// to. Nothing about the answer changes as the pointer moves: it is measured from where the
    /// drag began, against a scene that is not moving, so working it out again on every mouse
    /// event would be the same sum with the same answer.
    /// </summary>
    private float? contactAhead, contactBehind;
    private bool dragChanged;

    public GizmoController(Canvas layer, IScreenProjector projector, Scene scene, UndoStack undo)
    {
        this.layer = layer;
        this.projector = projector;
        this.scene = scene;
        this.undo = undo;
    }

    /// <summary>Live readout while dragging, for the status bar.</summary>
    public event Action<string>? Feedback;

    public bool IsDragging => active is not null;

    /// <summary>How many handle shapes the current mode has laid out. Used by tests.</summary>
    public int HandleCount => handles.Count;
    public bool UniformScale { get; set; } = true;

    /// <summary>
    /// Off while another tool owns the object. Lettering puts its own handles on the very same
    /// object, and two sets of handles over one thing is a guess about which one a drag meant.
    /// </summary>
    public bool Enabled
    {
        get => enabled;
        set
        {
            if (enabled == value) return;

            enabled = value;
            Reposition();
        }
    }

    /// <summary>
    /// Hold the face opposite the handle still, so the object grows only the way it is dragged.
    /// Off by default, which matches how the old app behaved.
    /// </summary>
    public bool ScaleOneSide { get; set; }

    /// <summary>
    /// The keys held during a resize, each overriding one setting for as long as it is down.
    ///
    /// Ctrl turns Keep proportions the other way, Alt does the same to One way only, and Shift
    /// snaps the size to whole millimetres. They override rather than toggle, and the buttons are
    /// left as they were: reaching for a key mid-drag is a one-off, and a setting that stayed
    /// flipped after the key came up would surprise the next drag instead.
    /// </summary>
    public ModifierKeys Modifiers { get; set; }

    /// <summary>
    /// Stop a move where the object meets another rather than letting it pass through.
    ///
    /// Sliding one part up against another until it stops is how things get assembled, and it
    /// beats typing coordinates for it. Off by default, because parts often need to overlap on
    /// their way to a boolean.
    /// </summary>
    public bool StopOnContact { get; set; }

    /// <summary>Millimetres to snap a move to, or zero for free movement.</summary>
    public double SnapStep { get; set; }

    public bool SnapRotation { get; set; } = true;

    public GizmoMode Mode
    {
        get => mode;
        set
        {
            if (mode == value) return;
            mode = value;
            Rebuild();
        }
    }

    /// <summary>Recreates the handle set. Call when the mode or the selection changes.</summary>
    public void Rebuild()
    {
        layer.Children.Clear();
        handles.Clear();

        if (scene.Selection.Count == 0) return;

        if (mode is GizmoMode.Move or GizmoMode.Scale)
            BuildAxisArrows(showBox: mode == GizmoMode.Scale);
        else
            BuildRotationRings();

        Reposition();
    }

    /// <summary>
    /// True when the last layout could not be computed and the caller should try again. The
    /// viewport cannot project anything while it is being resized, and a caller that skips
    /// repositioning whenever the camera has not moved would otherwise keep a layout produced
    /// from those unusable projections - which is how the handles ended up stranded in a corner
    /// after a window resize.
    /// </summary>
    public bool NeedsReposition { get; private set; }

    private bool enabled = true;

    /// <summary>
    /// Whether the handles still sit where the current projection says they should.
    ///
    /// This compares the projection's output - where the selection's centre lands on screen -
    /// rather than the inputs that feed it. A resize changes the answer without touching the
    /// camera or the geometry, and worse, the viewport may still be reporting the old answer at
    /// the moment the resize is signalled. Watching the result instead means any change is
    /// noticed on the next frame, whatever caused it and whenever it settles.
    /// </summary>
    public bool IsStale()
    {
        if (handles.Count == 0) return false;

        var bounds = SelectionBounds();
        if (bounds.IsEmpty != lastLayoutBounds.IsEmpty) return true;
        if (bounds.IsEmpty) return false;
        if (bounds.Min != lastLayoutBounds.Min || bounds.Max != lastLayoutBounds.Max) return true;

        if (!projector.TryProject(bounds.Center, out Point centre)) return true;
        return Math.Abs(centre.X - lastLayoutCentre.X) > 0.5
            || Math.Abs(centre.Y - lastLayoutCentre.Y) > 0.5;
    }

    /// <summary>Per-frame update: moves the existing handles instead of recreating them.</summary>
    public void Reposition()
    {
        if (handles.Count == 0) return;

        if (!enabled)
        {
            layer.Visibility = Visibility.Collapsed;
            NeedsReposition = false;
            return;
        }

        var bounds = SelectionBounds();
        if (bounds.IsEmpty)
        {
            layer.Visibility = Visibility.Collapsed;
            NeedsReposition = false;
            return;
        }

        var frame = CurrentFrame(bounds);
        Vector3 centre = frame.Centre;
        UpdateScreenBox(frame);

        if (!CanLayOut(bounds))
        {
            // Hide rather than place handles at whatever the failed projection returned.
            layer.Visibility = Visibility.Collapsed;
            NeedsReposition = true;
            return;
        }

        layer.Visibility = Visibility.Visible;
        NeedsReposition = false;
        lastLayoutBounds = bounds;
        TryProject(centre, out lastLayoutCentre);

        foreach (var handle in handles)
        {
            switch (handle.Kind)
            {
                case HandleKind.AxisArrow: PositionArrow(handle, frame, centre); break;
                case HandleKind.Ring: PositionRing(handle, bounds, centre); break;
                case HandleKind.BoxOutline: PositionOutline(handle, frame); break;
                case HandleKind.Corner: PositionCorner(handle, frame); break;
            }
        }
    }

    // --- Building ---------------------------------------------------------------------

    private void BuildAxisArrows(bool showBox)
    {
        if (showBox)
        {
            var outline = new Polyline
            {
                Stroke = new SolidColorBrush(OutlineColour),
                StrokeThickness = 1.2,
                StrokeDashArray = new DoubleCollection { 4, 3 },
                IsHitTestVisible = false
            };
            AddHandle(new Handle(outline, Axis.X, HandleKind.BoxOutline, 0));

            for (int corner = 0; corner < 8; corner++)
            {
                AddHandle(new Handle(new Rectangle
                {
                    Width = 7,
                    Height = 7,
                    Fill = Brushes.White,
                    Stroke = new SolidColorBrush(Color.FromRgb(0x55, 0x5B, 0x63)),
                    StrokeThickness = 1,
                    IsHitTestVisible = false
                }, Axis.X, HandleKind.Corner, corner));
            }
        }

        foreach (var axis in new[] { Axis.X, Axis.Y, Axis.Z })
        {
            foreach (int sign in new[] { 1, -1 })
            {
                AddHandle(new Handle(new Path
                {
                    Data = DoubleArrowGeometry(),
                    Width = ArrowHalfLength * 2,
                    Height = ArrowHalfHeight * 2,
                    Fill = new SolidColorBrush(ColourFor(axis)),
                    Stroke = Brushes.White,
                    StrokeThickness = 0.9,
                    Cursor = Cursors.SizeAll,
                    RenderTransformOrigin = new Point(0.5, 0.5),
                    ToolTip = mode == GizmoMode.Scale
                        ? $"Drag to resize along {axis}"
                        : $"Drag to move along {axis}"
                }, axis, HandleKind.AxisArrow, sign));
            }
        }
    }

    private void BuildRotationRings()
    {
        foreach (var axis in new[] { Axis.X, Axis.Y, Axis.Z })
        {
            AddHandle(new Handle(new Path
            {
                Stroke = new SolidColorBrush(ColourFor(axis)),
                StrokeThickness = 3.0,
                // A transparent fill would swallow clicks meant for the object inside the ring.
                Fill = null,
                Cursor = Cursors.Hand,
                ToolTip = $"Drag to rotate around {axis}"
            }, axis, HandleKind.Ring, 1));
        }
    }

    private void AddHandle(Handle handle)
    {
        handle.Visual.Tag = handle;
        handles.Add(handle);
        layer.Children.Add(handle.Visual);
    }

    // --- The box the handles are laid out on -------------------------------------------

    /// <summary>
    /// Where the handles sit and which way they point.
    ///
    /// Resizing works on the object's own box, turned with it. Moving and turning work on the
    /// world-aligned box round the selection, because along X and about Z mean the world's X and
    /// Z there - the position boxes say so.
    ///
    /// Resizing cannot: the size boxes give the object's own width, height and depth, and the
    /// scale behind them is applied before the turn. Pointing the arrows along the world axes
    /// while the drag stretched the object along its own was the whole complaint - the arrow said
    /// one thing and the object did another as soon as anything was turned.
    /// </summary>
    private readonly record struct Frame(Vector3 Centre, Vector3 X, Vector3 Y, Vector3 Z, Vector3 Size)
    {
        public Vector3 Along(Axis axis) => axis switch { Axis.X => X, Axis.Y => Y, _ => Z };

        public float Reach(Axis axis) =>
            axis switch { Axis.X => Size.X, Axis.Y => Size.Y, _ => Size.Z };

        public Vector3 Corner(int index) => Centre
            + X * (((index & 1) == 0 ? -0.5f : 0.5f) * Size.X)
            + Y * (((index & 2) == 0 ? -0.5f : 0.5f) * Size.Y)
            + Z * (((index & 4) == 0 ? -0.5f : 0.5f) * Size.Z);
    }

    /// <summary>
    /// The object's own box while resizing one thing, and the world-aligned box otherwise. A
    /// group has no shared frame to turn the arrows into, so a multiple selection keeps the
    /// world axes and each part still scales along its own, as it always did.
    /// </summary>
    private Frame CurrentFrame(Bounds bounds)
    {
        if (mode == GizmoMode.Scale && scene.Selection.Count == 1)
        {
            var o = scene.Selection[0];
            var turn = MeshTransform.Rotation(o.Rotation);

            return new Frame(
                Vector3.Transform(o.LocalCentre, o.Transform),
                Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitX, turn)),
                Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitY, turn)),
                Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitZ, turn)),
                new Vector3(o.SizeX, o.SizeY, o.SizeZ));
        }

        return new Frame(
            bounds.Center, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ, bounds.Size);
    }

    // --- Placement --------------------------------------------------------------------

    private void PositionArrow(Handle handle, Frame frame, Vector3 centre)
    {
        Vector3 direction = frame.Along(handle.Axis) * handle.Sign;
        Vector3 anchor = centre + direction * (frame.Reach(handle.Axis) / 2f);

        if (!TryProject(centre, out Point centreScreen) ||
            !TryProject(anchor, out Point anchorScreen) ||
            !TryProject(anchor + direction, out Point aheadScreen))
        {
            handle.Visual.Visibility = Visibility.Collapsed;
            return;
        }

        var outward = new Vector(aheadScreen.X - anchorScreen.X, aheadScreen.Y - anchorScreen.Y);
        if (outward.Length < 1e-6)
        {
            // The axis points almost straight at the camera - an arrow there means nothing.
            handle.Visual.Visibility = Visibility.Collapsed;
            return;
        }
        outward.Normalize();

        // Offsetting from the face centre is not enough: a face seen at an angle projects well
        // inside the object's outline, so the arrow lands on top of the solid. Walking out from
        // the centre until the ray leaves the projected silhouette puts every handle clear of
        // the object regardless of the viewing angle.
        double exit = DistanceToSilhouetteEdge(centreScreen, outward);
        Point position = centreScreen + outward * (exit + HandleOffsetPixels);

        handle.Visual.Visibility = Visibility.Visible;
        handle.Visual.RenderTransform = new RotateTransform(Math.Atan2(outward.Y, outward.X) * 180.0 / Math.PI);
        Canvas.SetLeft(handle.Visual, position.X - ArrowHalfLength);
        Canvas.SetTop(handle.Visual, position.Y - ArrowHalfHeight);
    }

    /// <summary>
    /// How far a ray from the projected centre travels before leaving the screen-space box of
    /// the selection's eight corners.
    /// </summary>
    private double DistanceToSilhouetteEdge(Point centre, Vector direction)
    {
        if (!screenBoxValid) return 0;

        double best = double.MaxValue;
        if (Math.Abs(direction.X) > 1e-9)
        {
            double edge = direction.X > 0 ? screenBox.Right : screenBox.Left;
            best = Math.Min(best, (edge - centre.X) / direction.X);
        }
        if (Math.Abs(direction.Y) > 1e-9)
        {
            double edge = direction.Y > 0 ? screenBox.Bottom : screenBox.Top;
            best = Math.Min(best, (edge - centre.Y) / direction.Y);
        }

        return best is double.MaxValue or < 0 ? 0 : best;
    }

    /// <summary>
    /// Whether the projections just computed are usable.
    ///
    /// A viewport mid-resize projects every point to the same place - usually the origin - so a
    /// solid with real size collapses to a dot on screen. That is indistinguishable from a valid
    /// layout by coordinates alone, but not by extent, which is what this checks.
    /// </summary>
    private bool CanLayOut(Bounds bounds)
    {
        if (!projector.IsReady || !screenBoxValid) return false;

        bool hasSizeInWorld = bounds.Diagonal > 1e-3f;
        bool hasSizeOnScreen = screenBox.Width > 0.5 || screenBox.Height > 0.5;
        return !hasSizeInWorld || hasSizeOnScreen;
    }

    /// <summary>Screen-space bounds of the selection, recomputed once per layout pass.</summary>
    private void UpdateScreenBox(Frame frame)
    {
        screenBoxValid = false;
        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;

        for (int i = 0; i < 8; i++)
        {
            if (!TryProject(frame.Corner(i), out Point p)) return;
            minX = Math.Min(minX, p.X);
            minY = Math.Min(minY, p.Y);
            maxX = Math.Max(maxX, p.X);
            maxY = Math.Max(maxY, p.Y);
        }

        screenBox = new Rect(minX, minY, Math.Max(maxX - minX, 0), Math.Max(maxY - minY, 0));
        screenBoxValid = true;
    }

    private void PositionRing(Handle handle, Bounds bounds, Vector3 centre)
    {
        float radius = MathF.Max(bounds.Diagonal * 0.46f, 1f);
        var (u, v) = BasisFor(handle.Axis);

        const int segments = 72;
        var points = new List<Point>(segments);
        for (int i = 0; i < segments; i++)
        {
            double t = Math.Tau * i / segments;
            Vector3 world = centre + (u * (float)Math.Cos(t) + v * (float)Math.Sin(t)) * radius;
            if (TryProject(world, out Point p)) points.Add(p);
        }

        if (points.Count < 3)
        {
            handle.Visual.Visibility = Visibility.Collapsed;
            return;
        }

        double minX = points.Min(p => p.X);
        double minY = points.Min(p => p.Y);

        var figure = new PathFigure { StartPoint = new Point(points[0].X - minX, points[0].Y - minY), IsClosed = true };
        for (int i = 1; i < points.Count; i++)
            figure.Segments.Add(new LineSegment(new Point(points[i].X - minX, points[i].Y - minY), true));

        handle.Visual.Visibility = Visibility.Visible;
        ((Path)handle.Visual).Data = new PathGeometry { Figures = { figure } };
        Canvas.SetLeft(handle.Visual, minX);
        Canvas.SetTop(handle.Visual, minY);
    }

    private void PositionOutline(Handle handle, Frame frame)
    {
        // One stroke that walks all twelve edges without lifting the pen.
        int[] order = [0, 1, 3, 2, 0, 4, 5, 7, 6, 4, 5, 1, 3, 7, 6, 2];
        var points = new List<Point>(order.Length);
        foreach (int index in order)
            if (TryProject(frame.Corner(index), out Point p))
                points.Add(p);

        var polyline = (Polyline)handle.Visual;
        if (points.Count < 2)
        {
            polyline.Visibility = Visibility.Collapsed;
            return;
        }

        double minX = points.Min(p => p.X);
        double minY = points.Min(p => p.Y);

        polyline.Visibility = Visibility.Visible;
        polyline.Points = new PointCollection(points.Select(p => new Point(p.X - minX, p.Y - minY)));
        Canvas.SetLeft(polyline, minX);
        Canvas.SetTop(polyline, minY);
    }

    private void PositionCorner(Handle handle, Frame frame)
    {
        if (!TryProject(frame.Corner(handle.Sign), out Point p))
        {
            handle.Visual.Visibility = Visibility.Collapsed;
            return;
        }
        handle.Visual.Visibility = Visibility.Visible;
        Canvas.SetLeft(handle.Visual, p.X - handle.Visual.Width / 2);
        Canvas.SetTop(handle.Visual, p.Y - handle.Visual.Height / 2);
    }

    // --- Dragging ---------------------------------------------------------------------

    /// <summary>True when the press landed on a handle, meaning the gizmo owns this drag.</summary>
    public bool TryBeginDrag(Point screen, object? hitElement)
    {
        if (!enabled) return false;
        if (hitElement is not FrameworkElement { Tag: Handle handle }) return false;
        if (handle.Kind is HandleKind.BoxOutline or HandleKind.Corner) return false;

        var selection = scene.Selection;
        if (selection.Count == 0) return false;

        active = handle;
        dragStart = screen;
        lastScreen = screen;
        dragCentre = SelectionBounds().Center;
        dragFrame = CurrentFrame(SelectionBounds());
        dragObjects = selection.ToList();
        dragBefore = dragObjects.Select(TransformState.Capture).ToList();
        dragChanged = false;
        contactAhead = null;
        contactBehind = null;
        return true;
    }

    public void ContinueDrag(Point screen)
    {
        if (active is null) return;

        lastScreen = screen;

        if (active.Kind == HandleKind.Ring) DragRotate(screen);
        else if (mode == GizmoMode.Scale) DragScale(screen);
        else DragMove(screen);

        Reposition();
    }

    /// <summary>
    /// Runs the drag again where the pointer already is.
    ///
    /// A modifier pressed or let go mid-drag changes the answer without the mouse moving, and
    /// waiting for the next mouse move to show it makes the key feel as though it did nothing.
    /// </summary>
    public void Refresh()
    {
        if (active is not null) ContinueDrag(lastScreen);
    }

    public void EndDrag()
    {
        if (active is null) return;

        if (dragChanged &&
            TransformCommand.CreateIfChanged(LabelFor(mode), dragObjects, dragBefore) is { } command)
        {
            undo.Execute(command);
        }

        active = null;
        dragObjects = [];
        dragBefore = [];
        Rebuild();
    }

    /// <summary>
    /// How far this drag may actually go before something is in the way.
    ///
    /// Measured from where the drag started rather than from where the objects are now, so
    /// pushing further into an obstacle and easing back off behaves the same as approaching it
    /// for the first time.
    /// </summary>
    private float ContactLimit(float travel)
    {
        if (travel == 0) return 0;

        int direction = travel > 0 ? 1 : -1;
        float allowed = direction > 0
            ? contactAhead ??= ContactDistance(1)
            : contactBehind ??= ContactDistance(-1);

        return Math.Min(Math.Abs(travel), allowed) * direction;
    }

    /// <summary>
    /// How far the selection can go this way before it meets something, measured on the shapes
    /// themselves. The box each shape sits in is a poor stand-in for a cone or anything turned,
    /// and stopping short of the thing you were trying to touch is the whole complaint.
    /// </summary>
    private float ContactDistance(int direction)
    {
        var moving = new List<Mesh>(dragObjects.Count);
        for (int i = 0; i < dragObjects.Count; i++)
            moving.Add(WorldMeshAt(dragObjects[i], dragBefore[i]));

        var obstacles = new List<Mesh>();
        foreach (var o in scene.Objects)
            if (!dragObjects.Contains(o))
                obstacles.Add(o.ToWorldMesh());

        return MeshSweep.Distance(moving, obstacles, active!.Axis, direction);
    }

    /// <summary>The object in world space as it was when the drag began.</summary>
    private static Mesh WorldMeshAt(SceneObject o, TransformState state)
    {
        var was = TransformState.Capture(o);
        state.ApplyTo(o);
        var mesh = o.ToWorldMesh();
        was.ApplyTo(o);

        return mesh;
    }

    private void DragMove(Point screen)
    {
        if (!TryAxisScreenScale(active!.Axis, out Vector axisScreen, out double pixelsPerMm)) return;

        var delta = new Vector(screen.X - dragStart.X, screen.Y - dragStart.Y);
        double millimetres = GizmoMath.MillimetresAlongAxis(delta, axisScreen, pixelsPerMm);

        // Snapped against the first object's own position, and the correction is then shared by
        // the whole selection, so a group lands on the grid without being pulled apart.
        millimetres = GizmoMath.SnapTravel(Component(dragBefore[0].Position, active.Axis), millimetres, SnapStep);

        bool stopped = false;
        if (StopOnContact)
        {
            float allowed = ContactLimit((float)millimetres);
            stopped = Math.Abs(allowed - millimetres) > 1e-4;
            millimetres = allowed;
        }

        Vector3 offset = AxisVector(active.Axis) * (float)millimetres;
        for (int i = 0; i < dragObjects.Count; i++)
            dragObjects[i].Position = dragBefore[i].Position + offset;

        dragChanged = true;
        Feedback?.Invoke(stopped
            ? $"Move {active.Axis} {millimetres:+0.##;-0.##;0} mm - stopped against another object"
            : $"Move {active.Axis} {millimetres:+0.##;-0.##;0} mm");
    }

    private void DragScale(Point screen)
    {
        // Measured along the object's own axis, which is the one the arrow is drawn on. On
        // anything turned, that is not the world axis of the same name: the scale is applied
        // before the turn, so stretching X stretches the object's X wherever it now points.
        Vector3 direction = dragFrame.Along(active!.Axis);
        if (!TryScreenScale(direction, out Vector axisScreen, out double pixelsPerMm)) return;

        var delta = new Vector(screen.X - dragStart.X, screen.Y - dragStart.Y);
        // Folding in the handle's sign means dragging outward always grows, on either face.
        double millimetres = GizmoMath.MillimetresAlongAxis(delta, axisScreen, pixelsPerMm) * active.Sign;

        float startExtent = dragFrame.Reach(active.Axis);
        if (startExtent < 1e-4f) return;

        bool uniform = UniformScale ^ Modifiers.HasFlag(ModifierKeys.Control);
        bool oneSide = ScaleOneSide ^ Modifiers.HasFlag(ModifierKeys.Alt);
        bool whole = Modifiers.HasFlag(ModifierKeys.Shift);

        float ratio = GizmoMath.ScaleRatio(startExtent, millimetres, aboutCentre: !oneSide);
        if (whole) ratio = GizmoMath.WholeMillimetres(startExtent, ratio);

        // A point on the face that stays put: whichever one the handle is not on.
        Vector3 held = dragFrame.Centre - direction * (active.Sign * startExtent / 2f);

        for (int i = 0; i < dragObjects.Count; i++)
        {
            Vector3 before = dragBefore[i].Scale;
            dragObjects[i].Scale = uniform
                ? before * ratio
                : active.Axis switch
                {
                    Axis.X => before with { X = before.X * ratio },
                    Axis.Y => before with { Y = before.Y * ratio },
                    _ => before with { Z = before.Z * ratio }
                };

            // Holding a face still means the object has to travel as it grows. Positions are
            // taken from where the drag started rather than from where they are now, so a long
            // drag cannot accumulate rounding.
            if (!oneSide) continue;

            Vector3 was = dragBefore[i].Position;
            dragObjects[i].Position =
                was + direction * (Vector3.Dot(was - held, direction) * (ratio - 1f));
        }

        dragChanged = true;
        string stillThere = oneSide ? ", far side held" : "";
        string size = whole ? $", {startExtent * ratio:0} mm" : "";
        Feedback?.Invoke(uniform
            ? $"Resize {ratio * 100:0.#}% (uniform{stillThere}{size})"
            : $"Resize {active.Axis} {ratio * 100:0.#}%{stillThere}{size}");
    }

    private void DragRotate(Point screen)
    {
        if (!TryProject(dragCentre, out Point centre)) return;

        double degrees = GizmoMath.RotationDegrees(
            centre, dragStart, screen, FacingSign(active!.Axis), SnapRotation, RotationSnapDegrees);

        // Turned about the world axis the ring is drawn on, by composing with what the object
        // already had. Adding the amount to one of the three angles instead is the obvious thing
        // and is wrong: the three are applied in order, so only the last of them lines up with
        // the world and the other two turn the object about its own axes. After one turn a
        // second went somewhere other than where the ring said it would.
        float radians = (float)(degrees * Math.PI / 180.0);
        var turn = active.Axis switch
        {
            Axis.X => Matrix4x4.CreateRotationX(radians),
            Axis.Y => Matrix4x4.CreateRotationY(radians),
            _ => Matrix4x4.CreateRotationZ(radians)
        };

        for (int i = 0; i < dragObjects.Count; i++)
        {
            dragObjects[i].Rotation =
                MeshTransform.EulerFrom(MeshTransform.Rotation(dragBefore[i].Rotation) * turn);
        }

        dragChanged = true;
        Feedback?.Invoke($"Rotate {active.Axis} {degrees:+0.#;-0.#;0} deg{(SnapRotation ? " (snapped)" : "")}");
    }

    /// <summary>+1 when the axis points towards the camera, -1 when away.</summary>
    private float FacingSign(Axis axis) =>
        Vector3.Dot(AxisVector(axis), -projector.ViewDirection) >= 0 ? 1f : -1f;

    /// <summary>
    /// The unit screen direction of a world axis, plus how many pixels one millimetre spans
    /// along it. Every drag is measured against this.
    /// </summary>
    private bool TryAxisScreenScale(Axis axis, out Vector screenDirection, out double pixelsPerMm) =>
        TryScreenScale(AxisVector(axis), out screenDirection, out pixelsPerMm);

    /// <summary>
    /// Where a world direction points on screen, and how many pixels one millimetre spans along
    /// it. Takes a direction rather than an axis because resizing works along the object's own
    /// axes, which are only world axes while nothing is turned.
    /// </summary>
    private bool TryScreenScale(Vector3 direction, out Vector screenDirection, out double pixelsPerMm)
    {
        screenDirection = default;
        pixelsPerMm = 0;

        if (!TryProject(dragCentre, out Point a) ||
            !TryProject(dragCentre + direction * (float)AxisReferenceLength, out Point b))
        {
            return false;
        }

        var span = new Vector(b.X - a.X, b.Y - a.Y);
        double length = span.Length;
        if (length < 1e-6) return false; // the axis is edge-on to the camera

        screenDirection = span / length;
        pixelsPerMm = length / AxisReferenceLength;
        return true;
    }

    // --- Helpers ----------------------------------------------------------------------

    private Bounds SelectionBounds()
    {
        var bounds = Bounds.Empty;
        foreach (var o in scene.Selection)
            bounds = bounds.Union(o.WorldBounds);
        return bounds;
    }

    private bool TryProject(Vector3 world, out Point screen) => projector.TryProject(world, out screen);

    private static Vector3 AxisVector(Axis axis) => axis switch
    {
        Axis.X => Vector3.UnitX,
        Axis.Y => Vector3.UnitY,
        _ => Vector3.UnitZ
    };

    private static (Vector3 U, Vector3 V) BasisFor(Axis axis) => axis switch
    {
        Axis.X => (Vector3.UnitY, Vector3.UnitZ),
        Axis.Y => (Vector3.UnitZ, Vector3.UnitX),
        _ => (Vector3.UnitX, Vector3.UnitY)
    };

    private static float Component(Vector3 v, Axis axis) => axis switch
    {
        Axis.X => v.X,
        Axis.Y => v.Y,
        _ => v.Z
    };

    private static Color ColourFor(Axis axis) => axis switch
    {
        Axis.X => XColour,
        Axis.Y => YColour,
        _ => ZColour
    };

    private static string LabelFor(GizmoMode mode) => mode switch
    {
        GizmoMode.Move => "Move",
        GizmoMode.Rotate => "Rotate",
        _ => "Resize"
    };

    /// <summary>Double-headed arrow pointing along +X, drawn with its top-left at the origin.</summary>
    private static WpfGeometry DoubleArrowGeometry()
    {
        const double w = ArrowHalfLength * 2, h = ArrowHalfHeight * 2;
        const double head = 10, thick = 2.6;
        double mid = h / 2;

        var figure = new PathFigure { StartPoint = new Point(0, mid), IsClosed = true };
        foreach (var p in new[]
        {
            new Point(head, 0),
            new Point(head, mid - thick),
            new Point(w - head, mid - thick),
            new Point(w - head, 0),
            new Point(w, mid),
            new Point(w - head, h),
            new Point(w - head, mid + thick),
            new Point(head, mid + thick),
            new Point(head, h)
        })
        {
            figure.Segments.Add(new LineSegment(p, true));
        }

        var geometry = new PathGeometry { Figures = { figure } };
        geometry.Freeze();
        return geometry;
    }

    private enum HandleKind
    {
        AxisArrow,
        Ring,
        BoxOutline,
        Corner
    }

    private sealed class Handle(Shape visual, Axis axis, GizmoController.HandleKind kind, int sign)
    {
        public Shape Visual { get; } = visual;
        public Axis Axis { get; } = axis;
        public HandleKind Kind { get; } = kind;

        /// <summary>Which end of the axis, or which corner index for corner dots.</summary>
        public int Sign { get; } = sign;
    }
}
