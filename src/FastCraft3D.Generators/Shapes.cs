using System.Numerics;
using FastCraft3D.Geometry;
using FastCraft3D.Geometry.Csg;

namespace FastCraft3D.Generators;

/// <summary>
/// Settings that cannot be made, found partway through making them. Thrown from anywhere in a
/// build and turned into a refusal by <see cref="Generator.Make"/>, so a helper deep inside a
/// generator can say why without every caller passing the reason back up.
/// </summary>
public sealed class Refusal(string why) : Exception(why);

/// <summary>
/// The solids generators are made of: outlines, prisms, lofts and the booleans that join them.
///
/// Laid face by face where the shape allows - a prism, a loft, a turned profile - since that is
/// closed by construction and has no seam inside. Where parts have to be joined or cut, through
/// Manifold and never the BSP engine: the BSP tears on fine detail against a curve, and a
/// generator has no one watching to notice. If Manifold is missing on a PC, the generator refuses
/// rather than falling back.
/// </summary>
public static class Shapes
{
    /// <summary>Enough sides that a printed circle does not show its facets, and no more.</summary>
    public static int Sides(float radius) => Math.Clamp((int)MathF.Ceiling(2f * MathF.PI * radius / 0.6f), 20, 180);

    /// <summary>A circle, anticlockwise.</summary>
    public static List<Vector2> Circle(float radius, Vector2 centre = default, int sides = 0)
    {
        int n = sides > 0 ? sides : Sides(radius);
        var loop = new List<Vector2>(n);
        for (int i = 0; i < n; i++)
        {
            float a = 2f * MathF.PI * i / n;
            loop.Add(centre + radius * new Vector2(MathF.Cos(a), MathF.Sin(a)));
        }

        return loop;
    }

    /// <summary>A regular polygon with a flat at the bottom when it has an even number of sides: a hexagon for a nut.</summary>
    public static List<Vector2> Polygon(float acrossFlats, int sides, Vector2 centre = default)
    {
        float radius = acrossFlats / 2f / MathF.Cos(MathF.PI / sides);
        var loop = new List<Vector2>(sides);
        for (int i = 0; i < sides; i++)
        {
            float a = MathF.PI / sides + 2f * MathF.PI * i / sides;
            loop.Add(centre + radius * new Vector2(MathF.Cos(a), MathF.Sin(a)));
        }

        return loop;
    }

    public static List<Vector2> Rect(float x0, float y0, float x1, float y1) =>
        [new(x0, y0), new(x1, y0), new(x1, y1), new(x0, y1)];

    /// <summary>
    /// A rectangle <paramref name="width"/> by <paramref name="depth"/> about <paramref name="centre"/>
    /// with its corners rounded, anticlockwise. With <paramref name="steps"/> given, every corner has
    /// exactly that many steps whatever its radius, so loops of different sizes can be lofted.
    /// </summary>
    public static List<Vector2> RoundedRect(float width, float depth, float radius, Vector2 centre = default, int steps = 0)
    {
        float hw = width / 2f, hd = depth / 2f;
        radius = Math.Clamp(radius, 0f, MathF.Min(hw, hd));

        if (radius < 0.05f && steps == 0)
            return Rect(centre.X - hw, centre.Y - hd, centre.X + hw, centre.Y + hd);

        radius = MathF.Max(radius, 0.05f);
        int n = steps > 0 ? steps : Math.Clamp((int)MathF.Ceiling(radius * 1.5f), 4, 24);
        var corners = new[]
        {
            (new Vector2(hw - radius, -hd + radius), -MathF.PI / 2f),
            (new Vector2(hw - radius, hd - radius), 0f),
            (new Vector2(-hw + radius, hd - radius), MathF.PI / 2f),
            (new Vector2(-hw + radius, -hd + radius), MathF.PI)
        };

        var loop = new List<Vector2>();
        foreach (var (c, from) in corners)
            for (int i = 0; i <= n; i++)
            {
                float a = from + MathF.PI / 2f * i / n;
                var p = centre + c + radius * new Vector2(MathF.Cos(a), MathF.Sin(a));

                // With lofting, points stay even where they coincide, so every loop keeps its count.
                if (steps > 0 || loop.Count == 0 || Vector2.DistanceSquared(loop[^1], p) > 1e-6f) loop.Add(p);
            }

        if (steps == 0 && Vector2.DistanceSquared(loop[0], loop[^1]) <= 1e-6f) loop.RemoveAt(loop.Count - 1);
        return loop;
    }

    /// <summary>An outline and its holes stood up from <paramref name="low"/> to <paramref name="high"/>, closed.</summary>
    public static Mesh Prism(IReadOnlyList<Vector2> outline, IReadOnlyList<IReadOnlyList<Vector2>> holes, float low, float high)
    {
        var outer = Anticlockwise(outline);
        var inner = holes.Select(Anticlockwise).ToList();

        var mesh = new Mesh();
        Cap(mesh, outer, inner, low, up: false);
        Cap(mesh, outer, inner, high, up: true);
        Side(mesh, outer, low, high, outward: true);
        foreach (var hole in inner) Side(mesh, hole, low, high, outward: false);

        return mesh.Welded();
    }

    public static Mesh Prism(IReadOnlyList<Vector2> outline, float low, float high) => Prism(outline, [], low, high);

    public static Mesh Box(float x0, float y0, float z0, float x1, float y1, float z1) =>
        Prism(Rect(MathF.Min(x0, x1), MathF.Min(y0, y1), MathF.Max(x0, x1), MathF.Max(y0, y1)), MathF.Min(z0, z1), MathF.Max(z0, z1));

    public static Mesh Cylinder(float radius, float low, float high, Vector2 centre = default, int sides = 0) =>
        Prism(Circle(radius, centre, sides), low, high);

    public static Mesh Tube(float outside, float inside, float low, float high, Vector2 centre = default) =>
        Prism(Circle(outside, centre), [Circle(inside, centre, Sides(outside))], low, high);

    /// <summary>
    /// A solid through a stack of loops, each at its own height, all with the same number of
    /// points and starting at the same corner: a chamfered foot, a tapering body.
    /// </summary>
    public static Mesh Loft(IReadOnlyList<(float Z, List<Vector2> Loop)> layers)
    {
        if (layers.Count < 2) throw new ArgumentException("A loft needs two layers.");
        int n = layers[0].Loop.Count;
        if (layers.Any(l => l.Loop.Count != n)) throw new ArgumentException("Every layer of a loft needs the same number of points.");

        var loops = layers.Select(l => Anticlockwise(l.Loop)).ToList();
        var mesh = new Mesh();
        Cap(mesh, loops[0], [], layers[0].Z, up: false);
        Cap(mesh, loops[^1], [], layers[^1].Z, up: true);

        for (int k = 0; k + 1 < layers.Count; k++)
        {
            float z0 = layers[k].Z, z1 = layers[k + 1].Z;
            var a = loops[k];
            var b = loops[k + 1];
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                Vector3 p0 = new(a[i], z0), q0 = new(a[j], z0), p1 = new(b[i], z1), q1 = new(b[j], z1);
                mesh.AddTriangle(p0, q0, q1);
                mesh.AddTriangle(p0, q1, p1);
            }
        }

        var built = mesh.Welded();
        if (built.ComputeSignedVolume() < 0) built.FlipWinding();
        return built;
    }

    /// <summary>A loft of rounded rectangles: (height, width, depth, corner radius) at each layer.</summary>
    public static Mesh RoundedLoft(IReadOnlyList<(float Z, float Width, float Depth, float Radius)> layers, Vector2 centre = default) =>
        Loft(layers.Select(l => (l.Z, RoundedRect(l.Width, l.Depth, l.Radius, centre, steps: 8))).ToList());

    /// <summary>
    /// A profile turned about the Z axis: the loop is (radius, height) pairs, anticlockwise, and
    /// must not touch the axis - a disc with a hole, a ring, a race.
    /// </summary>
    public static Mesh Turned(IReadOnlyList<Vector2> profile, int sides = 0)
    {
        var loop = Anticlockwise(profile);
        int n = sides > 0 ? sides : Sides(loop.Max(p => p.X));
        var mesh = new Mesh();

        for (int s = 0; s < n; s++)
        {
            float a0 = 2f * MathF.PI * s / n, a1 = 2f * MathF.PI * (s + 1) / n;
            Vector3 At(Vector2 p, float a) => new(p.X * MathF.Cos(a), p.X * MathF.Sin(a), p.Y);

            for (int i = 0; i < loop.Count; i++)
            {
                var p = loop[i];
                var q = loop[(i + 1) % loop.Count];
                mesh.AddTriangle(At(p, a0), At(q, a1), At(q, a0));
                mesh.AddTriangle(At(p, a0), At(p, a1), At(q, a1));
            }
        }

        var built = mesh.Welded();
        if (built.ComputeSignedVolume() < 0) built.FlipWinding();
        return built;
    }

    public static Mesh Moved(Mesh mesh, float x, float y, float z) =>
        MeshTransform.Transformed(mesh, Matrix4x4.CreateTranslation(x, y, z));

    public static Mesh Turned(Mesh mesh, float radiansAboutZ, Vector2 about = default) =>
        MeshTransform.Transformed(mesh,
            Matrix4x4.CreateTranslation(-about.X, -about.Y, 0) * Matrix4x4.CreateRotationZ(radiansAboutZ)
            * Matrix4x4.CreateTranslation(about.X, about.Y, 0));

    /// <summary>A solid rod along X, from <paramref name="x0"/> to <paramref name="x1"/>, its axis at (y, z).</summary>
    public static Mesh RodAlongX(float radius, float x0, float x1, float y, float z, int sides = 0) =>
        MeshTransform.Transformed(Cylinder(radius, x0, x1, sides: sides),
            Matrix4x4.CreateRotationY(MathF.PI / 2f) * Matrix4x4.CreateTranslation(0, y, z));

    /// <summary>A solid rod along Y, from <paramref name="y0"/> to <paramref name="y1"/>, its axis at (x, z).</summary>
    public static Mesh RodAlongY(float radius, float y0, float y1, float x, float z, int sides = 0) =>
        MeshTransform.Transformed(Cylinder(radius, -y1, -y0, sides: sides),
            Matrix4x4.CreateRotationX(MathF.PI / 2f) * Matrix4x4.CreateTranslation(x, 0, z));

    /// <summary>
    /// Parts laid in a row along X to print, each standing on the plate, the row centred on the
    /// origin. Each is given as it prints, standing on the plate anywhere, with where it goes when
    /// the set is put together - or null for a set that is only ever printed.
    /// </summary>
    public static List<GeneratedPart> InARow(IReadOnlyList<(string Name, string Role, Mesh Printed, Matrix4x4? Together)> parts, float gap = 5f)
    {
        var laid = new List<(string Name, string Role, Mesh Mesh, Vector3 Moved, Matrix4x4? Together)>();
        float cursor = 0f;

        foreach (var (name, role, printed, together) in parts)
        {
            var b = printed.ComputeBounds();
            var moved = new Vector3(cursor - b.Min.X, -b.Center.Y, -b.Min.Z);
            laid.Add((name, role, Moved(printed, moved.X, moved.Y, moved.Z), moved, together));
            cursor += b.Size.X + gap;
        }

        float middle = (cursor - gap) / 2f;
        return laid.Select(l =>
        {
            var mesh = Moved(l.Mesh, -middle, 0, 0);
            var shift = l.Moved + new Vector3(-middle, 0, 0);
            var b = mesh.ComputeBounds();
            return new GeneratedPart(l.Name, mesh,
                l.Together is { } together ? Matrix4x4.CreateTranslation(-shift) * together : null,
                Role: l.Role,
                Pivot: new Vector3(b.Center.X, b.Center.Y, 0));
        }).ToList();
    }

    /// <summary>Joins solids into one. Refuses, rather than falling back to the BSP engine, if they will not join.</summary>
    public static Mesh Union(params Mesh[] pieces) => Union((IReadOnlyList<Mesh>)pieces);

    public static Mesh Union(IReadOnlyList<Mesh> pieces)
    {
        var parts = pieces.Where(p => p.TriangleCount > 0).ToList();
        if (parts.Count == 0) throw new Refusal("There is nothing to make.");
        if (parts.Count == 1) return parts[0];

        return ManifoldCsg.UnionAll(parts) is { TriangleCount: > 0 } joined ? Tidied(joined) : throw Failed();
    }

    /// <summary>Takes the tools out of the solid. Refuses if they will not cut cleanly.</summary>
    public static Mesh Subtract(Mesh solid, params Mesh[] tools) => Subtract(solid, (IReadOnlyList<Mesh>)tools);

    public static Mesh Subtract(Mesh solid, IReadOnlyList<Mesh> tools)
    {
        var cutting = tools.Where(t => t.TriangleCount > 0).ToList();
        if (cutting.Count == 0) return solid;

        return ManifoldCsg.SubtractAll(solid, cutting) is { TriangleCount: > 0 } cut ? Tidied(cut) : throw Failed();
    }

    public static Mesh Intersect(Mesh solid, Mesh tool) =>
        ManifoldCsg.Intersect(solid, tool) is { TriangleCount: > 0 } common ? Tidied(common) : throw Failed();

    /// <summary>
    /// The result less any triangle that lies on its own reverse. Manifold now and then hands one
    /// back - three points in a line, once each way round - where cuts meet along a line, as the
    /// rows of a number cut into a thin base did. The solid is right and the pair adds nothing to
    /// it, but welded for the health check the line has four faces on it. Only exact pairs go, and
    /// only when that is what closes the result; otherwise it is returned as it came.
    /// </summary>
    private static Mesh Tidied(Mesh mesh)
    {
        var welded = mesh.Welded();
        var idx = welded.Indices;
        var seen = new Dictionary<(int, int, int), int>();
        var drop = new HashSet<int>();

        for (int t = 0; t + 2 < idx.Count; t += 3)
        {
            int a = idx[t], b = idx[t + 1], c = idx[t + 2];
            var key = Sorted(a, b, c);
            if (seen.TryGetValue(key, out int other) && !drop.Contains(other) && Reverses(idx, t, other))
            {
                drop.Add(t);
                drop.Add(other);
                seen.Remove(key);
            }
            else
            {
                seen[key] = t;
            }
        }

        if (drop.Count == 0) return mesh;

        var kept = new List<int>(idx.Count - 3 * drop.Count);
        for (int t = 0; t + 2 < idx.Count; t += 3)
            if (!drop.Contains(t)) kept.AddRange([idx[t], idx[t + 1], idx[t + 2]]);

        var tidied = new Mesh(welded.Positions, kept);
        return tidied.CheckHealth().IsWatertight && !mesh.CheckHealth().IsWatertight ? tidied : mesh;

        static (int, int, int) Sorted(int a, int b, int c)
        {
            if (a > b) (a, b) = (b, a);
            if (b > c) (b, c) = (c, b);
            if (a > b) (a, b) = (b, a);
            return (a, b, c);
        }

        // The same three corners, wound the other way.
        static bool Reverses(List<int> idx, int t, int u)
        {
            for (int i = 0; i < 3; i++)
                if (idx[u + i] == idx[t])
                    return idx[u + (i + 2) % 3] == idx[t + 1];
            return false;
        }
    }

    private static Refusal Failed() => new(ManifoldCsg.Unavailable
        ? "This needs the Manifold boolean engine, which did not load on this PC."
        : "The pieces would not join into one clean solid. Try slightly different numbers.");

    private static List<Vector2> Anticlockwise(IReadOnlyList<Vector2> loop)
    {
        var copy = loop.ToList();
        if (Polygon2.SignedArea(copy) < 0) copy.Reverse();
        return copy;
    }

    private static void Cap(Mesh mesh, List<Vector2> outline, List<List<Vector2>> holes, float z, bool up)
    {
        var (points, triangles) = Polygon2.Triangulate(outline, holes.Cast<IReadOnlyList<Vector2>>().ToList());

        for (int i = 0; i + 2 < triangles.Count; i += 3)
        {
            Vector2 a = points[triangles[i]], b = points[triangles[i + 1]], c = points[triangles[i + 2]];

            // The triangulation says it winds anticlockwise; asked rather than trusted, since one
            // face the wrong way round is a solid that is not closed. But a triangle with no area -
            // three corners in a line, as a stair's inner corners are - has no winding to ask, and
            // flipped at random it turned six faces of every stair; it keeps the one it was given.
            float turn = (b.X - a.X) * (c.Y - a.Y) - (c.X - a.X) * (b.Y - a.Y);
            bool anticlockwise = turn > 0 || MathF.Abs(turn) < 1e-9f;
            if (anticlockwise != up) (b, c) = (c, b);

            mesh.AddTriangle(new(a, z), new(b, z), new(c, z));
        }
    }

    private static void Side(Mesh mesh, List<Vector2> loop, float low, float high, bool outward)
    {
        for (int i = 0; i < loop.Count; i++)
        {
            Vector3 p0 = new(loop[i], low), q0 = new(loop[(i + 1) % loop.Count], low);
            Vector3 p1 = new(loop[i], high), q1 = new(loop[(i + 1) % loop.Count], high);

            if (outward)
            {
                mesh.AddTriangle(p0, q0, q1);
                mesh.AddTriangle(p0, q1, p1);
            }
            else
            {
                mesh.AddTriangle(p0, q1, q0);
                mesh.AddTriangle(p0, p1, q1);
            }
        }
    }
}
