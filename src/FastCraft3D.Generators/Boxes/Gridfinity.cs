using System.Numerics;
using FastCraft3D.Geometry;

namespace FastCraft3D.Generators.Boxes;

/// <summary>
/// The dimensions of Gridfinity, the open modular storage system: a 42 mm grid, 7 mm height
/// units, and the stepped profile a bin's feet and a baseplate's pockets share. From the
/// published specification, pinned by a test so a slip here cannot go unnoticed.
/// </summary>
public static class GridfinitySpec
{
    public const float Pitch = 42f;
    public const float HeightUnit = 7f;

    /// <summary>A bin is this much narrower than its cells, so neighbours do not rub.</summary>
    public const float Gap = 0.5f;

    public const float OuterRadius = 3.75f;

    /// <summary>The foot, bottom up: (height, width across, corner radius). 0.8 at 45 degrees, 1.8 upright, 2.15 at 45 degrees.</summary>
    public static readonly (float Z, float Width, float Radius)[] Foot =
    [
        (0f, 35.6f, 0.8f),
        (0.8f, 37.2f, 1.6f),
        (2.6f, 37.2f, 1.6f),
        (4.75f, 41.5f, 3.75f)
    ];

    /// <summary>The baseplate's pocket, bottom up: 0.7 at 45 degrees, 1.8 upright, 2.15 at 45 degrees.</summary>
    public static readonly (float Z, float Width, float Radius)[] Pocket =
    [
        (0f, 35.8f, 0.8f),
        (0.7f, 37.2f, 1.6f),
        (2.5f, 37.2f, 1.6f),
        (4.65f, 41.5f, 3.75f)
    ];

    public const float FootHeight = 4.75f;
    public const float PlateHeight = 4.65f;
}

/// <summary>A Gridfinity bin: a foot in every cell it covers, walls, a floor, and dividers if wanted.</summary>
public sealed class GridfinityBin : Generator<GridfinityBin.Settings>
{
    public override string Id => "box.gridfinity-bin";
    public override int Version => 1;
    public override string Category => "Boxes";
    public override string Title => "Gridfinity bin";
    public override string Summary => "A bin for the Gridfinity storage grid: 42 mm cells, 7 mm height units. No stacking lip yet.";

    public sealed record Settings(
        [Count("Cells across", 1, 6, UnitText = "x 42 mm")] int UnitsX = 2,
        [Count("Cells deep", 1, 6, UnitText = "x 42 mm")] int UnitsY = 1,
        [Count("Height", 2, 12, UnitText = "x 7 mm", Hint = "In height units, counting the foot")] int Units = 6,
        [Wall("Wall", 0.8, 3)] float Wall = 1.2f,
        [Length("Floor", 0.6, 4, Hint = "Above the top of the feet")] float Floor = 1f,
        [Count("Dividers", 0, 8, Hint = "Walls across the bin, splitting it into compartments side by side")] int Dividers = 0);

    protected override IEnumerable<(string Name, Settings Settings)> Shipped =>
    [
        ("1 x 1", Default with { UnitsX = 1, UnitsY = 1 }),
        ("2 x 1", Default),
        ("3 x 2, three compartments", Default with { UnitsX = 3, UnitsY = 2, Dividers = 2 })
    ];

    protected override IEnumerable<string> Check(Settings s, Printer printer)
    {
        float width = s.UnitsX * GridfinitySpec.Pitch - GridfinitySpec.Gap - 2 * s.Wall;
        if ((width - s.Dividers * s.Wall) / (s.Dividers + 1) < 5)
            yield return "Too many dividers for the width.";
    }

    protected override Generated Build(Settings s, Printer printer, CancellationToken token)
    {
        float width = s.UnitsX * GridfinitySpec.Pitch - GridfinitySpec.Gap;
        float depth = s.UnitsY * GridfinitySpec.Pitch - GridfinitySpec.Gap;
        float height = s.Units * GridfinitySpec.HeightUnit;
        float floor = GridfinitySpec.FootHeight + s.Floor;

        var pieces = new List<Mesh>();
        for (int i = 0; i < s.UnitsX; i++)
            for (int j = 0; j < s.UnitsY; j++)
            {
                var centre = new Vector2((i - (s.UnitsX - 1) / 2f) * GridfinitySpec.Pitch, (j - (s.UnitsY - 1) / 2f) * GridfinitySpec.Pitch);
                pieces.Add(Shapes.RoundedLoft(GridfinitySpec.Foot.Select(f => (f.Z, f.Width, f.Width, f.Radius)).ToList(), centre));
            }

        pieces.Add(Shapes.Prism(Shapes.RoundedRect(width, depth, GridfinitySpec.OuterRadius), GridfinitySpec.FootHeight - 0.01f, height));

        token.ThrowIfCancellationRequested();
        var bin = Shapes.Union(pieces);

        var cavity = Shapes.Prism(
            Shapes.RoundedRect(width - 2 * s.Wall, depth - 2 * s.Wall, MathF.Max(GridfinitySpec.OuterRadius - s.Wall, 0.5f)),
            floor, height + 1);

        var dividers = new List<Mesh>();
        float inner = width - 2 * s.Wall;
        for (int d = 1; d <= s.Dividers; d++)
        {
            float x = -inner / 2f + d * inner / (s.Dividers + 1);
            dividers.Add(Shapes.Box(x - s.Wall / 2f, -depth / 2f, floor - 0.5f, x + s.Wall / 2f, depth / 2f, height));
        }

        var hollow = Shapes.Subtract(bin, cavity);
        var made = dividers.Count > 0 ? Shapes.Union([hollow, .. dividers.Select(d => Shapes.Intersect(d, bin))]) : hollow;

        return new Generated([new GeneratedPart("Gridfinity bin", made, Role: "bin")],
            [$"{s.UnitsX} x {s.UnitsY} cells, {s.Units} units ({height:0} mm) tall."]);
    }
}

/// <summary>A Gridfinity baseplate: a pocket for every cell, open underneath, to screw or stick down in a drawer.</summary>
public sealed class GridfinityBaseplate : Generator<GridfinityBaseplate.Settings>
{
    public override string Id => "box.gridfinity-baseplate";
    public override int Version => 1;
    public override string Category => "Boxes";
    public override string Title => "Gridfinity baseplate";
    public override string Summary => "The grid Gridfinity bins stand in, open underneath. Several print side by side to fill a drawer.";

    public sealed record Settings(
        [Count("Cells across", 1, 5, UnitText = "x 42 mm", Hint = "Five across is 210 mm, about the most a common plate takes")] int UnitsX = 4,
        [Count("Cells deep", 1, 5, UnitText = "x 42 mm")] int UnitsY = 4);

    protected override Generated Build(Settings s, Printer printer, CancellationToken token)
    {
        float width = s.UnitsX * GridfinitySpec.Pitch, depth = s.UnitsY * GridfinitySpec.Pitch;
        var plate = Shapes.Prism(Shapes.RoundedRect(width, depth, 4f), 0, GridfinitySpec.PlateHeight);

        // Each pocket run past the plate's top and bottom by the same profile, so it cuts clean through.
        var profile = GridfinitySpec.Pocket.ToList();
        profile.Insert(0, (-0.5f, profile[0].Width, profile[0].Radius));
        profile.Add((GridfinitySpec.PlateHeight + 0.5f, profile[^1].Width, profile[^1].Radius));

        var pockets = new List<Mesh>();
        for (int i = 0; i < s.UnitsX; i++)
            for (int j = 0; j < s.UnitsY; j++)
            {
                var centre = new Vector2((i - (s.UnitsX - 1) / 2f) * GridfinitySpec.Pitch, (j - (s.UnitsY - 1) / 2f) * GridfinitySpec.Pitch);
                pockets.Add(Shapes.RoundedLoft(profile.Select(p => (p.Z, p.Width, p.Width, p.Radius)).ToList(), centre));
            }

        token.ThrowIfCancellationRequested();
        return new Generated([new GeneratedPart("Gridfinity baseplate", Shapes.Subtract(plate, pockets), Role: "plate")],
            [$"{width:0} x {depth:0} mm."]);
    }
}
