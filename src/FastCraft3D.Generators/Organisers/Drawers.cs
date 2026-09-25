using System.Numerics;
using FastCraft3D.Generators.Boxes;
using FastCraft3D.Geometry;

namespace FastCraft3D.Generators.Organisers;

/// <summary>A cabinet with a stack of slots, and a drawer for each that slides into it with the printer's clearance.</summary>
public sealed class Drawers : Generator<Drawers.Settings>
{
    public override string Id => "organiser.drawers";
    public override int Version => 1;
    public override string Category => "Organisers";
    public override string Title => "Drawers and cabinet";
    public override string Summary => "A cabinet and the drawers that slide into it, each with a pull.";

    public sealed record Settings(
        [Length("Width", 30, 200, Group = "Cabinet")] float Width = 100f,
        [Length("Depth", 30, 200, Group = "Cabinet")] float Depth = 100f,
        [Length("Height", 20, 200, Group = "Cabinet")] float Height = 90f,
        [Count("Drawers", 1, 8, Group = "Cabinet")] int Count = 3,
        [Wall("Wall", 0.8, 5, Group = "Cabinet")] float Wall = 1.6f,
        [Wall("Drawer wall", 0.8, 4, Group = "Drawers")] float DrawerWall = 1.2f,
        [Clearance("Fit", 0.1, 1, Group = "Drawers", Hint = "The gap round each drawer in its slot")] float Fit = 0.3f);

    protected override IEnumerable<(string Name, Settings Settings)> Shipped =>
    [
        ("Three small drawers", Default),
        ("Tall five", Default with { Width = 80, Depth = 120, Height = 160, Count = 5 })
    ];

    private static float SlotHeight(Settings s) => (s.Height - s.Wall * (s.Count + 1)) / s.Count;

    protected override IEnumerable<string> Check(Settings s, Printer printer)
    {
        if (SlotHeight(s) < 10) yield return $"Each drawer would be {SlotHeight(s):0.#} mm tall. Fewer drawers, or a taller cabinet.";
        if (s.Width - 2 * s.Wall - 2 * s.Fit - 4 * s.DrawerWall < 8) yield return "The drawers would have no room inside.";
    }

    protected override Generated Build(Settings s, Printer printer, CancellationToken token)
    {
        float slot = SlotHeight(s);
        float slotWidth = s.Width - 2 * s.Wall;
        var cabinet = Shapes.Box(-s.Width / 2f, -s.Depth / 2f, 0, s.Width / 2f, s.Depth / 2f, s.Height);

        var slots = Enumerable.Range(0, s.Count)
            .Select(i => Shapes.Box(-slotWidth / 2f, -s.Depth / 2f - 1, Floor(i), slotWidth / 2f, s.Depth / 2f - s.Wall, Floor(i) + slot))
            .ToList();

        token.ThrowIfCancellationRequested();
        var parts = new List<(string, string, Mesh, Matrix4x4?)> { ("Cabinet", "cabinet", Shapes.Subtract(cabinet, slots), Matrix4x4.Identity) };

        float width = slotWidth - 2 * s.Fit;
        float height = slot - s.Fit;
        float depth = s.Depth - s.Wall - s.Fit;
        for (int i = 0; i < s.Count; i++)
        {
            var tray = OpenBox.Tray(width, depth, height, s.DrawerWall, s.DrawerWall, 1f);
            float pull = MathF.Min(8f, width / 4f);
            var handle = Shapes.Box(-pull, -depth / 2f - 6, height / 2f - 2, pull, -depth / 2f + 0.5f, height / 2f + 2);
            var drawer = Shapes.Union(tray, handle);

            // In its slot: resting on the shelf, its front flush with the cabinet's.
            var together = Matrix4x4.CreateTranslation(0, -s.Depth / 2f + depth / 2f, Floor(i));
            parts.Add(($"Drawer {i + 1}", $"drawer {i + 1}", drawer, together));
        }

        return new Generated(Shapes.InARow(parts), [$"{s.Count} drawers, {width:0} x {depth:0} x {height:0} mm each outside."]);

        float Floor(int i) => s.Wall + i * (slot + s.Wall);
    }
}
