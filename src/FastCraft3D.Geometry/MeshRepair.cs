using System.Numerics;

namespace FastCraft3D.Geometry;

/// <summary>
/// Post-processing that turns a geometrically-closed CSG result into a topologically
/// manifold one.
/// </summary>
public static class MeshRepair
{
    /// <summary>
    /// Removes T-junctions.
    ///
    /// BSP-based CSG routinely leaves them: when a plane cuts one face into two triangles,
    /// the neighbouring face may not be cut at the same point, so a long edge on one side
    /// meets two short edges on the other. The surface has no gap in space, but the long
    /// edge is referenced by a single triangle, which reads as a hole to any topological
    /// check - including our own pre-export watertightness test - and makes some slicers
    /// report the model as broken.
    ///
    /// Only edges that are actually unmatched are examined, so the cost is proportional to
    /// the defects rather than the mesh size. Triangles needing repair are re-triangulated
    /// as a fan around their centroid, which honours every point on the boundary; a fan from
    /// a corner would silently skip the very edges we are trying to introduce.
    /// </summary>
    /// <summary>
    /// Full clean-up applied to every CSG result.
    ///
    /// The two repairs have to run in a loop rather than once each: closing a T-junction
    /// re-triangulates and re-welds, which can merge vertices and turn two previously distinct
    /// slivers into a coincident pair - creating exactly the fins the earlier pass removed.
    /// Iterating until neither pass changes anything settles both defects together.
    /// </summary>
    public static Mesh Repair(Mesh mesh, float tolerance = 1e-4f, int maxPasses = 6)
    {
        var current = mesh;

        for (int pass = 0; pass < maxPasses; pass++)
        {
            bool changed = false;

            var deduped = RemoveDegenerateFins(current);
            if (!ReferenceEquals(deduped, current))
            {
                current = deduped;
                changed = true;
            }

            var stitched = RepairTJunctionsOnce(current, tolerance);
            if (!ReferenceEquals(stitched, current))
            {
                current = stitched;
                changed = true;
            }

            if (!changed) break;
        }

        return current;
    }

    /// <summary>
    /// Cancels coincident triangle pairs.
    ///
    /// Where two curved surfaces meet at a shallow angle, CSG can emit the same sliver twice
    /// with opposite winding - a zero-thickness "fin". It contributes no volume and no visible
    /// surface, but it adds two extra uses to each of its three edges, so those edges read as
    /// non-manifold. Because the pair cancels exactly, dropping both restores every affected
    /// edge to two users without leaving a hole; dropping only one would tear the surface.
    /// </summary>
    public static Mesh RemoveDegenerateFins(Mesh mesh)
    {
        var groups = new Dictionary<(int, int, int), List<(int Triangle, bool Positive)>>();

        for (int t = 0; t + 2 < mesh.Indices.Count; t += 3)
        {
            int a = mesh.Indices[t], b = mesh.Indices[t + 1], c = mesh.Indices[t + 2];
            var key = Sorted(a, b, c);
            // Is (a,b,c) an even permutation of the sorted triple?
            bool positive = (a < b && b < c) || (b < c && c < a) || (c < a && a < b);
            if (!groups.TryGetValue(key, out var list))
                groups[key] = list = new List<(int, bool)>();
            list.Add((t / 3, positive));
        }

        var dropped = new HashSet<int>();
        foreach (var (_, list) in groups)
        {
            if (list.Count < 2) continue;

            var positives = new List<int>();
            var negatives = new List<int>();
            foreach (var (triangle, positive) in list)
                (positive ? positives : negatives).Add(triangle);

            // Cancel as many opposing pairs as exist; any surplus keeps its orientation.
            int pairs = Math.Min(positives.Count, negatives.Count);
            for (int i = 0; i < pairs; i++)
            {
                dropped.Add(positives[i]);
                dropped.Add(negatives[i]);
            }

            // Exact duplicates of the same orientation are redundant too - keep one.
            for (int i = pairs + 1; i < positives.Count; i++) dropped.Add(positives[i]);
            for (int i = pairs + 1; i < negatives.Count; i++) dropped.Add(negatives[i]);
        }

        if (dropped.Count == 0) return mesh;

        var indices = new List<int>(mesh.Indices.Count - dropped.Count * 3);
        for (int t = 0; t + 2 < mesh.Indices.Count; t += 3)
        {
            if (dropped.Contains(t / 3)) continue;
            indices.Add(mesh.Indices[t]);
            indices.Add(mesh.Indices[t + 1]);
            indices.Add(mesh.Indices[t + 2]);
        }

        return new Mesh(mesh.Positions, indices);

        static (int, int, int) Sorted(int a, int b, int c)
        {
            if (a > b) (a, b) = (b, a);
            if (b > c) (b, c) = (c, b);
            if (a > b) (a, b) = (b, a);
            return (a, b, c);
        }
    }

    /// <summary>Returns the same instance when there is nothing to stitch, so callers can detect a no-op.</summary>
    public static Mesh RepairTJunctionsOnce(Mesh mesh, float tolerance = 1e-4f)
    {
        var unmatched = FindUnmatchedEdges(mesh);
        if (unmatched.Count == 0) return mesh;

        var intruders = FindIntrudingVertices(mesh, unmatched, tolerance);
        if (intruders.Count == 0) return mesh;

        return Retriangulate(mesh, intruders);
    }

    private static List<(int A, int B)> FindUnmatchedEdges(Mesh mesh)
    {
        var counts = new Dictionary<(int, int), int>(mesh.Indices.Count);
        for (int i = 0; i + 2 < mesh.Indices.Count; i += 3)
        {
            int a = mesh.Indices[i], b = mesh.Indices[i + 1], c = mesh.Indices[i + 2];
            Bump(counts, a, b);
            Bump(counts, b, c);
            Bump(counts, c, a);
        }

        var result = new List<(int, int)>();
        foreach (var (edge, count) in counts)
            if (count == 1)
                result.Add(edge);
        return result;

        static void Bump(Dictionary<(int, int), int> d, int x, int y)
        {
            var key = x < y ? (x, y) : (y, x);
            d[key] = d.TryGetValue(key, out int v) ? v + 1 : 1;
        }
    }

    /// <summary>
    /// For each unmatched edge, finds vertices lying strictly inside it, ordered along the
    /// edge from A to B.
    /// </summary>
    private static Dictionary<(int A, int B), List<int>> FindIntrudingVertices(
        Mesh mesh, List<(int A, int B)> edges, float tolerance)
    {
        var grid = new VertexGrid(mesh.Positions, tolerance);
        var result = new Dictionary<(int, int), List<int>>();

        foreach (var (ai, bi) in edges)
        {
            Vector3 a = mesh.Positions[ai], b = mesh.Positions[bi];
            Vector3 ab = b - a;
            float lengthSq = ab.LengthSquared();
            if (lengthSq < tolerance * tolerance) continue;

            List<(float T, int Index)>? hits = null;

            foreach (int vi in grid.QuerySegment(a, b, tolerance))
            {
                if (vi == ai || vi == bi) continue;

                Vector3 v = mesh.Positions[vi];
                float t = Vector3.Dot(v - a, ab) / lengthSq;
                if (t <= 0f || t >= 1f) continue;

                Vector3 closest = a + ab * t;
                if (Vector3.DistanceSquared(v, closest) > tolerance * tolerance) continue;

                // Ignore points that merely sit at the very ends within tolerance.
                float distanceAlong = t * MathF.Sqrt(lengthSq);
                if (distanceAlong <= tolerance || distanceAlong >= MathF.Sqrt(lengthSq) - tolerance) continue;

                (hits ??= new List<(float, int)>()).Add((t, vi));
            }

            if (hits is null) continue;

            hits.Sort((x, y) => x.T.CompareTo(y.T));
            var ordered = new List<int>(hits.Count);
            foreach (var (_, index) in hits)
                if (ordered.Count == 0 || ordered[^1] != index)
                    ordered.Add(index);

            result[(ai, bi)] = ordered;
        }

        return result;
    }

    private static Mesh Retriangulate(Mesh mesh, Dictionary<(int A, int B), List<int>> intruders)
    {
        var positions = new List<Vector3>(mesh.Positions);
        var indices = new List<int>(mesh.Indices.Count);

        for (int i = 0; i + 2 < mesh.Indices.Count; i += 3)
        {
            int a = mesh.Indices[i], b = mesh.Indices[i + 1], c = mesh.Indices[i + 2];

            var boundary = new List<int>(6);
            bool changed = false;
            changed |= AppendEdge(boundary, a, b, intruders);
            changed |= AppendEdge(boundary, b, c, intruders);
            changed |= AppendEdge(boundary, c, a, intruders);

            if (!changed)
            {
                indices.Add(a);
                indices.Add(b);
                indices.Add(c);
                continue;
            }

            // Fan from the centroid: it is strictly interior, so every boundary segment
            // becomes a real edge and no fan triangle collapses.
            Vector3 centroid = (positions[a] + positions[b] + positions[c]) / 3f;
            int centre = positions.Count;
            positions.Add(centroid);

            for (int k = 0; k < boundary.Count; k++)
            {
                int p = boundary[k];
                int q = boundary[(k + 1) % boundary.Count];
                indices.Add(centre);
                indices.Add(p);
                indices.Add(q);
            }
        }

        return new Mesh(positions, indices).Welded();
    }

    /// <summary>Appends the start vertex plus any vertices found inside the edge.</summary>
    private static bool AppendEdge(List<int> boundary, int from, int to,
        Dictionary<(int A, int B), List<int>> intruders)
    {
        boundary.Add(from);

        if (intruders.TryGetValue((from, to), out var forward))
        {
            boundary.AddRange(forward);
            return true;
        }

        if (intruders.TryGetValue((to, from), out var backward))
        {
            for (int i = backward.Count - 1; i >= 0; i--)
                boundary.Add(backward[i]);
            return true;
        }

        return false;
    }

    /// <summary>Uniform grid over the vertices, sized so a segment query touches few cells.</summary>
    private sealed class VertexGrid
    {
        private readonly Dictionary<(int, int, int), List<int>> cells = new();
        private readonly float cellSize;

        public VertexGrid(List<Vector3> positions, float tolerance)
        {
            var bounds = Bounds.FromPoints(positions);
            cellSize = bounds.IsEmpty ? 1f : MathF.Max(bounds.Diagonal / 64f, tolerance * 10f);

            for (int i = 0; i < positions.Count; i++)
            {
                var key = KeyOf(positions[i]);
                if (!cells.TryGetValue(key, out var list))
                    cells[key] = list = new List<int>();
                list.Add(i);
            }
        }

        private (int, int, int) KeyOf(Vector3 p) => (
            (int)MathF.Floor(p.X / cellSize),
            (int)MathF.Floor(p.Y / cellSize),
            (int)MathF.Floor(p.Z / cellSize));

        public IEnumerable<int> QuerySegment(Vector3 a, Vector3 b, float tolerance)
        {
            Vector3 min = Vector3.Min(a, b) - new Vector3(tolerance);
            Vector3 max = Vector3.Max(a, b) + new Vector3(tolerance);
            var lo = KeyOf(min);
            var hi = KeyOf(max);

            for (int x = lo.Item1; x <= hi.Item1; x++)
                for (int y = lo.Item2; y <= hi.Item2; y++)
                    for (int z = lo.Item3; z <= hi.Item3; z++)
                        if (cells.TryGetValue((x, y, z), out var list))
                            foreach (int i in list)
                                yield return i;
        }
    }
}
