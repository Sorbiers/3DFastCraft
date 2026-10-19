using System.Numerics;

namespace FastCraft3D.Geometry;

/// <param name="Mesh">The mesh with the face laid again, or the one given when nothing changed.</param>
/// <param name="TrianglesBefore">The face's triangles before.</param>
/// <param name="TrianglesAfter">And after.</param>
/// <param name="PointsRemoved">Points that were inside the face and are gone.</param>
/// <param name="Refusal">Why the face was left as it was, when it was refused.</param>
/// <param name="Changed">Whether <paramref name="Mesh"/> is a new mesh.</param>
public sealed record FaceRebuildResult(Mesh Mesh, int TrianglesBefore, int TrianglesAfter, int PointsRemoved, string? Refusal, bool Changed);

/// <summary>
/// Lays one flat face again from its outline alone.
///
/// A boolean or a split leaves a flat face cut into a web of long thin triangles running out to
/// points in the middle of it, which describe nothing: the face is flat, and its outline says all
/// there is to say about it. So the face's triangles are thrown away and the outline - every
/// point of it, holes included - is filled again. Nothing on the outline moves or goes, so the
/// faces round it still meet it exactly and the shape does not change by so much as a rounding
/// error; the points inside are simply no longer used.
///
/// Neither of the other tools does this. Simplify collapses edges towards a count and cannot turn
/// one, so it can thin a web but not straighten it; Rebuild resamples the whole surface on a grid
/// and loses the shape.
/// </summary>
public static class FaceRebuild
{
    /// <summary>
    /// How far a point of the face may sit off its plane and still be laid flat.
    ///
    /// Surface info gathers a face at a hundredth of a millimetre, which is right for reading one
    /// and too loose for this: a point inside the face is dropped, so whatever it stood off the
    /// plane is the amount the surface moves. A thousandth is far below anything a printer can
    /// show and well under the finest deliberate step in the app's own parts.
    /// </summary>
    public const float FlatnessMm = 1e-3f;

    /// <summary>
    /// The thinnest triangle the new face may have. The outline's own points can lie in a
    /// straight line, and a triangle across three of them would have no width at all.
    /// </summary>
    internal const double LeastHeightMm = 1e-6;

    /// <summary>How much better a turned edge has to make its pair before it is turned.</summary>
    private const double TurnMargin = 1e-4;

    /// <summary>
    /// Lays the face made of <paramref name="triangles"/> - offsets into the mesh's indices, as
    /// <see cref="FacePatch.Triangles"/> gives them - again from its outline.
    /// </summary>
    public static FaceRebuildResult Rebuild(Mesh mesh, IReadOnlyList<int> triangles)
    {
        int before = triangles.Count;
        FaceRebuildResult Keep(string? why) => new(mesh, before, before, 0, why, false);

        if (before < 2) return Keep(null);

        var points = mesh.Positions;
        var indices = mesh.Indices;
        var inFace = new HashSet<int>(triangles);

        // The plane, area weighted and in double: a face cut into slivers has as many noisy
        // normals as it has slivers, and the big triangles are the ones that know.
        var normal = Vector3D.Zero;
        var centre = Vector3D.Zero;
        double area = 0;
        foreach (int t in triangles)
        {
            var a = Vector3D.Of(points[indices[t]]);
            var b = Vector3D.Of(points[indices[t + 1]]);
            var c = Vector3D.Of(points[indices[t + 2]]);
            var cross = (b - a).Cross(c - a);
            double twice = cross.Length;
            normal += cross;
            centre += (a + b + c) * (twice / 3);
            area += twice / 2;
        }

        if (normal.Length < 1e-12 || area < 1e-12) return Keep("the face has no area");
        normal /= normal.Length;
        centre /= area * 2;

        // Edges of the face with no partner inside it are its outline.
        var directed = new HashSet<(int, int)>();
        var faceVertices = new HashSet<int>();
        foreach (int t in triangles)
            for (int k = 0; k < 3; k++)
            {
                int a = indices[t + k], b = indices[t + (k + 1) % 3];
                if (!directed.Add((a, b))) return Keep("the face is folded over itself");
                faceVertices.Add(a);
            }

        foreach (int v in faceVertices)
            if (Math.Abs((Vector3D.Of(points[v]) - centre).Dot(normal)) > FlatnessMm)
                return Keep("the face is not quite flat");

        var next = new Dictionary<int, int>();
        foreach (var (a, b) in directed)
            if (!directed.Contains((b, a)) && !next.TryAdd(a, b))
                return Keep("its outline touches itself at a point");

        var onOutline = new HashSet<int>(next.Keys);
        var inside = faceVertices.Where(v => !onOutline.Contains(v)).ToList();

        // A point in the middle of the face that something else also uses is not spare: a cone
        // standing on its tip there would be left hanging.
        var insideSet = new HashSet<int>(inside);
        for (int t = 0; t + 2 < indices.Count; t += 3)
        {
            if (inFace.Contains(t)) continue;
            for (int k = 0; k < 3; k++)
                if (insideSet.Contains(indices[t + k]))
                    return Keep("a point inside the face also belongs to another face");
        }

        if (!TryLoops(next, out var loops)) return Keep("its outline does not close");

        // Into the face's own plane. The same point always lands on the same 2D point, which is
        // how the triangulation's points are matched back to the mesh's.
        var (u, v2) = FacePatch.PlaneAxes(normal.ToVector3());
        var origin = centre.ToVector3();
        var flat = new Dictionary<int, Vector2>();
        var back = new Dictionary<Vector2, int>();
        foreach (var loop in loops)
            foreach (int p in loop)
            {
                var d = points[p] - origin;
                var uv = new Vector2(Vector3.Dot(d, u), Vector3.Dot(d, v2));
                if (!back.TryAdd(uv, p)) return Keep("two points of its outline fall on the same spot");
                flat[p] = uv;
            }

        var rings = loops.Select(l => l.Select(p => flat[p]).ToList()).ToList();
        var shapes = Polygon2.Nest(rings);
        if (shapes.Count != 1) return Keep("its outline is not one piece");

        var (laid, corners) = Polygon2.Triangulate(shapes[0].Outline, shapes[0].Holes);

        // Each hole adds two triangles and each point one, less two. Anything else means a hole
        // was not reached and has been covered over, which the triangulator does without a word.
        int outlinePoints = loops.Sum(l => l.Count);
        int wanted = outlinePoints + 2 * (loops.Count - 1) - 2;
        if (corners.Count / 3 != wanted) return Keep("its outline could not be filled");

        var tris = new List<int[]>(wanted);
        for (int i = 0; i + 2 < corners.Count; i += 3)
        {
            if (!back.TryGetValue(laid[corners[i]], out int a)
                || !back.TryGetValue(laid[corners[i + 1]], out int b)
                || !back.TryGetValue(laid[corners[i + 2]], out int c))
                return Keep("its outline could not be filled");

            tris.Add(Orient(flat, a, b, c) >= 0 ? [a, b, c] : [a, c, b]);
        }

        // The triangulator clips ears from one corner, so it lays a fan - the same long thin
        // triangles over again. Turning edges until no turn helps gives the best shapes the
        // outline allows.
        Improve(tris, flat, onOutline, next);

        foreach (var t in tris)
            if (Height(flat[t[0]], flat[t[1]], flat[t[2]]) < LeastHeightMm)
                return Keep("its outline leaves a triangle with no width");

        double angleBefore = SmallestAngle(triangles.Select(t => (
            points[indices[t]], points[indices[t + 1]], points[indices[t + 2]])));
        double angleAfter = SmallestAngle(tris.Select(t => (points[t[0]], points[t[1]], points[t[2]])));

        bool fewer = tris.Count < before;
        bool shapelier = angleAfter >= angleBefore + Math.PI / 180;
        if (!fewer && !shapelier) return Keep(null);

        // Anticlockwise in the plane's frame faces along U x V; the face may face the other way.
        var uxv = Vector3.Cross(u, v2);
        bool reverse = Vector3.Dot(uxv, normal.ToVector3()) < 0;

        var built = new List<int>(indices.Count);
        for (int t = 0; t + 2 < indices.Count; t += 3)
        {
            if (inFace.Contains(t)) continue;
            built.Add(indices[t]);
            built.Add(indices[t + 1]);
            built.Add(indices[t + 2]);
        }

        foreach (var t in tris)
        {
            built.Add(t[0]);
            built.AddRange(reverse ? [t[2], t[1]] : [t[1], t[2]]);
        }

        var result = Compacted(points, built);

        // The outline and the rest of the mesh are as they were, so nothing can have opened or
        // crossed; but that is the sort of thing that is true until it is not, and a model that
        // will not print is worse than a face with too many triangles in it.
        var was = mesh.CheckHealth();
        var now = result.CheckHealth();
        if (now.BoundaryEdges > was.BoundaryEdges || now.NonManifoldEdges > was.NonManifoldEdges
            || now.InconsistentEdges > was.InconsistentEdges)
            return Keep("the result would not have been watertight");

        if (MeshStitch.Count(result) > MeshStitch.Count(mesh))
            return Keep("the result would have left edges meeting partway");

        double areaAfter = tris.Sum(t => Area(points[t[0]], points[t[1]], points[t[2]]));
        if (Math.Abs(areaAfter - area) > 1e-6 * area + 1e-6)
            return Keep("the new face would not have covered the old one exactly");

        return new FaceRebuildResult(result, before, tris.Count, inside.Count, null, true);
    }

    /// <summary>The outline as closed loops of points, each in the face's winding.</summary>
    private static bool TryLoops(Dictionary<int, int> next, out List<List<int>> loops)
    {
        loops = [];
        if (next.Count < 3) return false;

        var walked = new HashSet<int>();
        foreach (int from in next.Keys.OrderBy(k => k))
        {
            if (walked.Contains(from)) continue;

            var loop = new List<int>();
            int at = from;
            do
            {
                loop.Add(at);
                walked.Add(at);
                if (!next.TryGetValue(at, out at)) return false;
            }
            while (at != from && loop.Count <= next.Count);

            if (at != from || loop.Count < 3) return false;
            loops.Add(loop);
        }

        return true;
    }

    /// <summary>
    /// Turns edges inside the face while that makes the thinner of the two triangles beside one
    /// fatter. The outline's edges are never turned, so its points stay joined to what they were.
    /// </summary>
    internal static void Improve(List<int[]> tris, Dictionary<int, Vector2> flat, HashSet<int> onOutline, Dictionary<int, int> next)
    {
        bool IsOutline(int a, int b) =>
            (next.TryGetValue(a, out int n) && n == b) || (next.TryGetValue(b, out int m) && m == a);

        for (int pass = 0; pass < 64; pass++)
        {
            var owner = new Dictionary<(int, int), int>();
            for (int i = 0; i < tris.Count; i++)
                for (int k = 0; k < 3; k++)
                    owner[(tris[i][k], tris[i][(k + 1) % 3])] = i;

            bool turned = false;
            var touched = new HashSet<int>();

            for (int i = 0; i < tris.Count; i++)
            {
                if (touched.Contains(i)) continue;

                for (int k = 0; k < 3; k++)
                {
                    int a = tris[i][k], b = tris[i][(k + 1) % 3], c = tris[i][(k + 2) % 3];
                    if (IsOutline(a, b) || !owner.TryGetValue((b, a), out int j) || touched.Contains(j)) continue;

                    int d = tris[j].First(p => p != a && p != b);
                    if (owner.ContainsKey((c, d)) || owner.ContainsKey((d, c))) continue;

                    Vector2 pa = flat[a], pb = flat[b], pc = flat[c], pd = flat[d];

                    // The quad a, d, b, c must be strictly convex, or the new pair would fold.
                    if (Height(pc, pa, pd) < LeastHeightMm || Height(pd, pb, pc) < LeastHeightMm) continue;
                    if (Orient(pc, pa, pd) <= 0 || Orient(pd, pb, pc) <= 0) continue;

                    double now = Math.Min(Smallest(pa, pb, pc), Smallest(pb, pa, pd));
                    double then = Math.Min(Smallest(pc, pa, pd), Smallest(pd, pb, pc));
                    if (then <= now + TurnMargin) continue;

                    tris[i] = [c, a, d];
                    tris[j] = [d, b, c];
                    touched.Add(i);
                    touched.Add(j);
                    turned = true;
                    break;
                }
            }

            if (!turned) return;
        }
    }

    internal static Mesh Compacted(List<Vector3> points, List<int> indices)
    {
        var map = new int[points.Count];
        Array.Fill(map, -1);

        var kept = new List<Vector3>();
        var remapped = new List<int>(indices.Count);
        foreach (int i in indices)
        {
            if (map[i] < 0)
            {
                map[i] = kept.Count;
                kept.Add(points[i]);
            }

            remapped.Add(map[i]);
        }

        return new Mesh(kept, remapped);
    }

    private static double Orient(Dictionary<int, Vector2> flat, int a, int b, int c) => Orient(flat[a], flat[b], flat[c]);

    internal static double Orient(Vector2 a, Vector2 b, Vector2 c) =>
        ((double)b.X - a.X) * ((double)c.Y - a.Y) - ((double)b.Y - a.Y) * ((double)c.X - a.X);

    /// <summary>Twice the area over the longest side: how wide the triangle is at its widest.</summary>
    internal static double Height(Vector2 a, Vector2 b, Vector2 c)
    {
        double longest = Math.Max(Distance(a, b), Math.Max(Distance(b, c), Distance(c, a)));
        return longest <= 0 ? 0 : Math.Abs(Orient(a, b, c)) / longest;
    }

    private static double Distance(Vector2 a, Vector2 b) =>
        Math.Sqrt(((double)a.X - b.X) * ((double)a.X - b.X) + ((double)a.Y - b.Y) * ((double)a.Y - b.Y));

    private static double Smallest(Vector2 a, Vector2 b, Vector2 c) =>
        Math.Min(Angle(a, b, c), Math.Min(Angle(b, c, a), Angle(c, a, b)));

    /// <summary>The angle at <paramref name="at"/>.</summary>
    private static double Angle(Vector2 at, Vector2 p, Vector2 q)
    {
        double ux = (double)p.X - at.X, uy = (double)p.Y - at.Y, vx = (double)q.X - at.X, vy = (double)q.Y - at.Y;
        double lengths = Math.Sqrt((ux * ux + uy * uy) * (vx * vx + vy * vy));
        return lengths <= 0 ? 0 : Math.Acos(Math.Clamp((ux * vx + uy * vy) / lengths, -1, 1));
    }

    private static double SmallestAngle(IEnumerable<(Vector3 A, Vector3 B, Vector3 C)> triangles)
    {
        double least = Math.PI;
        foreach (var (a, b, c) in triangles)
        {
            least = Math.Min(least, Angle3(a, b, c));
            least = Math.Min(least, Angle3(b, c, a));
            least = Math.Min(least, Angle3(c, a, b));
        }

        return least;
    }

    private static double Angle3(Vector3 at, Vector3 p, Vector3 q)
    {
        var u = Vector3D.Of(p) - Vector3D.Of(at);
        var v = Vector3D.Of(q) - Vector3D.Of(at);
        double lengths = u.Length * v.Length;
        return lengths <= 0 ? 0 : Math.Acos(Math.Clamp(u.Dot(v) / lengths, -1, 1));
    }

    private static double Area(Vector3 a, Vector3 b, Vector3 c) =>
        (Vector3D.Of(b) - Vector3D.Of(a)).Cross(Vector3D.Of(c) - Vector3D.Of(a)).Length / 2;

    /// <summary>Just enough of a double-precision vector for the sums above.</summary>
    internal readonly record struct Vector3D(double X, double Y, double Z)
    {
        public static Vector3D Zero => new(0, 0, 0);
        public static Vector3D Of(Vector3 v) => new(v.X, v.Y, v.Z);
        public double Length => Math.Sqrt(X * X + Y * Y + Z * Z);
        public double Dot(Vector3D o) => X * o.X + Y * o.Y + Z * o.Z;
        public Vector3D Cross(Vector3D o) => new(Y * o.Z - Z * o.Y, Z * o.X - X * o.Z, X * o.Y - Y * o.X);
        public Vector3 ToVector3() => new((float)X, (float)Y, (float)Z);
        public static Vector3D operator +(Vector3D a, Vector3D b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static Vector3D operator -(Vector3D a, Vector3D b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static Vector3D operator *(Vector3D a, double s) => new(a.X * s, a.Y * s, a.Z * s);
        public static Vector3D operator /(Vector3D a, double s) => new(a.X / s, a.Y / s, a.Z / s);
    }
}
