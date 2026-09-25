using FastCraft3D.Geometry;

namespace FastCraft3D.Generators.Boxes;

/// <summary>An open tray divided into a grid of compartments, each divider reaching into the walls so it is one solid.</summary>
public sealed class DividedTray : Generator<DividedTray.Settings>
{
    public override string Id => "box.divided-tray";
    public override int Version => 1;
    public override string Category => "Boxes";
    public override string Title => "Divided tray";
    public override string Summary => "An open tray split into a grid of compartments, for screws, beads or anything sorted.";

    public sealed record Settings(
        [Length("Width", 20, 200, Group = "Size")] float Width = 120f,
        [Length("Depth", 20, 200, Group = "Size")] float Depth = 80f,
        [Length("Height", 5, 100, Group = "Size")] float Height = 25f,
        [Wall("Wall", 0.8, 6, Group = "Size")] float Wall = 1.6f,
        [Length("Floor", 0.6, 6, Group = "Size")] float Floor = 1.2f,
        [Length("Corner radius", 0, 30, Group = "Size")] float Radius = 3f,
        [Count("Across", 1, 12, UnitText = "compartments", Group = "Compartments")] int Columns = 4,
        [Count("Back to front", 1, 12, UnitText = "compartments", Group = "Compartments")] int Rows = 3,
        [Wall("Dividers", 0.8, 4, Group = "Compartments", Hint = "How thick the dividers are")] float Divider = 1.2f,
        [Length("Divider drop", 0, 50, Group = "Compartments", Hint = "How far below the rim the dividers stop, for fingers to reach across")] float Drop = 0f);

    protected override IEnumerable<(string Name, Settings Settings)> Shipped =>
    [
        ("Screw sorter", Default with { Width = 150, Depth = 100, Height = 25, Columns = 5, Rows = 4 }),
        ("Bead tray", Default with { Width = 100, Depth = 100, Height = 12, Columns = 6, Rows = 6, Radius = 2 })
    ];

    protected override IEnumerable<string> Check(Settings s, Printer printer)
    {
        float wide = (s.Width - 2 * s.Wall - (s.Columns - 1) * s.Divider) / s.Columns;
        float deep = (s.Depth - 2 * s.Wall - (s.Rows - 1) * s.Divider) / s.Rows;
        if (wide < 3 || deep < 3)
            yield return $"Compartments of {MathF.Min(wide, deep):0.#} mm are too small to use. Fewer of them, or a bigger tray.";

        if (s.Height - s.Floor < 2)
            yield return "The tray is no deeper than its floor.";

        if (s.Drop > s.Height - s.Floor - 1)
            yield return "The dividers would stop below the floor.";

        if (s.Radius > MathF.Min(s.Width, s.Depth) / 2f)
            yield return "The corner radius is more than half the tray across.";
    }

    protected override Generated Build(Settings s, Printer printer, CancellationToken token)
    {
        var pieces = new List<Mesh> { OpenBox.Tray(s.Width, s.Depth, s.Height, s.Wall, s.Floor, s.Radius) };

        float innerWidth = s.Width - 2 * s.Wall, innerDepth = s.Depth - 2 * s.Wall;
        float top = s.Height - s.Drop;
        float half = s.Divider / 2f;

        // Each reaching halfway into the walls and a little into the floor, so they join rather than touch.
        for (int c = 1; c < s.Columns; c++)
        {
            float x = -innerWidth / 2f + c * innerWidth / s.Columns;
            pieces.Add(Shapes.Box(x - half, -s.Depth / 2f + s.Wall / 2f, s.Floor / 2f, x + half, s.Depth / 2f - s.Wall / 2f, top));
        }

        for (int r = 1; r < s.Rows; r++)
        {
            float y = -innerDepth / 2f + r * innerDepth / s.Rows;
            pieces.Add(Shapes.Box(-s.Width / 2f + s.Wall / 2f, y - half, s.Floor / 2f, s.Width / 2f - s.Wall / 2f, y + half, top));
        }

        token.ThrowIfCancellationRequested();
        return new Generated([new GeneratedPart("Tray", Shapes.Union(pieces), Role: "tray")], []);
    }
}
