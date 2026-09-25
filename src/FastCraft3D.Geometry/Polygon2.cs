using System.Numerics;

namespace FastCraft3D.Geometry;

/// <summary>
/// Turns outlines into triangles, holes and all.
///
/// This is the piece that letters need and rectangles do not. The letter O is two loops, an
/// outer and an inner, and no amount of splitting the outer one will produce its middle; the
/// hole has to be understood as a hole. The same goes for A, B, D, P, R, and for any lettering
/// worth embossing.
///
/// Holes are handled by cutting a bridge to them: a hole is joined to the outline around it by a
/// pair of coincident edges, which turns a shape with a hole into one simple loop that ordinary
/// ear clipping can chew through. The bridge leaves two triangles sharing an edge exactly, which
/// is harmless - the surface is continuous across it.
/// </summary>
public static class Polygon2
{
    /// <summary>
    /// Triangulates one outline and any holes inside it.
    ///
    /// Returns the points used - which include the outline's and the holes', possibly repeated
    /// where a bridge was cut - and three indices per triangle, wound anticlockwise.
    /// </summary>
    public static (List<Vector2> Points, List<int> Triangles) Triangulate(
        IReadOnlyList<Vector2> outline, IReadOnlyList<IReadOnlyList<Vector2>> holes)
    {
        var loop = Prepare(outline, holes);
        if (loop.Count < 3) return ([], []);

        return (loop, EarClip(loop));
    }

    /// <summary>
    /// Sorts a pile of loops into shapes: each outline with the loops that are holes in it.
    ///
    /// Which are holes is decided by containment rather than by winding direction. Fonts
    /// disagree about which way round they draw a counter, drawing programs disagree about which
    /// way round they draw anything, and flattened geometry does not always keep the convention
    /// either; whether a loop sits inside an odd number of others is not a matter of opinion.
    ///
    /// A loop inside a hole is a shape again - an island in a lake - and is given its own entry,
    /// which costs nothing to allow and is wrong to ignore.
    /// </summary>
    public static List<(List<Vector2> Outline, List<List<Vector2>> Holes)> Nest(
        List<List<Vector2>> loops)
    {
        var depth = new int[loops.Count];
        for (int i = 0; i < loops.Count; i++)
            for (int j = 0; j < loops.Count; j++)
                if (i != j && Contains(loops[j], loops[i][0]))
                    depth[i]++;

        var shapes = new List<(List<Vector2> Outline, List<List<Vector2>> Holes)>();
        var index = new Dictionary<int, int>();

        for (int i = 0; i < loops.Count; i++)
        {
            if (depth[i] % 2 != 0) continue;

            index[i] = shapes.Count;
            shapes.Add((loops[i], []));
        }

        for (int i = 0; i < loops.Count; i++)
        {
            if (depth[i] % 2 == 0) continue;

            int parent = -1;
            float smallest = float.MaxValue;

            // Its immediate parent is the smallest loop one level up that contains it, which is
            // what puts a letter inside a counter with the counter rather than with the letter.
            for (int j = 0; j < loops.Count; j++)
            {
                if (i == j || depth[j] != depth[i] - 1) continue;
                if (!Contains(loops[j], loops[i][0])) continue;

                float area = Math.Abs(SignedArea(loops[j]));
                if (area >= smallest) continue;

                smallest = area;
                parent = j;
            }

            if (parent >= 0 && index.TryGetValue(parent, out int at))
                shapes[at].Holes.Add(loops[i]);
        }

        return shapes;
    }

    /// <summary>Crossing count along a ray: odd means inside.</summary>
    public static bool Contains(IReadOnlyList<Vector2> loop, Vector2 point)
    {
        bool inside = false;

        for (int i = 0, j = loop.Count - 1; i < loop.Count; j = i++)
        {
            if (loop[i].Y > point.Y == loop[j].Y > point.Y) continue;

            float x = loop[i].X
                + (point.Y - loop[i].Y) / (loop[j].Y - loop[i].Y) * (loop[j].X - loop[i].X);

            if (point.X < x) inside = !inside;
        }

        return inside;
    }

    /// <summary>Twice the signed area. Positive means the loop runs anticlockwise.</summary>
    public static float SignedArea(IReadOnlyList<Vector2> loop)
    {
        float total = 0;
        for (int i = 0; i < loop.Count; i++)
        {
            var a = loop[i];
            var b = loop[(i + 1) % loop.Count];
            total += a.X * b.Y - b.X * a.Y;
        }

        return total;
    }

    /// <summary>
    /// Joins every hole into the outline, leaving one simple loop.
    ///
    /// Holes are taken rightmost first. Each is bridged to a point on the loop that can see it,
    /// so a hole bridged earlier is already part of the loop and later ones may attach to it -
    /// which is what lets several holes in one letter work rather than only the first.
    /// </summary>
    private static List<Vector2> Prepare(
        IReadOnlyList<Vector2> outline, IReadOnlyList<IReadOnlyList<Vector2>> holes)
    {
        var loop = new List<Vector2>(outline);
        if (SignedArea(loop) < 0) loop.Reverse(); // outlines run anticlockwise

        var pending = holes
            .Where(h => h.Count >= 3)
            .Select(h =>
            {
                var copy = new List<Vector2>(h);
                if (SignedArea(copy) > 0) copy.Reverse(); // holes run the other way
                return copy;
            })
            .OrderByDescending(h => h.Max(p => p.X))
            .ToList();

        foreach (var hole in pending) Bridge(loop, hole);

        return loop;
    }

    /// <summary>
    /// Cuts a channel between the loop and one hole.
    ///
    /// The hole's rightmost point is the one to bridge from: a ray cast to the right from it
    /// leaves the hole immediately, so the first edge of the loop it meets is the nearest piece
    /// of outline on that side. Eberly's rule then picks which end of that edge to join to,
    /// checking that nothing reflex stands between the two.
    /// </summary>
    private static void Bridge(List<Vector2> loop, List<Vector2> hole)
    {
        int start = 0;
        for (int i = 1; i < hole.Count; i++)
            if (hole[i].X > hole[start].X)
                start = i;

        var from = hole[start];
        if (!VisibleFrom(loop, from, out int join)) return;

        // The loop, up to the joining point; the hole walked once from its rightmost point and
        // back to it; then the joining point again and the rest of the loop.
        var joined = new List<Vector2>(loop.Count + hole.Count + 2);
        joined.AddRange(loop.Take(join + 1));

        for (int i = 0; i <= hole.Count; i++)
            joined.Add(hole[(start + i) % hole.Count]);

        joined.Add(loop[join]);
        joined.AddRange(loop.Skip(join + 1));

        loop.Clear();
        loop.AddRange(joined);
    }

    /// <summary>
    /// A vertex of the loop that the given point can be joined to without crossing anything.
    /// </summary>
    private static bool VisibleFrom(List<Vector2> loop, Vector2 from, out int index)
    {
        index = -1;

        float nearest = float.MaxValue;
        var hit = Vector2.Zero;
        int edge = -1;

        // Rightward ray: the first edge it meets is the nearest outline on that side.
        for (int i = 0; i < loop.Count; i++)
        {
            var a = loop[i];
            var b = loop[(i + 1) % loop.Count];

            if (a.Y == b.Y) continue;
            if (from.Y < MathF.Min(a.Y, b.Y) || from.Y > MathF.Max(a.Y, b.Y)) continue;

            float t = (from.Y - a.Y) / (b.Y - a.Y);
            float x = a.X + t * (b.X - a.X);
            if (x < from.X) continue;

            if (x - from.X >= nearest) continue;

            nearest = x - from.X;
            hit = new Vector2(x, from.Y);
            edge = i;
        }

        if (edge < 0) return false;

        // Of the edge's two ends, the one further right is the candidate; anything reflex inside
        // the triangle from-hit-candidate would be in the way, so the best of those wins instead.
        int candidate = loop[edge].X > loop[(edge + 1) % loop.Count].X ? edge : (edge + 1) % loop.Count;

        int best = candidate;
        float bestAngle = float.MaxValue;

        for (int i = 0; i < loop.Count; i++)
        {
            if (i == candidate) continue;
            if (!Reflex(loop, i)) continue;
            if (!InTriangle(loop[i], from, hit, loop[candidate])) continue;

            // The one nearest the ray, measured by how far it sits off it.
            var direction = loop[i] - from;
            float angle = direction.LengthSquared() < 1e-12f
                ? float.MaxValue
                : MathF.Abs(direction.Y) / direction.Length();

            if (angle >= bestAngle) continue;

            bestAngle = angle;
            best = i;
        }

        index = best;
        return true;
    }

    private static bool Reflex(List<Vector2> loop, int i)
    {
        var previous = loop[(i - 1 + loop.Count) % loop.Count];
        var here = loop[i];
        var next = loop[(i + 1) % loop.Count];

        return Cross(here - previous, next - here) < 0;
    }

    /// <summary>
    /// Clips ears off the loop until nothing is left. An ear is a corner whose triangle lies
    /// wholly inside and holds no other corner of the loop.
    /// </summary>
    private static List<int> EarClip(List<Vector2> loop)
    {
        var triangles = new List<int>();
        var remaining = new List<int>(loop.Count);
        for (int i = 0; i < loop.Count; i++) remaining.Add(i);

        // Bounded rather than "until done": a self-intersecting outline has no valid ear, and a
        // font that produces one should give a poor result rather than hang.
        int guard = loop.Count * loop.Count + 16;

        while (remaining.Count > 3 && guard-- > 0)
        {
            bool clipped = false;

            for (int k = 0; k < remaining.Count; k++)
            {
                int ia = remaining[(k - 1 + remaining.Count) % remaining.Count];
                int ib = remaining[k];
                int ic = remaining[(k + 1) % remaining.Count];

                var a = loop[ia];
                var b = loop[ib];
                var c = loop[ic];

                if (Cross(b - a, c - b) <= 0) continue; // reflex or straight: not an ear

                bool clear = true;
                foreach (int other in remaining)
                {
                    if (other == ia || other == ib || other == ic) continue;

                    // A bridge to a hole leaves two vertices sitting exactly on top of each
                    // other, and the copy would otherwise report itself as inside every ear that
                    // touches it - blocking all of them, and leaving the whole loop unclipped.
                    var p = loop[other];
                    if (p == a || p == b || p == c) continue;

                    if (!InTriangle(p, a, b, c)) continue;

                    clear = false;
                    break;
                }

                if (!clear) continue;

                triangles.Add(ia);
                triangles.Add(ib);
                triangles.Add(ic);
                remaining.RemoveAt(k);
                clipped = true;
                break;
            }

            if (!clipped) break;
        }

        if (remaining.Count == 3)
        {
            triangles.Add(remaining[0]);
            triangles.Add(remaining[1]);
            triangles.Add(remaining[2]);
        }

        return triangles;
    }

    private static float Cross(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;

    /// <summary>Inclusive of the edges, so a point sitting on one is not clipped through.</summary>
    private static bool InTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        float d1 = Cross(b - a, p - a);
        float d2 = Cross(c - b, p - b);
        float d3 = Cross(a - c, p - c);

        bool negative = d1 < 0 || d2 < 0 || d3 < 0;
        bool positive = d1 > 0 || d2 > 0 || d3 > 0;

        return !(negative && positive);
    }
}
