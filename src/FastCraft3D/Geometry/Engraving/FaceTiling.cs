using System.Numerics;

namespace FastCraft3D.Geometry.Engraving;

/// <summary>
/// Retiles the patterned part of a face around outlines of any shape at all - grain, a ring, a
/// logo - the way <see cref="FaceRelief"/>'s cell grid does for rectangles.
///
/// The grid cannot take a curve: it changes state only at a rectangle's edge, so a flowing line
/// would need a column per sample point and the grid is the product of its columns and its rows.
/// A vertical decomposition has the same virtue and no such cost. Split the face at every
/// outline vertex's u, and inside one of those slabs no outline has a corner - every edge
/// crossing it is a single straight segment from one side to the other. Sort those segments by
/// height and the slab falls into a stack of bands, each of them wholly inside a shape or wholly
/// outside it, decided by the winding number as the stack is walked upward.
///
/// Ear clipping would have been the obvious answer and does not scale: the background is the
/// face with one hole per brick or per grain line, and clipping ears off a loop of several
/// thousand points is quadratic in a way this is not.
///
/// Every wall is read back out of the bands rather than drawn along the outlines, and that is
/// worth the extra bookkeeping. Two shapes that overlap have outline inside solid material, and
/// walling along it puts a partition inside the piece; asking the bands where the level actually
/// changes cannot make that mistake, so overlapping shapes simply merge - which is what someone
/// who has drawn two circles on top of each other means by it.
///
/// What makes the result watertight is the one fiddly part. Two neighbouring slabs need not
/// break their shared column at the same heights - a shape may begin exactly there - so every
/// band's vertical side is divided at every height either side asks for. Then each little
/// segment of that column is covered exactly once from the left and once from the right, and
/// where the two are at different levels a wall goes in between them.
/// </summary>
internal static class FaceTiling
{
    /// <summary>Columns times edges beyond this and the decomposition costs more than it saves.</summary>
    public const long MaximumWork = 20_000_000;

    /// <summary>And a limit on what comes out, for a drawing far finer than anything prints.</summary>
    public const int MaximumBands = 500_000;

    /// <summary>How close two coordinates must be to count as the same one.</summary>
    private const float Eps = 1e-4f;

    /// <summary>
    /// Lays <paramref name="shapes"/> proud of the face across <paramref name="area"/>, filling
    /// everything the shapes do not cover at the face's own level.
    /// </summary>
    /// <param name="filled">The patterned area, as triangles.</param>
    /// <param name="inner">
    /// Where the frame around it has to meet it: the points along each of the four sides of
    /// <paramref name="area"/>, in winding order, starting at the bottom-left corner.
    /// </param>
    public static bool TryFill(
        FacePatch face, Rect2 area, IReadOnlyList<IReadOnlyList<Vector2>> shapes, float rise,
        out Mesh filled, out List<Vector2>[] inner)
    {
        filled = new Mesh();
        inner = [];

        if (rise <= 0 || area.IsEmpty || shapes.Count == 0) return false;

        var loops = Oriented(shapes);
        if (loops.Count == 0 || !Within(loops, area)) return false;

        float[] us = Columns(loops, area);
        if (us.Length < 2) return false;

        Snap(loops, us);

        var edges = Edges(loops);
        if (edges.Count == 0) return false;
        if ((long)us.Length * edges.Count > MaximumWork) return false;
        if (Cross(edges, us)) return false;

        float[][] breaks = Breaks(edges, us, area);

        var stacks = new List<Band>[us.Length - 1];
        int total = 0;

        for (int slab = 0; slab < stacks.Length; slab++)
        {
            stacks[slab] = Bands(edges, area, us[slab], us[slab + 1], breaks[slab], breaks[slab + 1]);
            total += stacks[slab].Count;

            if (total > MaximumBands) return false;
        }

        var mesh = new Mesh();

        for (int slab = 0; slab < stacks.Length; slab++)
        {
            AddTiles(mesh, face, breaks, slab, us[slab], us[slab + 1], stacks[slab], rise);
            AddSteps(mesh, face, us[slab], us[slab + 1], stacks[slab], rise);
        }

        for (int column = 0; column < us.Length; column++)
            AddColumnSteps(mesh, face, breaks[column], us[column], stacks, column, rise);

        filled = mesh;
        inner = Sides(us, breaks, area);
        return true;
    }

    /// <summary>
    /// The outlines of one ribbon: one loop for an open strip, two for a ring - the outside and
    /// the inside, which the nesting works out for itself.
    ///
    /// A strip that runs left to right without ever turning back is offset straight up and down
    /// rather than at right angles to itself. That is not laziness about the width - it is the
    /// difference between an outline whose columns are the centre line's own, and one with two
    /// fresh columns per point. Grain is a dozen lines sharing one set of samples, so keeping
    /// them costs a hundred columns instead of several thousand. What it costs in return is a
    /// line on a slope coming out very slightly narrower than asked, which nothing will see.
    /// </summary>
    public static List<List<Vector2>> Outline(Polyline2 ribbon)
    {
        if (!GrooveSolid.Edges(ribbon, out var points, out var left, out var right)) return [];

        float half = ribbon.Width * 0.5f;

        if (!ribbon.Closed && IsGraph(points))
        {
            var loop = points.Select(p => new Vector2(p.X, p.Y + half)).ToList();

            for (int i = points.Count - 1; i >= 0; i--)
                loop.Add(new Vector2(points[i].X, points[i].Y - half));

            return [loop];
        }

        if (ribbon.Closed) return [left.ToList(), right.ToList()];

        var strip = new List<Vector2>(left);
        for (int i = right.Length - 1; i >= 0; i--) strip.Add(right[i]);

        return [strip];
    }

    private static bool IsGraph(List<Vector2> points)
    {
        for (int i = 1; i < points.Count; i++)
            if (points[i].X <= points[i - 1].X)
                return false;

        return true;
    }

    // --- Setting up ---------------------------------------------------------------------

    /// <summary>
    /// Outlines anticlockwise, holes clockwise, so that walking upward through the stack and
    /// adding one for each edge crossed leaves the count at zero exactly where the material is
    /// not - and above zero where two shapes overlap, which is how they come out merged.
    /// </summary>
    private static List<List<Vector2>> Oriented(IReadOnlyList<IReadOnlyList<Vector2>> shapes)
    {
        var given = shapes.Where(s => s.Count >= 3).Select(s => s.ToList()).ToList();
        var loops = new List<List<Vector2>>();

        foreach (var (outline, holes) in Polygon2.Nest(given))
        {
            loops.Add(Wound(outline, anticlockwise: true));

            foreach (var hole in holes)
                loops.Add(Wound(hole, anticlockwise: false));
        }

        return loops;
    }

    private static List<Vector2> Wound(List<Vector2> loop, bool anticlockwise)
    {
        if (Polygon2.SignedArea(loop) < 0 == anticlockwise) loop.Reverse();
        return loop;
    }

    private static bool Within(List<List<Vector2>> loops, Rect2 area)
    {
        foreach (var loop in loops)
            foreach (var point in loop)
                if (point.X < area.MinU - Eps || point.X > area.MaxU + Eps ||
                    point.Y < area.MinV - Eps || point.Y > area.MaxV + Eps)
                    return false;

        return true;
    }

    /// <summary>Every u the arrangement can change at: the area's sides and every corner.</summary>
    private static float[] Columns(List<List<Vector2>> loops, Rect2 area)
    {
        var values = new List<float> { area.MinU, area.MaxU };

        foreach (var loop in loops)
            foreach (var point in loop)
                values.Add(point.X);

        return Distinct(values);
    }

    private static float[] Distinct(List<float> values)
    {
        values.Sort();

        var kept = new List<float> { values[0] };
        foreach (float value in values)
            if (value - kept[^1] > Eps)
                kept.Add(value);

        return kept.ToArray();
    }

    /// <summary>
    /// Puts every corner exactly on a column. Two that were a hair apart become one, which turns
    /// their edge vertical - handled - where leaving them apart would leave a slab a hair wide
    /// and a band too thin to be any use.
    /// </summary>
    private static void Snap(List<List<Vector2>> loops, float[] us)
    {
        foreach (var loop in loops)
            for (int i = 0; i < loop.Count; i++)
                loop[i] = new Vector2(Nearest(us, loop[i].X), loop[i].Y);
    }

    private static float Nearest(float[] values, float value)
    {
        int index = Array.BinarySearch(values, value);
        if (index >= 0) return values[index];

        int after = ~index;
        if (after == 0) return values[0];
        if (after == values.Length) return values[^1];

        return value - values[after - 1] <= values[after] - value ? values[after - 1] : values[after];
    }

    private static List<Edge> Edges(List<List<Vector2>> loops)
    {
        var edges = new List<Edge>();

        foreach (var loop in loops)
            for (int i = 0; i < loop.Count; i++)
            {
                var from = loop[i];
                var to = loop[(i + 1) % loop.Count];

                if (from != to) edges.Add(new Edge(from, to));
            }

        return edges;
    }

    /// <summary>
    /// The heights each column has to be broken at: where an edge crosses it, where one ends on
    /// it, and the area's own top and bottom.
    /// </summary>
    private static float[][] Breaks(List<Edge> edges, float[] us, Rect2 area)
    {
        var breaks = new List<float>[us.Length];
        for (int column = 0; column < us.Length; column++) breaks[column] = [area.MinV, area.MaxV];

        foreach (var edge in edges)
        {
            int from = Array.BinarySearch(us, edge.MinU);
            int to = Array.BinarySearch(us, edge.MaxU);
            if (from < 0 || to < 0) continue; // snapped, so this cannot happen

            for (int column = from; column <= to; column++)
                breaks[column].Add(edge.VAt(us[column]));
        }

        return breaks.Select(Distinct).ToArray();
    }

    /// <summary>
    /// Whether any two outlines cross each other, which is the one thing here has no answer for.
    ///
    /// Two reasons, and the second is the fatal one. A slab works because an edge runs clean
    /// through it without changing places with its neighbours, so sorting them once at the
    /// middle gives the order everywhere in it; a crossing breaks that. Worse, which loops are
    /// holes is decided by asking whether one loop's point lies inside another, and for two
    /// shapes that overlap that question has no answer - the point is inside for some of the
    /// loop and outside for the rest, so the winding comes out backwards over the overlap.
    ///
    /// Splitting the arrangement at every intersection would settle both, and is a good deal
    /// more machinery than the case deserves: a drawing whose shapes overlap is unusual, and the
    /// boolean unions them correctly, if slowly. So this says no and the caller takes that road.
    /// </summary>
    private static bool Cross(List<Edge> edges, float[] us)
    {
        long work = 0;

        for (int slab = 0; slab + 1 < us.Length; slab++)
        {
            float left = us[slab], right = us[slab + 1];
            var here = new List<Edge>();

            foreach (var edge in edges)
                if (!edge.IsUpright && edge.MinU <= left && edge.MaxU >= right)
                    here.Add(edge);

            work += (long)here.Count * here.Count;
            if (work > MaximumWork) return true; // too tangled to be worth checking

            for (int i = 0; i < here.Count; i++)
            {
                float leftI = here[i].VAt(left), rightI = here[i].VAt(right);

                for (int j = i + 1; j < here.Count; j++)
                {
                    float below = leftI - here[j].VAt(left);
                    float above = rightI - here[j].VAt(right);

                    // One passes the other only if it starts clear on one side and ends clear on
                    // the other. Two that stay within a hair of each other are the same line twice.
                    if (below > Eps && above < -Eps) return true;
                    if (below < -Eps && above > Eps) return true;
                }
            }
        }

        return false;
    }

    // --- One slab -------------------------------------------------------------------------

    /// <summary>
    /// The stack between two columns. Every edge crossing the slab runs clean through it, so
    /// sorting them by height gives the order they are met in, and the winding count says which
    /// of the bands between them is material.
    /// </summary>
    /// <param name="downBreaks">Where the left column is broken, and</param>
    /// <param name="upBreaks">where the right one is. Every band boundary is put on one of
    /// them, so what the bands say and what the columns say cannot drift apart - at a crossing
    /// the two edges arrive within a whisper of each other, and a whisper is enough to leave a
    /// seam if one side rounds it differently from the other.</param>
    private static List<Band> Bands(
        List<Edge> edges, Rect2 area, float left, float right, float[] downBreaks, float[] upBreaks)
    {
        float middle = (left + right) * 0.5f;
        var crossing = new List<(float Left, float Right, float Middle, int Turn)>();

        foreach (var edge in edges)
        {
            if (edge.IsUpright || edge.MinU > left || edge.MaxU < right) continue;

            crossing.Add((
                Nearest(downBreaks, edge.VAt(left)), Nearest(upBreaks, edge.VAt(right)),
                edge.VAt(middle), edge.To.X > edge.From.X ? 1 : -1));
        }

        crossing.Sort((a, b) => a.Middle.CompareTo(b.Middle));

        var bands = new List<Band>(crossing.Count + 1);
        float lowLeft = area.MinV, lowRight = area.MinV;
        int winding = 0;

        foreach (var (edgeLeft, edgeRight, _, turn) in crossing)
        {
            bands.Add(new Band(lowLeft, edgeLeft, lowRight, edgeRight, winding != 0));

            winding += turn;
            lowLeft = edgeLeft;
            lowRight = edgeRight;
        }

        bands.Add(new Band(lowLeft, area.MaxV, lowRight, area.MaxV, winding != 0));
        return bands;
    }

    private static void AddTiles(
        Mesh mesh, FacePatch face, float[][] breaks, int slab, float left, float right,
        List<Band> bands, float rise)
    {
        foreach (var band in bands)
        {
            if (band.LeftHigh - band.LeftLow <= Eps && band.RightHigh - band.RightLow <= Eps)
                continue;

            AddTile(mesh, face, breaks[slab], breaks[slab + 1], left, right, band,
                band.Up ? rise : 0f);
        }
    }

    /// <summary>
    /// One band, with both its vertical sides broken wherever the columns say - which is what
    /// makes it meet the slab next door edge for edge.
    /// </summary>
    private static void AddTile(
        Mesh mesh, FacePatch face, float[] downBreaks, float[] upBreaks,
        float left, float right, Band band, float height)
    {
        var down = Chain(downBreaks, band.LeftLow, band.LeftHigh);
        var up = Chain(upBreaks, band.RightLow, band.RightHigh);

        int i = 0, j = 0;

        while (i < down.Count - 1 || j < up.Count - 1)
        {
            bool takeRight = i >= down.Count - 1 ||
                (j < up.Count - 1 && up[j + 1] <= down[i + 1]);

            if (takeRight)
            {
                Add(mesh, face, height,
                    new Vector2(left, down[i]), new Vector2(right, up[j]),
                    new Vector2(right, up[j + 1]));
                j++;
            }
            else
            {
                Add(mesh, face, height,
                    new Vector2(left, down[i]), new Vector2(right, up[j]),
                    new Vector2(left, down[i + 1]));
                i++;
            }
        }
    }

    private static List<float> Chain(float[] breaks, float low, float high)
    {
        if (high - low <= Eps) return [low];

        var chain = new List<float> { low };

        foreach (float value in breaks)
            if (value > low + Eps && value < high - Eps)
                chain.Add(value);

        chain.Add(high);
        return chain;
    }

    private static void Add(Mesh mesh, FacePatch face, float height, Vector2 a, Vector2 b, Vector2 c) =>
        mesh.AddTriangle(face.ToLocal(a, height), face.ToLocal(b, height), face.ToLocal(c, height));

    // --- Where the level changes ---------------------------------------------------------

    /// <summary>
    /// The wall between one band and the one above it. Walking it with the material on the left
    /// is what faces the wall outward, so which way round depends on which of the two is raised.
    /// </summary>
    private static void AddSteps(
        Mesh mesh, FacePatch face, float left, float right, List<Band> bands, float rise)
    {
        for (int k = 0; k + 1 < bands.Count; k++)
        {
            if (bands[k].Up == bands[k + 1].Up) continue;

            var at = new Vector2(left, bands[k].LeftHigh);
            var to = new Vector2(right, bands[k].RightHigh);

            if (bands[k + 1].Up) GrooveSolid.AddWall(mesh, face, at, to, rise, 0f);
            else GrooveSolid.AddWall(mesh, face, to, at, rise, 0f);
        }
    }

    /// <summary>
    /// The wall standing on a column, where the slab on one side of it is raised and the slab on
    /// the other is not. That is where a shape has a vertical edge, or begins and ends exactly
    /// on the column - the one thing the sloping steps cannot express.
    /// </summary>
    private static void AddColumnSteps(
        Mesh mesh, FacePatch face, float[] breaks, float u, List<Band>[] stacks, int column,
        float rise)
    {
        var before = column > 0 ? stacks[column - 1] : null;
        var after = column < stacks.Length ? stacks[column] : null;

        for (int i = 0; i + 1 < breaks.Length; i++)
        {
            float low = breaks[i], high = breaks[i + 1];
            if (high - low <= Eps) continue;

            float middle = (low + high) * 0.5f;

            // Outside the area is the frame, which is at the face's own level.
            bool leftUp = before is not null && UpAt(before, middle, right: true);
            bool rightUp = after is not null && UpAt(after, middle, right: false);

            if (leftUp == rightUp) continue;

            var bottom = new Vector2(u, low);
            var top = new Vector2(u, high);

            // Walking upward keeps what is to the left of the column on the left.
            if (leftUp) GrooveSolid.AddWall(mesh, face, bottom, top, rise, 0f);
            else GrooveSolid.AddWall(mesh, face, top, bottom, rise, 0f);
        }
    }

    /// <summary>
    /// Whether the stack is material at a given height on one of its two sides. The last band
    /// starting at or below it is the one it is in, which steps over any band pinched to nothing.
    /// </summary>
    private static bool UpAt(List<Band> bands, float v, bool right)
    {
        bool up = false;

        foreach (var band in bands)
        {
            if ((right ? band.RightLow : band.LeftLow) > v) break;
            up = band.Up;
        }

        return up;
    }

    // --- Where the frame meets it -------------------------------------------------------------

    private static List<Vector2>[] Sides(float[] us, float[][] breaks, Rect2 area)
    {
        var bottom = us.Select(u => new Vector2(u, area.MinV)).ToList();
        var top = us.Reverse().Select(u => new Vector2(u, area.MaxV)).ToList();

        var right = breaks[^1].Select(v => new Vector2(area.MaxU, v)).ToList();
        var left = breaks[0].Reverse().Select(v => new Vector2(area.MinU, v)).ToList();

        return [bottom, right, top, left];
    }

    // --- Pieces ---------------------------------------------------------------------------------

    /// <param name="Up">Whether this band is inside a shape, and so stands proud.</param>
    private readonly record struct Band(
        float LeftLow, float LeftHigh, float RightLow, float RightHigh, bool Up);

    private readonly record struct Edge(Vector2 From, Vector2 To)
    {
        public float MinU => Math.Min(From.X, To.X);
        public float MaxU => Math.Max(From.X, To.X);

        public bool IsUpright => From.X == To.X;

        /// <summary>Endpoints are returned exactly, so a shared corner cannot drift apart.</summary>
        public float VAt(float u)
        {
            if (u == From.X) return From.Y;
            if (u == To.X) return To.Y;

            return From.Y + (To.Y - From.Y) * (u - From.X) / (To.X - From.X);
        }
    }
}
