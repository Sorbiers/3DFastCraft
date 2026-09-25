using System.Numerics;
using FastCraft3D.Geometry;

namespace FastCraft3D.Generators.Boxes;

public enum LidKind
{
    /// <summary>A plate with a lip that drops inside the rim.</summary>
    [ShownAs("Lift off")] LiftOff,

    /// <summary>A plate that slides in along grooves in the walls, in through the front.</summary>
    Sliding
}

/// <summary>
/// A box and a lid for it: a lift-off lid with a lip that drops inside the rim, or a lid that
/// slides in along grooves in the side and back walls. The fit between them is the printer's
/// clearance, all round.
/// </summary>
public sealed class LiddedBox : Generator<LiddedBox.Settings>
{
    public override string Id => "box.lidded";
    public override int Version => 1;
    public override string Category => "Boxes";
    public override string Title => "Box with a lid";
    public override string Summary => "A box and its lid, lift-off or sliding, printed side by side and fitted by the printer's clearance.";

    public sealed record Settings(
        [Length("Width", 20, 200, Group = "Size", Hint = "Outside, left to right")] float Width = 80f,
        [Length("Depth", 20, 200, Group = "Size", Hint = "Outside, front to back. A sliding lid goes in from the front.")] float Depth = 60f,
        [Length("Height", 10, 200, Group = "Size", Hint = "The box alone, without its lid")] float Height = 40f,
        [Wall("Wall", 0.8, 6, Group = "Walls")] float Wall = 1.6f,
        [Length("Floor", 0.6, 6, Group = "Walls")] float Floor = 1.2f,
        [Length("Corner radius", 0, 30, Group = "Walls", Hint = "On the outside. A sliding lid wants corners no rounder than the wall.")] float Radius = 3f,
        [Choice("Lid", Group = "Lid")] LidKind Lid = LidKind.LiftOff,
        [Length("Lid thickness", 1, 6, Group = "Lid")] float LidThickness = 2f,
        [Length("Lip", 1, 20, Group = "Lid", Hint = "How far the lid's lip reaches down inside the box"), ShowWhen(nameof(Lid), LidKind.LiftOff)] float Lip = 4f,
        [Clearance("Fit", 0.05, 1, Group = "Lid", Hint = "The gap all round between the lid and the box")] float Fit = 0.2f);

    protected override IEnumerable<(string Name, Settings Settings)> Shipped =>
    [
        ("Trinket box", Default with { Width = 60, Depth = 60, Height = 30, Radius = 5 }),
        ("Pencil case", Default with { Width = 200, Depth = 50, Height = 30, Lid = LidKind.Sliding, Radius = 1 }),
        ("Card box", Default with { Width = 72, Depth = 98, Height = 35, Radius = 2 })
    ];

    protected override IEnumerable<string> Check(Settings s, Printer printer)
    {
        if (s.Width - 4 * s.Wall - 2 * s.Fit < 4 || s.Depth - 4 * s.Wall - 2 * s.Fit < 4)
            yield return "The walls and the lid's lip leave no room inside.";

        if (s.Radius > MathF.Min(s.Width, s.Depth) / 2f)
            yield return "The corner radius is more than half the box across.";

        if (s.Lid == LidKind.LiftOff && s.Height - s.Floor < s.Lip + 1)
            yield return $"A lip of {s.Lip:0.#} mm reaches the floor of a box {s.Height:0.#} mm high.";

        if (s.Lid == LidKind.Sliding)
        {
            if (s.Height - s.Floor < s.Wall + s.LidThickness + 2 * s.Fit + 2)
                yield return "The box is too shallow for a sliding lid of that thickness.";
        }
    }

    /// <summary>
    /// A sliding lid's grooves run into the corners, and would break through ones rounder than
    /// the wall. Refusing that was tried first, and choosing Sliding from the defaults, with their
    /// 3 mm corners, then made nothing at all; holding the corners in, and saying so, does better.
    /// </summary>
    private static float Corners(Settings s) => s.Lid == LidKind.Sliding ? MathF.Min(s.Radius, s.Wall) : s.Radius;

    protected override Generated Build(Settings s, Printer printer, CancellationToken token)
    {
        var box = OpenBox.Tray(s.Width, s.Depth, s.Height, s.Wall, s.Floor, Corners(s));
        Mesh lid;
        Matrix4x4 together;

        if (s.Lid == LidKind.LiftOff)
        {
            lid = LiftOffLid(s.Width, s.Depth, s.Radius, s.Wall, s.Fit, s.LidThickness, s.Lip);

            // Printed plate down, lip up; put on, turned over onto the rim.
            together = Matrix4x4.CreateRotationX(MathF.PI) * Matrix4x4.CreateTranslation(0, 0, s.Height + s.LidThickness);
        }
        else
        {
            float groove = s.Wall / 2f;
            float grooveHeight = s.LidThickness + 2 * s.Fit;
            float grooveTop = s.Height - s.Wall;
            float grooveBottom = grooveTop - grooveHeight;
            float inside = s.Width / 2f - s.Wall + groove;

            // The grooves, and the front wall above them, taken out so the lid slides in.
            box = Shapes.Subtract(box,
                Shapes.Box(-inside, -s.Depth / 2f - 1, grooveBottom, inside, s.Depth / 2f - s.Wall + groove, grooveTop),
                Shapes.Box(-inside, -s.Depth / 2f - 1, grooveBottom, inside, -s.Depth / 2f + s.Wall + 0.01f, s.Height + 1));

            float lidHalf = inside - s.Fit;
            float back = s.Depth / 2f - s.Wall + groove - s.Fit;
            lid = Shapes.Box(-lidHalf, -s.Depth / 2f, 0, lidHalf, back, s.LidThickness);
            together = Matrix4x4.CreateTranslation(0, 0, grooveBottom + s.Fit);
        }

        var parts = Shapes.InARow([("Box", "box", box, Matrix4x4.Identity), ("Lid", "lid", lid, together)]);

        var notes = new List<string>();
        if (s.Lid == LidKind.Sliding) notes.Add("The lid slides in from the front, along grooves in the side and back walls.");
        if (Corners(s) < s.Radius) notes.Add($"Corners held to {Corners(s):0.##} mm, the wall's thickness, so the grooves do not break through them.");
        return new Generated(parts, notes);
    }

    /// <summary>
    /// A lid for an open box of this outside: a plate the size of the box, and a lip standing on
    /// it that drops inside the rim with <paramref name="fit"/> all round. Plate down, lip up.
    /// </summary>
    public static Mesh LiftOffLid(float width, float depth, float radius, float wall, float fit, float thickness, float lip)
    {
        float inset = wall + fit;
        var plate = Shapes.Prism(Shapes.RoundedRect(width, depth, radius), 0, thickness);
        var ring = Shapes.Prism(
            Shapes.RoundedRect(width - 2 * inset, depth - 2 * inset, MathF.Max(radius - inset, 0)),
            [Shapes.RoundedRect(width - 2 * (inset + wall), depth - 2 * (inset + wall), MathF.Max(radius - inset - wall, 0))],
            thickness - 0.01f, thickness + lip);

        return Shapes.Union(plate, ring);
    }
}
