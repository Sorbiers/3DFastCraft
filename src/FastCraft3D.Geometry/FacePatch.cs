using System.Numerics;

namespace FastCraft3D.Geometry;

/// <summary>
/// A line in a face's own 2D frame that nothing may cross, with the side that is allowed.
/// </summary>
/// <param name="Point">Any point on the line.</param>
/// <param name="Inward">Unit normal pointing into the face.</param>
public readonly record struct Fence(Vector2 Point, Vector2 Inward)
{
    /// <summary>How far inside the fence a point sits. Negative means it is over the line.</summary>
    public float Clearance(Vector2 uv) => Vector2.Dot(uv - Point, Inward);
}

/// <summary>
/// One flat face of a mesh: the connected run of triangles that share a plane.
///
/// A box face is two triangles, a boolean result's face can be dozens, and either way the user
/// thinks of it as one surface. Engraving works on this patch rather than on whatever triangle
/// happened to be under the cursor.
/// </summary>
public sealed class FacePatch
{
    /// <summary>How far off the plane a vertex may sit and still count as part of the face.</summary>
    public const float DefaultToleranceMm = 0.01f;

    /// <summary>How far a triangle's normal may tilt and still count as the same face.</summary>
    public const float DefaultAngleDegrees = 1f;

    /// <summary>
    /// How sharply the mesh must turn at a face's edge before it is safe to run a pattern past
    /// it.
    ///
    /// The real threshold is a right angle. At a box's corner the next face is exactly 90
    /// degrees away, so the pattern's overhang runs along it and touches nothing. Anywhere the
    /// surface turns by less - a cylinder facet at 11 degrees, a wedge's slope meeting its base
    /// at 45, a tetrahedron at 70 - the next face rises under the overhang and the groove shaves
    /// it at a glancing angle, leaving slivers the boolean cannot resolve. Set just under 90 so
    /// a box, the shape this tool is really for, keeps running its pattern off the edge.
    /// </summary>
    public const float FenceAngleDegrees = 85f;

    private FacePatch(
        Mesh mesh, List<int> triangles, Vector3 normal, Vector3 origin, Vector3 u, Vector3 v,
        Vector2 min, Vector2 max, float area, List<(int A, int B)> boundary, List<Fence> fences)
    {
        Fences = fences;
        Mesh = mesh;
        Triangles = triangles;
        Normal = normal;
        Origin = origin;
        U = u;
        V = v;
        Min = min;
        Max = max;
        Area = area;
        Boundary = boundary;
    }

    public Mesh Mesh { get; }

    /// <summary>Offsets into <see cref="Mesh.Indices"/>, one per triangle in the face.</summary>
    public IReadOnlyList<int> Triangles { get; }

    /// <summary>Unit outward normal of the face.</summary>
    public Vector3 Normal { get; }

    /// <summary>The point the face's 2D coordinates are measured from.</summary>
    public Vector3 Origin { get; }

    /// <summary>In-plane axes. <see cref="U"/> is horizontal wherever the face is not itself flat.</summary>
    public Vector3 U { get; }
    public Vector3 V { get; }

    /// <summary>The face's extent in its own 2D frame, in millimetres.</summary>
    public Vector2 Min { get; }
    public Vector2 Max { get; }

    public float Area { get; }

    /// <summary>
    /// Directed edges walked in the triangles' own winding, one per edge that has no neighbour
    /// inside the face. These are the face's outline, and they wind consistently, so extruding
    /// them gives a solid with its walls facing outward.
    /// </summary>
    public IReadOnlyList<(int A, int B)> Boundary { get; }

    /// <summary>
    /// The edges the pattern must stop at, because the surface carries on almost flat past them.
    /// Empty on a face that meets its neighbours at a proper corner, which is the usual case and
    /// the one where a groove is meant to run off the edge.
    /// </summary>
    public IReadOnlyList<Fence> Fences { get; }

    public Vector2 Size => Max - Min;

    /// <summary>
    /// Whether a point in the face's frame is clear of every fence by <paramref name="margin"/>.
    /// A face with no fences takes anything, so a pattern on a box still runs off the edge.
    /// </summary>
    public bool Clears(Vector2 uv, float margin)
    {
        foreach (var fence in Fences)
            if (fence.Clearance(uv) < margin)
                return false;

        return true;
    }

    public Vector2 ToUv(Vector3 point)
    {
        var offset = point - Origin;
        return new Vector2(Vector3.Dot(offset, U), Vector3.Dot(offset, V));
    }

    public Vector3 ToLocal(Vector2 uv, float height = 0) =>
        Origin + U * uv.X + V * uv.Y + Normal * height;

    /// <summary>
    /// The flat face containing <paramref name="point"/>, or null when the point is not on one.
    /// Both arguments are in the mesh's own space.
    /// </summary>
    public static FacePatch? Find(
        Mesh mesh,
        Vector3 point,
        Vector3 hintNormal,
        float toleranceMm = DefaultToleranceMm,
        float angleDegrees = DefaultAngleDegrees)
    {
        if (mesh.TriangleCount == 0) return null;

        var normals = TriangleNormals(mesh);
        int seed = FindSeed(mesh, normals, point, hintNormal, toleranceMm);
        if (seed < 0) return null;

        return Grow(mesh, normals, seed, toleranceMm, angleDegrees);
    }

    /// <summary>The face a given triangle belongs to. Used by the tests and by repeat engraving.</summary>
    public static FacePatch? FromTriangle(
        Mesh mesh,
        int triangleOffset,
        float toleranceMm = DefaultToleranceMm,
        float angleDegrees = DefaultAngleDegrees)
    {
        if (triangleOffset < 0 || triangleOffset + 2 >= mesh.Indices.Count) return null;

        var normals = TriangleNormals(mesh);
        return normals[triangleOffset / 3] == Vector3.Zero
            ? null
            : Grow(mesh, normals, triangleOffset, toleranceMm, angleDegrees);
    }

    private static Vector3[] TriangleNormals(Mesh mesh)
    {
        var normals = new Vector3[mesh.TriangleCount];
        for (int t = 0, i = 0; t + 2 < mesh.Indices.Count; t += 3, i++)
        {
            Vector3 a = mesh.Positions[mesh.Indices[t]];
            Vector3 b = mesh.Positions[mesh.Indices[t + 1]];
            Vector3 c = mesh.Positions[mesh.Indices[t + 2]];
            var n = Vector3.Cross(b - a, c - a);

            // Slivers have no reliable normal; they are carried along by their neighbours rather
            // than being allowed to seed or steer a face.
            normals[i] = n.LengthSquared() < 1e-20f ? Vector3.Zero : Vector3.Normalize(n);
        }
        return normals;
    }

    /// <summary>
    /// The triangle the click landed on: nearest to the point, among those facing the way the
    /// hit test said the surface faces. The normal matters because a thin wall has a triangle
    /// on each side, both within tolerance of the same point.
    /// </summary>
    private static int FindSeed(
        Mesh mesh, Vector3[] normals, Vector3 point, Vector3 hintNormal, float tolerance)
    {
        bool haveHint = hintNormal.LengthSquared() > 1e-12f;
        var hint = haveHint ? Vector3.Normalize(hintNormal) : Vector3.Zero;

        int best = -1;
        float bestDistance = float.MaxValue;

        for (int t = 0, i = 0; t + 2 < mesh.Indices.Count; t += 3, i++)
        {
            if (normals[i] == Vector3.Zero) continue;
            if (haveHint && Vector3.Dot(normals[i], hint) < 0.5f) continue;

            float distance = DistanceToTriangle(
                point,
                mesh.Positions[mesh.Indices[t]],
                mesh.Positions[mesh.Indices[t + 1]],
                mesh.Positions[mesh.Indices[t + 2]]);

            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = t;
            }
        }

        // A generous limit: the hit point comes from the renderer's own intersection, so it is
        // already on the surface - this only rejects a click that missed the mesh entirely.
        return bestDistance <= Math.Max(tolerance, 0.5f) ? best : -1;
    }

    private static FacePatch Grow(
        Mesh mesh, Vector3[] normals, int seed, float tolerance, float angleDegrees)
    {
        Vector3 normal = normals[seed / 3];
        Vector3 origin = mesh.Positions[mesh.Indices[seed]];
        float planeOffset = Vector3.Dot(normal, origin);
        float cosAngle = MathF.Cos(angleDegrees * MathF.PI / 180f);

        var neighbours = BuildEdgeMap(mesh);
        var taken = new HashSet<int> { seed };
        var queue = new Queue<int>();
        queue.Enqueue(seed);

        var triangles = new List<int>();

        while (queue.Count > 0)
        {
            int t = queue.Dequeue();
            triangles.Add(t);

            for (int k = 0; k < 3; k++)
            {
                var key = EdgeKey(mesh.Indices[t + k], mesh.Indices[t + (k + 1) % 3]);
                if (!neighbours.TryGetValue(key, out var sharing)) continue;

                // `taken` records what has been tested, not what was accepted: the plane test
                // does not depend on which edge we arrived by, so a rejected triangle would
                // only be rejected again.
                foreach (int other in sharing)
                {
                    if (other == t || !taken.Add(other)) continue;

                    if (SharesPlane(mesh, normals, other, normal, planeOffset, tolerance, cosAngle))
                        queue.Enqueue(other);
                }
            }
        }

        triangles.Sort();
        var (u, v) = PlaneAxes(normal);
        var (min, max, area) = Measure(mesh, triangles, origin, u, v);

        var boundary = BoundaryEdges(mesh, triangles);
        // The accepted triangles, not `taken`: that set records everything tested, rejections
        // included, so using it would make every neighbour look like part of the face and no
        // fence would ever be raised.
        var fences = BuildFences(
            mesh, neighbours, new HashSet<int>(triangles), boundary, normal, origin, u, v);

        return new FacePatch(
            mesh, triangles, normal, origin, u, v, min, max, area, boundary, fences);
    }

    /// <summary>
    /// Turns the boundary edges the surface continues across into lines the pattern must respect.
    ///
    /// Only edges where the mesh carries on nearly flat produce a fence. A cube gets none, so
    /// nothing changes for the case the tool is really for; a cylinder facet gets one per side,
    /// which is what stops a groove shaving the facet next door.
    /// </summary>
    private static List<Fence> BuildFences(
        Mesh mesh, Dictionary<(int, int), List<int>> neighbours, HashSet<int> patch,
        List<(int A, int B)> boundary, Vector3 normal, Vector3 origin, Vector3 u, Vector3 v)
    {
        var fences = new List<Fence>();
        float cosFence = MathF.Cos(FenceAngleDegrees * MathF.PI / 180f);

        foreach (var (a, b) in boundary)
        {
            if (!neighbours.TryGetValue(EdgeKey(a, b), out var sharing)) continue;
            if (!sharing.Any(t => !patch.Contains(t) && TurnsGently(mesh, t, normal, cosFence)))
                continue;

            var from = Project(mesh.Positions[a], origin, u, v);
            var to = Project(mesh.Positions[b], origin, u, v);

            var along = to - from;
            if (along.LengthSquared() < 1e-12f) continue;

            along = Vector2.Normalize(along);

            // Boundary edges run in their triangle's winding, which leaves the face on the left.
            fences.Add(new Fence(from, new Vector2(-along.Y, along.X)));
        }

        return fences;
    }

    private static bool TurnsGently(Mesh mesh, int triangle, Vector3 normal, float cosFence)
    {
        Vector3 a = mesh.Positions[mesh.Indices[triangle]];
        var other = Vector3.Cross(
            mesh.Positions[mesh.Indices[triangle + 1]] - a,
            mesh.Positions[mesh.Indices[triangle + 2]] - a);

        if (other.LengthSquared() < 1e-20f) return false;

        return Vector3.Dot(Vector3.Normalize(other), normal) > cosFence;
    }

    private static Vector2 Project(Vector3 point, Vector3 origin, Vector3 u, Vector3 v)
    {
        var offset = point - origin;
        return new Vector2(Vector3.Dot(offset, u), Vector3.Dot(offset, v));
    }

    private static bool SharesPlane(
        Mesh mesh, Vector3[] normals, int triangle,
        Vector3 normal, float planeOffset, float tolerance, float cosAngle)
    {
        var candidate = normals[triangle / 3];
        if (candidate != Vector3.Zero && Vector3.Dot(candidate, normal) < cosAngle) return false;

        // The angle test alone would walk around a cylinder one facet at a time; the plane test
        // is what keeps the face flat.
        for (int k = 0; k < 3; k++)
        {
            float distance = Vector3.Dot(normal, mesh.Positions[mesh.Indices[triangle + k]]) - planeOffset;
            if (Math.Abs(distance) > tolerance) return false;
        }

        return true;
    }

    private static Dictionary<(int, int), List<int>> BuildEdgeMap(Mesh mesh)
    {
        var map = new Dictionary<(int, int), List<int>>(mesh.Indices.Count);
        for (int t = 0; t + 2 < mesh.Indices.Count; t += 3)
        {
            for (int k = 0; k < 3; k++)
            {
                var key = EdgeKey(mesh.Indices[t + k], mesh.Indices[t + (k + 1) % 3]);
                if (!map.TryGetValue(key, out var list)) map[key] = list = new List<int>();
                list.Add(t);
            }
        }
        return map;
    }

    /// <summary>Edges used by exactly one of the face's triangles, kept in winding order.</summary>
    private static List<(int A, int B)> BoundaryEdges(Mesh mesh, List<int> triangles)
    {
        var used = new Dictionary<(int, int), int>();
        var directed = new List<(int A, int B)>();

        foreach (int t in triangles)
        {
            for (int k = 0; k < 3; k++)
            {
                int a = mesh.Indices[t + k], b = mesh.Indices[t + (k + 1) % 3];
                var key = EdgeKey(a, b);
                used[key] = used.GetValueOrDefault(key) + 1;
                directed.Add((a, b));
            }
        }

        return directed.Where(e => used[EdgeKey(e.A, e.B)] == 1).ToList();
    }

    /// <summary>
    /// In-plane axes chosen so patterns land the way people expect: on anything but a floor or a
    /// ceiling, U runs horizontally, so brick courses and siding come out level rather than
    /// tilted by whatever basis the maths happened to produce.
    /// </summary>
    public static (Vector3 U, Vector3 V) PlaneAxes(Vector3 normal)
    {
        var up = Vector3.UnitZ;
        var u = Vector3.Cross(up, normal);

        if (u.LengthSquared() < 1e-8f) u = Vector3.Cross(Vector3.UnitY, normal); // face is level
        if (u.LengthSquared() < 1e-8f) u = Vector3.UnitX;

        u = Vector3.Normalize(u);
        return (u, Vector3.Normalize(Vector3.Cross(normal, u)));
    }

    private static (Vector2 Min, Vector2 Max, float Area) Measure(
        Mesh mesh, List<int> triangles, Vector3 origin, Vector3 u, Vector3 v)
    {
        var min = new Vector2(float.MaxValue);
        var max = new Vector2(float.MinValue);
        float area = 0;

        foreach (int t in triangles)
        {
            Vector3 a = mesh.Positions[mesh.Indices[t]];
            Vector3 b = mesh.Positions[mesh.Indices[t + 1]];
            Vector3 c = mesh.Positions[mesh.Indices[t + 2]];
            area += Vector3.Cross(b - a, c - a).Length() * 0.5f;

            for (int k = 0; k < 3; k++)
            {
                var offset = mesh.Positions[mesh.Indices[t + k]] - origin;
                var uv = new Vector2(Vector3.Dot(offset, u), Vector3.Dot(offset, v));
                min = Vector2.Min(min, uv);
                max = Vector2.Max(max, uv);
            }
        }

        return (min, max, area);
    }

    private static (int, int) EdgeKey(int a, int b) => a < b ? (a, b) : (b, a);

    private static float DistanceToTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
    {
        // Ericson, Real-Time Collision Detection: closest point on a triangle by Voronoi region.
        Vector3 ab = b - a, ac = c - a, ap = p - a;
        float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
        if (d1 <= 0 && d2 <= 0) return (p - a).Length();

        Vector3 bp = p - b;
        float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
        if (d3 >= 0 && d4 <= d3) return (p - b).Length();

        float vc = d1 * d4 - d3 * d2;
        if (vc <= 0 && d1 >= 0 && d3 <= 0)
            return (p - (a + ab * (d1 / (d1 - d3)))).Length();

        Vector3 cp = p - c;
        float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
        if (d6 >= 0 && d5 <= d6) return (p - c).Length();

        float vb = d5 * d2 - d1 * d6;
        if (vb <= 0 && d2 >= 0 && d6 <= 0)
            return (p - (a + ac * (d2 / (d2 - d6)))).Length();

        float va = d3 * d6 - d5 * d4;
        if (va <= 0 && d4 - d3 >= 0 && d5 - d6 >= 0)
            return (p - (b + (c - b) * ((d4 - d3) / (d4 - d3 + (d5 - d6))))).Length();

        float denominator = 1f / (va + vb + vc);
        return (p - (a + ab * (vb * denominator) + ac * (vc * denominator))).Length();
    }
}
