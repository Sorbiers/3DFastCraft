using System.Numerics;

namespace FastCraft3D.Geometry;

/// <param name="Normal">Outward, so laying the object on it turns this to face straight down.</param>
/// <param name="Centre">The middle of its area.</param>
/// <param name="Area">In square millimetres.</param>
/// <param name="Triangles">Its triangles as corners in threes, wound outward.</param>
public sealed record RestingFace(Vector3 Normal, Vector3 Centre, float Area, IReadOnlyList<Vector3> Triangles);

/// <summary>
/// The faces an object can be stood on and stay there.
///
/// Taken from the convex hull rather than the mesh, because an object rests on its hull: a cup
/// stands on the rim of its foot, not on the hollow under it, and it can be stood upside down on
/// the rim of its mouth, where there is no face of the mesh at all. The hull's triangles are
/// joined into flat faces, and a face is kept only if the object's centre of mass falls over it -
/// laid on any other, it would topple onto a neighbour.
/// </summary>
public static class RestingFaces
{
    /// <summary>
    /// How closely hull triangles have to agree to be one face: about two degrees. A scan's flat
    /// underside is not quite flat, and at a tighter figure it came apart into slivers; the
    /// segments of a 32-sided cylinder are eleven degrees apart and stay separate.
    /// </summary>
    private const float SameFlat = 0.9994f;

    /// <summary>Beyond this many the plate is more patches than model.</summary>
    private const int MostShown = 60;

    /// <summary>How far the shrunk outline is drawn in, as a share of the way to the centre.</summary>
    public const float DrawnShare = 0.86f;

    /// <summary>The faces, largest first, in the coordinates of <paramref name="world"/>.</summary>
    public static List<RestingFace> Find(Mesh world)
    {
        if (world.TriangleCount == 0) return [];

        var hull = ConvexHull.Build(world.Positions);
        if (hull.Count == 0) return [];

        var p = world.Positions;
        var normals = hull.Select(t => Vector3.Cross(p[t.B] - p[t.A], p[t.C] - p[t.A])).ToList();
        float hullArea = normals.Sum(v => v.Length()) / 2f;

        var byEdge = new Dictionary<(int, int), int>();
        for (int i = 0; i < hull.Count; i++)
        {
            var (a, b, c) = hull[i];
            byEdge[(a, b)] = i;
            byEdge[(b, c)] = i;
            byEdge[(c, a)] = i;
        }

        var centreOfMass = CentreOfMass(world);
        float smallest = MathF.Max(hullArea * 0.002f, 1f);

        var taken = new bool[hull.Count];
        var faces = new List<RestingFace>();
        var region = new List<int>();
        var search = new Stack<int>();

        for (int seed = 0; seed < hull.Count; seed++)
        {
            if (taken[seed] || normals[seed].LengthSquared() == 0) continue;

            // Grown from one triangle and compared with that one, not with each neighbour in turn:
            // neighbour to neighbour, a gentle curve creeps round in steps too small to refuse.
            var direction = Vector3.Normalize(normals[seed]);
            region.Clear();
            taken[seed] = true;
            search.Push(seed);
            while (search.Count > 0)
            {
                int t = search.Pop();
                region.Add(t);

                var (a, b, c) = hull[t];
                foreach (var (u, v) in new[] { (a, b), (b, c), (c, a) })
                {
                    if (!byEdge.TryGetValue((v, u), out int across) || taken[across]) continue;
                    if (normals[across].LengthSquared() == 0) continue;
                    if (Vector3.Dot(Vector3.Normalize(normals[across]), direction) < SameFlat) continue;

                    taken[across] = true;
                    search.Push(across);
                }
            }

            var face = Make(region);
            if (face.Area >= smallest && Holds(face, centreOfMass)) faces.Add(face);
        }

        return faces.OrderByDescending(f => f.Area).Take(MostShown).ToList();

        RestingFace Make(List<int> triangles)
        {
            var weighted = Vector3.Zero;
            var middle = Vector3.Zero;
            float area = 0;
            var corners = new List<Vector3>(triangles.Count * 3);

            foreach (int t in triangles)
            {
                var (a, b, c) = hull[t];
                float twice = normals[t].Length();
                weighted += normals[t];
                middle += (p[a] + p[b] + p[c]) / 3f * twice;
                area += twice;
                corners.Add(p[a]);
                corners.Add(p[b]);
                corners.Add(p[c]);
            }

            return new RestingFace(Vector3.Normalize(weighted), area > 0 ? middle / area : corners[0], area / 2f, corners);
        }
    }

    /// <summary>Whether the object, standing on this face, has its weight over it.</summary>
    private static bool Holds(RestingFace face, Vector3 centreOfMass)
    {
        var onPlane = centreOfMass - face.Normal * Vector3.Dot(centreOfMass - face.Centre, face.Normal);
        return Contains(face, onPlane, slack: 1e-3f);
    }

    /// <summary>
    /// The face under a line of sight - the nearest one facing it that it passes through - or -1.
    /// Tested against the shape as drawn, shrunk towards its middle, so the pointer picks what it
    /// is visibly over.
    /// </summary>
    public static int Under(IReadOnlyList<RestingFace> faces, Vector3 origin, Vector3 direction)
    {
        int found = -1;
        float nearest = float.MaxValue;

        for (int i = 0; i < faces.Count; i++)
        {
            var face = faces[i];
            float facing = Vector3.Dot(direction, face.Normal);
            if (facing >= 0) continue;

            float t = Vector3.Dot(face.Centre - origin, face.Normal) / facing;
            if (t <= 0 || t >= nearest) continue;

            var point = origin + direction * t;
            var unshrunk = face.Centre + (point - face.Centre) / DrawnShare;
            if (!Contains(face, unshrunk, slack: 0f)) continue;

            nearest = t;
            found = i;
        }

        return found;
    }

    /// <summary>Whether a point on the face's plane lies inside one of its triangles.</summary>
    private static bool Contains(RestingFace face, Vector3 point, float slack)
    {
        var n = face.Normal;
        var tris = face.Triangles;
        for (int i = 0; i + 2 < tris.Count; i += 3)
        {
            var a = tris[i];
            var b = tris[i + 1];
            var c = tris[i + 2];
            float twice = Vector3.Dot(Vector3.Cross(b - a, c - a), n);
            if (twice <= 0) continue;

            float tolerance = -slack * twice;
            if (Vector3.Dot(Vector3.Cross(b - a, point - a), n) >= tolerance
                && Vector3.Dot(Vector3.Cross(c - b, point - b), n) >= tolerance
                && Vector3.Dot(Vector3.Cross(a - c, point - c), n) >= tolerance)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Where the object's weight acts, taking it as solid throughout - which a print with infill is
    /// not quite, but the difference moves the point far less than it takes to tip a part over.
    /// An open mesh encloses no volume to weigh, and its middle is the best there is.
    /// </summary>
    public static Vector3 CentreOfMass(Mesh mesh)
    {
        double volume = 0, mx = 0, my = 0, mz = 0;
        var p = mesh.Positions;
        var ix = mesh.Indices;
        for (int i = 0; i + 2 < ix.Count; i += 3)
        {
            var a = p[ix[i]];
            var b = p[ix[i + 1]];
            var c = p[ix[i + 2]];
            double v = Vector3.Dot(a, Vector3.Cross(b, c)) / 6.0;
            volume += v;
            var sum = a + b + c;
            mx += v * sum.X / 4.0;
            my += v * sum.Y / 4.0;
            mz += v * sum.Z / 4.0;
        }

        return Math.Abs(volume) < 1e-9
            ? mesh.ComputeBounds().Center
            : new Vector3((float)(mx / volume), (float)(my / volume), (float)(mz / volume));
    }
}
