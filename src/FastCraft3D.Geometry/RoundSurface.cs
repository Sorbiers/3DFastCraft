using System.Numerics;
using System.Runtime.CompilerServices;

namespace FastCraft3D.Geometry;

/// <summary>
/// A round surface picked on a mesh - a pin's side, a hole's wall, a boss - and the axis it goes
/// round, so two of them can be put on one line: a pin in its hole, a shaft in its bore.
///
/// A flat face will not do for this. A cylinder is facets, and the one under the pointer has its
/// middle off to one side of the axis by the radius; a hole straight through has no floor to pick
/// at all. So the facets round the one clicked are gathered while they keep turning about one
/// line, and a circle is fitted to their corners across that line.
/// </summary>
public sealed class RoundSurface
{
    /// <summary>How sharply two neighbouring facets may meet and still be one round surface.</summary>
    private const float TurnDegrees = 40f;

    /// <summary>How far a facet's normal may lean along the axis and still go round it.</summary>
    private const float Lean = 0.2f;

    private RoundSurface(FacePatch patch, Vector3 axis, Vector3 centre, float radius, float length, bool isHole)
    {
        Patch = patch;
        Axis = axis;
        Centre = centre;
        Radius = radius;
        Length = length;
        IsHole = isHole;
    }

    /// <summary>The triangles gathered, to light in the viewport.</summary>
    public FacePatch Patch { get; }

    /// <summary>Unit direction of the axis, Z-up where it can be.</summary>
    public Vector3 Axis { get; }

    /// <summary>On the axis, halfway along the surface.</summary>
    public Vector3 Centre { get; }

    public float Radius { get; }

    /// <summary>How far the surface runs along its axis.</summary>
    public float Length { get; }

    /// <summary>Whether it faces its axis - a hole or a bore - rather than away from it.</summary>
    public bool IsHole { get; }

    public float Diameter => 2f * Radius;

    /// <summary>Normals and which triangles share each edge, kept per mesh: hovering asks on every move.</summary>
    private sealed record Topology(Vector3[] Normals, Dictionary<(int, int), List<int>> Edges, int[] Corner);

    private static readonly ConditionalWeakTable<Mesh, Topology> Known = new();

    /// <summary>
    /// The round surface containing <paramref name="point"/>, or null when it is on a flat face,
    /// or on something that does not go round one line. Both arguments are in the mesh's space.
    /// </summary>
    public static RoundSurface? Find(Mesh mesh, Vector3 point, Vector3 hintNormal)
    {
        if (mesh.TriangleCount == 0) return null;

        var topology = Known.GetValue(mesh, Build);
        var normals = topology.Normals;
        int seed = FacePatch.FindSeed(mesh, normals, point, hintNormal, FacePatch.DefaultToleranceMm * 10);
        if (seed < 0) return null;

        var start = normals[seed / 3];
        float turn = MathF.Cos(TurnDegrees * MathF.PI / 180f);
        float alike = MathF.Cos(3f * MathF.PI / 180f);

        Vector3? axis = null;
        var region = new List<int> { seed };
        var seen = new HashSet<int> { seed };
        var queue = new Queue<int>();
        queue.Enqueue(seed);

        while (queue.Count > 0)
        {
            int t = queue.Dequeue();
            var n = normals[t / 3];

            for (int k = 0; k < 3; k++)
            {
                var key = Key(topology.Corner[mesh.Indices[t + k]], topology.Corner[mesh.Indices[t + (k + 1) % 3]]);
                if (!topology.Edges.TryGetValue(key, out var sharing)) continue;

                foreach (int u in sharing)
                {
                    if (seen.Contains(u)) continue;
                    var m = normals[u / 3];
                    if (m == Vector3.Zero || Vector3.Dot(n, m) < turn) continue;

                    // The first facet that turns away from the one clicked says which line the
                    // surface goes round; after that only facets going round the same line join.
                    if (axis is { } a)
                    {
                        if (MathF.Abs(Vector3.Dot(m, a)) > Lean) continue;
                    }
                    else if (Vector3.Dot(start, m) < alike)
                    {
                        var c = Vector3.Cross(start, m);
                        if (c.LengthSquared() < 1e-12f) continue;
                        axis = Vector3.Normalize(c);
                    }

                    seen.Add(u);
                    region.Add(u);
                    queue.Enqueue(u);
                }
            }
        }

        if (axis is null || region.Count < 3) return null;

        // The line the normals are all square to: the least of their spread. Weighted by area,
        // so a sliver along a seam does not tilt it.
        var spread = new double[3, 3];
        float area = 0;
        foreach (int t in region)
        {
            var a = mesh.Positions[mesh.Indices[t]];
            var b = mesh.Positions[mesh.Indices[t + 1]];
            var c = mesh.Positions[mesh.Indices[t + 2]];
            float w = Vector3.Cross(b - a, c - a).Length() / 2f;
            area += w;
            var n = normals[t / 3];
            for (int i = 0; i < 3; i++)
                for (int j = 0; j < 3; j++)
                    spread[i, j] += w * n[i] * n[j];
        }

        var line = Least(spread, axis.Value);

        // Going round a fair part of a turn, or it is a gentle bend and not a round surface.
        var (u0, v0) = FacePatch.PlaneAxes(line);
        float Angle(Vector3 n) => MathF.Atan2(Vector3.Dot(n, v0), Vector3.Dot(n, u0));
        float first = Angle(start), widest = 0;
        foreach (int t in region)
        {
            float d = MathF.Abs(Angle(normals[t / 3]) - first);
            widest = MathF.Max(widest, MathF.Min(d, MathF.Tau - d));
        }
        if (widest < 40f * MathF.PI / 180f) return null;

        // A circle through the corners, seen down the axis: least squares on x² + y² + Dx + Ey + F.
        var corners = new HashSet<int>();
        foreach (int t in region)
            for (int k = 0; k < 3; k++) corners.Add(mesh.Indices[t + k]);

        var points = corners.Select(i => mesh.Positions[i]).ToList();
        var mean = points.Aggregate(Vector3.Zero, (s, p) => s + p) / points.Count;

        double sxx = 0, sxy = 0, syy = 0, sx = 0, sy = 0, sxr = 0, syr = 0, sr = 0;
        float low = float.MaxValue, high = float.MinValue;
        foreach (var p in points)
        {
            var d = p - mean;
            double x = Vector3.Dot(d, u0), y = Vector3.Dot(d, v0), r = x * x + y * y;
            sxx += x * x; sxy += x * y; syy += y * y; sx += x; sy += y;
            sxr += x * r; syr += y * r; sr += r;
            float h = Vector3.Dot(d, line);
            low = MathF.Min(low, h);
            high = MathF.Max(high, h);
        }

        if (!Solve(sxx, sxy, sx, sxy, syy, sy, sx, sy, points.Count, -sxr, -syr, -sr, out double dx, out double ey, out double f))
            return null;

        double cx = -dx / 2, cy = -ey / 2, rr = cx * cx + cy * cy - f;
        if (!(rr > 1e-8)) return null;
        float radius = (float)Math.Sqrt(rr);

        // Round, not merely bent: the corners have to sit near the circle.
        double miss = 0;
        foreach (var p in points)
        {
            var d = p - mean;
            double x = Vector3.Dot(d, u0) - cx, y = Vector3.Dot(d, v0) - cy;
            miss = Math.Max(miss, Math.Abs(Math.Sqrt(x * x + y * y) - radius));
        }
        if (miss > 0.1 * radius + 0.02) return null;

        var centre = mean + u0 * (float)cx + v0 * (float)cy + line * ((low + high) / 2f);

        // A hole's wall faces its axis.
        float facing = 0;
        foreach (int t in region)
        {
            var p = mesh.Positions[mesh.Indices[t]];
            var off = p - centre;
            off -= line * Vector3.Dot(off, line);
            facing += Vector3.Dot(normals[t / 3], off);
        }

        return new RoundSurface(FacePatch.Curved(mesh, region, area), line, centre, radius, high - low, facing < 0);
    }

    private static Topology Build(Mesh mesh)
    {
        // Corners matched by where they are, not by index: an imported mesh repeats a corner for
        // every triangle it belongs to.
        var corner = new int[mesh.Positions.Count];
        var byPlace = new Dictionary<(long, long, long), int>();
        for (int i = 0; i < mesh.Positions.Count; i++)
        {
            var p = mesh.Positions[i];
            var key = ((long)MathF.Round(p.X * 1e4f), (long)MathF.Round(p.Y * 1e4f), (long)MathF.Round(p.Z * 1e4f));
            if (!byPlace.TryGetValue(key, out int id)) byPlace[key] = id = i;
            corner[i] = id;
        }

        var edges = new Dictionary<(int, int), List<int>>();
        for (int t = 0; t + 2 < mesh.Indices.Count; t += 3)
            for (int k = 0; k < 3; k++)
            {
                var key = Key(corner[mesh.Indices[t + k]], corner[mesh.Indices[t + (k + 1) % 3]]);
                if (!edges.TryGetValue(key, out var list)) edges[key] = list = [];
                list.Add(t);
            }

        return new Topology(FacePatch.TriangleNormals(mesh), edges, corner);
    }

    private static (int, int) Key(int a, int b) => a < b ? (a, b) : (b, a);

    /// <summary>
    /// The direction the spread is least along, found by power iteration on its complement,
    /// started from the first guess. Pointed up, or along its largest part, so it reads the same
    /// whichever facet was clicked.
    /// </summary>
    private static Vector3 Least(double[,] m, Vector3 guess)
    {
        double trace = m[0, 0] + m[1, 1] + m[2, 2];
        double x = guess.X, y = guess.Y, z = guess.Z;
        for (int i = 0; i < 100; i++)
        {
            double nx = trace * x - (m[0, 0] * x + m[0, 1] * y + m[0, 2] * z);
            double ny = trace * y - (m[1, 0] * x + m[1, 1] * y + m[1, 2] * z);
            double nz = trace * z - (m[2, 0] * x + m[2, 1] * y + m[2, 2] * z);
            double length = Math.Sqrt(nx * nx + ny * ny + nz * nz);
            if (length < 1e-12) break;
            x = nx / length; y = ny / length; z = nz / length;
        }

        var axis = Vector3.Normalize(new Vector3((float)x, (float)y, (float)z));
        float biggest = MathF.Abs(axis.Z) > 0.3f ? axis.Z
            : MathF.Abs(axis.X) >= MathF.Abs(axis.Y) ? axis.X : axis.Y;
        return biggest < 0 ? -axis : axis;
    }

    /// <summary>Three equations in three unknowns, by Cramer's rule.</summary>
    private static bool Solve(
        double a, double b, double c, double d, double e, double f, double g, double h, double i,
        double p, double q, double r, out double x, out double y, out double z)
    {
        double det = a * (e * i - f * h) - b * (d * i - f * g) + c * (d * h - e * g);
        x = y = z = 0;
        if (Math.Abs(det) < 1e-12) return false;

        x = (p * (e * i - f * h) - b * (q * i - f * r) + c * (q * h - e * r)) / det;
        y = (a * (q * i - f * r) - p * (d * i - f * g) + c * (d * r - q * g)) / det;
        z = (a * (e * r - q * h) - b * (d * r - q * g) + p * (d * h - e * g)) / det;
        return true;
    }
}
