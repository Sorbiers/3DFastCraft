using System.Globalization;
using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace FastCraft3D.Render;

/// <summary>
/// Draws the measuring tape: the marked points, the lines between them, and the distances.
///
/// Entirely in two dimensions, over the viewport rather than in it. A tape measure that
/// disappeared behind the model would be useless - the whole point is to read it - so the line is
/// projected and drawn on top, and is never occluded.
/// </summary>
public sealed class MeasureOverlay(Canvas canvas, IScreenProjector projector)
{
    private static readonly Color Ink = Color.FromRgb(0x1A, 0x73, 0xE8);

    /// <summary>The right triangle's legs and angles, in a colour of their own so they are not read as the tape.</summary>
    private static readonly Color LegInk = Color.FromRgb(0xE0, 0x6C, 0x1B);

    private IReadOnlyList<Vector3> points = [];
    private IReadOnlyList<IReadOnlyList<Vector3>> kept = [];
    private bool legs, multipoint, finished, editing;

    /// <summary>
    /// Whether the points can be taken hold of - while Shift is held. Otherwise a click on a point
    /// already down puts a new one there, as a click anywhere else does: measuring from the end
    /// of one measurement is common, and it kept grabbing the old point instead.
    /// </summary>
    public bool Editing
    {
        get => editing;
        set
        {
            if (editing == value) return;
            editing = value;
            Reposition();
        }
    }
    private Vector3? hover;
    private bool stretching;

    /// <summary>How near the cursor has to be to a point to take hold of it, in pixels.</summary>
    private const double Reach = 11;

    /// <summary>
    /// The point under <paramref name="screen"/>, with every other point standing on the same
    /// spot: of the measurement being taken (tape -1) or of those kept from before (their place
    /// in the list). Empty for none.
    ///
    /// All of them, not the newest. A measurement is often begun where the last one ended, and
    /// taking hold of that corner moved only the new one and left the old one where it was - it
    /// looked as though the old one could not be moved at all. A corner two measurements share
    /// moves for both.
    /// </summary>
    public List<(int Tape, int Index)> PointsAt(Point screen)
    {
        var found = new List<(int Tape, int Index)>();
        if (!projector.IsReady) return found;

        var all = new List<(int Tape, int Index, Vector3 At)>();
        for (int i = 0; i < points.Count; i++) all.Add((-1, i, points[i]));
        for (int t = 0; t < kept.Count; t++)
            for (int i = 0; i < kept[t].Count; i++) all.Add((t, i, kept[t][i]));

        Vector3? grabbed = null;
        double nearest = Reach;
        foreach (var (_, _, at) in all)
        {
            if (!projector.TryProject(at, out var p)) continue;
            double away = (screen - p).Length;
            if (away > nearest) continue;
            nearest = away;
            grabbed = at;
        }

        if (grabbed is not { } spot) return found;

        foreach (var (tape, index, at) in all)
            if (Vector3.DistanceSquared(at, spot) < 1e-8f) found.Add((tape, index));

        return found;
    }

    private static bool Near(Point cursor, Point marker) =>
        (cursor - marker).Length <= Reach;

    public void Show(IReadOnlyList<Vector3> marked, bool withLegs, bool several, bool done,
                     IReadOnlyList<IReadOnlyList<Vector3>>? earlier = null)
    {
        points = marked.ToList();
        kept = earlier?.ToList() ?? [];
        legs = withLegs;
        multipoint = several;
        finished = done;
        stretching = hover is not null && points.Count > 0 && !finished;
        Reposition();
    }

    /// <summary>
    /// Where the next point would go, under the cursor: a ring there, and - while a point is
    /// waiting for its partner - the tape stretched to it with its reading, so the second click
    /// is aimed rather than guessed.
    /// </summary>
    public void Hover(Vector3? at)
    {
        hover = at;
        stretching = at is not null && points.Count > 0 && !finished;
        Reposition();
    }

    public void Clear() => Show([], legs, multipoint, false, []);

    /// <summary>
    /// Redraws at the current camera. Cheap enough to call on every frame the camera moved,
    /// which is what keeps the tape stuck to the model rather than to the screen.
    /// </summary>
    public void Reposition()
    {
        canvas.Children.Clear();
        if (!projector.IsReady) return;

        foreach (var tape in kept) Earlier(tape);

        var screen = new List<Point>(points.Count);
        foreach (var p in points)
        {
            if (!projector.TryProject(p, out var at)) return;
            screen.Add(at);
        }

        float total = 0;
        for (int i = 1; i < points.Count; i++)
        {
            Stretch(points[i - 1], points[i], screen[i - 1], screen[i], 1.0);
            total += Vector3.Distance(points[i - 1], points[i]);
        }

        if (stretching && hover is { } next && points.Count > 0 && projector.TryProject(next, out var pn))
        {
            Stretch(points[^1], next, screen[^1], pn, 0.6);
            total += Vector3.Distance(points[^1], next);
        }

        foreach (var at in screen) AddMarker(at);

        if (multipoint && points.Count + (stretching ? 1 : 0) > 2)
        {
            var end = stretching && hover is { } h && projector.TryProject(h, out var ph) ? ph : screen[^1];
            AddLabel(new Point(end.X, end.Y + 34), $"Total {Millimetres(total)}", Ink, 1.0);
        }

        if (hover is { } ring && projector.TryProject(ring, out var pr)) AddRing(pr);
    }

    /// <summary>
    /// A measurement kept from before: the same tape and readings, a little fainter, its points
    /// taken hold of with Shift as the current one's are.
    /// </summary>
    private void Earlier(IReadOnlyList<Vector3> tape)
    {
        var at = new List<Point>(tape.Count);
        foreach (var p in tape)
        {
            if (!projector.TryProject(p, out var on)) return;
            at.Add(on);
        }

        float total = 0;
        for (int i = 1; i < tape.Count; i++)
        {
            Stretch(tape[i - 1], tape[i], at[i - 1], at[i], 0.75);
            total += Vector3.Distance(tape[i - 1], tape[i]);
        }

        foreach (var on in at)
        {
            if (editing) Add(on, Reach * 2, Brushes.Transparent, null, true);
            Add(on, 7, Brushes.White, new SolidColorBrush(Ink), editing);
        }

        if (tape.Count > 2) AddLabel(new Point(at[^1].X, at[^1].Y + 34), $"Total {Millimetres(total)}", Ink, 0.75);
    }

    /// <summary>One stretch of tape with its reading, and its triangle when that is asked for.</summary>
    private void Stretch(Vector3 a, Vector3 b, Point pa, Point pb, double opacity)
    {
        AddLine(pa, pb, Ink, 1.8, opacity);

        if (legs) AddLegs(a, b, opacity);

        AddLabel(new Point((pa.X + pb.X) / 2, (pa.Y + pb.Y) / 2), Millimetres(Vector3.Distance(a, b)), Ink, opacity);
    }

    /// <summary>
    /// The right triangle the stretch makes with the plate: along from the lower point to under
    /// the higher one, then straight up to it, and the two angles where the tape meets them.
    /// Left out when the stretch is level or upright, where there is no triangle to draw.
    /// </summary>
    private void AddLegs(Vector3 a, Vector3 b, double opacity)
    {
        var (low, high) = a.Z <= b.Z ? (a, b) : (b, a);
        var corner = new Vector3(high.X, high.Y, low.Z);

        float along = Vector3.Distance(low, corner);
        float up = high.Z - low.Z;
        if (along < 0.01f || up < 0.01f) return;

        if (!projector.TryProject(low, out var pl) || !projector.TryProject(high, out var ph)
            || !projector.TryProject(corner, out var pc))
            return;

        AddLine(pl, pc, LegInk, 1.4, opacity);
        AddLine(pc, ph, LegInk, 1.4, opacity);

        AddLabel(new Point((pl.X + pc.X) / 2, (pl.Y + pc.Y) / 2 + 30), Millimetres(along), LegInk, opacity);
        AddLabel(new Point((pc.X + ph.X) / 2 + 40, (pc.Y + ph.Y) / 2 + 12), Millimetres(up), LegInk, opacity);

        float slope = MathF.Atan2(up, along) * 180f / MathF.PI;
        AddLabel(new Point(pl.X, pl.Y + 30), $"{slope:0.#}°", LegInk, opacity);
        AddLabel(new Point(ph.X, ph.Y - 6), $"{90f - slope:0.#}°", LegInk, opacity);
    }

    private static string Millimetres(float mm) => mm.ToString("0.## mm", CultureInfo.CurrentCulture);

    private void AddLine(Point from, Point to, Color colour, double thickness, double opacity)
    {
        canvas.Children.Add(new Line
        {
            X1 = from.X, Y1 = from.Y, X2 = to.X, Y2 = to.Y,
            Stroke = new SolidColorBrush(colour),
            StrokeThickness = thickness,
            StrokeDashArray = [4, 3],
            Opacity = opacity,
            IsHitTestVisible = false
        });
    }

    private void AddMarker(Point at)
    {
        // A transparent disc behind the visible one, so there is something to take hold of. The
        // marker is drawn 9 pixels across because that is the right size to look at; a 9-pixel
        // target is not the right size to hit, and the first attempt at dragging an end missed
        // by four pixels and looked like the drag was not implemented at all.
        Add(at, Reach * 2, Brushes.Transparent, null, editing);
        Add(at, 9, Brushes.White, new SolidColorBrush(Ink), editing);
    }

    /// <summary>Where a click would land, after snapping: not something to take hold of.</summary>
    private void AddRing(Point at) => Add(at, 13, Brushes.Transparent, new SolidColorBrush(Ink), false);

    private void Add(Point at, double size, Brush fill, Brush? stroke, bool hold)
    {
        var marker = new Ellipse
        {
            Width = size,
            Height = size,
            Fill = fill,
            Stroke = stroke,
            StrokeThickness = 2,
            Cursor = hold ? Cursors.SizeAll : null,
            IsHitTestVisible = hold
        };

        Canvas.SetLeft(marker, at.X - size / 2);
        Canvas.SetTop(marker, at.Y - size / 2);
        canvas.Children.Add(marker);
    }

    /// <summary>
    /// A reading, on a plate rather than bare text: it sits over the model, and dark text on a
    /// dark part is unreadable at exactly the moment it is wanted.
    /// </summary>
    private void AddLabel(Point at, string text, Color colour, double opacity)
    {
        var plate = new Border
        {
            Background = new SolidColorBrush(colour),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(7, 3, 7, 3),
            Opacity = Math.Max(opacity, 0.85),
            IsHitTestVisible = false,
            Child = new TextBlock
            {
                Text = text,
                Foreground = Brushes.White,
                FontSize = 12.5,
                FontWeight = FontWeights.SemiBold,
                IsHitTestVisible = false
            }
        };

        // Measured before placing, so the plate is centred on the line rather than hanging off it.
        plate.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(plate, at.X - plate.DesiredSize.Width / 2);
        Canvas.SetTop(plate, at.Y - plate.DesiredSize.Height - 10);
        canvas.Children.Add(plate);
    }
}
