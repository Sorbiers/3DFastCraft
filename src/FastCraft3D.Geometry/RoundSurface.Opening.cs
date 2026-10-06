using System.Numerics;

namespace FastCraft3D.Geometry;

/// <summary>
/// The other kind of hole: one with flat sides - a window's opening in a wall, a square socket, a
/// hexagonal pocket for a nut. Its walls are flat faces that meet along lines parallel to one axis,
/// and picked one at a time none of them has its middle where the hole's is.
/// </summary>
public sealed partial class RoundSurface
{
    /// <summary>How alike the normals of two facets have to be for them to be one flat wall - a degree.</summary>
    private const float Coplanar = 0.9998f;

    /// <summary>How far a wall's normal may lean along the axis and still be a wall of the hole.</summary>
    private const float Square = 0.05f;

    /// <summary>An edge counts as running along the axis when it is this close to it.</summary>
    private const float AlongAxis = 0.999f;

    /// <summary>The most walls an opening has before it is not one: an octagon is eight.</summary>
    private const int MostWalls = 32;

    /// <summary>The shallowest a hole is worth finding, which also keeps a sheet's edge from being one.</summary>
    private const float LeastDepth = 0.05f;

    private sealed class Wall(List<int> triangles, HashSet<int> members, Vector3 normal)
    {
        public List<int> Triangles { get; } = triangles;
        public HashSet<int> Members { get; } = members;
        public Vector3 Normal { get; } = normal;

        /// <summary>The walls it meets along a line parallel to the axis: one each side, in a hole.</summary>
        public HashSet<int> Neighbours { get; } = [];
    }

    private readonly record struct Edge(Vector3 From, Vector3 To, List<int> Across);

    /// <summary>
    /// The hole with flat sides whose wall is under <paramref name="point"/>, or null when it is
    /// not one: the wall of a hole is a flat face, and so is the side of a block, so what tells
    /// them apart is that the walls close round a line and face in toward it.
    ///
    /// Found by the walls that meet it. The wall clicked is a flat face; the lines it meets its
    /// neighbours on are the candidates for the hole's axis, and the right one is where the walls
    /// run on round and close up, every one as deep as the first. A groove in a textured wall that
    /// runs into the opening is not a wall of it - it is shallow - and does not take part.
    /// </summary>
    public static RoundSurface? FindOpening(Mesh mesh, Vector3 point, Vector3 hintNormal)
    {
        if (mesh.TriangleCount == 0) return null;

        var topology = Known.GetValue(mesh, Build);
        int seed = FacePatch.FindSeed(mesh, topology.Normals, point, hintNormal, FacePatch.DefaultToleranceMm * 10);
        if (seed < 0) return null;

        if (WallAt(mesh, topology, seed) is not { } first) return null;

        RoundSurface? best = null;
        float smallest = float.MaxValue;

        // The longest few: a wall's own corners and its top and bottom edges are candidates too, and
        // they close round something else - the whole building, say - so the smallest that closes wins.
        foreach (var axis in EdgeDirections(mesh, topology, first).Take(4))
        {
            if (Enclose(mesh, topology, first, Upright(axis)) is not { } found || found.Area >= smallest) continue;

            best = found.Surface;
            smallest = found.Area;
        }

        return best;
    }

    /// <summary>The flat wall a triangle is part of: the triangles joined to it, all lying one way.</summary>
    private static Wall? WallAt(Mesh mesh, Topology topology, int seed)
    {
        var normals = topology.Normals;
        var normal = normals[seed / 3];
        if (normal == Vector3.Zero) return null;

        var members = new HashSet<int> { seed };
        var triangles = new List<int>();
        var queue = new Queue<int>();
        queue.Enqueue(seed);

        while (queue.Count > 0)
        {
            int t = queue.Dequeue();
            triangles.Add(t);

            for (int k = 0; k < 3; k++)
            {
                var key = Key(topology.Corner[mesh.Indices[t + k]], topology.Corner[mesh.Indices[t + (k + 1) % 3]]);
                if (!topology.Edges.TryGetValue(key, out var sharing)) continue;

                foreach (int u in sharing)
                {
                    // Against the first triangle's normal, not the last one's, so a surface that bends a
                    // little at a time cannot be walked round into one wall.
                    if (members.Contains(u) || normals[u / 3] == Vector3.Zero) continue;
                    if (Vector3.Dot(normals[u / 3], normal) < Coplanar) continue;

                    members.Add(u);
                    queue.Enqueue(u);
                }
            }
        }

        return new Wall(triangles, members, normal);
    }

    /// <summary>The edges a wall shares with triangles that are not part of it, and what is across each.</summary>
    private static List<Edge> BoundaryOf(Mesh mesh, Topology topology, Wall wall)
    {
        var edges = new List<Edge>();

        foreach (int t in wall.Triangles)
            for (int k = 0; k < 3; k++)
            {
                int a = mesh.Indices[t + k], b = mesh.Indices[t + (k + 1) % 3];
                if (!topology.Edges.TryGetValue(Key(topology.Corner[a], topology.Corner[b]), out var sharing)) continue;

                var across = sharing.Where(u => !wall.Members.Contains(u)).ToList();
                if (across.Count > 0) edges.Add(new Edge(mesh.Positions[a], mesh.Positions[b], across));
            }

        return edges;
    }

    /// <summary>The directions the wall's boundary runs in, the one with the most edge first.</summary>
    private static IEnumerable<Vector3> EdgeDirections(Mesh mesh, Topology topology, Wall wall)
    {
        var clusters = new List<(Vector3 Direction, float Length)>();

        foreach (var edge in BoundaryOf(mesh, topology, wall))
        {
            var d = edge.To - edge.From;
            float length = d.Length();
            if (length < 1e-5f) continue;

            d /= length;
            int i = clusters.FindIndex(c => MathF.Abs(Vector3.Dot(c.Direction, d)) > 0.9995f);
            if (i < 0) clusters.Add((d, length));
            else clusters[i] = (clusters[i].Direction, clusters[i].Length + length);
        }

        return clusters.OrderByDescending(c => c.Length).Select(c => c.Direction);
    }

    private static (float Low, float High) Span(Mesh mesh, IEnumerable<int> triangles, Vector3 axis)
    {
        float low = float.MaxValue, high = float.MinValue;
        foreach (int t in triangles)
            for (int k = 0; k < 3; k++)
            {
                float h = Vector3.Dot(mesh.Positions[mesh.Indices[t + k]], axis);
                low = MathF.Min(low, h);
                high = MathF.Max(high, h);
            }

        return (low, high);
    }

    /// <summary>The surface the walls make round <paramref name="axis"/>, if they close up and face in.</summary>
    private static (RoundSurface Surface, float Area)? Enclose(Mesh mesh, Topology topology, Wall first, Vector3 axis)
    {
        var normals = topology.Normals;
        var (low, high) = Span(mesh, first.Triangles, axis);
        float depth = high - low;
        if (depth < LeastDepth) return null;

        // Walls of one hole all run the whole depth of it. A groove, a ledge or a stone's side that
        // meets the opening along a line parallel to the axis is shorter, and is not one of them.
        float slack = MathF.Max(0.02f, depth * 0.02f);
        float turn = MathF.Cos(135f * MathF.PI / 180f);

        var walls = new List<Wall> { first };
        var owner = new Dictionary<int, int>();
        foreach (int t in first.Triangles) owner[t] = 0;

        var queue = new Queue<int>();
        queue.Enqueue(0);

        while (queue.Count > 0 && walls.Count <= MostWalls)
        {
            int index = queue.Dequeue();
            var wall = walls[index];

            foreach (var edge in BoundaryOf(mesh, topology, wall))
            {
                var d = edge.To - edge.From;
                float length = d.Length();
                if (length < 1e-5f || MathF.Abs(Vector3.Dot(d / length, axis)) < AlongAxis) continue;

                foreach (int u in edge.Across)
                {
                    if (owner.TryGetValue(u, out int known))
                    {
                        wall.Neighbours.Add(known);
                        walls[known].Neighbours.Add(index);
                        continue;
                    }

                    var m = normals[u / 3];
                    if (m == Vector3.Zero || MathF.Abs(Vector3.Dot(m, axis)) > Square) continue;
                    if (Vector3.Dot(m, wall.Normal) < turn) continue;

                    if (WallAt(mesh, topology, u) is not { } next) continue;

                    var (a, b) = Span(mesh, next.Triangles, axis);
                    if (MathF.Abs(a - low) > slack || MathF.Abs(b - high) > slack) continue;

                    walls.Add(next);
                    int added = walls.Count - 1;
                    foreach (int t in next.Triangles) owner[t] = added;

                    wall.Neighbours.Add(added);
                    next.Neighbours.Add(index);
                    queue.Enqueue(added);
                }
            }
        }

        // Closed up: three walls at least, each meeting one on either side.
        if (walls.Count < 3 || walls.Count > MostWalls || walls.Any(w => w.Neighbours.Count < 2)) return null;

        // And going all the way round, not a notch in a wall that happens to have three sides.
        var (u0, v0) = FacePatch.PlaneAxes(axis);
        var angles = walls.Select(w => MathF.Atan2(Vector3.Dot(w.Normal, v0), Vector3.Dot(w.Normal, u0))).OrderBy(x => x).ToList();
        float widest = angles[0] + MathF.Tau - angles[^1];
        for (int i = 1; i < angles.Count; i++) widest = MathF.Max(widest, angles[i] - angles[i - 1]);
        if (widest > 125f * MathF.PI / 180f) return null;

        // The corners seen down the axis, and the smallest rectangle that holds them lying along one
        // of the walls: its middle is the hole's, and its sides what a pin has to fit between.
        var corners = new HashSet<int>();
        var points = new List<Vector3>();
        foreach (var wall in walls)
            foreach (int t in wall.Triangles)
                for (int k = 0; k < 3; k++)
                    if (corners.Add(topology.Corner[mesh.Indices[t + k]])) points.Add(mesh.Positions[mesh.Indices[t + k]]);

        var mean = points.Aggregate(Vector3.Zero, (s, p) => s + p) / points.Count;
        var flat = new List<(float X, float Y)>(points.Count);
        float bottom = float.MaxValue, top = float.MinValue;
        foreach (var p in points)
        {
            var off = p - mean;
            flat.Add((Vector3.Dot(off, u0), Vector3.Dot(off, v0)));
            float h = Vector3.Dot(off, axis);
            bottom = MathF.Min(bottom, h);
            top = MathF.Max(top, h);
        }

        float smallest = float.MaxValue, cx = 0, cy = 0, wide = 0, narrow = 0;
        foreach (float angle in angles)
        {
            float c = MathF.Cos(angle), s = MathF.Sin(angle);
            float minA = float.MaxValue, maxA = float.MinValue, minB = float.MaxValue, maxB = float.MinValue;

            foreach (var (x, y) in flat)
            {
                float a = x * c + y * s, b = -x * s + y * c;
                minA = MathF.Min(minA, a); maxA = MathF.Max(maxA, a);
                minB = MathF.Min(minB, b); maxB = MathF.Max(maxB, b);
            }

            float area = (maxA - minA) * (maxB - minB);
            if (area >= smallest) continue;

            smallest = area;
            float midA = (minA + maxA) / 2f, midB = (minB + maxB) / 2f;
            cx = midA * c - midB * s;
            cy = midA * s + midB * c;
            wide = MathF.Max(maxA - minA, maxB - minB);
            narrow = MathF.Min(maxA - minA, maxB - minB);
        }

        var centre = mean + u0 * cx + v0 * cy + axis * ((bottom + top) / 2f);

        // A hole's walls face in toward its axis; the sides of a block face out, and are left to be
        // the flat faces they are.
        var region = new List<int>();
        float facing = 0, total = 0;
        foreach (var wall in walls)
            foreach (int t in wall.Triangles)
            {
                region.Add(t);

                var a = mesh.Positions[mesh.Indices[t]];
                var b = mesh.Positions[mesh.Indices[t + 1]];
                var c = mesh.Positions[mesh.Indices[t + 2]];
                total += Vector3.Cross(b - a, c - a).Length() / 2f;

                var off = a - centre;
                off -= axis * Vector3.Dot(off, axis);
                facing += Vector3.Dot(normals[t / 3], off);
            }

        if (facing >= 0) return null;

        region.Sort();
        var surface = new RoundSurface(
            FacePatch.Curved(mesh, region, total), axis, centre, (wide + narrow) / 4f, top - bottom,
            isHole: true, isRound: false, wide, narrow);

        return (surface, total);
    }
}
