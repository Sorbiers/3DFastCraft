using System.Numerics;
using FastCraft3D.Geometry;

namespace FastCraft3D.Generators.Calibration;

/// <summary>
/// What the test towers share: bands, what each band is to be printed at, and the heights to tell
/// the slicer. The towers are only shapes - the slicer does the changing - so the heights are said
/// exactly, from the plate, as the slicer counts them.
/// </summary>
internal static class Bands
{
    /// <summary>"Z 1–6 mm: 0.5 mm" for every band, bottom first.</summary>
    public static IEnumerable<string> Table(float floor, float height, int bands, Func<int, string> value) =>
        Enumerable.Range(0, bands).Select(k => $"Z {floor + k * height:0.##}–{floor + (k + 1) * height:0.##} mm: {value(k)}");

    /// <summary>A rounded plate for the towers to stand on.</summary>
    public static Mesh Base(float wide, float deep, float thick, Vector2 centre = default) =>
        Shapes.Prism(Shapes.RoundedRect(wide, deep, MathF.Min(2f, MathF.Min(wide, deep) / 4f), centre), 0, thick);
}

/// <summary>
/// Two towers a travel apart, in bands, for the slicer to print with a longer retraction in each
/// band than the one below. Stringing between the towers shows which bands retract too little;
/// blobs and gaps where a layer starts show which retract too much. Each band is numbered with its
/// retraction and a groove runs round the towers between one band and the next.
/// </summary>
public sealed class RetractionTest : Generator<RetractionTest.Settings>
{
    public override string Id => "calibration.retraction";
    public override int Version => 1;
    public override string Category => "Calibration";
    public override string Title => "Retraction test";
    public override string Summary => "Two towers in numbered bands, for the slicer to retract more in each: the least that leaves no strings wins.";

    public sealed record Settings(
        [Number("First retraction", 0, 10, UnitText = "mm", Hint = "For the bottom band")] float From = 0.4f,
        [Number("Step", 0.05, 2, UnitText = "mm", Hint = "More in each band than the one below")] float Step = 0.4f,
        [Count("Bands", 2, 12)] int Bands = 8,
        [Length("Band height", 3, 20)] float BandHeight = 5f,
        [Length("Towers apart", 20, 150, Hint = "Middle to middle. The longer the travel between them, the more a string shows.")] float Apart = 50f,
        [Length("Tower size", 6, 30)] float Tower = 10f,
        [Length("Base", 0.4, 5, Hint = "The plate the towers stand on")] float Base = 1f);

    protected override IEnumerable<(string Name, Settings Settings)> Shipped =>
    [
        ("Direct drive", Default),
        ("Bowden", Default with { From = 1f, Step = 1f }),
        ("Fine, direct drive", Default with { From = 0.2f, Step = 0.2f, Bands = 10, BandHeight = 4f })
    ];

    private static float At(Settings s, int band) => MathF.Round(s.From + band * s.Step, 3);

    /// <summary>How deep the grooves and the numbers go into a tower's faces.</summary>
    private const float Cut = 0.4f;

    protected override IEnumerable<string> Check(Settings s, Printer printer)
    {
        if (s.Apart < s.Tower + 5) yield return "The towers are closer than the travel between them needs: move them apart.";
    }

    protected override Generated Build(Settings s, Printer printer, CancellationToken token)
    {
        float t = s.Tower, top = s.Base + s.Bands * s.BandHeight;
        var solid = new List<Mesh> { Bands.Base(s.Apart + t + 6f, t + 6f, s.Base) };
        var cuts = new List<Mesh>();

        foreach (float x in new[] { -s.Apart / 2f, s.Apart / 2f })
        {
            solid.Add(Shapes.Box(x - t / 2f, -t / 2f, s.Base - 0.01f, x + t / 2f, t / 2f, top));

            // A groove round the tower where one band gives way to the next, to count them by.
            for (int k = 1; k < s.Bands; k++)
            {
                float z = s.Base + k * s.BandHeight;
                cuts.Add(Shapes.Subtract(
                    Shapes.Box(x - t / 2f - 1, -t / 2f - 1, z - 0.3f, x + t / 2f + 1, t / 2f + 1, z + 0.3f),
                    Shapes.Box(x - t / 2f + Cut, -t / 2f + Cut, z - 1, x + t / 2f - Cut, t / 2f - Cut, z + 1)));
            }
        }

        // Each band's retraction on the front of the left tower.
        for (int k = 0; k < s.Bands; k++)
        {
            string label = PixelText.Number(At(s, k));
            float pixel = PixelText.Fit(label, t - 1.5f, s.BandHeight - 1.6f, 0.6f);
            if (pixel < 0.3f) continue;
            cuts.AddRange(PixelText.OnFront(label, pixel, new Vector3(-s.Apart / 2f, -t / 2f, s.Base + (k + 0.5f) * s.BandHeight), Cut));
        }

        token.ThrowIfCancellationRequested();
        var mesh = Shapes.Subtract(Shapes.Union(solid), cuts);

        var notes = new List<string> { "Have the slicer set the retraction at these heights:" };
        notes.AddRange(Bands.Table(s.Base, s.BandHeight, s.Bands, k => $"{PixelText.Number(At(s, k))} mm"));
        notes.Add("In Cura: Extensions > Post Processing > ChangeAtZ, one for each band. OrcaSlicer and Bambu Studio have their own retraction test that does it for you.");
        notes.Add("Use the lowest band with no strings between the towers.");

        return new Generated([new GeneratedPart("Retraction test", mesh, Role: "towers")], notes);
    }
}

/// <summary>
/// A tower in bands, for the slicer to print each band at a temperature of its own: every band
/// has walls, a bridge between its two pillars, an overhang standing out from one side, and flat
/// tops, so the band that does all four best is the temperature to use. With more than one tower,
/// each is its own object on the plate for the slicer to print at a speed of its own - the same
/// temperatures at each speed, side by side.
/// </summary>
public sealed class TemperatureTower : Generator<TemperatureTower.Settings>
{
    public override string Id => "calibration.temperature";
    public override int Version => 1;
    public override string Category => "Calibration";
    public override string Title => "Temperature and speed tower";
    public override string Summary => "Bands of walls, bridge, overhang and flat top, one temperature to a band, and a tower for each speed.";

    public sealed record Settings(
        [Number("First temperature", 150, 320, UnitText = "°C", Hint = "For the bottom band")] float From = 230f,
        [Number("Step", -20, 20, UnitText = "°C", Hint = "Band to band going up; negative gets cooler")] float Step = -5f,
        [Count("Bands", 2, 12)] int Bands = 6,
        [Length("Band height", 6, 20)] float BandHeight = 8f,
        [Length("Bridge", 5, 60, Hint = "How far each band's bridge spans")] float Bridge = 20f,
        [Angle("Overhang", 20, 70, Hint = "From upright. Forty-five is where most printers start to struggle.")] float Overhang = 45f,
        [Count("Speeds", 1, 4, Group = "Speed", Hint = "Towers side by side, each for the slicer to print at its own speed")] int Speeds = 1,
        [Number("First speed", 10, 500, UnitText = "mm/s", Group = "Speed")] float SpeedFrom = 60f,
        [Number("Speed step", -200, 200, UnitText = "mm/s", Group = "Speed", Hint = "Tower to tower")] float SpeedStep = 40f,
        [Length("Base", 0.6, 5, Hint = "The plate each tower stands on")] float Base = 1.2f);

    protected override IEnumerable<(string Name, Settings Settings)> Shipped =>
    [
        ("PLA", Default),
        ("PETG", Default with { From = 250f }),
        ("ABS and ASA", Default with { From = 260f }),
        ("PLA at three speeds", Default with { Speeds = 3, SpeedFrom = 40f, SpeedStep = 60f })
    ];

    protected override bool Shows(Settings s, string parameter) =>
        parameter is not (nameof(Settings.SpeedFrom) or nameof(Settings.SpeedStep)) || s.Speeds > 1;

    private const float Pillar = 8f, Deep = 10f, Cut = 0.4f, Front = 7f;

    private static float Temperature(Settings s, int band) => s.From + band * s.Step;

    private static float Speed(Settings s, int tower) => s.SpeedFrom + tower * s.SpeedStep;

    protected override IEnumerable<string> Check(Settings s, Printer printer)
    {
        if (s.Speeds > 1 && Enumerable.Range(0, s.Speeds).Any(i => Speed(s, i) < 5))
            yield return "The speeds run down below 5 mm/s. A smaller step, or start faster.";
        if (Enumerable.Range(0, s.Bands).Any(k => Temperature(s, k) is < 150 or > 320))
            yield return "The temperatures run outside 150 to 320 °C. Fewer bands, or a smaller step.";
    }

    protected override Generated Build(Settings s, Printer printer, CancellationToken token)
    {
        var towers = new List<(string, string, Mesh, Matrix4x4?)>();
        for (int i = 0; i < s.Speeds; i++)
        {
            token.ThrowIfCancellationRequested();
            string name = s.Speeds > 1 ? $"Tower {PixelText.Number(Speed(s, i))} mm/s" : "Temperature tower";
            towers.Add((name, $"tower {i + 1}", Tower(s, printer, s.Speeds > 1 ? PixelText.Number(Speed(s, i)) : null), null));
        }

        var notes = new List<string> { "Have the slicer set the nozzle temperature at these heights:" };
        notes.AddRange(Bands.Table(s.Base, s.BandHeight, s.Bands, k => $"{PixelText.Number(Temperature(s, k))} °C"));
        notes.Add("In Cura: Extensions > Post Processing > ChangeAtZ, one for each band. In PrusaSlicer: a custom G-code at each height, M104 S and the temperature.");
        if (s.Speeds > 1)
            notes.Add($"Each tower is an object of its own, with its speed on the base in front: give each its print speed as a per-object setting ({string.Join(", ", Enumerable.Range(0, s.Speeds).Select(i => PixelText.Number(Speed(s, i))))} mm/s).");
        notes.Add("Look for the band with clean walls, a bridge that does not sag, an overhang that does not curl and a smooth flat top.");

        return new Generated(Shapes.InARow(towers, gap: 6f), notes);
    }

    /// <summary>One tower, its bands stacked on a base with room in front for the speed.</summary>
    private static Mesh Tower(Settings s, Printer printer, string? speed)
    {
        float wide = s.Bridge + 2 * Pillar, left = -wide / 2f, right = wide / 2f;
        float top = s.Base + s.Bands * s.BandHeight;
        float bridge = MathF.Max(1f, 4 * printer.Layer);

        // Out as far as the band leaves room for, at the angle asked; four millimetres at most.
        float slope = MathF.Tan(s.Overhang * MathF.PI / 180f);
        float reach = MathF.Min(4f, (s.BandHeight - 1f) * slope);
        float drop = reach / slope;

        // The tower drawn side on and run back through its depth, rather than pillars, bridges and
        // overhangs as boxes laid against each other: built that way, their faces met in the same
        // planes and a few of the sweep's towers came back with edges of four faces. The outline
        // runs up the right pillar, across the top and down the left side past each overhang.
        var side = new List<Vector2> { new(right, s.Base - 0.01f), new(right, top) };
        for (int k = s.Bands - 1; k >= 0; k--)
        {
            float z0 = s.Base + k * s.BandHeight, z1 = z0 + s.BandHeight;
            side.Add(new(left - reach, z1));
            side.Add(new(left, z1 - drop));
            side.Add(new(left, k == 0 ? s.Base - 0.01f : z0));
        }

        var tower = MeshTransform.Transformed(Shapes.Prism(side, 0, Deep), new Matrix4x4(1, 0, 0, 0, 0, 0, 1, 0, 0, -1, 0, 0, 0, Deep, 0, 1));

        // Under each bridge, the gap between the pillars; the first through the foot of the tower,
        // before it is stood on its base, so the base's top is left alone.
        var gaps = Enumerable.Range(0, s.Bands).Select(k =>
            Shapes.Box(left + Pillar, -1, k == 0 ? s.Base - 1 : s.Base + k * s.BandHeight,
                       right - Pillar, Deep + 1, s.Base + (k + 1) * s.BandHeight - bridge)).ToList();
        tower = Shapes.Subtract(tower, gaps);

        var solid = new List<Mesh> { Bands.Base(wide + reach + 6f, Deep + Front + 3f, s.Base, new Vector2(-reach / 2f, (Deep - Front) / 2f)), tower };
        var cuts = new List<Mesh>();
        for (int k = 0; k < s.Bands; k++)
        {
            float z0 = s.Base + k * s.BandHeight;
            string label = PixelText.Number(Temperature(s, k));
            float pixel = PixelText.Fit(label, Pillar - 1.5f, s.BandHeight - bridge - 1.5f, 0.6f);
            if (pixel >= 0.3f)
                cuts.AddRange(PixelText.OnFront(label, pixel, new Vector3(right - Pillar / 2f, 0, z0 + (s.BandHeight - bridge) / 2f), Cut));
        }

        if (speed is not null)
        {
            float pixel = PixelText.Fit(speed, wide - 2f, Front - 2f, 0.8f);
            float depth = MathF.Min(Cut, s.Base / 2f);
            cuts.AddRange(PixelText.Boxes(speed, pixel, s.Base - depth, s.Base + 1)
                .Select(b => Shapes.Moved(b, 0, -Front / 2f, 0)));
        }

        return Shapes.Subtract(Shapes.Union(solid), cuts);
    }
}
