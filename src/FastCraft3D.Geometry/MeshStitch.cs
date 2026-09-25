using System.Numerics;

namespace FastCraft3D.Geometry;

/// <summary>
/// Joins up edges that meet a vertex partway along instead of at a corner.
///
/// A boolean leaves these behind constantly. Where it splits one polygon it need not split the
/// one next to it in the same place, so a long edge on one side ends up facing two shorter ones
/// on the other, with a vertex sitting in the middle of it. The surface is closed to look at and
/// prints perfectly, but it is not closed as a list of triangles: that one edge is used three
/// times and the two short ones once each, which is reported as a torn model.
///
/// This is why repairing could not save it. There is no hole to fill and nothing is facing the
/// wrong way - the two sides simply do not agree on where their corners are. The mend is to split
/// the long edge at the vertex already sitting on it, which changes the shape not at all.
/// </summary>
public static class MeshStitch
{
    /// <summary>
    /// How many times to go round. Each pass splits every edge that needs it, so the only reason
    /// for a second is a vertex sitting on an edge the first pass created. Three or four is
    /// already generous; the loop stops as soon as a pass finds nothing.
    /// </summary>
    private const int MaximumPasses = 8;

    /// <summary>
    /// Splits every edge that has a vertex lying along it, so both sides share their corners.
    /// The shape is untouched: the new corners are on the edge already.
    /// </summary>
    public static Mesh CloseTJunctions(Mesh mesh, float tolerance = 1e-4f)
    {
        if (mesh.TriangleCount == 0) return mesh;

        var current = mesh;
        var finder = new Finder(mesh.Positions, tolerance);

        for (int pass = 0; pass < MaximumPasses; pass++)
        {
            var next = OnePass(current, finder);
            if (next is null) break;

            current = next;
        }

        return current;
    }

    /// <summary>How many edges are still met partway along. Zero means nothing to stitch.</summary>
    public static int Count(Mesh mesh, float tolerance = 1e-4f)
    {
        if (mesh.TriangleCount == 0) return 0;

        var finder = new Finder(mesh.Positions, tolerance);
        int found = 0;

        for (int t = 0; t + 2 < mesh.Indices.Count; t += 3)
            for (int k = 0; k < 3; k++)
                if (finder.Along(mesh.Indices[t + k], mesh.Indices[t + (k + 1) % 3]) >= 0) found++;

        return found;
    }

    /// <summary>
    /// One pass, splitting all three edges of a triangle at once.
    ///
    /// Doing them one at a time was the first attempt and does not settle: two triangles either
    /// side of an edge each split whichever of their own edges came first, so for a pass the two
    /// sides disagreed about that edge as well, and on a mesh with a few dozen of them it never
    /// caught up. Splitting every edge that needs it means both sides of any edge always split it
    /// together, and the mesh is never left part-way.
    ///
    /// Null when there was nothing left to split, which is how the passes end.
    /// </summary>
    private static Mesh? OnePass(Mesh mesh, Finder finder)
    {
        var indices = new List<int>(mesh.Indices.Count);
        bool split = false;

        for (int t = 0; t + 2 < mesh.Indices.Count; t += 3)
        {
            int a = mesh.Indices[t], b = mesh.Indices[t + 1], c = mesh.Indices[t + 2];

            int ab = finder.Along(a, b), bc = finder.Along(b, c), ca = finder.Along(c, a);

            if (ab < 0 && bc < 0 && ca < 0)
            {
                indices.AddRange([a, b, c]);
                continue;
            }

            split = true;

            if (bc < 0 && ca < 0) Two(a, ab, b, c);
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

        return split ? new Mesh(new List<Vector3>(mesh.Positions), indices) : null;

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

    /// <summary>
    /// Which vertex, if any, sits along a given edge.
    ///
    /// The answer is kept once it is worked out. A pass asks about the same edge from both of the
    /// triangles either side of it, and the passes after that ask about the halves of everything
    /// already split, so without this the same search runs over and over - which is what made the
    /// first version of this slower than the boolean it was mending.
    /// </summary>
    private sealed class Finder
    {
        private readonly IReadOnlyList<Vector3> points;
        private readonly float tolerance;
        private readonly Dictionary<(int, int), int> known = new();
        private readonly Dictionary<(int, int, int), List<int>> cells = new();
        private readonly float cell;

        public Finder(IReadOnlyList<Vector3> positions, float toleranceMm)
        {
            points = positions;
            tolerance = toleranceMm;

            var low = new Vector3(float.MaxValue);
            var high = new Vector3(float.MinValue);
            foreach (var p in points) { low = Vector3.Min(low, p); high = Vector3.Max(high, p); }

            // Cells sized by the square root of the count, not the cube root: the vertices of a
            // mesh sit on a surface rather than filling the space, so cube-rooting leaves scores
            // of them in every cell and each lookup walks the lot.
            float span = points.Count == 0 ? 1f : MathF.Max((high - low).Length(), 1e-3f);
            cell = MathF.Max(span / MathF.Max(MathF.Sqrt(points.Count), 1f), tolerance * 4f);

            for (int i = 0; i < points.Count; i++)
            {
                var at = At(points[i]);
                if (!cells.TryGetValue(at, out var bucket)) cells[at] = bucket = new List<int>();
                bucket.Add(i);
            }
        }

        /// <summary>
        /// The vertex nearest the start of this edge that lies strictly along it, or -1. Taking
        /// the nearest means repeated passes work their way along an edge in order.
        /// </summary>
        public int Along(int from, int to)
        {
            var key = from < to ? (from, to) : (to, from);
            if (known.TryGetValue(key, out int cached)) return cached;

            int found = Search(from, to);
            known[key] = found;

            return found;
        }

        private int Search(int from, int to)
        {
            var p = points[from];
            var q = points[to];

            var line = q - p;
            float lengthSq = line.LengthSquared();
            if (lengthSq < tolerance * tolerance) return -1;

            int best = -1;
            float nearest = float.MaxValue;

            var (x0, y0, z0) = At(Vector3.Min(p, q));
            var (x1, y1, z1) = At(Vector3.Max(p, q));

            for (int x = x0 - 1; x <= x1 + 1; x++)
                for (int y = y0 - 1; y <= y1 + 1; y++)
                    for (int z = z0 - 1; z <= z1 + 1; z++)
                    {
                        if (!cells.TryGetValue((x, y, z), out var bucket)) continue;

                        foreach (int i in bucket)
                        {
                            if (i == from || i == to) continue;

                            float along = Vector3.Dot(points[i] - p, line) / lengthSq;

                            // Strictly between the ends: a vertex at either end is a corner they
                            // already share, and welding is what deals with those.
                            if (along <= 0 || along >= 1 || along >= nearest) continue;
                            if ((points[i] - (p + line * along)).LengthSquared() > tolerance * tolerance)
                                continue;

                            nearest = along;
                            best = i;
                        }
                    }

            return best;
        }

        private (int, int, int) At(Vector3 p) => (
            (int)MathF.Floor(p.X / cell),
            (int)MathF.Floor(p.Y / cell),
            (int)MathF.Floor(p.Z / cell));
    }
}
