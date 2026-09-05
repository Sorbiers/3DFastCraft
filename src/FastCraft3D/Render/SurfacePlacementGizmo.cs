using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using FastCraft3D.Geometry.Engraving;
using Vector = System.Windows.Vector;

namespace FastCraft3D.Render;

/// <summary>Which handles a tool wants. Not every tool can use all of them.</summary>
[Flags]
public enum PlacementHandles
{
    Move = 1,

    Turn = 2,

    /// <summary>The dashed outline showing how much of the surface is covered.</summary>
    Box = 4,

    All = Move | Turn | Box
}

/// <summary>
/// Handles for placing something on a surface: a grip that slides it about, a knob that turns
/// it, and a dashed box showing what it covers.
///
/// Everything here is expressed in the flat layout of whatever is being placed and asked of the
/// <see cref="IPlacementSurface"/>, never of a face. That is what lets the same two handles drag
/// a word across a wall and around a barrel: the gizmo asks where a layout point ends up and how
/// far one millimetre of layout carries on screen, and the surface answers. Nothing in this file
/// knows which projection is in use, or whether it is lettering or a pattern being placed - so
/// engraving lines its brickwork up with the very same grip.
/// </summary>
public sealed class SurfacePlacementGizmo
{
    /// <summary>How far the handles float off the surface, so they are not buried in it.</summary>
    private const float LiftMm = 0.4f;

    /// <summary>Layout distance used to measure how far a millimetre carries on screen.</summary>
    private const float ReferenceMm = 4f;

    /// <summary>
    /// What the two handles answer to. Matched by tag rather than by object identity so that a
    /// drag can be started from a test without reaching inside for the shapes themselves.
    /// </summary>
    public const string MoveTag = "lettering-move";

    public const string TurnTag = "lettering-turn";

    private const double GripRadius = 9.0;
    private const double KnobRadius = 7.0;

    /// <summary>How far the knob sits from the grip at the very least, so both stay grabbable.</summary>
    private const double MinimumKnobPixels = 34.0;
    private const double RotationSnapDegrees = 15.0;

    private static readonly Color GripColour = Color.FromRgb(0x3B, 0x9C, 0xF0);
    private static readonly Color KnobColour = Color.FromRgb(0xF0, 0x9A, 0x2B);
    private static readonly Color BoxColour = Color.FromRgb(0x2E, 0x6E, 0xB0);

    private readonly Canvas layer;
    private readonly IScreenProjector projector;

    private readonly Path box = new()
    {
        Stroke = new SolidColorBrush(BoxColour),
        StrokeThickness = 1.4,
        StrokeDashArray = new DoubleCollection { 4, 3 },
        Fill = null,
        IsHitTestVisible = false
    };

    private readonly Ellipse grip = new()
    {
        Width = GripRadius * 2,
        Height = GripRadius * 2,
        Fill = new SolidColorBrush(GripColour),
        Stroke = Brushes.White,
        StrokeThickness = 1.6,
        Cursor = Cursors.SizeAll,
        Tag = MoveTag,
        ToolTip = "Drag to move the lettering over the object"
    };

    private readonly Ellipse knob = new()
    {
        Width = KnobRadius * 2,
        Height = KnobRadius * 2,
        Fill = new SolidColorBrush(KnobColour),
        Stroke = Brushes.White,
        StrokeThickness = 1.6,
        Cursor = Cursors.Hand,
        Tag = TurnTag,
        ToolTip = "Drag to turn the lettering"
    };

    private readonly Line stem = new()
    {
        Stroke = new SolidColorBrush(KnobColour),
        StrokeThickness = 1.6,
        IsHitTestVisible = false
    };

    private IPlacementSurface? surface;
    private SurfacePlacement placement = SurfacePlacement.Middle;
    private Vector2 extent;
    private PlacementHandles wanted = PlacementHandles.All;
    private bool active;

    private bool movingDrag;
    private bool turningDrag;
    private Point dragStart;
    private SurfacePlacement dragStartPlacement;

    public SurfacePlacementGizmo(Canvas layer, IScreenProjector projector)
    {
        this.layer = layer;
        this.projector = projector;

        layer.Children.Add(box);
        layer.Children.Add(stem);
        layer.Children.Add(grip);
        layer.Children.Add(knob);
        layer.Visibility = Visibility.Collapsed;
    }

    /// <summary>Raised while dragging, with where the lettering has got to.</summary>
    public event Action<SurfacePlacement>? Changed;

    /// <summary>Live readout for the status bar.</summary>
    public event Action<string>? Feedback;

    public bool IsDragging => movingDrag || turningDrag;
    public bool SnapRotation { get; set; } = true;

    /// <summary>What the readout calls the thing being placed.</summary>
    public string Noun { get; set; } = "Lettering";

    /// <summary>Shows the handles for whatever is being placed, or hides them when nothing is.</summary>
    public void Show(
        bool on, IPlacementSurface? map, SurfacePlacement current, Vector2 halfSize,
        PlacementHandles handles = PlacementHandles.All)
    {
        surface = map;
        placement = current;
        extent = halfSize;
        wanted = handles;
        active = on && map is not null && halfSize.X > 0 && halfSize.Y > 0;

        Reposition();
    }

    /// <summary>Re-places the handles for the current camera.</summary>
    public void Reposition()
    {
        if (!active || surface is null || !projector.IsReady)
        {
            layer.Visibility = Visibility.Collapsed;
            return;
        }

        if (!projector.TryProject(WorldAt(placement.OffsetMm), out Point centre))
        {
            layer.Visibility = Visibility.Collapsed;
            return;
        }

        var outline = wanted.HasFlag(PlacementHandles.Box) ? BoxOutline() : null;
        box.Visibility = outline is null ? Visibility.Collapsed : Visibility.Visible;
        if (outline is not null) box.Data = Closed(outline);

        layer.Visibility = Visibility.Visible;

        grip.Visibility = Show(PlacementHandles.Move);
        knob.Visibility = Show(PlacementHandles.Turn);
        stem.Visibility = knob.Visibility;

        Place(grip, centre, GripRadius);

        if (!wanted.HasFlag(PlacementHandles.Turn)) return;

        Point turn = KnobPoint(centre);

        stem.X1 = centre.X;
        stem.Y1 = centre.Y;
        stem.X2 = turn.X;
        stem.Y2 = turn.Y;

        Place(knob, turn, KnobRadius);

        Visibility Show(PlacementHandles handle) =>
            wanted.HasFlag(handle) ? Visibility.Visible : Visibility.Collapsed;
    }

    private static void Place(FrameworkElement element, Point at, double radius)
    {
        Canvas.SetLeft(element, at.X - radius);
        Canvas.SetTop(element, at.Y - radius);
    }

    /// <summary>
    /// The dashed box round the lettering, sampled along each edge rather than corner to corner:
    /// on anything curved a straight screen line between two corners cuts through the object.
    /// </summary>
    private List<Point>? BoxOutline()
    {
        const int perEdge = 8;
        var points = new List<Point>(perEdge * 4);

        var corners = new[]
        {
            new Vector2(-extent.X, -extent.Y), new Vector2(extent.X, -extent.Y),
            new Vector2(extent.X, extent.Y), new Vector2(-extent.X, extent.Y)
        };

        for (int side = 0; side < 4; side++)
        {
            var from = corners[side];
            var to = corners[(side + 1) % 4];

            for (int step = 0; step < perEdge; step++)
            {
                var at = Vector2.Lerp(from, to, step / (float)perEdge);
                if (projector.TryProject(WorldAt(placement.Apply(at)), out Point p)) points.Add(p);
            }
        }

        return points.Count >= 4 ? points : null;
    }

    private static PathGeometry Closed(List<Point> points)
    {
        var figure = new PathFigure { StartPoint = points[0], IsClosed = true };
        for (int i = 1; i < points.Count; i++)
            figure.Segments.Add(new LineSegment(points[i], true));

        return new PathGeometry { Figures = { figure } };
    }

    /// <summary>
    /// The turning knob: up from the grip, in the direction the lettering itself calls up, and
    /// far enough out to be grabbed.
    ///
    /// How far out is decided in pixels rather than millimetres. It was millimetres first, held
    /// a little above the top of the box, and small lettering on a face seen at an angle put the
    /// knob underneath the grip - so the two handles sat on top of each other exactly when the
    /// text was too small to place by eye.
    /// </summary>
    private Point KnobPoint(Point centre)
    {
        var up = Screen(centre, placement.Apply(new Vector2(0, MathF.Max(extent.Y, 1f))));

        // The lettering is edge-on; there is no up to speak of, so the knob goes up the screen.
        if (up.Length < 1e-3) return new Point(centre.X, centre.Y - MinimumKnobPixels);

        double along = Math.Max(up.Length + GripRadius + KnobRadius, MinimumKnobPixels);
        return centre + up / up.Length * along;
    }

    /// <summary>Where a layout point lands relative to a screen point, or nothing if it cannot.</summary>
    private Vector Screen(Point from, Vector2 uv) =>
        projector.TryProject(WorldAt(uv), out Point p)
            ? new Vector(p.X - from.X, p.Y - from.Y)
            : new Vector(0, 0);

    private Vector3 WorldAt(Vector2 uv) => surface!.At(uv, LiftMm);

    // --- Dragging ---------------------------------------------------------------------

    public bool TryBeginDrag(Point screen, object? hitElement)
    {
        if (!active) return false;

        string? tag = (hitElement as FrameworkElement)?.Tag as string;

        movingDrag = tag == MoveTag && wanted.HasFlag(PlacementHandles.Move);
        turningDrag = tag == TurnTag && wanted.HasFlag(PlacementHandles.Turn);
        if (!movingDrag && !turningDrag) return false;

        dragStart = screen;
        dragStartPlacement = placement;
        return true;
    }

    public void ContinueDrag(Point screen)
    {
        if (surface is null) return;

        if (movingDrag) DragMove(screen);
        else if (turningDrag) DragTurn(screen);
        else return;

        Changed?.Invoke(placement);
        Reposition();
    }

    public void EndDrag()
    {
        movingDrag = false;
        turningDrag = false;
    }

    private void DragMove(Point screen)
    {
        var origin = dragStartPlacement.OffsetMm;
        if (!projector.TryProject(WorldAt(origin), out Point anchor)) return;

        if (!ScreenStep(origin, new Vector2(ReferenceMm, 0), anchor, out Vector across)) return;
        if (!ScreenStep(origin, new Vector2(0, ReferenceMm), anchor, out Vector up)) return;

        var delta = new Vector(screen.X - dragStart.X, screen.Y - dragStart.Y);
        if (GizmoMath.AcrossSurface(delta, across, up) is not { } moved)
        {
            Feedback?.Invoke($"Turn the view - the {Noun.ToLowerInvariant()} is edge-on");
            return;
        }

        placement = dragStartPlacement with
        {
            OffsetMm = origin + new Vector2((float)moved.X, (float)moved.Y)
        };

        Feedback?.Invoke(
            $"{Noun} {placement.OffsetMm.X:0.#}, {placement.OffsetMm.Y:0.#} mm from the middle");
    }

    /// <summary>How far one step of layout carries on screen, from the anchor.</summary>
    private bool ScreenStep(Vector2 origin, Vector2 step, Point anchor, out Vector perMm)
    {
        perMm = default;
        if (!projector.TryProject(WorldAt(origin + step), out Point ahead)) return false;

        perMm = new Vector(ahead.X - anchor.X, ahead.Y - anchor.Y) / step.Length();
        return true;
    }

    private void DragTurn(Point screen)
    {
        var origin = dragStartPlacement.OffsetMm;
        if (!projector.TryProject(WorldAt(origin), out Point pivot)) return;

        double degrees = GizmoMath.RotationDegrees(
            pivot, dragStart, screen, Facing(origin, pivot), SnapRotation, RotationSnapDegrees);

        placement = dragStartPlacement with
        {
            AngleDegrees = GizmoMath.NormaliseDegrees(dragStartPlacement.AngleDegrees + (float)degrees)
        };

        Feedback?.Invoke($"{Noun} turned to {placement.AngleDegrees:0.#} deg");
    }

    /// <summary>
    /// Which way the pointer has to sweep to turn the lettering forwards. Read off the surface
    /// rather than off a fixed axis, because on a barrel the answer changes with where the
    /// lettering has been dragged to.
    /// </summary>
    private float Facing(Vector2 origin, Point pivot)
    {
        if (!ScreenStep(origin, new Vector2(ReferenceMm, 0), pivot, out Vector across)) return 1f;
        if (!ScreenStep(origin, new Vector2(0, ReferenceMm), pivot, out Vector up)) return 1f;

        // Screen Y runs down, so a layout frame seen from the front comes out clockwise here.
        return across.X * up.Y - across.Y * up.X <= 0 ? 1f : -1f;
    }
}
