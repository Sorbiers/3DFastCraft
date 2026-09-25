using System.Numerics;
using FastCraft3D.Geometry;

namespace FastCraft3D.Generators.Organisers;

public enum Cell
{
    AA,
    AAA,
    C,
    D,
    [ShownAs("18650")] Li18650,
    [ShownAs("21700")] Li21700,
    [ShownAs("CR2032 coin")] CR2032,
    [ShownAs("CR2025 coin")] CR2025
}

/// <summary>A block with a pocket for each cell, standing up, in rows. The cells' sizes are the IEC ones.</summary>
public sealed class BatteryHolder : Generator<BatteryHolder.Settings>
{
    public override string Id => "organiser.battery";
    public override int Version => 1;
    public override string Category => "Organisers";
    public override string Title => "Battery holder";
    public override string Summary => "A block with a pocket for every cell, in rows and columns.";

    /// <summary>Diameter and length, in millimetres. A coin cell's length is its thickness.</summary>
    public static (float Diameter, float Length) Size(Cell cell) => cell switch
    {
        Cell.AA => (14.5f, 50.5f),
        Cell.AAA => (10.5f, 44.5f),
        Cell.C => (26.2f, 50f),
        Cell.D => (34.2f, 61.5f),
        Cell.Li18650 => (18.6f, 65.2f),
        Cell.Li21700 => (21.7f, 70.2f),
        Cell.CR2032 => (20f, 3.2f),
        _ => (20f, 2.5f)
    };

    public sealed record Settings(
        [Choice("Cells")] Cell Cell = Cell.AA,
        [Count("Across", 1, 12)] int Columns = 4,
        [Count("Rows", 1, 12)] int Rows = 2,
        [Number("Pocket depth", 20, 100, UnitText = "% of the cell", Hint = "How much of each cell sits in the block")] float Depth = 50f,
        [Wall("Between", 0.8, 6, Hint = "The wall between pockets and round the outside")] float Wall = 1.6f,
        [Length("Floor", 0.6, 5)] float Floor = 1.2f,
        [Clearance("Fit", 0.05, 1, Hint = "The gap round each cell")] float Fit = 0.2f);

    protected override IEnumerable<(string Name, Settings Settings)> Shipped =>
    [
        ("Eight AA", Default with { Columns = 4, Rows = 2 }),
        ("Twelve AAA", Default with { Cell = Cell.AAA, Columns = 6, Rows = 2 }),
        ("Coin cells", Default with { Cell = Cell.CR2032, Columns = 5, Rows = 2, Depth = 100 })
    ];

    protected override IEnumerable<string> Check(Settings s, Printer printer)
    {
        var (width, depth) = Footprint(s);
        if (width > 250 || depth > 250) yield return $"{width:0} x {depth:0} mm is bigger than any plate. Fewer cells.";
    }

    private static (float Width, float Depth) Footprint(Settings s)
    {
        float pocket = Size(s.Cell).Diameter + 2 * s.Fit;
        return (s.Columns * pocket + (s.Columns + 1) * s.Wall, s.Rows * pocket + (s.Rows + 1) * s.Wall);
    }

    protected override Generated Build(Settings s, Printer printer, CancellationToken token)
    {
        var (diameter, length) = Size(s.Cell);
        float pocket = diameter + 2 * s.Fit;
        float sunk = MathF.Max(length * s.Depth / 100f, 1f);
        var (width, depth) = Footprint(s);

        var block = Shapes.Prism(Shapes.RoundedRect(width, depth, MathF.Min(3f, s.Wall + pocket / 4f)), 0, s.Floor + sunk);

        var pockets = new List<Mesh>();
        for (int c = 0; c < s.Columns; c++)
            for (int r = 0; r < s.Rows; r++)
            {
                var centre = new Vector2(-width / 2f + s.Wall + pocket / 2f + c * (pocket + s.Wall),
                                         -depth / 2f + s.Wall + pocket / 2f + r * (pocket + s.Wall));
                pockets.Add(Shapes.Cylinder(pocket / 2f, s.Floor, s.Floor + sunk + 1, centre));
            }

        token.ThrowIfCancellationRequested();
        return new Generated([new GeneratedPart("Battery holder", Shapes.Subtract(block, pockets), Role: "holder")],
            [$"{s.Columns * s.Rows} cells, {sunk:0.#} mm of each held."]);
    }
}
