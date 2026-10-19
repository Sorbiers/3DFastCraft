using System.Numerics;
using V3 = FastCraft3D.Geometry.FaceRebuild.Vector3D;

namespace FastCraft3D.Geometry;

/// <param name="Mesh">The rebuilt mesh, or the one given when nothing changed.</param>
/// <param name="TrianglesBefore">The whole mesh's triangles before.</param>
/// <param name="TrianglesAfter">And after.</param>
/// <param name="PointsRemoved">Points that were not corners and are gone.</param>
/// <param name="FacesLeft">Flat faces left as they were: with holes, or that could not be filled.</param>
/// <param name="Refusal">Why the whole object was left as it was, when it was refused.</param>
/// <param name="Changed">Whether <paramref name="Mesh"/> is a new mesh.</param>
public sealed record OutlineRebuildResult(
    Mesh Mesh, int TrianglesBefore, int TrianglesAfter, int PointsRemoved, int FacesLeft, string? Refusal, bool Changed);

/// <summary>
/// Rebuilds a whole object from its corners.
///
/// Laying one face again from its outline cannot finish the job: a point halfway along a straight
/// edge belongs to the outline of both faces that meet there, so each face on its own has to keep
/// it, and the fans that run out to it stay. Taken a whole object at a time, such a point can go
/// from both faces at once. So every flat face is found, every point that is not a corner - in the
/// middle of a face, or on a straight edge between exactly two faces - is dropped, and each face
/// is filled again from the corners left on its outline. No corner moves, so the shape is exactly
/// what it was.
///
/// A face with a hole in it is left as it was for now, and so is every point on its outline.
/// </summary>
public static class OutlineRebuild
{
    /// <summary>How far a point may sit off its face's plane, or off the straight edge it is dropped from.</summary>
    public const float ToleranceMm = FaceRebuild.FlatnessMm;

    public static OutlineRebuildResult Rebuild(Mesh input, CancellationToken token = default)
    {
        OutlineRebuildResult Keep(string? why) =>
            new(input, input.TriangleCount, input.TriangleCount, 0, 0, why, false);

        if (input.TriangleCount < 4) return Keep(null);

        var mesh = input.Welded();
        var points = mesh.Positions;
        var indices = mesh.Indices;
        int count = mesh.TriangleCount;

        var cross = new V3[count];
        for (int i = 0; i < count; i++)
        {
            var a = V3.Of(points[indices[3 * i]]);
            cross[i] = (V3.Of(points[indices[3 * i + 1]]) - a).Cross(V3.Of(points[indices[3 * i + 2]]) - a);
        }

        var byEdge = new Dictionary<long, List<int>>();
        for (int i = 0; i < count; i++)
            for (int k = 0; k < 3; k++)
            {
                long key = Key(indices[3 * i + k], indices[3 * i + (k + 1) % 3]);
                if (!byEdge.TryGetValue(key, out var list)) byEdge[key] = list = new List<int>(2);
                list.Add(i);
            }

        // Flat faces, grown from the biggest triangles first: a sliver's normal is float noise,
        // and the plane is taken from the seed and never from its neighbours, so it cannot drift.
        var region = new int[count];
        Array.Fill(region, -1);
        var faces = new List<List<int>>();
        var normals = new List<V3>();

        foreach (int seed in Enumerable.Range(0, count).OrderByDescending(i => cross[i].Length).ThenBy(i => i))
        {
            if (region[seed] >= 0) continue;
            token.ThrowIfCancellationRequested();

            int r = faces.Count;
            var members = new List<int> { seed };
            region[seed] = r;
            faces.Add(members);

            double length = cross[seed].Length;
            var n = length > 1e-12 ? cross[seed] / length : new V3(0, 0, 0);
            normals.Add(n);
            if (length <= 1e-12) continue;

            double d = n.Dot(V3.Of(points[indices[3 * seed]]));

            for (int m = 0; m < members.Count; m++)
            {
                int t = members[m];
                for (int k = 0; k < 3; k++)
                {
                    var across = byEdge[Key(indices[3 * t + k], indices[3 * t + (k + 1) % 3])];
                    if (across.Count != 2) continue;

                    int u = across[0] == t ? across[1] : across[0];
                    if (region[u] >= 0) continue;
                    if (cross[u].Length > 1e-9 && cross[u].Dot(n) <= 0) continue;

                    bool flat = true;
                    for (int j = 0; j < 3 && flat; j++)
                        flat = Math.Abs(n.Dot(V3.Of(points[indices[3 * u + j]])) - d) <= ToleranceMm;
                    if (!flat) continue;

                    region[u] = r;
                    members.Add(u);
                }
            }
        }

        // Each face's outline, as one loop. A face with a hole, or one whose outline touches
        // itself, is left alone.
        var loops = new List<int>?[faces.Count];
        var left = new bool[faces.Count];
        for (int r = 0; r < faces.Count; r++)
        {
            if (faces[r].Count < 2 || normals[r].Length == 0) { left[r] = true; continue; }
            loops[r] = Loop(faces[r], indices);
            if (loops[r] is null) left[r] = true;
        }

        var facesAt = new List<int>[points.Count];
        for (int i = 0; i < count; i++)
            for (int k = 0; k < 3; k++)
            {
                int v = indices[3 * i + k];
                var list = facesAt[v] ??= new List<int>(4);
                if (!list.Contains(region[i])) list.Add(region[i]);
            }

        var rebuilt = new Dictionary<int, List<int[]>>();
        var keep = new bool[points.Count];

        // Rounds, because a face that cannot be filled has to keep its whole outline, and the
        // face on the other side of each of its edges then has to keep those points too.
        for (int round = 0; ; round++)
        {
            if (round > faces.Count + 1) return Keep("it could not be settled which points to keep");
            token.ThrowIfCancellationRequested();

            Corners(points, facesAt, loops, left, keep);

            rebuilt.Clear();
            int failed = -1;

            for (int r = 0; r < faces.Count && failed < 0; r++)
            {
                if (left[r] || loops[r] is not { } loop) continue;

                bool touched = faces[r].Any(t => !keep[indices[3 * t]] || !keep[indices[3 * t + 1]] || !keep[indices[3 * t + 2]]);
                if (!touched) continue;

                var corners = loop.Where(v => keep[v]).ToList();
                var laid = corners.Count >= 3 ? Fill(points, corners, normals[r]) : null;
                if (laid is null) failed = r;
                else rebuilt[r] = laid;
            }

            if (failed < 0) break;
            left[failed] = true;
        }

        if (rebuilt.Count == 0) return Keep(null);

        var built = new List<int>(indices.Count);
        for (int i = 0; i < count; i++)
        {
            if (rebuilt.ContainsKey(region[i])) continue;
            built.Add(indices[3 * i]);
            built.Add(indices[3 * i + 1]);
            built.Add(indices[3 * i + 2]);
        }

        foreach (var laid in rebuilt.Values)
            foreach (var t in laid)
                built.AddRange(t);

        var result = FaceRebuild.Compacted(points, built);

        var was = mesh.CheckHealth();
        var now = result.CheckHealth();
        if (now.BoundaryEdges > was.BoundaryEdges || now.NonManifoldEdges > was.NonManifoldEdges
            || now.InconsistentEdges > was.InconsistentEdges)
            return Keep("the result would not have been watertight");

        if (MeshStitch.Count(result) > MeshStitch.Count(mesh))
            return Keep("the result would have left edges meeting partway");

        double before = mesh.ComputeSignedVolume(), after = result.ComputeSignedVolume();
        if (Math.Abs(after - before) > 1e-6 * Math.Abs(before) + 1e-3)
            return Keep("the volume would have changed");

        var boxBefore = mesh.ComputeBounds();
        var boxAfter = result.ComputeBounds();
        if (boxBefore.Min != boxAfter.Min || boxBefore.Max != boxAfter.Max)
            return Keep("the size would have changed");

        if (result.TriangleCount >= input.TriangleCount) return Keep(null);

        int used = indices.Distinct().Count();
        return new OutlineRebuildResult(result, input.TriangleCount, result.TriangleCount,
            used - result.Positions.Count,
            Enumerable.Range(0, faces.Count).Count(r => left[r] && faces[r].Count >= 2 && normals[r].Length > 0), null, true);
    }

    /// <summary>
    /// Marks the points that have to stay: every one except those in the middle of a face and
    /// those halfway along a straight edge between exactly two faces that are both being rebuilt.
    /// </summary>
    private static void Corners(List<Vector3> points, List<int>[] facesAt, List<int>?[] loops, bool[] left, bool[] keep)
    {
        Array.Fill(keep, true);

        // Where each point sits on each face's outline, and its neighbours there.
        var around = new Dictionary<(int Face, int Point), (int Prev, int Next)>();
        for (int r = 0; r < loops.Length; r++)
        {
            if (left[r] || loops[r] is not { } loop) continue;
            for (int i = 0; i < loop.Count; i++)
                around[(r, loop[i])] = (loop[(i + loop.Count - 1) % loop.Count], loop[(i + 1) % loop.Count]);
        }

        for (int v = 0; v < points.Count; v++)
        {
            var at = facesAt[v];
            if (at is null) continue;

            if (at.Count == 1)
            {
                int r = at[0];
                if (!left[r] && loops[r] is not null && !around.ContainsKey((r, v))) keep[v] = false;
            }
            else if (at.Count == 2)
            {
                int f = at[0], q = at[1];
                if (left[f] || left[q]) continue;
                if (!around.TryGetValue((f, v), out var onF) || !around.TryGetValue((q, v), out var onQ)) continue;

                // The same two neighbours on both faces, the other way round, and in line.
                if (onF.Prev != onQ.Next || onF.Next != onQ.Prev) continue;
                if (Between(points[onF.Prev], points[v], points[onF.Next])) keep[v] = false;
            }
        }

        // Dropped one at a time, a gentle curve could go a point at a time, each one in line with
        // its neighbours; so each dropped point is checked against the corners it ends up between.
        bool changed = true;
        while (changed)
        {
            changed = false;
            for (int r = 0; r < loops.Length; r++)
            {
                if (left[r] || loops[r] is not { } loop) continue;
                int n = loop.Count;
                for (int i = 0; i < n; i++)
                {
                    int v = loop[i];
                    if (keep[v]) continue;

                    int back = i, ahead = i;
                    do back = (back + n - 1) % n; while (!keep[loop[back]] && back != i);
                    do ahead = (ahead + 1) % n; while (!keep[loop[ahead]] && ahead != i);
                    if (back == i || ahead == i || !Between(points[loop[back]], points[v], points[loop[ahead]]))
                    {
                        keep[v] = true;
                        changed = true;
                    }
                }
            }
        }
    }

    /// <summary>Whether <paramref name="v"/> lies on the segment from <paramref name="p"/> to <paramref name="q"/>.</summary>
    private static bool Between(Vector3 p, Vector3 v, Vector3 q)
    {
        var a = V3.Of(p);
        var line = V3.Of(q) - a;
        var to = V3.Of(v) - a;
        double length = line.Length;
        if (length <= 0) return false;

        double along = to.Dot(line) / length;
        if (along <= ToleranceMm || along >= length - ToleranceMm) return false;

        double off = to.Cross(line).Length / length;
        return off <= ToleranceMm;
    }

    /// <summary>The face's outline as one loop in its winding, or null for a hole or a pinch.</summary>
    private static List<int>? Loop(List<int> triangles, List<int> indices)
    {
        var directed = new HashSet<(int, int)>();
        foreach (int t in triangles)
            for (int k = 0; k < 3; k++)
                if (!directed.Add((indices[3 * t + k], indices[3 * t + (k + 1) % 3]))) return null;

        var next = new Dictionary<int, int>();
        foreach (var (a, b) in directed)
            if (!directed.Contains((b, a)) && !next.TryAdd(a, b)) return null;

        if (next.Count < 3) return null;

        int start = next.Keys.Min();
        var loop = new List<int>();
        int at = start;
        do
        {
            loop.Add(at);
            if (!next.TryGetValue(at, out at)) return null;
        }
        while (at != start && loop.Count <= next.Count);

        // More points on the outline than in this loop means a second loop: a hole.
        return at == start && loop.Count == next.Count ? loop : null;
    }

    /// <summary>The face filled from its corners, wound as the face was, or null when it cannot be.</summary>
    private static List<int[]>? Fill(List<Vector3> points, List<int> corners, V3 normal)
    {
        var (u, v) = FacePatch.PlaneAxes(normal.ToVector3());
        var origin = points[corners[0]];

        var flat = new Dictionary<int, Vector2>();
        var back = new Dictionary<Vector2, int>();
        foreach (int p in corners)
        {
            var d = points[p] - origin;
            var uv = new Vector2(Vector3.Dot(d, u), Vector3.Dot(d, v));
            if (!back.TryAdd(uv, p)) return null;
            flat[p] = uv;
        }

        var (laid, triangles) = Polygon2.Triangulate(corners.Select(p => flat[p]).ToList(), []);
        if (triangles.Count / 3 != corners.Count - 2) return null;

        var tris = new List<int[]>();
        for (int i = 0; i + 2 < triangles.Count; i += 3)
        {
            if (!back.TryGetValue(laid[triangles[i]], out int a) || !back.TryGetValue(laid[triangles[i + 1]], out int b)
                || !back.TryGetValue(laid[triangles[i + 2]], out int c))
                return null;
            tris.Add(FaceRebuild.Orient(flat[a], flat[b], flat[c]) >= 0 ? [a, b, c] : [a, c, b]);
        }

        var next = new Dictionary<int, int>();
        for (int i = 0; i < corners.Count; i++) next[corners[i]] = corners[(i + 1) % corners.Count];
        FaceRebuild.Improve(tris, flat, new HashSet<int>(corners), next);

        foreach (var t in tris)
            if (FaceRebuild.Height(flat[t[0]], flat[t[1]], flat[t[2]]) < FaceRebuild.LeastHeightMm) return null;

        // Anticlockwise in the plane faces along U x V; the face may face the other way.
        bool reverse = Vector3.Dot(Vector3.Cross(u, v), normal.ToVector3()) < 0;
        return reverse ? tris.Select(t => new[] { t[0], t[2], t[1] }).ToList() : tris;
    }

    private static long Key(int a, int b) => a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
}
