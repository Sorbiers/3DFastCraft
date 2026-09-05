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
    private Vector3 dragCentre;
    private List<SceneObject> dragObjects = [];
    private List<TransformState> dragBefore = [];
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

        var bounds = SelectionBounds();
        if (bounds.IsEmpty)
        {
            layer.Visibility = Visibility.Collapsed;
            NeedsReposition = false;
            return;
        }

        Vector3 centre = bounds.Center;
        UpdateScreenBox(bounds);

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
                case HandleKind.AxisArrow: PositionArrow(handle, bounds, centre); break;
                case HandleKind.Ring: PositionRing(handle, bounds, centre); break;
                case HandleKind.BoxOutline: PositionOutline(handle, bounds); break;
                case HandleKind.Corner: PositionCorner(handle, bounds); break;
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

    // --- Placement --------------------------------------------------------------------

    private void PositionArrow(Handle handle, Bounds bounds, Vector3 centre)
    {
        Vector3 direction = AxisVector(handle.Axis) * handle.Sign;
        Vector3 anchor = centre + direction * (Extent(bounds, handle.Axis) / 2f);

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
    private void UpdateScreenBox(Bounds bounds)
    {
        screenBoxValid = false;
        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;

        for (int i = 0; i < 8; i++)
        {
            if (!TryProject(CornerOf(bounds, i), out Point p)) return;
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

    private void PositionOutline(Handle handle, Bounds bounds)
    {
        // One stroke that walks all twelve edges without lifting the pen.
        int[] order = [0, 1, 3, 2, 0, 4, 5, 7, 6, 4, 5, 1, 3, 7, 6, 2];
        var points = new List<Point>(order.Length);
        foreach (int index in order)
            if (TryProject(CornerOf(bounds, index), out Point p))
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

    private void PositionCorner(Handle handle, Bounds bounds)
    {
        if (!TryProject(CornerOf(bounds, handle.Sign), out Point p))
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
        if (hitElement is not FrameworkElement { Tag: Handle handle }) return false;
        if (handle.Kind is HandleKind.BoxOutline or HandleKind.Corner) return false;

        var selection = scene.Selection;
        if (selection.Count == 0) return false;

        active = handle;
        dragStart = screen;
        dragCentre = SelectionBounds().Center;
        dragObjects = selection.ToList();
        dragBefore = dragObjects.Select(TransformState.Capture).ToList();
        dragChanged = false;
        return true;
    }

    public void ContinueDrag(Point screen)
    {
        if (active is null) return;

        if (active.Kind == HandleKind.Ring) DragRotate(screen);
        else if (mode == GizmoMode.Scale) DragScale(screen);
        else DragMove(screen);

        Reposition();
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

    private void DragMove(Point screen)
    {
        if (!TryAxisScreenScale(active!.Axis, out Vector axisScreen, out double pixelsPerMm)) return;

        var delta = new Vector(screen.X - dragStart.X, screen.Y - dragStart.Y);
        double millimetres = GizmoMath.MillimetresAlongAxis(delta, axisScreen, pixelsPerMm);

        // Snapped against the first object's own position, and the correction is then shared by
        // the whole selection, so a group lands on the grid without being pulled apart.
        millimetres = GizmoMath.SnapTravel(Component(dragBefore[0].Position, active.Axis), millimetres, SnapStep);

        Vector3 offset = AxisVector(active.Axis) * (float)millimetres;
        for (int i = 0; i < dragObjects.Count; i++)
            dragObjects[i].Position = dragBefore[i].Position + offset;

        dragChanged = true;
        Feedback?.Invoke($"Move {active.Axis} {millimetres:+0.##;-0.##;0} mm");
    }

    private void DragScale(Point screen)
    {
        if (!TryAxisScreenScale(active!.Axis, out Vector axisScreen, out double pixelsPerMm)) return;

        var delta = new Vector(screen.X - dragStart.X, screen.Y - dragStart.Y);
        // Folding in the handle's sign means dragging outward always grows, on either face.
        double millimetres = GizmoMath.MillimetresAlongAxis(delta, axisScreen, pixelsPerMm) * active.Sign;

        float startExtent = Extent(StartBounds(), active.Axis);
        if (startExtent < 1e-4f) return;

        float ratio = GizmoMath.ScaleRatio(startExtent, millimetres);

        for (int i = 0; i < dragObjects.Count; i++)
        {
            Vector3 before = dragBefore[i].Scale;
            dragObjects[i].Scale = UniformScale
                ? before * ratio
                : active.Axis switch
                {
                    Axis.X => before with { X = before.X * ratio },
                    Axis.Y => before with { Y = before.Y * ratio },
                    _ => before with { Z = before.Z * ratio }
                };
        }

        dragChanged = true;
        Feedback?.Invoke(UniformScale
            ? $"Resize {ratio * 100:0.#}% (uniform)"
            : $"Resize {active.Axis} {ratio * 100:0.#}%");
    }

    private void DragRotate(Point screen)
    {
        if (!TryProject(dragCentre, out Point centre)) return;

        double degrees = GizmoMath.RotationDegrees(
            centre, dragStart, screen, FacingSign(active!.Axis), SnapRotation, RotationSnapDegrees);

        for (int i = 0; i < dragObjects.Count; i++)
        {
            Vector3 before = dragBefore[i].Rotation;
            dragObjects[i].Rotation = active.Axis switch
            {
                Axis.X => before with { X = GizmoMath.NormaliseDegrees(before.X + (float)degrees) },
                Axis.Y => before with { Y = GizmoMath.NormaliseDegrees(before.Y + (float)degrees) },
                _ => before with { Z = GizmoMath.NormaliseDegrees(before.Z + (float)degrees) }
            };
        }

        dragChanged = true;
        Feedback?.Invoke($"Rotate {active.Axis} {degrees:+0.#;-0.#;0} deg{(SnapRotation ? " (snapped)" : "")}");
    }

    /// <summary>The selection's extent as it was when the drag started.</summary>
    private Bounds StartBounds()
    {
        var current = dragObjects.Select(TransformState.Capture).ToList();
        for (int i = 0; i < dragObjects.Count; i++) dragBefore[i].ApplyTo(dragObjects[i]);
        var bounds = SelectionBounds();
        for (int i = 0; i < dragObjects.Count; i++) current[i].ApplyTo(dragObjects[i]);
        return bounds;
    }

    /// <summary>+1 when the axis points towards the camera, -1 when away.</summary>
    private float FacingSign(Axis axis) =>
        Vector3.Dot(AxisVector(axis), -projector.ViewDirection) >= 0 ? 1f : -1f;

    /// <summary>
    /// The unit screen direction of a world axis, plus how many pixels one millimetre spans
    /// along it. Every drag is measured against this.
    /// </summary>
    private bool TryAxisScreenScale(Axis axis, out Vector screenDirection, out double pixelsPerMm)
    {
        screenDirection = default;
        pixelsPerMm = 0;

        Vector3 direction = AxisVector(axis);
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

    private static float Extent(Bounds bounds, Axis axis) => axis switch
    {
        Axis.X => bounds.Size.X,
        Axis.Y => bounds.Size.Y,
        _ => bounds.Size.Z
    };

    private static Vector3 CornerOf(Bounds bounds, int index) => new(
        (index & 1) == 0 ? bounds.Min.X : bounds.Max.X,
        (index & 2) == 0 ? bounds.Min.Y : bounds.Max.Y,
        (index & 4) == 0 ? bounds.Min.Z : bounds.Max.Z);

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
