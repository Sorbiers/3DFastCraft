using System.Numerics;

namespace FastCraft3D.Geometry;

/// <summary>
/// Splits every triangle into four, giving smoothing something to work with.
///
/// Nothing moves: each edge gains a vertex at its midpoint and each triangle becomes four, so
/// the shape is exactly what it was, only described more finely. That is the point - smoothing
/// can only round a surface by moving the vertices it has, so a cube with eight of them has
/// nothing to round. Subdivide first and the same smoothing turns it into a rounded cube.
///
/// Midpoints are shared between the two triangles either side of an edge, so the mesh stays
/// welded and watertight rather than falling into loose triangles.
/// </summary>
public static class MeshSubdivision
{
    /// <summary>
    /// Enough triangles for smoothing to have something to work on. A shape below this is
    /// subdivided up to it; anything already denser is left alone, since an import does not need
    /// four times the triangles to be smoothed.
    /// </summary>
    public const int ComfortableDensity = 3000;

    /// <summary>Each level multiplies the triangle count by four, so this is already 256 times.</summary>
    public const int MaximumLevels = 4;

    public static Mesh Subdivide(Mesh mesh, int levels = 1)
    {
        var current = mesh;
        for (int i = 0; i < levels && i < MaximumLevels; i++) current = Once(current);
        return current;
    }

    /// <summary>
    /// How many times to split before smoothing, so a simple shape rounds and a dense one is not
    /// needlessly quadrupled.
    /// </summary>
    public static int LevelsFor(Mesh mesh, int target = ComfortableDensity)
    {
        if (mesh.TriangleCount == 0) return 0;

        int levels = 0;
        long count = mesh.TriangleCount;

        while (count < target && levels < MaximumLevels)
        {
            count *= 4;
            levels++;
        }

        return levels;
    }

    /// <summary>
    /// Splits only the edges that need it, and keeps the mesh closed while doing so.
    ///
    /// Splitting everything is far simpler and was the first thing here, but it quadruples the
    /// whole mesh to fix a handful of edges: lettering wrapped round a barrel came out at a
    /// hundred and fifty thousand triangles and took half a minute to cut. The saving is that a
    /// split is decided per edge, from its two endpoints alone, so both triangles either side of
    /// it reach the same answer and the surface cannot come apart along it.
    ///
    /// A triangle with one, two or three split edges becomes two, three or four - the standard
    /// patterns. The two-edge cases produce longer, thinner triangles than uniform splitting
    /// would, which is the price of not splitting everything else.
    /// </summary>
    /// <param name="needsSplit">Whether the edge between two points has to be broken in half.</param>
    /// <param name="maxRounds">Each round halves what it splits, so this bounds how fine it gets.</param>
    /// <param name="maxTriangles">
    /// Stops early rather than running away on a shape that asks for the impossible. The result
    /// is then a little coarser than asked for, which is far better than not arriving.
    /// </param>
    public static Mesh SplitEdgesWhere(
        Mesh mesh, Func<Vector3, Vector3, bool> needsSplit, int maxRounds = 8,
        int maxTriangles = 120_000)
    {
        var current = mesh;

        for (int round = 0; round < maxRounds; round++)
        {
            if (current.TriangleCount >= maxTriangles) break;

            var next = SplitOnce(current, needsSplit);
            if (next is null) break;

            current = next;
        }

        return current;
    }

    /// <summary>One pass. Null when nothing needed splitting, which is how the rounds end.</summary>
    private static Mesh? SplitOnce(Mesh mesh, Func<Vector3, Vector3, bool> needsSplit)
    {
        var positions = new List<Vector3>(mesh.Positions);
        var splits = new Dictionary<(int, int), int>();
        var judged = new HashSet<(int, int)>();

        for (int t = 0; t + 2 < mesh.Indices.Count; t += 3)
        {
            int a = mesh.Indices[t], b = mesh.Indices[t + 1], c = mesh.Indices[t + 2];

            Consider(a, b);
            Consider(b, c);
            Consider(c, a);
        }

        if (splits.Count == 0) return null;

        var indices = new List<int>(mesh.Indices.Count * 2);

        for (int t = 0; t + 2 < mesh.Indices.Count; t += 3)
        {
            int a = mesh.Indices[t], b = mesh.Indices[t + 1], c = mesh.Indices[t + 2];

            int ab = Split(a, b), bc = Split(b, c), ca = Split(c, a);

            if (ab < 0 && bc < 0 && ca < 0) indices.AddRange([a, b, c]);
            else if (bc < 0 && ca < 0) Two(a, ab, b, c);
            else if (ab < 0 && ca < 0) Two(b, bc, c, a);
            else if (ab < 0 && bc < 0) Two(c, ca, a, b);
            else if (ca < 0) Three(a, ab, b, bc, c);
            else if (ab < 0) Three(b, bc, c, ca, a);
            else if (bc < 0) Three(c, ca, a, ab, b);
            else
            {
                indices.AddRange([a, ab, ca]);
                indices.AddRange([ab, b, bc]);
                indices.AddRange([ca, bc, c]);
                indices.AddRange([ab, bc, ca]);
            }
        }

        return new Mesh(positions, indices);

        void Consider(int from, int to)
        {
            var key = from < to ? (from, to) : (to, from);
            if (!judged.Add(key)) return;

            if (!needsSplit(positions[from], positions[to])) return;

            splits[key] = positions.Count;
            positions.Add((positions[from] + positions[to]) * 0.5f);
        }

        int Split(int from, int to)
        {
            var key = from < to ? (from, to) : (to, from);
            return splits.TryGetValue(key, out int at) ? at : -1;
        }

        // One edge split: the opposite corner sees two triangles instead of one.
        void Two(int from, int middle, int to, int opposite)
        {
            indices.AddRange([from, middle, opposite]);
            indices.AddRange([middle, to, opposite]);
        }

        // Two edges split, meeting at one corner: a small triangle across that corner, and a fan
        // of two over the four-sided piece left behind.
        void Three(int opposite, int first, int corner, int second, int last)
        {
            indices.AddRange([first, corner, second]);
            indices.AddRange([opposite, first, second]);
            indices.AddRange([opposite, second, last]);
        }
    }

    private static Mesh Once(Mesh mesh)
    {
        var positions = new List<Vector3>(mesh.Positions);
        var indices = new List<int>(mesh.Indices.Count * 4);
        var midpoints = new Dictionary<(int, int), int>(mesh.Indices.Count);

        for (int t = 0; t + 2 < mesh.Indices.Count; t += 3)
        {
            int a = mesh.Indices[t], b = mesh.Indices[t + 1], c = mesh.Indices[t + 2];

            int ab = Midpoint(positions, midpoints, a, b);
            int bc = Midpoint(positions, midpoints, b, c);
            int ca = Midpoint(positions, midpoints, c, a);

            // Three corner triangles and the middle one, all wound as the original was.
            indices.AddRange([a, ab, ca]);
            indices.AddRange([ab, b, bc]);
            indices.AddRange([ca, bc, c]);
            indices.AddRange([ab, bc, ca]);
        }

        return new Mesh(positions, indices);
    }

    /// <summary>
    /// The vertex halfway along an edge, made once and shared. Keyed on the edge rather than the
    /// triangle, so the neighbour on the other side gets the same vertex and the surface does not
    /// come apart along every edge it was split on.
    /// </summary>
    private static int Midpoint(
        List<Vector3> positions, Dictionary<(int, int), int> made, int from, int to)
    {
        var key = from < to ? (from, to) : (to, from);
        if (made.TryGetValue(key, out int existing)) return existing;

        int index = positions.Count;
        positions.Add((positions[from] + positions[to]) * 0.5f);
        made[key] = index;

        return index;
    }
}
