using System.Globalization;
using System.Numerics;

namespace FastCraft3D.Geometry.Sketches;

public enum SketchTool
{
    /// <summary>Straight lines, point after point, closed back onto the first.</summary>
    Line,

    /// <summary>Two opposite corners.</summary>
    Rectangle,

    /// <summary>The middle, then a point on the edge.</summary>
    Circle,

    /// <summary>An arc on from the last point: where it ends, then a point it passes through. Joins lines into one outline.</summary>
    Arc,

    /// <summary>A smooth closed curve through the points clicked.</summary>
    Curve,

    /// <summary>Drawn with the pointer held down, closed when it is let go.</summary>
    Freehand
}

/// <summary>
/// Outlines drawn on the plate, to be extruded or turned into solids.
///
/// Only closed outlines are kept, and only ones that make sense as a solid's section: an outline
/// may not cross itself, and two may not cross each other. One inside another is a hole in it, as
/// the middle of an O is - decided by containment, as lettering's are, not by which way round it
/// was drawn.
///
/// Measured in millimetres on the plate, X to the right and Y away. The outline in progress is kept
/// apart from the finished ones until it closes.
/// </summary>
public sealed class Sketch
{
    /// <summary>How short the straight pieces standing for a curved edge are.</summary>
    private const float Piece = 0.5f;

    /// <summary>How far the pointer moves before a freehand stroke takes another point.</summary>
    private const float StrokeStep = 0.25f;

    /// <summary>
    /// An edge shorter than this was laid out rather than clicked: an arc, a circle and a curve
    /// are drawn as pieces <see cref="Piece"/> long. It is what tells a corner that can be
    /// dragged from a point that is only there to make a curve look round.
    /// </summary>
    private const float Clicked = 4 * Piece;

    /// <summary>How far a freehand outline may be straightened from the pointer's path: its jitter, not its shape.</summary>
    private const float StrokeTolerance = 0.2f;

    /// <summary>What the points of the outline being drawn are.</summary>
    private enum ChainKind
    {
        /// <summary>The edge itself: lines, and arcs laid out as short straight pieces.</summary>
        Lines,

        /// <summary>The points a curve passes through, laid out when it closes.</summary>
        Curve,

        /// <summary>The pointer's path.</summary>
        Stroke
    }

    public List<List<Vector2>> Loops { get; } = [];

    /// <summary>The outline being drawn now: see <see cref="ChainKind"/> for what its points are.</summary>
    public List<Vector2> Chain { get; } = [];

    /// <summary>Where <see cref="Chain"/> stood before each click, so Undo takes an arc back whole rather than a piece of it.</summary>
    private readonly List<int> steps = [];

    /// <summary>Where <see cref="Loops"/> stood before each outline was added, so Undo takes a loaded drawing back whole.</summary>
    private readonly List<int> loopMarks = [];

    private ChainKind kind;

    /// <summary>Where an arc being drawn ends, waiting for the click that bends it.</summary>
    public Vector2? ArcEnd { get; private set; }

    /// <summary>The arc waiting to be bent ends on the first point, and closes the outline.</summary>
    private bool arcCloses;

    public bool IsEmpty => Loops.Count == 0 && Chain.Count == 0;

    /// <summary>Whether an outline is half drawn.</summary>
    public bool IsDrawing => Chain.Count > 0;

    /// <summary>Whether a freehand stroke is under way.</summary>
    public bool IsDrawingStroke => kind == ChainKind.Stroke && Chain.Count > 0;

    /// <summary>The finished outlines, each with the ones inside it as its holes.</summary>
    public List<(List<Vector2> Outline, List<List<Vector2>> Holes)> Shapes() => Polygon2.Nest(Loops);

    /// <summary>The points clicked, to be marked: the ends of lines and arcs, not the pieces of an arc or a stroke.</summary>
    public List<Vector2> Corners
    {
        get
        {
            var corners = new List<Vector2>();
            if (kind == ChainKind.Stroke)
            {
                if (Chain.Count > 0) corners.Add(Chain[0]);
            }
            else
            {
                for (int i = 0; i < steps.Count; i++)
                {
                    int end = i + 1 < steps.Count ? steps[i + 1] : Chain.Count;
                    if (end > 0) corners.Add(Chain[end - 1]);
                }
            }

            if (ArcEnd is { } arcEnd) corners.Add(arcEnd);
            return corners;
        }
    }

    /// <summary>
    /// The points that can be dragged to change the shape: which outline, which point of it, and
    /// where it is. The outline still being drawn is outline -1.
    ///
    /// A point is offered only where the edges either side of it are long enough to have been
    /// clicked. The pieces an arc, a circle or a curve is laid out as are a fraction of a
    /// millimetre long, and dragging one of those would dent the curve rather than edit it.
    /// </summary>
    public List<(int Loop, int Index, Vector2 At)> Handles()
    {
        var handles = new List<(int, int, Vector2)>();

        // A stroke is the pointer's own path, and every point of it was laid out, not placed.
        if (kind != ChainKind.Stroke)
        {
            for (int i = 0; i < Chain.Count; i++)
            {
                bool before = i == 0 || Vector2.Distance(Chain[i - 1], Chain[i]) >= Clicked;
                bool after = i == Chain.Count - 1 || Vector2.Distance(Chain[i], Chain[i + 1]) >= Clicked;

                if (before && after) handles.Add((-1, i, Chain[i]));
            }
        }

        for (int loop = 0; loop < Loops.Count; loop++)
        {
            var points = Loops[loop];

            for (int i = 0; i < points.Count; i++)
            {
                var before = points[(i - 1 + points.Count) % points.Count];
                var after = points[(i + 1) % points.Count];

                if (Vector2.Distance(before, points[i]) >= Clicked && Vector2.Distance(points[i], after) >= Clicked)
                    handles.Add((loop, i, points[i]));
            }
        }

        return handles;
    }

    /// <summary>Puts a point somewhere else. Nothing is checked here - see <see cref="SettleHandle"/>.</summary>
    public void MoveHandle(int loop, int index, Vector2 to)
    {
        var points = Points(loop);
        if (points is null || index < 0 || index >= points.Count) return;

        points[index] = to;
    }

    /// <summary>
    /// The end of a drag. A finished outline is checked as it would have been on closing, and put
    /// back where it was if the move has made it cross itself or another.
    ///
    /// Checked here rather than on every step of the drag: it walks every edge against every
    /// other, and an outline loaded from a drawing has hundreds. The outline being drawn is not
    /// checked at all, since closing it will do that anyway.
    /// </summary>
    public string SettleHandle(int loop, int index, Vector2 from)
    {
        if (loop < 0 || loop >= Loops.Count) return "";

        var points = Loops[loop];
        if (index < 0 || index >= points.Count) return "";

        if (!IsSimple(points))
        {
            points[index] = from;
            return "Put back: there, the outline would cross itself.";
        }

        for (int other = 0; other < Loops.Count; other++)
        {
            if (other == loop || !Crosses(points, Loops[other])) continue;

            points[index] = from;
            return "Put back: there, the outline would cross another.";
        }

        return $"Moved the point to {F(points[index].X)}, {F(points[index].Y)} mm.";
    }

    /// <summary>
    /// Finishes what is being drawn where it stands: closed into an outline if it has the points
    /// for one, dropped if it has not. What the right button does.
    /// </summary>
    public string EndLine()
    {
        if (kind == ChainKind.Stroke) return EndStroke();
        if (Chain.Count == 0) return "";
        if (Chain.Count >= 3) return Close();

        DropChain();
        return "Dropped the line - an outline needs three points or more.";
    }

    private List<Vector2>? Points(int loop) =>
        loop < 0 ? Chain : loop < Loops.Count ? Loops[loop] : null;

    /// <summary>
    /// Places a point with a tool, and says what came of it. <paramref name="onFirst"/> is whether
    /// the point was placed on the first point of the outline being drawn, which closes it.
    /// </summary>
    public string Place(Vector2 point, SketchTool tool, bool onFirst)
    {
        switch (tool)
        {
            case SketchTool.Line:
                ArcEnd = null;
                if (onFirst && Chain.Count >= 3) return Close();
                if (onFirst && Chain.Count > 0) return "An outline needs three points or more before it closes.";
                if (Chain.Count > 0 && Vector2.Distance(Chain[^1], point) < 1e-4f) return "That point is already there.";
                Begin(ChainKind.Lines);
                steps.Add(Chain.Count);
                Chain.Add(point);
                return Chain.Count == 1
                    ? "Click the next point. Click the first again, press Enter or right-click to close the outline. Drag a point to move it."
                    : $"{steps.Count} points. Click the first point, press Enter or right-click to close it.";

            case SketchTool.Arc:
                return PlaceArc(point, onFirst);

            case SketchTool.Curve:
                if (onFirst && Chain.Count >= 3) return Close();
                if (onFirst && Chain.Count > 0) return "A curve needs three points or more before it closes.";
                if (Chain.Count > 0 && Vector2.Distance(Chain[^1], point) < 0.05f) return "That point is already there.";
                Begin(ChainKind.Curve);
                steps.Add(Chain.Count);
                Chain.Add(point);
                return Chain.Count == 1
                    ? "Click the points the curve passes through. Click the first again, press Enter or right-click to close it."
                    : $"{Chain.Count} points on the curve. Click the first, press Enter or right-click to close it.";

            case SketchTool.Freehand:
                return "Press and drag to draw round the shape; let go to close it.";

            case SketchTool.Rectangle:
                if (Chain.Count == 0)
                {
                    Begin(ChainKind.Lines);
                    steps.Add(0);
                    Chain.Add(point);
                    return "Click the opposite corner.";
                }
                else
                {
                    var loop = Rectangle(Chain[0], point);
                    if (loop is null) return "A rectangle needs some width and some depth.";
                    DropChain();
                    return AddLoop(loop, "rectangle");
                }

            default:
                if (Chain.Count == 0)
                {
                    Begin(ChainKind.Lines);
                    steps.Add(0);
                    Chain.Add(point);
                    return "Click a point on the edge.";
                }
                else
                {
                    float radius = Vector2.Distance(Chain[0], point);
                    if (radius < 0.05f) return "A circle needs a radius.";
                    var loop = Circle(Chain[0], radius);
                    DropChain();
                    return AddLoop(loop, "circle");
                }
        }
    }

    /// <summary>
    /// The start, then where the arc ends, then a point it passes through. Ending and bending are two
    /// clicks rather than one so the arc can be seen bending with the pointer before it is set.
    /// </summary>
    private string PlaceArc(Vector2 point, bool onFirst)
    {
        if (Chain.Count == 0)
        {
            Begin(ChainKind.Lines);
            steps.Add(0);
            Chain.Add(point);
            return "Click where the arc ends.";
        }

        if (ArcEnd is not { } end)
        {
            if (onFirst && Chain.Count < 2) return "An arc cannot end where it starts.";
            if (!onFirst && Vector2.Distance(Chain[^1], point) < 0.05f) return "An arc cannot end where it starts.";

            ArcEnd = onFirst ? Chain[0] : point;
            arcCloses = onFirst;
            return "Move to bend the arc, and click to set it.";
        }

        var arc = Arc(Chain[^1], point, end, out float radius);
        bool closes = arcCloses;
        ArcEnd = null;
        arcCloses = false;

        steps.Add(Chain.Count);
        if (closes) arc.RemoveAt(arc.Count - 1);
        Chain.AddRange(arc);

        if (closes) return Close();
        return float.IsFinite(radius)
            ? $"An arc of radius {F(radius)} mm. Click where the next arc ends, or change to Line to go on straight."
            : "Straight, since the point was in line with the ends. Click where the next arc ends.";
    }

    /// <summary>Closes the outline being drawn, if it can be one.</summary>
    public string Close()
    {
        ArcEnd = null;
        arcCloses = false;
        if (kind == ChainKind.Stroke) return EndStroke();

        if (Chain.Count < 3) return kind == ChainKind.Curve ? "A curve needs three points or more." : "An outline needs three points or more.";

        var loop = kind == ChainKind.Curve ? CurveThrough(Chain) : new List<Vector2>(Chain);
        string result = AddLoop(loop, kind == ChainKind.Curve ? "curve" : "outline");
        if (Loops.Count > 0 && ReferenceEquals(Loops[^1], loop)) DropChain();
        return result;
    }

    /// <summary>Takes back the last click, or the last outline when nothing is being drawn.</summary>
    public string Undo()
    {
        if (ArcEnd is not null)
        {
            ArcEnd = null;
            arcCloses = false;
            return "Took back where the arc ends.";
        }

        if (Chain.Count > 0)
        {
            if (kind == ChainKind.Stroke || steps.Count == 0)
            {
                DropChain();
                return "Dropped the outline being drawn.";
            }

            int back = steps[^1];
            steps.RemoveAt(steps.Count - 1);
            bool arc = Chain.Count - back > 1;
            Chain.RemoveRange(back, Chain.Count - back);
            return arc ? "Took back the last arc." : "Took back the last point.";
        }

        if (Loops.Count == 0) return "Nothing to take back.";

        int from = loopMarks.Count > 0 && loopMarks[^1] < Loops.Count ? loopMarks[^1] : Loops.Count - 1;
        if (loopMarks.Count > 0) loopMarks.RemoveAt(loopMarks.Count - 1);
        int taken = Loops.Count - from;
        Loops.RemoveRange(from, taken);
        return taken == 1 ? "Took back the last outline." : $"Took back the {taken} outlines loaded together.";
    }

    public void Clear()
    {
        Loops.Clear();
        loopMarks.Clear();
        DropChain();
    }

    /// <summary>
    /// Outlines from elsewhere - a drawing - added as one step. Each is straightened within the
    /// tolerance, since a drawing's curves come flattened far finer than a printer draws, and then
    /// checked as a drawn one is: those that cross themselves or another outline are left out.
    /// </summary>
    public (int Added, int LeftOut) AddOutlines(IEnumerable<IReadOnlyList<Vector2>> loops, float tolerance)
    {
        DropChain();
        int mark = Loops.Count, leftOut = 0;

        foreach (var given in loops)
        {
            var loop = SimplifyLoop(given, tolerance);
            if (!IsSimple(loop) || Loops.Any(other => Crosses(loop, other)))
            {
                leftOut++;
                continue;
            }

            Loops.Add(loop);
        }

        if (Loops.Count > mark) loopMarks.Add(mark);
        return (Loops.Count - mark, leftOut);
    }

    /// <summary>Forgets the outline being drawn.</summary>
    public void DropChain()
    {
        Chain.Clear();
        steps.Clear();
        ArcEnd = null;
        arcCloses = false;
        kind = ChainKind.Lines;
    }

    /// <summary>
    /// The tool changes. Lines and arcs join into one outline, so changing between the two carries
    /// on with it; anything else starts afresh. Says whether an outline being drawn was dropped.
    /// </summary>
    public bool Retool(SketchTool from, SketchTool to)
    {
        ArcEnd = null;
        arcCloses = false;
        if (Chain.Count == 0 || from == to) return false;
        if ((from is SketchTool.Line or SketchTool.Arc) && (to is SketchTool.Line or SketchTool.Arc) && kind == ChainKind.Lines) return false;

        DropChain();
        return true;
    }

    private void Begin(ChainKind what)
    {
        if (Chain.Count > 0) return;
        steps.Clear();
        kind = what;
    }

    // --- Freehand ----------------------------------------------------------------------

    public void BeginStroke(Vector2 point)
    {
        DropChain();
        kind = ChainKind.Stroke;
        Chain.Add(point);
    }

    public void ExtendStroke(Vector2 point)
    {
        if (IsDrawingStroke && Vector2.Distance(Chain[^1], point) >= StrokeStep) Chain.Add(point);
    }

    /// <summary>Closes a freehand stroke into an outline, straightened just enough to lose the pointer's jitter.</summary>
    public string EndStroke()
    {
        if (!IsDrawingStroke) return "";

        var path = new List<Vector2>(Chain);
        DropChain();

        var min = path.Aggregate(Vector2.Min);
        var max = path.Aggregate(Vector2.Max);
        if (path.Count < 3 || Vector2.Distance(min, max) < 1f) return "Too small to be an outline - press and draw round the shape.";

        var loop = Simplify(TrimOvershoot(path), StrokeTolerance);

        // The closing edge runs from the last point back to the first; one sitting on it adds nothing.
        if (loop.Count > 3 && Vector2.Distance(loop[^1], loop[0]) < StrokeStep) loop.RemoveAt(loop.Count - 1);
        if (loop.Count < 3) return "Too thin to be an outline - draw round the shape.";

        string said = AddLoop(loop, "outline");
        return Loops.Count > 0 && ReferenceEquals(Loops[^1], loop) ? said : said + " Draw it again.";
    }

    /// <summary>
    /// A hand-drawn outline usually runs on past where it began, to be sure of closing. Where its end
    /// crosses its beginning it is cut there, so it closes where the two meet rather than in a little
    /// knot that would make it cross itself.
    /// </summary>
    private static List<Vector2> TrimOvershoot(List<Vector2> path)
    {
        int n = path.Count;
        for (int i = n - 2; i >= n / 2; i--)
            for (int j = 0; j + 1 <= n / 2 && j < i - 1; j++)
                if (Intersection(path[i], path[i + 1], path[j], path[j + 1]) is { } meet)
                    return [meet, .. path.Skip(j + 1).Take(i - j)];

        return path;
    }

    /// <summary>Douglas-Peucker: the fewest of the points that stay within the tolerance of all of them.</summary>
    private static List<Vector2> Simplify(List<Vector2> path, float tolerance)
    {
        if (path.Count < 3) return path;

        var keep = new bool[path.Count];
        keep[0] = keep[^1] = true;
        var spans = new Stack<(int, int)>();
        spans.Push((0, path.Count - 1));

        while (spans.Count > 0)
        {
            var (a, b) = spans.Pop();
            float furthest = 0f;
            int at = -1;
            for (int i = a + 1; i < b; i++)
            {
                float d = DistanceToSegment(path[i], path[a], path[b]);
                if (d > furthest)
                {
                    furthest = d;
                    at = i;
                }
            }

            if (at < 0 || furthest <= tolerance) continue;
            keep[at] = true;
            spans.Push((a, at));
            spans.Push((at, b));
        }

        return path.Where((_, i) => keep[i]).ToList();
    }

    /// <summary>A closed loop simplified, opened at its first point and closed again.</summary>
    private static List<Vector2> SimplifyLoop(IReadOnlyList<Vector2> loop, float tolerance)
    {
        if (loop.Count < 4) return [.. loop];

        var simpler = Simplify([.. loop, loop[0]], tolerance);
        simpler.RemoveAt(simpler.Count - 1);
        return simpler;
    }

    private static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        float length = ab.LengthSquared();
        if (length < 1e-12f) return Vector2.Distance(p, a);
        float t = Math.Clamp(Vector2.Dot(p - a, ab) / length, 0f, 1f);
        return Vector2.Distance(p, a + t * ab);
    }

    /// <summary>Where two segments cross, strictly inside both.</summary>
    private static Vector2? Intersection(Vector2 p, Vector2 q, Vector2 r, Vector2 s)
    {
        var pq = q - p;
        var rs = s - r;
        float denominator = pq.X * rs.Y - pq.Y * rs.X;
        if (MathF.Abs(denominator) < 1e-12f) return null;

        var pr = r - p;
        float t = (pr.X * rs.Y - pr.Y * rs.X) / denominator;
        float u = (pr.X * pq.Y - pr.Y * pq.X) / denominator;
        return t > 0f && t < 1f && u > 0f && u < 1f ? p + t * pq : null;
    }

    // --- Checking and showing ------------------------------------------------------------

    private string AddLoop(List<Vector2> loop, string what)
    {
        if (!IsSimple(loop)) return $"That {what} crosses itself, so it cannot be the edge of a solid.";
        if (Loops.Any(other => Crosses(loop, other))) return $"That {what} crosses another outline. Outlines may sit inside one another, but not cross.";

        loopMarks.Add(Loops.Count);
        Loops.Add(loop);
        var shapes = Shapes();
        int holes = shapes.Sum(s => s.Holes.Count);
        return $"{Loops.Count} outline(s) - {shapes.Count} shape(s)" + (holes > 0 ? $", {holes} hole(s)" : "") + ". Extrude or Revolve it, or draw more.";
    }

    /// <summary>
    /// The segments showing what is being drawn and what the tool would make next with the pointer
    /// here: the rest of the line, the arc bending, the curve closing, the rectangle or the circle.
    /// </summary>
    public List<(Vector2 A, Vector2 B)> Pending(Vector2? cursor, SketchTool tool)
    {
        var segments = new List<(Vector2, Vector2)>();

        if (kind == ChainKind.Curve)
        {
            var through = new List<Vector2>(Chain);
            if (cursor is { } next && Chain.Count > 0 && Vector2.Distance(Chain[^1], next) > 0.05f) through.Add(next);
            if (through.Count >= 3) Ring(CurveThrough(through), segments);
            else if (through.Count == 2) segments.Add((through[0], through[1]));
            return segments;
        }

        for (int i = 0; i + 1 < Chain.Count; i++) segments.Add((Chain[i], Chain[i + 1]));
        if (cursor is not { } c || Chain.Count == 0 || kind == ChainKind.Stroke) return segments;

        switch (tool)
        {
            case SketchTool.Arc when ArcEnd is { } end:
                var from = Chain[^1];
                foreach (var p in Arc(from, c, end, out _))
                {
                    segments.Add((from, p));
                    from = p;
                }
                break;
            case SketchTool.Line or SketchTool.Arc:
                segments.Add((Chain[^1], c));
                break;
            case SketchTool.Rectangle when Rectangle(Chain[0], c) is { } r:
                Ring(r, segments);
                break;
            case SketchTool.Circle when Vector2.Distance(Chain[0], c) > 0.05f:
                Ring(Circle(Chain[0], Vector2.Distance(Chain[0], c)), segments);
                break;
        }

        return segments;
    }

    private static void Ring(List<Vector2> loop, List<(Vector2, Vector2)> segments)
    {
        for (int i = 0; i < loop.Count; i++) segments.Add((loop[i], loop[(i + 1) % loop.Count]));
    }

    /// <summary>What the pointer is at, and what the next click would make, in words.</summary>
    public string Readout(Vector2 cursor, SketchTool tool)
    {
        string at = $"X {F(cursor.X)}  Y {F(cursor.Y)}";
        if (Chain.Count == 0 || kind == ChainKind.Stroke) return at;

        if (tool == SketchTool.Arc && ArcEnd is { } end)
        {
            Arc(Chain[^1], cursor, end, out float radius);
            return float.IsFinite(radius) ? $"{at}   radius {F(radius)} mm" : $"{at}   straight";
        }

        var from = tool is SketchTool.Rectangle or SketchTool.Circle ? Chain[0] : Chain[^1];
        var d = cursor - from;
        return tool switch
        {
            SketchTool.Rectangle => $"{at}   {F(MathF.Abs(d.X))} x {F(MathF.Abs(d.Y))} mm",
            SketchTool.Circle => $"{at}   radius {F(d.Length())}, diameter {F(2f * d.Length())} mm",
            _ => $"{at}   length {F(d.Length())}, {F(MathF.Atan2(d.Y, d.X) * 180f / MathF.PI)} degrees"
        };
    }

    private static string F(float value) => value.ToString("0.##", CultureInfo.CurrentCulture);

    // --- Shapes ------------------------------------------------------------------------

    public static List<Vector2>? Rectangle(Vector2 a, Vector2 b)
    {
        var min = Vector2.Min(a, b);
        var max = Vector2.Max(a, b);
        if (max.X - min.X < 0.05f || max.Y - min.Y < 0.05f) return null;

        return [min, new(max.X, min.Y), max, new(min.X, max.Y)];
    }

    /// <summary>How many straight sides stand for a whole circle: none longer than half a millimetre, 24 at the least.</summary>
    private static int Sides(double radius) => Math.Clamp((int)Math.Ceiling(Math.Tau * radius / Piece), 24, 256);

    /// <summary>A circle as straight sides no longer than half a millimetre, 24 at the least.</summary>
    public static List<Vector2> Circle(Vector2 centre, float radius)
    {
        int sides = Sides(radius);
        return Enumerable.Range(0, sides)
            .Select(i => centre + radius * new Vector2(MathF.Cos(MathF.Tau * i / sides), MathF.Sin(MathF.Tau * i / sides)))
            .ToList();
    }

    /// <summary>
    /// The arc from <paramref name="start"/> to <paramref name="end"/> through a point, as the points
    /// after the start up to and including the end, the end exactly. Going round whichever way passes
    /// through the point. Straight, with an infinite radius, when the three are in line.
    /// </summary>
    public static List<Vector2> Arc(Vector2 start, Vector2 through, Vector2 end, out float radius)
    {
        double ax = start.X, ay = start.Y, bx = through.X, by = through.Y, cx = end.X, cy = end.Y;
        double d = 2.0 * (ax * (by - cy) + bx * (cy - ay) + cx * (ay - by));
        double chord = Math.Max((double)Vector2.DistanceSquared(start, end), 1e-6);

        radius = float.PositiveInfinity;
        if (Math.Abs(d) < 1e-6 * chord) return [end];

        double a2 = ax * ax + ay * ay, b2 = bx * bx + by * by, c2 = cx * cx + cy * cy;
        double ux = (a2 * (by - cy) + b2 * (cy - ay) + c2 * (ay - by)) / d;
        double uy = (a2 * (cx - bx) + b2 * (ax - cx) + c2 * (bx - ax)) / d;
        double r = Math.Sqrt((ax - ux) * (ax - ux) + (ay - uy) * (ay - uy));

        double from = Math.Atan2(ay - uy, ax - ux);
        double sweep = Around(Math.Atan2(cy - uy, cx - ux) - from);
        if (Around(Math.Atan2(by - uy, bx - ux) - from) > sweep) sweep -= Math.Tau;

        radius = (float)r;
        int pieces = Math.Max(2, (int)Math.Ceiling(Sides(r) * Math.Abs(sweep) / Math.Tau));
        var points = new List<Vector2>(pieces);
        for (int i = 1; i < pieces; i++)
        {
            double angle = from + sweep * i / pieces;
            points.Add(new Vector2((float)(ux + r * Math.Cos(angle)), (float)(uy + r * Math.Sin(angle))));
        }

        points.Add(end);
        return points;

        static double Around(double angle) => angle - Math.Tau * Math.Floor(angle / Math.Tau);
    }

    /// <summary>
    /// A smooth closed curve through every point, as short straight pieces. Centripetal Catmull-Rom:
    /// the uniform kind overshoots between points close together and far apart, looping back on
    /// itself, which an outline may not do.
    /// </summary>
    public static List<Vector2> CurveThrough(IReadOnlyList<Vector2> points)
    {
        int n = points.Count;
        var curve = new List<Vector2>();

        for (int i = 0; i < n; i++)
        {
            Vector2 p0 = points[(i - 1 + n) % n], p1 = points[i], p2 = points[(i + 1) % n], p3 = points[(i + 2) % n];

            float t0 = 0f;
            float t1 = t0 + Knot(p0, p1);
            float t2 = t1 + Knot(p1, p2);
            float t3 = t2 + Knot(p2, p3);

            int pieces = Math.Clamp((int)MathF.Ceiling(Vector2.Distance(p1, p2) / Piece), 4, 64);
            curve.Add(p1);
            for (int k = 1; k < pieces; k++)
            {
                float t = t1 + (t2 - t1) * k / pieces;
                var a1 = Mix(p0, p1, t0, t1, t);
                var a2 = Mix(p1, p2, t1, t2, t);
                var a3 = Mix(p2, p3, t2, t3, t);
                var b1 = Mix(a1, a2, t0, t2, t);
                var b2 = Mix(a2, a3, t1, t3, t);
                curve.Add(Mix(b1, b2, t1, t2, t));
            }
        }

        return curve;

        static float Knot(Vector2 a, Vector2 b) => MathF.Max(MathF.Sqrt(Vector2.Distance(a, b)), 1e-3f);

        static Vector2 Mix(Vector2 a, Vector2 b, float ta, float tb, float t) =>
            (tb - t) / (tb - ta) * a + (t - ta) / (tb - ta) * b;
    }

    /// <summary>Whether a closed outline has an area and no edge crossing or touching another.</summary>
    public static bool IsSimple(IReadOnlyList<Vector2> loop)
    {
        if (loop.Count < 3 || MathF.Abs(Polygon2.SignedArea(loop)) < 1e-4f) return false;

        int n = loop.Count;
        for (int i = 0; i < n; i++)
            for (int j = i + 1; j < n; j++)
            {
                // Neighbouring edges share a corner and nothing more.
                if (j == i + 1 || (i == 0 && j == n - 1)) continue;
                if (Touch(loop[i], loop[(i + 1) % n], loop[j], loop[(j + 1) % n])) return false;
            }

        return true;
    }

    /// <summary>Whether any edge of one outline crosses or touches any edge of another.</summary>
    public static bool Crosses(IReadOnlyList<Vector2> a, IReadOnlyList<Vector2> b)
    {
        // Most pairs are nowhere near each other, which a loaded drawing of many shapes makes worth knowing first.
        var (aMin, aMax) = Extent(a);
        var (bMin, bMax) = Extent(b);
        if (aMax.X < bMin.X - 1e-4f || bMax.X < aMin.X - 1e-4f || aMax.Y < bMin.Y - 1e-4f || bMax.Y < aMin.Y - 1e-4f) return false;

        for (int i = 0; i < a.Count; i++)
            for (int j = 0; j < b.Count; j++)
                if (Touch(a[i], a[(i + 1) % a.Count], b[j], b[(j + 1) % b.Count])) return true;

        return false;
    }

    private static (Vector2 Min, Vector2 Max) Extent(IReadOnlyList<Vector2> loop)
    {
        Vector2 min = new(float.MaxValue), max = new(float.MinValue);
        foreach (var p in loop)
        {
            min = Vector2.Min(min, p);
            max = Vector2.Max(max, p);
        }

        return (min, max);
    }

    private static bool Touch(Vector2 p, Vector2 q, Vector2 r, Vector2 s)
    {
        float d1 = Cross(r, s, p), d2 = Cross(r, s, q), d3 = Cross(p, q, r), d4 = Cross(p, q, s);
        if (((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0))) return true;

        const float e = 1e-6f;
        return (MathF.Abs(d1) < e && Between(r, s, p)) || (MathF.Abs(d2) < e && Between(r, s, q))
            || (MathF.Abs(d3) < e && Between(p, q, r)) || (MathF.Abs(d4) < e && Between(p, q, s));

        static float Cross(Vector2 a, Vector2 b, Vector2 c) => (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);

        static bool Between(Vector2 a, Vector2 b, Vector2 c) =>
            c.X >= MathF.Min(a.X, b.X) - 1e-5f && c.X <= MathF.Max(a.X, b.X) + 1e-5f
            && c.Y >= MathF.Min(a.Y, b.Y) - 1e-5f && c.Y <= MathF.Max(a.Y, b.Y) + 1e-5f;
    }
}
