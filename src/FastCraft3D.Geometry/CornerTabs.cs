using System.Numerics;
using FastCraft3D.Geometry.Csg;

namespace FastCraft3D.Geometry;

/// <summary>How many tabs hold each corner.</summary>
public enum TabsPerCorner
{
    /// <summary>One off each of the two sides that meet there, just short of the corner.</summary>
    Two,

    /// <summary>One out of the point of the corner itself.</summary>
    One
}

/// <param name="PerCorner">A tab off each side of a corner, or one out of its point.</param>
/// <param name="Frame">A frame round the part that every tab runs out to, rather than each ending in a pad of its own.</param>
/// <param name="Reach">How far the tabs stand out from the part: to the frame's inner side, or to the pad.</param>
/// <param name="FrameWidth">Width of the frame.</param>
/// <param name="Pad">Width of the round pad each tab ends in when there is no frame; nought for none.</param>
/// <param name="Width">Width of a tab.</param>
/// <param name="Height">Height of the tabs, pads and frame, standing on the plate.</param>
/// <param name="GateWidth">Width of a gate where it meets the part: what is cut.</param>
/// <param name="GateHeight">Height of a gate where it meets the part.</param>
public readonly record struct CornerTabOptions(
    TabsPerCorner PerCorner, bool Frame, float Reach, float FrameWidth, float Pad, float Width, float Height, float GateWidth, float GateHeight)
{
    public static CornerTabOptions Default => new(
        TabsPerCorner.Two, Frame: true, Reach: 5f, FrameWidth: 4f, Pad: 8f, Width: 3f, Height: 2f, GateWidth: 1.2f, GateHeight: 0.8f);
}

/// <summary>A corner of a part's foot, where tabs can hold it.</summary>
/// <param name="At">The point of a sharp corner, or the middle of a rounded one.</param>
/// <param name="Turn">How far the outline turns there, in degrees: 90 at a box's corner.</param>
/// <param name="Lifts">Sharp enough to lift, so found without being picked.</param>
public sealed record TabCorner(Vector2 At, float Turn, bool Lifts)
{
    /// <summary>Where the side before the corner ends - the corner, or the start of its rounding - and which way it ran.</summary>
    internal (Vector2 End, Vector2 Along, float Length) Before { get; init; }

    /// <summary>Where the side after the corner starts and which way it runs.</summary>
    internal (Vector2 Start, Vector2 Along, float Length) After { get; init; }

    /// <summary>Straight out of the point, halfway between the two sides' outward faces.</summary>
    internal Vector2 Out { get; init; }
}

/// <param name="Mesh">The tabs, gates and frame as one closed solid standing where the part does; null when there is nothing to hold.</param>
/// <param name="Tabs">How many tabs there are.</param>
/// <param name="Held">How many corners they hold.</param>
/// <param name="Blocked">How many tabs were left off because the part itself is in their way.</param>
/// <param name="Size">The width and depth of it all, frame included.</param>
/// <param name="Why">What went wrong, when the tabs could not be made into one solid.</param>
public sealed record CornerTabLayout(Mesh? Mesh, int Tabs, int Held, int Blocked, Vector2 Size, string? Why = null);

/// <summary>
/// Tabs on the corners of a part that lift off the plate, to snip off after printing: each held
/// to the part by a thin gate at its foot, as a kit sprue holds its parts, and either run out to a
/// stiff frame round the part or ended in a pad of its own. A slicer's mouse ears are a single
/// layer, which curls up with the corner it was meant to hold; a tab as tall as a runner does not.
///
/// The corners are read from the outline of the part's first layer, wherever they point, so a
/// part turned on the plate, an L or a star is held as well as a box. A corner is where the
/// outline turns sharply within a few millimetres: a rounded corner counts, the gentle curve of
/// a cylinder does not.
///
/// Unlike the kit sprue the tabs run at any angle, so they are not one grid of boxes but boxes and
/// discs joined by Manifold - convex pieces it joins without trouble - and checked closed.
/// </summary>
public sealed class CornerTabs
{
    /// <summary>The thin stretch at the end of a tab, where it is cut.</summary>
    public const float GateLength = 1.5f;

    /// <summary>
    /// How far a gate runs into its part, so the slicer joins them. The two stay separate objects,
    /// and a gate that only touched its part's skin would print beside it, not on to it.
    /// </summary>
    private const float Overlap = 0.4f;

    /// <summary>
    /// How much of the outline is looked along at once for a corner. Wide enough to take in a
    /// corner rounded a couple of millimetres, which lifts as a sharp one does; narrow enough that
    /// a cylinder ten millimetres across turns too little in it to count.
    /// </summary>
    private const float Window = 4f;

    /// <summary>The least turn that is a corner at all, offered for picking by hand.</summary>
    private const float LeastTurn = 20f;

    /// <summary>The least turn that lifts, and is found: an octagon's corners and anything sharper.</summary>
    private const float LiftingTurn = 44.9f;

    /// <summary>How near a corner a click has to land to pick it.</summary>
    public const float Grab = 6f;

    private readonly float foot;
    private readonly List<(Vector2 A, Vector2 B)> outline = [];
    private readonly List<Vector2> hull;

    /// <param name="part">The part in world space, as it stands.</param>
    public CornerTabs(Mesh part)
    {
        var welded = part.Welded();
        var bounds = welded.ComputeBounds();
        foot = bounds.Min.Z;

        // The first layer, which is where a corner lets go of the plate.
        var loops = Loops(welded, foot + MathF.Min(0.2f, bounds.Size.Z / 2));
        foreach (var loop in loops)
            for (int i = 0; i < loop.Count; i++)
                outline.Add((loop[i], loop[(i + 1) % loop.Count]));

        // A part can be several bodies overlapping - a chimney stood into a wall - each cut to an
        // outline of its own. A corner of one inside another is not a corner of the foot at all.
        var areas = loops.Select(Polygon2.SignedArea).ToList();
        bool Covered(Vector2 point, int own) =>
            Enumerable.Range(0, loops.Count).Where(j => j != own && Polygon2.Contains(loops[j], point)).Sum(j => MathF.Sign(areas[j])) > 0;

        Corners = Enumerable.Range(0, loops.Count).Where(i => areas[i] > 0)
            .SelectMany(i => FindCorners(loops[i]).Where(c => !Covered(c.At, i))).ToList();
        hull = Hull(loops.Where((_, i) => areas[i] > 0).SelectMany(l => l).ToList());
    }

    /// <summary>Every corner of the part's foot that tabs can hold, sharp enough to lift or not.</summary>
    public IReadOnlyList<TabCorner> Corners { get; }

    /// <summary>The corners sharp enough to lift, by their place in <see cref="Corners"/>.</summary>
    public IReadOnlyList<int> Lifting => Enumerable.Range(0, Corners.Count).Where(i => Corners[i].Lifts).ToList();

    /// <summary>The corner nearest a point on the plate, if one is within <paramref name="within"/>.</summary>
    public int? CornerNear(Vector2 point, float within = Grab)
    {
        int? best = null;
        float nearest = within;
        for (int i = 0; i < Corners.Count; i++)
        {
            float d = Vector2.Distance(Corners[i].At, point);
            if (d > nearest) continue;
            nearest = d;
            best = i;
        }

        return best;
    }

    /// <param name="chosen">The corners to hold, by their place in <see cref="Corners"/>; null for those that lift.</param>
    public CornerTabLayout Lay(CornerTabOptions o, IReadOnlyCollection<int>? chosen = null)
    {
        float reach = MathF.Max(o.Reach, GateLength + 0.5f);
        float width = MathF.Max(o.Width, 0.8f);
        float height = MathF.Max(o.Height, 0.4f);
        float gateHeight = Math.Clamp(o.GateHeight, 0.2f, height);
        float gateWidth = Math.Clamp(o.GateWidth, 0.4f, width);
        float frameWidth = MathF.Max(o.FrameWidth, width);
        float pad = !o.Frame && o.Pad > width ? o.Pad : 0f;
        float top = foot + height;

        var corners = (chosen ?? Lifting).Where(i => i >= 0 && i < Corners.Count).Distinct().Order().Select(i => Corners[i]).ToList();
        var frame = o.Frame && hull.Count >= 3 ? Frame(reach, frameWidth) : default;

        // Just short of the corner, so the gate meets the side and not the point.
        float inset = gateWidth / 2 + 0.3f;

        var pieces = new List<Mesh>();
        int tabs = 0, held = 0, blocked = 0;
        foreach (var c in corners)
        {
            List<(Vector2 From, Vector2 Way, float Bite)> spots = o.PerCorner == TabsPerCorner.One
                ? [(c.At, c.Out, Overlap + Bite(c.Turn, gateWidth))]
                :
                [
                    (c.Before.End - c.Before.Along * MathF.Min(inset, 0.45f * c.Before.Length), Outward(c.Before.Along), Overlap),
                    (c.After.Start + c.After.Along * MathF.Min(inset, 0.45f * c.After.Length), Outward(c.After.Along), Overlap)
                ];

            bool any = false;
            foreach (var (from, way, bite) in spots)
            {
                var start = from + way * GateLength;
                var tip = frame.Inner is { } inner
                    ? from + way * (Exit(inner, from, way) ?? reach) + way * (frameWidth / 2)
                    : from + way * (pad > 0 ? reach + pad / 2 : reach);

                if (!Clear(start, tip, width, pad > 0 ? pad / 2 : 0f))
                {
                    blocked++;
                    continue;
                }

                pieces.Add(Slab(from - way * bite, from + way * (GateLength + 0.3f), gateWidth, foot, foot + gateHeight));
                pieces.Add(Slab(start, tip, width, foot, top));
                if (pad > 0) pieces.Add(Prism(Circle(tip, pad / 2), foot, top));
                tabs++;
                any = true;
            }

            if (any) held++;
        }

        if (tabs == 0) return new CornerTabLayout(null, 0, 0, blocked, Vector2.Zero);
        if (frame.Inner is { } ring) pieces.Add(Ring(ring, frame.Outer!, foot, top));

        var solid = ManifoldCsg.UnionAll(pieces);
        if (solid is null || !solid.CheckHealth().IsWatertight)
            return new CornerTabLayout(null, tabs, held, blocked, Vector2.Zero, "The tabs would not join into one closed solid.");

        var size = solid.ComputeBounds().Size;
        return new CornerTabLayout(solid, tabs, held, blocked, new Vector2(size.X, size.Y));
    }

    /// <summary>
    /// How much further a gate out of a corner's point runs in, so all of its width is in the part
    /// rather than only its middle touching the tip: the sharper the corner, the further.
    /// </summary>
    private static float Bite(float turn, float gateWidth)
    {
        float half = (180f - turn) / 2f * MathF.PI / 180f;
        return half <= 0.1f ? 3f : MathF.Min(gateWidth / 2 / MathF.Tan(half), 3f);
    }

    /// <summary>Outside is on the right of the way an outline runs: outlines run anticlockwise.</summary>
    private static Vector2 Outward(Vector2 along) => new(along.Y, -along.X);

    /// <summary>
    /// Whether a tab - its full width, from where the gate ends out to its tip and pad - keeps
    /// clear of the part. A tab off one side of a U runs into the other arm, and would join the
    /// two where nothing is meant to be cut.
    /// </summary>
    private bool Clear(Vector2 start, Vector2 tip, float width, float padRadius)
    {
        const float Margin = 0.3f;
        var along = Vector2.Normalize(tip - start);
        var side = new Vector2(-along.Y, along.X) * (width / 2 + Margin);
        Vector2[] box = [start - side, tip - side, tip + side, start + side];

        foreach (var (a, b) in outline)
        {
            if (Inside(box, a) || Inside(box, b)) return false;
            for (int k = 0; k < 4; k++)
                if (Cross(a, b, box[k], box[(k + 1) % 4])) return false;

            if (padRadius > 0 && Distance(tip, a, b) < padRadius + Margin) return false;
        }

        return true;
    }

    private static bool Inside(Vector2[] convex, Vector2 p)
    {
        for (int k = 0; k < convex.Length; k++)
            if (Det(convex[(k + 1) % convex.Length] - convex[k], p - convex[k]) < 0) return false;

        return true;
    }

    private static bool Cross(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
    {
        float d1 = Det(b - a, c - a), d2 = Det(b - a, d - a), d3 = Det(d - c, a - c), d4 = Det(d - c, b - c);
        return (d1 > 0) != (d2 > 0) && (d3 > 0) != (d4 > 0);
    }

    private static float Det(Vector2 u, Vector2 v) => u.X * v.Y - u.Y * v.X;

    private static float Distance(Vector2 p, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        float t = ab.LengthSquared() < 1e-12f ? 0 : Math.Clamp(Vector2.Dot(p - a, ab) / ab.LengthSquared(), 0, 1);
        return Vector2.Distance(p, a + ab * t);
    }

    /// <summary>How far along the line from inside the frame it reaches the frame's inner side.</summary>
    private static float? Exit(List<Vector2> frame, Vector2 from, Vector2 way)
    {
        float? nearest = null;
        for (int k = 0; k < frame.Count; k++)
        {
            var a = frame[k];
            var e = frame[(k + 1) % frame.Count] - a;
            float den = Det(way, e);
            if (MathF.Abs(den) < 1e-9f) continue;

            float t = Det(a - from, e) / den, u = Det(a - from, way) / den;
            if (t > 0 && u >= -1e-4f && u <= 1 + 1e-4f && (nearest is null || t < nearest)) nearest = t;
        }

        return nearest;
    }

    /// <summary>
    /// The frame's inner and outer sides: the part's convex hull grown by the reach and by the
    /// reach and the width. Mitred where the hull turns no more than a box's corner, so the frame
    /// round a box is square as the kit sprue's is; rounded where it turns more, where a mitre
    /// would run out to a spike.
    /// </summary>
    private (List<Vector2>? Inner, List<Vector2>? Outer) Frame(float reach, float width)
    {
        var inner = new List<Vector2>();
        var outer = new List<Vector2>();
        int n = hull.Count;
        for (int i = 0; i < n; i++)
        {
            var p = hull[i];
            var n0 = Outward(Vector2.Normalize(p - hull[(i + n - 1) % n]));
            var n1 = Outward(Vector2.Normalize(hull[(i + 1) % n] - p));
            float cosHalf = MathF.Sqrt(MathF.Max(0, (1 + Vector2.Dot(n0, n1)) / 2));

            if (cosHalf > 0.7f)
            {
                var mitre = Vector2.Normalize(n0 + n1) / cosHalf;
                inner.Add(p + mitre * reach);
                outer.Add(p + mitre * (reach + width));
                continue;
            }

            float from = MathF.Atan2(n0.Y, n0.X), turn = MathF.Acos(Math.Clamp(Vector2.Dot(n0, n1), -1, 1));
            int steps = Math.Max(2, (int)MathF.Ceiling(turn / (MathF.PI / 12)));
            for (int k = 0; k <= steps; k++)
            {
                float a = from + turn * k / steps;
                var d = new Vector2(MathF.Cos(a), MathF.Sin(a));
                inner.Add(p + d * reach);
                outer.Add(p + d * (reach + width));
            }
        }

        return (inner, outer);
    }

    /// <summary>The points' convex hull, anticlockwise, with no three in a line.</summary>
    private static List<Vector2> Hull(List<Vector2> points)
    {
        var sorted = points.Distinct().OrderBy(p => p.X).ThenBy(p => p.Y).ToList();
        if (sorted.Count < 3) return sorted;

        var hull = new List<Vector2>();
        foreach (var pass in new[] { sorted, Enumerable.Reverse(sorted).ToList() })
        {
            int floor = hull.Count;
            foreach (var p in pass)
            {
                while (hull.Count >= floor + 2 && Det(hull[^1] - hull[^2], p - hull[^2]) <= 1e-9f) hull.RemoveAt(hull.Count - 1);
                hull.Add(p);
            }

            hull.RemoveAt(hull.Count - 1);
        }

        return hull;
    }

    /// <summary>
    /// The corners of one outline, running anticlockwise. Each point is given the turn of the
    /// outline within half a window either way; a run of points over the least turn is one corner,
    /// from the first point in it that turns to the last. A run round the whole outline is a round
    /// part, and a long one a curve, and neither is a corner.
    /// </summary>
    private static IEnumerable<TabCorner> FindCorners(List<Vector2> loop)
    {
        int n = loop.Count;
        if (n < 3) yield break;

        var turn = new float[n];
        var at = new float[n];
        float length = 0;
        for (int i = 0; i < n; i++)
        {
            var into = loop[i] - loop[(i + n - 1) % n];
            var onward = loop[(i + 1) % n] - loop[i];
            turn[i] = MathF.Atan2(Det(into, onward), Vector2.Dot(into, onward)) * 180f / MathF.PI;
            at[i] = length;
            length += onward.Length();
        }

        float Ahead(int i, int j) => ((at[j] - at[i]) % length + length) % length;

        var windowed = new float[n];
        for (int i = 0; i < n; i++)
        {
            float sum = turn[i];
            for (int k = 1; k < n && Ahead(i, (i + k) % n) <= Window / 2; k++) sum += turn[(i + k) % n];
            for (int k = 1; k < n && Ahead((i + n - k) % n, i) <= Window / 2; k++) sum += turn[(i + n - k) % n];
            windowed[i] = sum;
        }

        // A point carries on the run of the one before when both turn and their windows overlap.
        // Counted from where a run starts, since a box's outline is all corners with no point
        // between them that does not turn.
        bool Turns(int i) => windowed[i] >= LeastTurn;
        bool Joined(int i) => Turns(i) && Turns((i + n - 1) % n) && Ahead((i + n - 1) % n, i) <= Window;

        int begin = Enumerable.Range(0, n).FirstOrDefault(i => Turns(i) && !Joined(i), -1);
        if (begin < 0) yield break;

        var runs = new List<List<int>>();
        for (int k = 0; k < n; k++)
        {
            int i = (begin + k) % n;
            if (!Turns(i)) continue;
            if (!Joined(i)) runs.Add([]);
            runs[^1].Add(i);
        }

        foreach (var run in runs)
        {
            var turning = run.Where(j => turn[j] > 0.5f).ToList();
            if (turning.Count == 0) continue;

            int first = turning[0], last = turning[^1];
            float total = 0;
            for (int j = run.IndexOf(first); j <= run.IndexOf(last); j++) total += turn[run[j]];

            float span = Ahead(first, last);
            if (total < LeastTurn || span > 3 * Window) continue;

            var before = loop[first] - loop[(first + n - 1) % n];
            var after = loop[(last + 1) % n] - loop[last];
            var beforeAlong = Vector2.Normalize(before);
            var afterAlong = Vector2.Normalize(after);
            var outward = Outward(beforeAlong) + Outward(afterAlong);

            yield return new TabCorner(PointAlong(loop, at, first, span / 2), total, total >= LiftingTurn)
            {
                Before = (loop[first], beforeAlong, before.Length()),
                After = (loop[last], afterAlong, after.Length()),
                Out = outward.LengthSquared() > 1e-6f ? Vector2.Normalize(outward) : beforeAlong
            };
        }
    }

    /// <summary>The point a distance on round the outline from one of its points.</summary>
    private static Vector2 PointAlong(List<Vector2> loop, float[] at, int from, float distance)
    {
        int n = loop.Count;
        for (int k = 0; k < n; k++)
        {
            var a = loop[(from + k) % n];
            var b = loop[(from + k + 1) % n];
            float step = Vector2.Distance(a, b);
            if (distance <= step) return step < 1e-9f ? a : Vector2.Lerp(a, b, distance / step);
            distance -= step;
        }

        return loop[from];
    }

    /// <summary>
    /// The part cut across at a height, as closed outlines with the outside on the right: outlines
    /// anticlockwise, holes clockwise. Each crossing point is known by the edge it lies on, so the
    /// two triangles either side of an edge agree on it exactly and the pieces join up.
    /// </summary>
    private static List<List<Vector2>> Loops(Mesh mesh, float z)
    {
        var p = mesh.Positions;
        var ix = mesh.Indices;
        var points = new Dictionary<(int, int), Vector2>();
        var next = new Dictionary<(int, int), (int, int)>();

        (int, int) Crossing(int a, int b)
        {
            var key = a < b ? (a, b) : (b, a);
            if (!points.ContainsKey(key))
            {
                var (u, v) = (p[key.Item1], p[key.Item2]);
                float s = (z - u.Z) / (v.Z - u.Z);
                points[key] = new Vector2(u.X + (v.X - u.X) * s, u.Y + (v.Y - u.Y) * s);
            }

            return key;
        }

        // Which way a piece runs is read from the triangle's winding - from where its edges come
        // down through the height to where they go up - not from its normal. A boolean leaves
        // slivers with no normal to speak of, and one piece the wrong way round broke the outline
        // of a house's walls, so that only its pin holes and the room inside were found.
        for (int t = 0; t + 2 < ix.Count; t += 3)
        {
            (int, int)? down = null, up = null;
            for (int e = 0; e < 3; e++)
            {
                int a = ix[t + e], b = ix[t + (e + 1) % 3];
                bool above = p[a].Z > z;
                if (above == p[b].Z > z) continue;
                if (above) down = Crossing(a, b);
                else up = Crossing(a, b);
            }

            if (down is { } from && up is { } to) next[from] = to;
        }

        var loops = new List<List<Vector2>>();
        var done = new HashSet<(int, int)>();
        foreach (var start in next.Keys)
        {
            if (done.Contains(start)) continue;

            var loop = new List<Vector2>();
            var key = start;
            bool closed = false;
            while (done.Add(key))
            {
                var point = points[key];
                if (loop.Count == 0 || Vector2.Distance(loop[^1], point) > 1e-4f) loop.Add(point);
                if (!next.TryGetValue(key, out key)) break;
                if (key == start)
                {
                    closed = true;
                    break;
                }
            }

            if (closed && loop.Count > 1 && Vector2.Distance(loop[0], loop[^1]) <= 1e-4f) loop.RemoveAt(loop.Count - 1);
            if (closed && loop.Count >= 3) loops.Add(Straightened(loop));
        }

        return loops;
    }

    /// <summary>The outline without the points along a straight side, which say nothing about its corners.</summary>
    private static List<Vector2> Straightened(List<Vector2> loop)
    {
        var kept = new List<Vector2>(loop.Count);
        int n = loop.Count;
        for (int i = 0; i < n; i++)
        {
            var into = loop[i] - (kept.Count > 0 ? kept[^1] : loop[(i + n - 1) % n]);
            var onward = loop[(i + 1) % n] - loop[i];
            if (MathF.Abs(MathF.Atan2(Det(into, onward), Vector2.Dot(into, onward))) > 1e-3f) kept.Add(loop[i]);
        }

        return kept.Count >= 3 ? kept : loop;
    }

    /// <summary>A bar from one point to another, as wide as given, standing between two heights.</summary>
    private static Mesh Slab(Vector2 a, Vector2 b, float width, float low, float high)
    {
        var along = Vector2.Normalize(b - a);
        var side = new Vector2(-along.Y, along.X) * (width / 2);
        return Prism([a - side, b - side, b + side, a + side], low, high);
    }

    private static List<Vector2> Circle(Vector2 centre, float radius)
    {
        const int Sides = 32;
        return Enumerable.Range(0, Sides)
            .Select(k => centre + radius * new Vector2(MathF.Cos(2 * MathF.PI * k / Sides), MathF.Sin(2 * MathF.PI * k / Sides)))
            .ToList();
    }

    /// <summary>A convex outline, anticlockwise, stood up between two heights.</summary>
    private static Mesh Prism(IReadOnlyList<Vector2> outline, float low, float high)
    {
        int n = outline.Count;
        var positions = outline.Select(q => new Vector3(q, low)).Concat(outline.Select(q => new Vector3(q, high))).ToList();
        var indices = new List<int>();
        for (int k = 1; k + 1 < n; k++)
        {
            indices.AddRange([0, k + 1, k]);
            indices.AddRange([n, n + k, n + k + 1]);
        }

        for (int k = 0; k < n; k++)
        {
            int j = (k + 1) % n;
            indices.AddRange([k, j, n + j, k, n + j, n + k]);
        }

        return new Mesh(positions, indices);
    }

    /// <summary>The ring between two outlines of as many points, both anticlockwise, stood up between two heights.</summary>
    private static Mesh Ring(List<Vector2> inner, List<Vector2> outer, float low, float high)
    {
        int n = inner.Count;
        var positions = new List<Vector3>(4 * n);
        foreach (var (ring, z) in new[] { (inner, low), (inner, high), (outer, low), (outer, high) })
            positions.AddRange(ring.Select(q => new Vector3(q, z)));

        int InLow(int k) => k % n;
        int InHigh(int k) => n + k % n;
        int OutLow(int k) => 2 * n + k % n;
        int OutHigh(int k) => 3 * n + k % n;

        var indices = new List<int>();
        void Quad(int a, int b, int c, int d) => indices.AddRange([a, b, c, a, c, d]);
        for (int k = 0; k < n; k++)
        {
            Quad(InHigh(k), OutHigh(k), OutHigh(k + 1), InHigh(k + 1));
            Quad(InLow(k + 1), OutLow(k + 1), OutLow(k), InLow(k));
            Quad(OutLow(k), OutLow(k + 1), OutHigh(k + 1), OutHigh(k));
            Quad(InLow(k + 1), InLow(k), InHigh(k), InHigh(k + 1));
        }

        return new Mesh(positions, indices);
    }
}
