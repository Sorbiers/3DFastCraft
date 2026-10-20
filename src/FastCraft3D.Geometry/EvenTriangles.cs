using System.Numerics;

namespace FastCraft3D.Geometry;

/// <param name="Mesh">The finer mesh, or the one given when nothing changed.</param>
/// <param name="TrianglesBefore">Triangles before.</param>
/// <param name="TrianglesAfter">And after.</param>
/// <param name="Refusal">Why it was left as it was, when it was refused.</param>
/// <param name="Changed">Whether <paramref name="Mesh"/> is a new mesh.</param>
public sealed record EvenTrianglesResult(Mesh Mesh, int TrianglesBefore, int TrianglesAfter, string? Refusal, bool Changed);

/// <summary>
/// Cuts a mesh into triangles of about one size without changing its shape.
///
/// Rebuild from scratch makes even triangles too, but by resampling the surface on a grid, which
/// rounds every edge and loses everything finer than a voxel. Here every new point is put on the
/// surface that is already there: an edge longer than wanted is split at its middle, which leaves
/// both triangles beside it exactly where they were; an edge between two triangles in one plane
/// is turned when that makes them less thin; and a point in the middle of a flat face is slid
/// within the face towards the middle of its neighbours. A point on a corner or a crease never
/// moves, so the shape is the same to the last rounding error.
///
/// Worth having before Smooth, Deform or a texture, which need points to move and a fine, even
/// mesh to move them on, and before a boolean, which goes wrong on slivers. A printer gains
/// nothing from it.
/// </summary>
public static class EvenTriangles
{
    /// <summary>More than this and the result would be too heavy to work with, so it is refused.</summary>
    public const int MostTriangles = 2_000_000;

    /// <summary>How far off one plane four points may be and still count as one flat face for turning an edge.</summary>
    private const double FlatMm = 1e-5;

    /// <summary>How many triangles a surface of this area comes to at this size, near enough to warn by.</summary>
    public static long Estimate(double areaMm2, float edgeMm) =>
        edgeMm <= 0 ? long.MaxValue : (long)(areaMm2 / (0.433 * edgeMm * edgeMm) * 1.2);

    public static EvenTrianglesResult Refine(Mesh input, float edgeMm, CancellationToken token = default)
    {
        EvenTrianglesResult Keep(string? why) => new(input, input.TriangleCount, input.TriangleCount, why, false);

        if (input.TriangleCount == 0 || !(edgeMm > 0)) return Keep(null);

        long estimate = Estimate(input.ComputeSurfaceArea(), edgeMm);
        if (estimate + input.TriangleCount > MostTriangles)
            return Keep($"that would be about {estimate:N0} triangles - choose a larger size");

        var mesh = input.Welded();
        var points = new List<Vector3>(mesh.Positions);
        var tris = new List<int[]>(mesh.TriangleCount);
        for (int i = 0; i + 2 < mesh.Indices.Count; i += 3)
            tris.Add([mesh.Indices[i], mesh.Indices[i + 1], mesh.Indices[i + 2]]);

        // Split at a third over the size and points are never closer than two thirds of it: the
        // usual band, which settles rather than splitting the same edge back and forth.
        double longest = edgeMm * 4.0 / 3.0;
        bool any = false;

        for (int round = 0; round < 6; round++)
        {
            token.ThrowIfCancellationRequested();
            int split = Split(points, tris, longest, token);
            if (tris.Count > MostTriangles) return Keep("it would come to too many triangles - choose a larger size");

            int turned = Turn(points, tris, token);
            int slid = Slide(points, tris, edgeMm, token);
            Turn(points, tris, token);

            any |= split > 0 || turned > 0 || slid > 0;
            if (split == 0 && slid == 0) break;
        }

        // Sliding can stretch an edge back over the size; one more split and turn without it.
        if (Split(points, tris, longest, token) > 0)
        {
            any = true;
            Turn(points, tris, token);
        }

        if (!any) return Keep(null);

        var indices = new List<int>(tris.Count * 3);
        foreach (var t in tris) indices.AddRange(t);
        var result = FaceRebuild.Compacted(points, indices);

        var was = mesh.CheckHealth();
        if (Worse(result.CheckHealth(), was))
        {
            // Given a chance to be mended before it is turned down - a point dropped while a
            // face beside it still used it leaves a hairline crack, which is the kind of thing
            // Repair closes. Kept only if mending actually gets it back to no worse than it came;
            // the volume and size checks below still have their say on what the mending did.
            var mended = MeshHealer.Heal(result).Mesh;
            if (Worse(mended.CheckHealth(), was)) return Keep("the result would not have been watertight");
            result = mended;
        }

        double before = mesh.ComputeSignedVolume(), after = result.ComputeSignedVolume();
        if (Math.Abs(after - before) > 1e-6 * Math.Abs(before) + 1e-3)
            return Keep("the volume would have changed");

        var boxBefore = mesh.ComputeBounds();
        var boxAfter = result.ComputeBounds();
        if (Vector3.Distance(boxBefore.Min, boxAfter.Min) > 1e-4f || Vector3.Distance(boxBefore.Max, boxAfter.Max) > 1e-4f)
            return Keep("the size would have changed");

        return new EvenTrianglesResult(result, input.TriangleCount, result.TriangleCount, null, true);
    }

    /// <summary>
    /// Splits every edge longer than <paramref name="longest"/> at its middle, in both triangles
    /// beside it, so no edge is ever left met partway. Longest first, and an edge whose
    /// triangles were already split this pass waits for the next.
    /// </summary>
    private static int Split(List<Vector3> points, List<int[]> tris, double longest, CancellationToken token)
    {
        int total = 0;

        for (int pass = 0; pass < 40; pass++)
        {
            token.ThrowIfCancellationRequested();

            var edges = Edges(tris);
            var tooLong = edges
                // Only an edge two triangles share. One on a crack - a triangle on one side only -
                // split at its middle puts a point the other side of the crack never gets, and one
                // open edge becomes two: a part already cracked was refused for being made worse.
                .Where(e => e.Value.Count == 2)
                .Select(e => (e.Key, e.Value, Length: Length(points, e.Key)))
                .Where(e => e.Length > longest)
                .OrderByDescending(e => e.Length).ThenBy(e => e.Key)
                .ToList();
            if (tooLong.Count == 0) break;

            var touched = new HashSet<int>();
            int split = 0;
            foreach (var (key, around, _) in tooLong)
            {
                if (around.Any(touched.Contains)) continue;

                int a = (int)(key >> 32), b = (int)(key & 0xFFFFFFFF);
                int m = points.Count;
                points.Add((points[a] + points[b]) * 0.5f);

                foreach (int t in around)
                {
                    var tri = tris[t];
                    for (int k = 0; k < 3; k++)
                    {
                        int x = tri[k], y = tri[(k + 1) % 3], c = tri[(k + 2) % 3];
                        if (!((x == a && y == b) || (x == b && y == a))) continue;

                        tris[t] = [x, m, c];
                        tris.Add([m, y, c]);
                        touched.Add(t);
                        touched.Add(tris.Count - 1);
                        break;
                    }
                }

                split++;
                if (tris.Count > MostTriangles) return total + split;
            }

            total += split;
        }

        return total;
    }

    /// <summary>
    /// Turns an edge between two triangles lying in one plane when that makes the thinner of the
    /// pair fatter. An edge where the surface bends is never turned: that would change the shape.
    /// </summary>
    private static int Turn(List<Vector3> points, List<int[]> tris, CancellationToken token)
    {
        int total = 0;

        for (int pass = 0; pass < 12; pass++)
        {
            token.ThrowIfCancellationRequested();

            var edges = Edges(tris);
            var existing = new HashSet<long>(edges.Keys);
            var touched = new HashSet<int>();
            int turned = 0;

            foreach (var (key, around) in edges)
            {
                if (around.Count != 2) continue;
                int t1 = around[0], t2 = around[1];
                if (touched.Contains(t1) || touched.Contains(t2)) continue;

                int a = (int)(key >> 32), b = (int)(key & 0xFFFFFFFF);
                if (!Opposite(tris[t1], a, b, out int p, out int q, out int c)) continue;
                if (!Opposite(tris[t2], q, p, out _, out _, out int d)) continue;
                if (c == d || existing.Contains(Key(c, d))) continue;

                Vector3 pp = points[p], pq = points[q], pc = points[c], pd = points[d];
                var n = Cross(pq - pp, pc - pp);
                double length = Math.Sqrt(Dot(n, n));
                if (length <= 0) continue;
                if (Math.Abs(Dot(n, Sub(pd, pp))) / length > FlatMm) continue;

                // The pair (p, q, c) and (q, p, d) becomes (c, p, d) and (d, q, c).
                var n1 = Cross(pp - pc, pd - pc);
                var n2 = Cross(pq - pd, pc - pd);
                if (Dot(n1, n) <= 0 || Dot(n2, n) <= 0) continue;

                double now = Math.Min(Smallest(pp, pq, pc), Smallest(pq, pp, pd));
                double then = Math.Min(Smallest(pc, pp, pd), Smallest(pd, pq, pc));
                if (then <= now + 1e-4) continue;

                tris[t1] = [c, p, d];
                tris[t2] = [d, q, c];
                existing.Remove(key);
                existing.Add(Key(c, d));
                touched.Add(t1);
                touched.Add(t2);
                turned++;
            }

            total += turned;
            if (turned == 0) break;
        }

        return total;
    }

    /// <summary>
    /// Slides each point in the middle of a flat face halfway towards the middle of its
    /// neighbours, within the face. A point whose neighbours are not all in one plane with it is
    /// on a corner or a crease, and stays.
    /// </summary>
    private static int Slide(List<Vector3> points, List<int[]> tris, float edgeMm, CancellationToken token)
    {
        var around = new List<int>?[points.Count];
        for (int t = 0; t < tris.Count; t++)
            foreach (int v in tris[t])
                (around[v] ??= new List<int>(6)).Add(t);

        var edges = Edges(tris);
        var open = new HashSet<int>();
        foreach (var (key, list) in edges)
            if (list.Count != 2)
            {
                open.Add((int)(key >> 32));
                open.Add((int)(key & 0xFFFFFFFF));
            }

        double thinnest = edgeMm * 0.05;
        int moved = 0;

        for (int v = 0; v < points.Count; v++)
        {
            if ((v & 4095) == 0) token.ThrowIfCancellationRequested();
            if (around[v] is not { Count: >= 3 } star || open.Contains(v)) continue;

            var at = points[v];
            var normal = new Vector3D();
            foreach (int t in star) normal = normal.Add(Cross(points[tris[t][1]] - points[tris[t][0]], points[tris[t][2]] - points[tris[t][0]]));
            double length = Math.Sqrt(normal.X * normal.X + normal.Y * normal.Y + normal.Z * normal.Z);
            if (length <= 0) continue;
            var n = new Vector3((float)(normal.X / length), (float)(normal.Y / length), (float)(normal.Z / length));

            var neighbours = new HashSet<int>();
            foreach (int t in star)
                foreach (int u in tris[t])
                    if (u != v) neighbours.Add(u);

            bool flat = true;
            var middle = Vector3.Zero;
            foreach (int u in neighbours)
            {
                if (Math.Abs(Vector3.Dot(points[u] - at, n)) > FlatMm) { flat = false; break; }
                middle += points[u];
            }
            if (!flat) continue;

            middle /= neighbours.Count;
            var step = (middle - at) * 0.5f;
            step -= n * Vector3.Dot(step, n);
            var to = at + step;

            bool fine = true;
            foreach (int t in star)
            {
                var tri = tris[t];
                var a = tri[0] == v ? to : points[tri[0]];
                var b = tri[1] == v ? to : points[tri[1]];
                var c = tri[2] == v ? to : points[tri[2]];
                var cross = Vector3.Cross(b - a, c - a);
                float longestSide = MathF.Max(Vector3.Distance(a, b), MathF.Max(Vector3.Distance(b, c), Vector3.Distance(c, a)));
                if (Vector3.Dot(cross, n) <= 0 || cross.Length() / longestSide < thinnest) { fine = false; break; }
            }

            if (!fine) continue;
            points[v] = to;
            moved++;
        }

        return moved;
    }

    private static bool Opposite(int[] tri, int a, int b, out int p, out int q, out int c)
    {
        for (int k = 0; k < 3; k++)
        {
            int x = tri[k], y = tri[(k + 1) % 3];
            if ((x == a && y == b) || (x == b && y == a))
            {
                p = x;
                q = y;
                c = tri[(k + 2) % 3];
                return true;
            }
        }

        p = q = c = -1;
        return false;
    }

    private static Dictionary<long, List<int>> Edges(List<int[]> tris)
    {
        var edges = new Dictionary<long, List<int>>(tris.Count * 2);
        for (int t = 0; t < tris.Count; t++)
            for (int k = 0; k < 3; k++)
            {
                long key = Key(tris[t][k], tris[t][(k + 1) % 3]);
                if (!edges.TryGetValue(key, out var list)) edges[key] = list = new List<int>(2);
                list.Add(t);
            }

        return edges;
    }

    private static double Length(List<Vector3> points, long key) =>
        Vector3.Distance(points[(int)(key >> 32)], points[(int)(key & 0xFFFFFFFF)]);

    private static long Key(int a, int b) => a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;

    private static Vector3D Cross(Vector3 u, Vector3 v) =>
        new((double)u.Y * v.Z - (double)u.Z * v.Y, (double)u.Z * v.X - (double)u.X * v.Z, (double)u.X * v.Y - (double)u.Y * v.X);

    private static Vector3D Sub(Vector3 a, Vector3 b) => new((double)a.X - b.X, (double)a.Y - b.Y, (double)a.Z - b.Z);

    private static double Dot(Vector3D a, Vector3D b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    private static double Smallest(Vector3 a, Vector3 b, Vector3 c) =>
        Math.Min(Angle(a, b, c), Math.Min(Angle(b, c, a), Angle(c, a, b)));

    private static double Angle(Vector3 at, Vector3 p, Vector3 q)
    {
        var u = Sub(p, at);
        var v = Sub(q, at);
        double lengths = Math.Sqrt(Dot(u, u) * Dot(v, v));
        return lengths <= 0 ? 0 : Math.Acos(Math.Clamp(Dot(u, v) / lengths, -1, 1));
    }

    private readonly record struct Vector3D(double X, double Y, double Z)
    {
        public Vector3D Add(Vector3D o) => new(X + o.X, Y + o.Y, Z + o.Z);
    }

    private static bool Worse(MeshHealth now, MeshHealth was) =>
        now.BoundaryEdges > was.BoundaryEdges || now.NonManifoldEdges > was.NonManifoldEdges
        || now.InconsistentEdges > was.InconsistentEdges;
}
