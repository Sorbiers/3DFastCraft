using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using FastCraft3D.Geometry;
using Vector = System.Windows.Vector;
using WpfGeometry = System.Windows.Media.Geometry;

namespace FastCraft3D.Render;

/// <summary>
/// Handles for the split plane: arrows that slide it along its own normal, and rings that tilt
/// it.
///
/// Separate from <see cref="GizmoController"/> on purpose. That one manipulates whatever is
/// selected and reads its state from the scene; this one manipulates a plane that is not a
/// scene object at all. Sharing a class would mean threading a second, unrelated mode through
/// every method. They do share the projector and the drag arithmetic.
/// </summary>
public sealed class SplitPlaneGizmo
{
    private const double HandleOffsetPixels = 26.0;
    private const double ArrowHalfLength = 21.0;
    private const double ArrowHalfHeight = 7.0;
    private const double AxisReferenceLength = 10.0;
    private const double RotationSnapDegrees = 15.0;

    private static readonly Color OffsetColour = Color.FromRgb(0x3B, 0x9C, 0xF0);

    private readonly Canvas layer;
    private readonly IScreenProjector projector;
    private readonly List<Handle> handles = new();

    private Vector3 normal = Vector3.UnitZ;
    private Vector3 centre;
    private float offset;
    private bool active;

    private Handle? dragging;
    private Point dragStart;
    private float dragStartOffset;
    private Vector3 dragStartNormal;

    public SplitPlaneGizmo(Canvas layer, IScreenProjector projector)
    {
        this.layer = layer;
        this.projector = projector;
    }

    /// <summary>Raised while dragging, with the new plane offset and normal.</summary>
    public event Action<float, Vector3>? Changed;

    /// <summary>Live readout for the status bar.</summary>
    public event Action<string>? Feedback;

    public bool IsDragging => dragging is not null;
    public bool SnapRotation { get; set; } = true;

    /// <summary>Shows the handles for a plane, or hides them when <paramref name="on"/> is false.</summary>
    public void Show(bool on, Vector3 planeNormal, float planeOffset, Vector3 solidCentre)
    {
        normal = Vector3.Normalize(planeNormal);
        offset = planeOffset;
        centre = solidCentre;

        if (active == on && handles.Count > 0)
        {
            Reposition();
            return;
        }

        active = on;
        Rebuild();
    }

    private void Rebuild()
    {
        layer.Children.Clear();
        handles.Clear();

        if (!active)
        {
            layer.Visibility = Visibility.Collapsed;
            return;
        }

        layer.Visibility = Visibility.Visible;

        // Two arrows, one each way along the normal, to slide the plane.
        foreach (int sign in new[] { 1, -1 })
        {
            Add(new Handle(new Path
            {
                Data = DoubleArrowGeometry(),
                Width = ArrowHalfLength * 2,
                Height = ArrowHalfHeight * 2,
                Fill = new SolidColorBrush(OffsetColour),
                Stroke = Brushes.White,
                StrokeThickness = 0.9,
                Cursor = Cursors.SizeAll,
                RenderTransformOrigin = new Point(0.5, 0.5),
                ToolTip = "Drag to slide the split plane"
            }, HandleKind.Offset, sign, Vector3.Zero));
        }

        // A ring per world axis to tilt the plane. One whose axis lines up with the normal is
        // left out: turning the plane about its own normal does not move it.
        foreach (var axis in new[] { Axis.X, Axis.Y, Axis.Z })
        {
            Vector3 spin = PlaneSplit.NormalFor(axis);
            if (MathF.Abs(Vector3.Dot(spin, normal)) > 0.98f) continue;

            Add(new Handle(new Path
            {
                Stroke = new SolidColorBrush(ColourFor(axis)),
                StrokeThickness = 2.6,
                Fill = null,
                Cursor = Cursors.Hand,
                ToolTip = $"Drag to tilt the split plane around {axis}"
            }, HandleKind.Tilt, 1, spin));
        }

        Reposition();
    }

    /// <summary>Re-places the handles for the current camera and plane.</summary>
    public void Reposition()
    {
        if (!active || handles.Count == 0) return;

        if (!projector.IsReady || !projector.TryProject(PlanePoint(), out Point origin))
        {
            layer.Visibility = Visibility.Collapsed;
            return;
        }

        layer.Visibility = Visibility.Visible;
        float radius = MathF.Max(RadiusHint(), 1f);

        foreach (var handle in handles)
        {
            if (handle.Kind == HandleKind.Offset) PositionArrow(handle, origin);
            else PositionRing(handle, radius);
        }
    }

    private void PositionArrow(Handle handle, Point origin)
    {
        Vector3 direction = normal * handle.Sign;
        if (!projector.TryProject(PlanePoint() + direction * (float)AxisReferenceLength, out Point ahead))
        {
            handle.Visual.Visibility = Visibility.Collapsed;
            return;
        }

        var outward = new Vector(ahead.X - origin.X, ahead.Y - origin.Y);
        if (outward.Length < 1e-6)
        {
            // The normal points at the camera; an arrow along it would convey nothing.
            handle.Visual.Visibility = Visibility.Collapsed;
            return;
        }
        outward.Normalize();

        Point position = origin + outward * HandleOffsetPixels;
        handle.Visual.Visibility = Visibility.Visible;
        handle.Visual.RenderTransform = new RotateTransform(Math.Atan2(outward.Y, outward.X) * 180.0 / Math.PI);
        Canvas.SetLeft(handle.Visual, position.X - ArrowHalfLength);
        Canvas.SetTop(handle.Visual, position.Y - ArrowHalfHeight);
    }

    private void PositionRing(Handle handle, float radius)
    {
        var (u, v) = BasisFor(handle.Spin);
        Vector3 anchor = PlanePoint();

        const int segments = 64;
        var points = new List<Point>(segments);
        for (int i = 0; i < segments; i++)
        {
            double t = Math.Tau * i / segments;
            Vector3 world = anchor + (u * (float)Math.Cos(t) + v * (float)Math.Sin(t)) * radius;
            if (projector.TryProject(world, out Point p)) points.Add(p);
        }

        if (points.Count < 3)
        {
            handle.Visual.Visibility = Visibility.Collapsed;
            return;
        }

        double minX = points.Min(p => p.X), minY = points.Min(p => p.Y);
        var figure = new PathFigure { StartPoint = Offsetted(points[0]), IsClosed = true };
        for (int i = 1; i < points.Count; i++)
            figure.Segments.Add(new LineSegment(Offsetted(points[i]), true));

        handle.Visual.Visibility = Visibility.Visible;
        ((Path)handle.Visual).Data = new PathGeometry { Figures = { figure } };
        Canvas.SetLeft(handle.Visual, minX);
        Canvas.SetTop(handle.Visual, minY);

        Point Offsetted(Point p) => new(p.X - minX, p.Y - minY);
    }

    // --- Dragging ---------------------------------------------------------------------

    public bool TryBeginDrag(Point screen, object? hitElement)
    {
        if (!active) return false;
        if (hitElement is not FrameworkElement { Tag: Handle handle }) return false;

        dragging = handle;
        dragStart = screen;
        dragStartOffset = offset;
        dragStartNormal = normal;
        return true;
    }

    public void ContinueDrag(Point screen)
    {
        if (dragging is null) return;

        if (dragging.Kind == HandleKind.Offset) DragOffset(screen);
        else DragTilt(screen);

        Changed?.Invoke(offset, normal);
        Reposition();
    }

    public void EndDrag() => dragging = null;

    private void DragOffset(Point screen)
    {
        if (!projector.TryProject(PlanePointFor(dragStartOffset, dragStartNormal), out Point a)) return;
        if (!projector.TryProject(PlanePointFor(dragStartOffset, dragStartNormal) + dragStartNormal * (float)AxisReferenceLength,
                out Point b)) return;

        var span = new Vector(b.X - a.X, b.Y - a.Y);
        double length = span.Length;
        if (length < 1e-6) return; // the normal is edge-on; sliding it would be a guess

        var delta = new Vector(screen.X - dragStart.X, screen.Y - dragStart.Y);
        double millimetres = GizmoMath.MillimetresAlongAxis(delta, span / length, length / AxisReferenceLength);

        offset = dragStartOffset + (float)millimetres;
        Feedback?.Invoke($"Split plane at {offset:0.##} mm");
    }

    private void DragTilt(Point screen)
    {
        if (!projector.TryProject(PlanePointFor(dragStartOffset, dragStartNormal), out Point pivot)) return;

        float facing = Vector3.Dot(dragging!.Spin, -projector.ViewDirection) >= 0 ? 1f : -1f;
        double degrees = GizmoMath.RotationDegrees(pivot, dragStart, screen, facing, SnapRotation, RotationSnapDegrees);

        var turn = Matrix4x4.CreateFromAxisAngle(dragging.Spin, (float)(degrees * Math.PI / 180.0));
        normal = Vector3.Normalize(Vector3.Transform(dragStartNormal, turn));

        // The offset is measured along the normal, so it has to be restated for the new one or
        // the plane would jump away from the solid as it tilts.
        offset = Vector3.Dot(normal, PlanePointFor(dragStartOffset, dragStartNormal));

        Feedback?.Invoke($"Split plane tilted {degrees:+0.#;-0.#;0} deg around {AxisNameOf(dragging.Spin)}");
    }

    // --- Helpers ----------------------------------------------------------------------

    /// <summary>The point of the plane nearest the solid, which is where the handles gather.</summary>
    private Vector3 PlanePoint() => PlanePointFor(offset, normal);

    private Vector3 PlanePointFor(float planeOffset, Vector3 planeNormal) =>
        centre - planeNormal * (Vector3.Dot(planeNormal, centre) - planeOffset);

    private float RadiusHint()
    {
        // Rings a little larger than the arrows so the two never sit on top of each other.
        if (!projector.TryProject(PlanePoint(), out Point a)) return 1f;
        if (!projector.TryProject(PlanePoint() + PerpendicularTo(normal) * (float)AxisReferenceLength, out Point b))
            return 1f;

        double pixelsPerMm = new Vector(b.X - a.X, b.Y - a.Y).Length / AxisReferenceLength;
        return pixelsPerMm > 1e-6 ? (float)(70.0 / pixelsPerMm) : 1f;
    }

    private static Vector3 PerpendicularTo(Vector3 v) =>
        Vector3.Normalize(Vector3.Cross(v, MathF.Abs(v.X) < 0.9f ? Vector3.UnitX : Vector3.UnitY));

    private static (Vector3 U, Vector3 V) BasisFor(Vector3 axis)
    {
        Vector3 u = PerpendicularTo(axis);
        return (u, Vector3.Normalize(Vector3.Cross(axis, u)));
    }

    private static string AxisNameOf(Vector3 axis) =>
        MathF.Abs(axis.X) > 0.5f ? "X" : MathF.Abs(axis.Y) > 0.5f ? "Y" : "Z";

    private static Color ColourFor(Axis axis) => axis switch
    {
        Axis.X => Color.FromRgb(0xE8, 0x54, 0x62),
        Axis.Y => Color.FromRgb(0x35, 0xC7, 0x5A),
        _ => Color.FromRgb(0x3B, 0x9C, 0xF0)
    };

    private void Add(Handle handle)
    {
        handle.Visual.Tag = handle;
        handles.Add(handle);
        layer.Children.Add(handle.Visual);
    }

    private static WpfGeometry DoubleArrowGeometry()
    {
        const double w = ArrowHalfLength * 2, h = ArrowHalfHeight * 2;
        const double head = 10, thick = 2.6;
        double mid = h / 2;

        var figure = new PathFigure { StartPoint = new Point(0, mid), IsClosed = true };
        foreach (var p in new[]
        {
            new Point(head, 0), new Point(head, mid - thick),
            new Point(w - head, mid - thick), new Point(w - head, 0),
            new Point(w, mid),
            new Point(w - head, h), new Point(w - head, mid + thick),
            new Point(head, mid + thick), new Point(head, h)
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
        Offset,
        Tilt
    }

    private sealed class Handle(Shape visual, SplitPlaneGizmo.HandleKind kind, int sign, Vector3 spin)
    {
        public Shape Visual { get; } = visual;
        public HandleKind Kind { get; } = kind;
        public int Sign { get; } = sign;

        /// <summary>World axis this ring turns the plane about. Unused for the offset arrows.</summary>
        public Vector3 Spin { get; } = spin;
    }
}
