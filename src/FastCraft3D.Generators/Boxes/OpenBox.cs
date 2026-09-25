using System.Numerics;
using FastCraft3D.Geometry;

namespace FastCraft3D.Generators.Boxes;

/// <summary>
/// A box with no lid, sized from the outside.
///
/// Built face by face as one closed solid rather than an outer box with an inner one subtracted.
/// The subtraction would leave the floor's top face and the rim to the boolean, which is the kind
/// of flat against flat it handles worst; laying the faces directly has no seam inside at all,
/// and every loop is shared exactly between the faces either side of it.
/// </summary>
public sealed class OpenBox : Generator<OpenBox.Settings>
{
    /// <summary>Below this a corner is square. A radius of a hundredth has points closer than the weld joins.</summary>
    private const float SmallestRadius = 0.05f;

    public override string Id => "box.open";
    public override int Version => 1;
    public override string Category => "Boxes";
    public override string Title => "Open box";
    public override string Summary => "A box with no lid, sized from the outside, with walls and floor as one solid.";

    public sealed record Settings(
        [Length("Width", 5, 200, Hint = "Outside, left to right", Group = "Size")] float Width = 60f,
        [Length("Depth", 5, 200, Hint = "Outside, front to back", Group = "Size")] float Depth = 40f,
        [Length("Height", 2, 200, Hint = "Outside, from the bottom to the rim", Group = "Size")] float Height = 30f,
        [Wall("Wall", 0.4, 20, Hint = "How thick the sides are. Two or three nozzle widths prints cleanly.", Group = "Walls")] float Wall = 1.6f,
        [Length("Floor", 0.4, 20, Hint = "How thick the bottom is", Group = "Walls")] float Floor = 1.2f,
        [Length("Corner radius", 0, 100, Hint = "On the outside. The inside is rounded by this less the wall, so the wall stays even all the way round.", Group = "Walls")] float Radius = 3f);

    protected override IEnumerable<(string Name, Settings Settings)> Shipped =>
    [
        ("Small parts tray", Default with { Width = 80, Depth = 50, Height = 20, Radius = 3 }),
        ("Card deck", Default with { Width = 70, Depth = 96, Height = 30, Radius = 2 }),
        ("Pen pot", Default with { Width = 70, Depth = 70, Height = 100, Radius = 35 }),
        ("Drawer bin", Default with { Width = 100, Depth = 150, Height = 50, Radius = 4 })
    ];

    protected override IEnumerable<string> Check(Settings s, Printer printer)
    {
        if (s.Width - 2 * s.Wall < 1f)
            yield return $"Two walls of {s.Wall:0.##} mm leave no room inside a box {s.Width:0.##} mm wide.";

        if (s.Depth - 2 * s.Wall < 1f)
            yield return $"Two walls of {s.Wall:0.##} mm leave no room inside a box {s.Depth:0.##} mm deep.";

        if (s.Height - s.Floor < 0.5f)
            yield return $"A floor of {s.Floor:0.##} mm fills a box {s.Height:0.##} mm high.";

        float half = MathF.Min(s.Width, s.Depth) / 2f;
        if (s.Radius > half)
            yield return $"A corner radius over {half:0.##} mm is more than half the box across.";
    }

    protected override Generated Build(Settings s, Printer printer, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var mesh = Tray(s.Width, s.Depth, s.Height, s.Wall, s.Floor, s.Radius);

        var notes = new List<string>();
        if (s.Wall < printer.Nozzle)
            notes.Add($"The wall is thinner than one nozzle width ({printer.Nozzle:0.##} mm), and a slicer may leave it out.");

        return new Generated([new GeneratedPart("Box", mesh, Role: "box")], notes);
    }

    /// <summary>
    /// An open box, centred on the origin, standing on the plate: the one solid every box, tray
    /// and drawer here starts from.
    /// </summary>
    public static Mesh Tray(float width, float depth, float height, float wall, float floor, float radius)
    {
        radius = radius < SmallestRadius ? 0f : MathF.Min(radius, MathF.Min(width, depth) / 2f);
        float inside = radius - wall < SmallestRadius ? 0f : radius - wall;

        var outer = RoundedRectangle(width / 2f, depth / 2f, radius);
        var inner = RoundedRectangle(width / 2f - wall, depth / 2f - wall, inside);

        var mesh = new Mesh();
        Cap(mesh, outer, [], 0f, up: false);
        Cap(mesh, outer, [inner], height, up: true);
        Cap(mesh, inner, [], floor, up: true);
        Side(mesh, outer, 0f, height, outward: true);
        Side(mesh, inner, floor, height, outward: false);

        return mesh.Welded();
    }

    /// <summary>A rectangle centred on the origin with its corners rounded, anticlockwise.</summary>
    internal static List<Vector2> RoundedRectangle(float halfWidth, float halfDepth, float radius)
    {
        if (radius <= 0f)
            return [new(-halfWidth, -halfDepth), new(halfWidth, -halfDepth), new(halfWidth, halfDepth), new(-halfWidth, halfDepth)];

        int steps = Math.Clamp((int)MathF.Ceiling(radius * 1.5f), 4, 24);
        var corners = new[]
        {
            (Centre: new Vector2(halfWidth - radius, -halfDepth + radius), From: -MathF.PI / 2f),
            (Centre: new Vector2(halfWidth - radius, halfDepth - radius), From: 0f),
            (Centre: new Vector2(-halfWidth + radius, halfDepth - radius), From: MathF.PI / 2f),
            (Centre: new Vector2(-halfWidth + radius, -halfDepth + radius), From: MathF.PI)
        };

        var loop = new List<Vector2>();
        foreach (var (centre, from) in corners)
            for (int i = 0; i <= steps; i++)
            {
                float a = from + MathF.PI / 2f * i / steps;
                var p = centre + radius * new Vector2(MathF.Cos(a), MathF.Sin(a));

                // Where the radius is the whole of a side, one corner ends where the next begins,
                // and two points in one place make an edge of no length.
                if (loop.Count == 0 || Vector2.DistanceSquared(loop[^1], p) > 1e-6f) loop.Add(p);
            }

        if (Vector2.DistanceSquared(loop[0], loop[^1]) <= 1e-6f) loop.RemoveAt(loop.Count - 1);
        return loop;
    }

    /// <summary>A flat face at height <paramref name="z"/>, facing up or down, with holes.</summary>
    internal static void Cap(Mesh mesh, List<Vector2> outline, List<List<Vector2>> holes, float z, bool up)
    {
        var (points, triangles) = Polygon2.Triangulate(outline, holes.Cast<IReadOnlyList<Vector2>>().ToList());

        for (int i = 0; i + 2 < triangles.Count; i += 3)
        {
            Vector2 a = points[triangles[i]], b = points[triangles[i + 1]], c = points[triangles[i + 2]];

            // The triangulation says it winds anticlockwise; asked rather than trusted, since
            // one face the wrong way round is a solid that is not closed.
            bool anticlockwise = (b.X - a.X) * (c.Y - a.Y) - (c.X - a.X) * (b.Y - a.Y) > 0;
            if (anticlockwise != up) (b, c) = (c, b);

            mesh.AddTriangle(new(a, z), new(b, z), new(c, z));
        }
    }

    /// <summary>
    /// The upright band round an anticlockwise loop from <paramref name="low"/> to
    /// <paramref name="high"/>, facing away from the inside of the loop or towards it.
    /// </summary>
    internal static void Side(Mesh mesh, List<Vector2> loop, float low, float high, bool outward)
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
