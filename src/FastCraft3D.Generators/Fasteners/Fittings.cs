using System.Numerics;
using FastCraft3D.Geometry;

namespace FastCraft3D.Generators.Fasteners;

public enum WasherSize { M3, M4, M5, M6, M8, M10, M12, M16, M20 }

/// <summary>A plain washer, to ISO 7089 by default: choosing a size fills in its three numbers, any of which can then be changed.</summary>
public sealed class Washer : Generator<Washer.Settings>
{
    public override string Id => "fastener.washer";
    public override int Version => 1;
    public override string Category => "Fasteners";
    public override string Title => "Washer";
    public override string Summary => "A plain washer, ISO 7089 sizes to start from.";

    /// <summary>Inside, outside and thickness, from ISO 7089.</summary>
    public static (float Inside, float Outside, float Thickness) Iso(WasherSize size) => size switch
    {
        WasherSize.M3 => (3.2f, 7f, 0.5f),
        WasherSize.M4 => (4.3f, 9f, 0.8f),
        WasherSize.M5 => (5.3f, 10f, 1f),
        WasherSize.M6 => (6.4f, 12f, 1.6f),
        WasherSize.M8 => (8.4f, 16f, 1.6f),
        WasherSize.M10 => (10.5f, 20f, 2f),
        WasherSize.M12 => (13f, 24f, 2.5f),
        WasherSize.M16 => (17f, 30f, 3f),
        _ => (21f, 37f, 3f)
    };

    public sealed record Settings(
        [Choice("Size")] WasherSize Size = WasherSize.M8,
        [Length("Inside", 1, 100)] float Inside = 8.4f,
        [Length("Outside", 2, 150)] float Outside = 16f,
        [Length("Thickness", 0.2, 20, Hint = "A printed washer is best a couple of layers thicker than a steel one")] float Thickness = 1.6f);

    protected override Settings Adjust(Settings before, Settings after, string changed)
    {
        if (changed != nameof(Settings.Size)) return after;
        var (inside, outside, thickness) = Iso(after.Size);
        return after with { Inside = inside, Outside = outside, Thickness = thickness };
    }

    protected override IEnumerable<string> Check(Settings s, Printer printer)
    {
        if (s.Outside - s.Inside < 2 * 0.8f) yield return "The ring would be under 0.8 mm wide.";
    }

    protected override Generated Build(Settings s, Printer printer, CancellationToken token)
    {
        var notes = new List<string>();
        if (s.Thickness < 3 * printer.Layer) notes.Add($"Under three layers thick: printed, it will be weak. Consider {3 * printer.Layer:0.#} mm.");
        return new Generated([new GeneratedPart($"Washer {s.Inside:0.#} x {s.Outside:0.#}", Shapes.Tube(s.Outside / 2f, s.Inside / 2f, 0, s.Thickness), Role: "washer")], notes);
    }
}

public enum KnobSize { M3, M4, M5, M6, M8, M10 }

/// <summary>
/// A knob to turn a bolt by hand: lobed or round, with a hexagon pocket underneath for the bolt's
/// head or a nut, and the bolt's hole through.
/// </summary>
public sealed class Knob : Generator<Knob.Settings>
{
    public override string Id => "fastener.knob";
    public override int Version => 1;
    public override string Category => "Fasteners";
    public override string Title => "Knob";
    public override string Summary => "A hand knob with a pocket for a nut or a bolt's head.";

    public sealed record Settings(
        [Choice("Size")] KnobSize Size = KnobSize.M6,
        [Length("Diameter", 10, 100)] float Diameter = 32f,
        [Length("Height", 5, 40)] float Height = 12f,
        [Count("Lobes", 0, 12, Hint = "Nought for a round knob")] int Lobes = 6,
        [Clearance("Fit", 0.05, 1, Hint = "Round the nut, and round the bolt")] float Fit = 0.2f);

    protected override IEnumerable<(string Name, Settings Settings)> Shipped =>
    [
        ("M4 thumb knob", Default with { Size = KnobSize.M4, Diameter = 20, Height = 8, Lobes = 0 }),
        ("M8 star knob", Default with { Size = KnobSize.M8, Diameter = 45, Height = 16, Lobes = 5 })
    ];

    private static MetricSize Metric(KnobSize size) => Threads.Metric.First(m => m.Name == size.ToString());

    protected override IEnumerable<string> Check(Settings s, Printer printer)
    {
        var m = Metric(s.Size);
        if (s.Diameter < m.AcrossFlats * 1.2f + 4) yield return $"Too small round a {m.Name} nut: at least {m.AcrossFlats * 1.2f + 4:0} mm.";
        if (s.Height < m.NutHeight + 2) yield return $"Too thin for a {m.Name} nut's pocket: at least {m.NutHeight + 2:0.#} mm.";
    }

    protected override Generated Build(Settings s, Printer printer, CancellationToken token)
    {
        var m = Metric(s.Size);
        float radius = s.Diameter / 2f;

        var outline = new List<Vector2>();
        const int points = 240;
        for (int i = 0; i < points; i++)
        {
            float a = 2f * MathF.PI * i / points;
            float r = s.Lobes == 0 ? radius : radius * (1f - 0.16f * (0.5f - 0.5f * MathF.Cos(s.Lobes * a)));
            outline.Add(new Vector2(r * MathF.Cos(a), r * MathF.Sin(a)));
        }

        var knob = Shapes.Subtract(Shapes.Prism(outline, 0, s.Height),
            Shapes.Cylinder(m.Diameter / 2f + s.Fit, -1, s.Height + 1),
            Shapes.Prism(Shapes.Polygon(m.AcrossFlats + 2 * s.Fit, 6), -1, m.NutHeight + 0.3f));

        return new Generated([new GeneratedPart($"{m.Name} knob", knob, Role: "knob")],
            [$"The pocket takes a {m.Name} nut or bolt head from underneath; a drop of glue keeps it."]);
    }
}

/// <summary>A spacer or standoff: a tube, round or hexagonal outside.</summary>
public sealed class Spacer : Generator<Spacer.Settings>
{
    public override string Id => "fastener.spacer";
    public override int Version => 1;
    public override string Category => "Fasteners";
    public override string Title => "Spacer";
    public override string Summary => "A tube to space two parts apart on a screw.";

    public sealed record Settings(
        [Length("Inside", 1, 40, Hint = "The screw's clearance: 3.4 for M3")] float Inside = 3.4f,
        [Length("Outside", 3, 60, Hint = "Across the flats, if hexagonal")] float Outside = 7f,
        [Length("Length", 1, 150)] float Length = 10f,
        [Toggle("Hexagonal")] bool Hexagonal = false);

    protected override IEnumerable<string> Check(Settings s, Printer printer)
    {
        if (s.Outside - s.Inside < 2 * 0.8f) yield return "The wall would be under 0.8 mm.";
    }

    protected override Generated Build(Settings s, Printer printer, CancellationToken token)
    {
        var outline = s.Hexagonal ? Shapes.Polygon(s.Outside, 6) : Shapes.Circle(s.Outside / 2f);
        var spacer = Shapes.Prism(outline, [Shapes.Circle(s.Inside / 2f)], 0, s.Length);
        return new Generated([new GeneratedPart($"Spacer {s.Length:0.#} mm", spacer, Role: "spacer")], []);
    }
}
