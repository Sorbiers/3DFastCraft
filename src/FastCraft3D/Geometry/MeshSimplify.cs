using System.Numerics;

namespace FastCraft3D.Geometry;

/// <summary>
/// Cuts a mesh's triangle count down while keeping its shape.
///
/// Edges are collapsed one at a time, cheapest first, where the cost of a collapse is how far it
/// moves the surface away from the planes the original triangles lay in. That measure - Garland
/// and Heckbert's quadric error - is what makes the result look like the original rather than
/// like a melted version of it: it costs almost nothing to collapse an edge in the middle of a
/// flat panel, and a great deal to collapse one across a crease, so the flat parts thin out and
/// the features stay.
///
/// A rebuilt model is the obvious customer. Rebuilding a 20 mm cube finely enough to keep its
/// detail produces a third of a million triangles, nearly all of them describing flat faces that
/// a handful would describe just as well.
/// </summary>
public static class MeshSimplify
{
    /// <summary>
    /// A collapse that would turn a triangle inside out is refused however cheap it looks.
    /// This is the cosine of how far a face may swing before that is assumed.
    /// </summary>
    private const float MaximumFlip = 0.1f;

    /// <summary>Reduces to a fraction of the original triangle count, 0 to 1.</summary>
    public static Mesh ByFraction(Mesh mesh, float keep, CancellationToken token = default) =>
        To(mesh, (int)(mesh.TriangleCount * Math.Clamp(keep, 0.001f, 1f)));

    /// <summary>
    /// Reduces to about <paramref name="targetTriangles"/>. Blocks; a caller on the UI thread
    /// should wrap it in Task.Run.
    /// </summary>
    public static Mesh To(Mesh mesh, int targetTriangles, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();

        int triangleCount = mesh.TriangleCount;
        if (triangleCount == 0 || targetTriangles >= triangleCount) return mesh;

        var work = new Simplifier(mesh);
        work.Reduce(Math.Max(targetTriangles, 4), token);

        return work.ToMesh();
    }

    /// <summary>
    /// The working state of one simplification: the mesh pulled apart into arrays that can be
    /// edited in place, since collapsing edges is nothing but repeated small edits.
    /// </summary>
    private sealed class Simplifier
    {
        private readonly List<Vector3> points;
        private readonly int[] corners;          // three per triangle
        private readonly bool[] gone;            // triangles removed by a collapse
        private readonly HashSet<int>[] around;  // triangles touching each vertex
        private readonly double[][] quadrics;    // ten numbers per vertex: a symmetric 4x4
        private readonly bool[] onRim;
        private int alive;

        public Simplifier(Mesh mesh)
        {
            var welded = mesh.Welded();
            points = new List<Vector3>(welded.Positions);
            corners = welded.Indices.ToArray();
            gone = new bool[welded.TriangleCount];
            alive = welded.TriangleCount;

            around = new HashSet<int>[points.Count];
            for (int i = 0; i < around.Length; i++) around[i] = [];

            for (int t = 0; t < gone.Length; t++)
                for (int k = 0; k < 3; k++)
                    around[corners[t * 3 + k]].Add(t);

            quadrics = new double[points.Count][];
            for (int i = 0; i < points.Count; i++) quadrics[i] = new double[10];

            for (int t = 0; t < gone.Length; t++) AddFaceQuadric(t);

            onRim = RimVertices();
        }

        public void Reduce(int target, CancellationToken token = default)
        {
            var queue = new PriorityQueue<(int A, int B, int Version), double>();
            var version = new int[points.Count];

            for (int t = 0; t < gone.Length; t++)
                for (int k = 0; k < 3; k++)
                    Offer(queue, version, corners[t * 3 + k], corners[t * 3 + (k + 1) % 3]);

            int since = 0;

            while (alive > target && queue.TryDequeue(out var edge, out _))
            {
                // A collapse is small, so counted rather than checked every time round.
                if (++since >= 1024)
                {
                    since = 0;
                    token.ThrowIfCancellationRequested();
                }

                var (a, b, stamp) = edge;

                // Lazily discarded rather than removed: an edge whose ends have moved since it
                // was queued is simply out of date, and re-costing every neighbour's entry on
                // each collapse would cost far more than letting the stale ones fall out here.
                if (stamp != version[a] + version[b]) continue;
                if (!Collapse(a, b)) continue;

                version[a]++;
                foreach (int other in Neighbours(a)) Offer(queue, version, a, other);
            }
        }

        public Mesh ToMesh()
        {
            var indices = new List<int>(alive * 3);
            for (int t = 0; t < gone.Length; t++)
            {
                if (gone[t]) continue;
                indices.Add(corners[t * 3]);
                indices.Add(corners[t * 3 + 1]);
                indices.Add(corners[t * 3 + 2]);
            }

            return new Mesh(points, indices).Welded();
        }

        private void Offer(
            PriorityQueue<(int, int, int), double> queue, int[] version, int a, int b)
        {
            if (a == b || gone.Length == 0) return;
            if (onRim[a] || onRim[b]) return; // a hole keeps its shape

            var target = BestPosition(a, b);
            queue.Enqueue((a, b, version[a] + version[b]), Cost(a, b, target));
        }

        private IEnumerable<int> Neighbours(int vertex)
        {
            var seen = new HashSet<int>();
            foreach (int t in around[vertex])
            {
                if (gone[t]) continue;
                for (int k = 0; k < 3; k++)
                {
                    int other = corners[t * 3 + k];
                    if (other != vertex) seen.Add(other);
                }
            }

            return seen;
        }

        /// <summary>
        /// Merges b into a, if that can be done without turning anything inside out.
        /// </summary>
        private bool Collapse(int a, int b)
        {
            if (a == b || onRim[a] || onRim[b]) return false;
            if (around[a].Count == 0 || around[b].Count == 0) return false;

            var target = BestPosition(a, b);
            if (Flips(a, b, target) || Flips(b, a, target)) return false;

            // Triangles using both ends collapse to nothing and go.
            var lost = new List<int>();
            foreach (int t in around[a])
                if (!gone[t] && Uses(t, b))
                    lost.Add(t);

            // A collapse that removes no triangle is not shrinking the mesh; it would loop.
            if (lost.Count == 0) return false;

            points[a] = target;
            for (int i = 0; i < 10; i++) quadrics[a][i] += quadrics[b][i];

            foreach (int t in around[b])
            {
                if (gone[t]) continue;
                for (int k = 0; k < 3; k++)
                    if (corners[t * 3 + k] == b)
                        corners[t * 3 + k] = a;

                around[a].Add(t);
            }

            around[b].Clear();

            foreach (int t in lost)
            {
                gone[t] = true;
                alive--;
                for (int k = 0; k < 3; k++) around[corners[t * 3 + k]].Remove(t);
            }

            return true;
        }

        private bool Uses(int triangle, int vertex) =>
            corners[triangle * 3] == vertex
            || corners[triangle * 3 + 1] == vertex
            || corners[triangle * 3 + 2] == vertex;

        /// <summary>
        /// Whether moving <paramref name="from"/> to <paramref name="target"/> would swing any of
        /// its triangles round. Triangles that are about to vanish are not consulted.
        /// </summary>
        private bool Flips(int from, int other, Vector3 target)
        {
            foreach (int t in around[from])
            {
                if (gone[t] || Uses(t, other)) continue;

                Vector3 a = points[corners[t * 3]];
                Vector3 b = points[corners[t * 3 + 1]];
                Vector3 c = points[corners[t * 3 + 2]];

                var before = Vector3.Cross(b - a, c - a);
                if (before.LengthSquared() < 1e-20f) continue;

                if (corners[t * 3] == from) a = target;
                else if (corners[t * 3 + 1] == from) b = target;
                else c = target;

                var after = Vector3.Cross(b - a, c - a);
                if (after.LengthSquared() < 1e-20f) return true;

                if (Vector3.Dot(Vector3.Normalize(before), Vector3.Normalize(after)) < MaximumFlip)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Where the merged vertex should sit: the point of least error if the quadric can be
        /// solved, otherwise the best of the two ends and their midpoint.
        /// </summary>
        private Vector3 BestPosition(int a, int b)
        {
            var q = new double[10];
            for (int i = 0; i < 10; i++) q[i] = quadrics[a][i] + quadrics[b][i];

            if (Solve(q, out var solved)) return solved;

            Vector3 middle = (points[a] + points[b]) * 0.5f;
            double ea = Error(q, points[a]), eb = Error(q, points[b]), em = Error(q, middle);

            if (ea <= eb && ea <= em) return points[a];
            return eb <= em ? points[b] : middle;
        }

        private double Cost(int a, int b, Vector3 target)
        {
            var q = new double[10];
            for (int i = 0; i < 10; i++) q[i] = quadrics[a][i] + quadrics[b][i];

            return Error(q, target);
        }

        /// <summary>The quadric of every triangle touching a vertex, summed onto it.</summary>
        private void AddFaceQuadric(int triangle)
        {
            Vector3 a = points[corners[triangle * 3]];
            Vector3 b = points[corners[triangle * 3 + 1]];
            Vector3 c = points[corners[triangle * 3 + 2]];

            var normal = Vector3.Cross(b - a, c - a);
            if (normal.LengthSquared() < 1e-20f) return;

            normal = Vector3.Normalize(normal);
            double d = -Vector3.Dot(normal, a);

            // The plane as a symmetric outer product, stored as its ten distinct entries.
            double[] q =
            [
                normal.X * normal.X, normal.X * normal.Y, normal.X * normal.Z, normal.X * d,
                normal.Y * normal.Y, normal.Y * normal.Z, normal.Y * d,
                normal.Z * normal.Z, normal.Z * d,
                d * d
            ];

            for (int k = 0; k < 3; k++)
            {
                var into = quadrics[corners[triangle * 3 + k]];
                for (int i = 0; i < 10; i++) into[i] += q[i];
            }
        }

        /// <summary>How far a point sits from all the planes a quadric remembers.</summary>
        private static double Error(double[] q, Vector3 p)
        {
            double x = p.X, y = p.Y, z = p.Z;

            return q[0] * x * x + 2 * q[1] * x * y + 2 * q[2] * x * z + 2 * q[3] * x
                 + q[4] * y * y + 2 * q[5] * y * z + 2 * q[6] * y
                 + q[7] * z * z + 2 * q[8] * z
                 + q[9];
        }

        /// <summary>
        /// The point of least error, where the quadric has one. It has none along a straight edge
        /// or on a flat plane, where every point on the line or surface is equally good - which
        /// is common enough that the caller must be ready for a refusal.
        /// </summary>
        private static bool Solve(double[] q, out Vector3 point)
        {
            point = default;

            double a = q[0], b = q[1], c = q[2];
            double d = q[4], e = q[5], f = q[7];

            double determinant = a * (d * f - e * e) - b * (b * f - e * c) + c * (b * e - d * c);
            if (Math.Abs(determinant) < 1e-10) return false;

            double bx = -q[3], by = -q[6], bz = -q[8];

            double x = (bx * (d * f - e * e) - b * (by * f - e * bz) + c * (by * e - d * bz)) / determinant;
            double y = (a * (by * f - e * bz) - bx * (b * f - e * c) + c * (b * bz - by * c)) / determinant;
            double z = (a * (d * bz - by * e) - b * (b * bz - by * c) + bx * (b * e - d * c)) / determinant;

            if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(z)) return false;

            point = new Vector3((float)x, (float)y, (float)z);
            return true;
        }

        /// <summary>Vertices on the edge of an open mesh, which are never moved or merged.</summary>
        private bool[] RimVertices()
        {
            var counts = new Dictionary<(int, int), int>(corners.Length);
            for (int t = 0; t < gone.Length; t++)
                for (int k = 0; k < 3; k++)
                {
                    int x = corners[t * 3 + k], y = corners[t * 3 + (k + 1) % 3];
                    var key = x < y ? (x, y) : (y, x);
                    counts[key] = counts.GetValueOrDefault(key) + 1;
                }

            var rim = new bool[points.Count];
            foreach (var ((x, y), count) in counts)
            {
                if (count == 2) continue;
                rim[x] = true;
                rim[y] = true;
            }

            return rim;
        }
    }
}
