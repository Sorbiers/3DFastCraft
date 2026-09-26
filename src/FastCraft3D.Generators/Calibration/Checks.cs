using System.Numerics;
using FastCraft3D.Geometry;

namespace FastCraft3D.Generators.Calibration;

/// <summary>
/// Pins printed in place in a row of holes, each with a wider gap round it than the last, the gap
/// cut beside each hole. The first pin that pushes out by hand is the clearance this printer needs
/// between two surfaces that must move apart - the number every hinge, lid, bearing and gear in
/// the Library starts its fits from.
/// </summary>
public sealed class ClearanceTest : Generator<ClearanceTest.Settings>
{
    public override string Id => "calibration.clearance";
    public override int Version => 1;
    public override string Category => "Calibration";
    public override string Title => "Clearance test";
    public override string Summary => "Pins printed in place in holes, a wider gap round each: the first that pushes free is your printer's clearance.";

    public sealed record Settings(
        [Number("First gap", 0.05, 1, UnitText = "mm", Hint = "Round the first pin, each side")] float From = 0.1f,
        [Number("Step", 0.02, 0.5, UnitText = "mm")] float Step = 0.1f,
        [Count("Pins", 2, 8)] int Pins = 5,
        [Length("Pin diameter", 3, 20)] float Diameter = 6f,
        [Length("Plate", 2, 20, Hint = "How thick the plate is, and so how long the gap round each pin")] float Thickness = 5f);

    protected override IEnumerable<(string Name, Settings Settings)> Shipped =>
    [
        ("Coarse, 0.1 to 0.5", Default),
        ("Fine, 0.05 to 0.3", Default with { From = 0.05f, Step = 0.05f, Pins = 6 })
    ];

    private static float Gap(Settings s, int i) => MathF.Round(s.From + i * s.Step, 3);

    protected override IEnumerable<string> Check(Settings s, Printer printer)
    {
        if (Gap(s, s.Pins - 1) > s.Diameter / 4f) yield return "The widest gap leaves the last pin too thin. Smaller steps, or bigger pins.";
    }

    protected override Generated Build(Settings s, Printer printer, CancellationToken token)
    {
        float d = s.Diameter, r = d / 2f, pitch = d + 5f, label = 4f;
        float wide = s.Pins * pitch + 2f, deep = d + 5f + label;
        float first = -(s.Pins - 1) * pitch / 2f;

        var holes = new List<Mesh>();
        var pins = new List<Mesh>();
        var cuts = new List<Mesh>();
        int sides = Shapes.Sides(r);

        for (int i = 0; i < s.Pins; i++)
        {
            token.ThrowIfCancellationRequested();
            var at = new Vector2(first + i * pitch, 0);

            // The same number of sides for hole and pin, so the gap is the gap at every corner.
            holes.Add(Shapes.Cylinder(r, -1, s.Thickness + 1, at, sides));
            pins.Add(Shapes.Cylinder(r - Gap(s, i), 0, s.Thickness + 2f, at, sides));

            string text = PixelText.Number(Gap(s, i));
            float pixel = PixelText.Fit(text, pitch - 1.5f, label - 1f, 0.6f);
            cuts.AddRange(PixelText.Boxes(text, pixel, s.Thickness - 0.4f, s.Thickness + 1)
                .Select(b => Shapes.Moved(b, at.X, -r - 2.5f - label / 2f + 0.5f, 0)));
        }

        var plate = Shapes.Box(-wide / 2f, -r - 2.5f - label, 0, wide / 2f, r + 2.5f, s.Thickness);
        plate = Shapes.Subtract(plate, [.. holes, .. cuts]);

        return new Generated(
        [
            new GeneratedPart("Clearance plate", plate, Role: "plate"),
            new GeneratedPart("Clearance pins", Mesh.Combine(pins), Role: "pins")
        ],
        [
            "Print it as it stands, the pins in their holes. Then push each pin out from below.",
            $"The first that comes free by hand is your clearance: set it as the Printer's clearance in any Library panel (now {printer.XyClearance:0.##} mm).",
            "Pins that turn but will not push out are a gap on the edge: try that one again with a looser step."
        ]);
    }
}

/// <summary>
/// Bridges of growing span side by side on one base, each with its span cut in front of it. The
/// longest that prints without sagging is how far the slicer can be trusted to bridge; past it,
/// a part wants support or a different way up.
/// </summary>
public sealed class BridgeTest : Generator<BridgeTest.Settings>
{
    public override string Id => "calibration.bridges";
    public override int Version => 1;
    public override string Category => "Calibration";
    public override string Title => "Bridge ladder";
    public override string Summary => "Bridges of growing span side by side: the longest that does not sag is how far you can bridge.";

    public sealed record Settings(
        [Length("First span", 5, 60)] float From = 10f,
        [Length("Step", 2, 30)] float Step = 10f,
        [Count("Bridges", 2, 8)] int Bridges = 6,
        [Length("Clear height", 2, 30, Hint = "Under each bridge")] float Height = 6f,
        [Length("Bridge width", 3, 20)] float Width = 6f,
        [Length("Bridge thickness", 0.4, 4)] float Thickness = 1f,
        [Length("Base", 0.6, 5)] float Base = 1f);

    private const float Pillar = 4f, Row = 4f, Label = 8f;

    private static float Span(Settings s, int i) => s.From + i * s.Step;

    protected override IEnumerable<string> Check(Settings s, Printer printer)
    {
        if (Span(s, s.Bridges - 1) > 150) yield return "The longest bridge would be over 150 mm. Fewer bridges, or a smaller step.";
    }

    protected override Generated Build(Settings s, Printer printer, CancellationToken token)
    {
        float longest = Span(s, s.Bridges - 1);
        float wide = longest + 2 * Pillar + Label + 2f, deep = s.Bridges * (s.Width + Row) + Row;
        float x0 = -wide / 2f + Label + 1f, y0 = -deep / 2f + Row;
        float top = s.Base + s.Height + s.Thickness;

        var solid = new List<Mesh> { Bands.Base(wide, deep, s.Base) };
        var cuts = new List<Mesh>();

        for (int i = 0; i < s.Bridges; i++)
        {
            token.ThrowIfCancellationRequested();
            float span = Span(s, i), y = y0 + i * (s.Width + Row);

            // Each bridge starts at the same left pillar, so the spans read as a ladder. Pillars and
            // bridge drawn side on as one outline and run across: as three boxes against each other,
            // their faces met in one plane and some ladders came back with edges of four faces.
            float bottom = s.Base - 0.01f, under = top - s.Thickness, far = x0 + Pillar + span;
            var arch = Shapes.Prism(
            [
                new(x0, bottom), new(x0 + Pillar, bottom), new(x0 + Pillar, under), new(far, under),
                new(far, bottom), new(far + Pillar, bottom), new(far + Pillar, top), new(x0, top)
            ], 0, s.Width);
            solid.Add(MeshTransform.Transformed(arch, new Matrix4x4(1, 0, 0, 0, 0, 0, 1, 0, 0, -1, 0, 0, 0, y + s.Width, 0, 1)));

            string text = PixelText.Number(span);
            float pixel = PixelText.Fit(text, Label - 1f, s.Width, 0.6f);
            float depth = MathF.Min(0.4f, s.Base / 2f);
            cuts.AddRange(PixelText.Boxes(text, pixel, s.Base - depth, s.Base + 1)
                .Select(b => Shapes.Moved(b, x0 - Label / 2f - 0.5f, y + s.Width / 2f, 0)));
        }

        var mesh = Shapes.Subtract(Shapes.Union(solid), cuts);
        return new Generated([new GeneratedPart("Bridge ladder", mesh, Role: "ladder")],
        [
            $"Spans {string.Join(", ", Enumerable.Range(0, s.Bridges).Select(i => PixelText.Number(Span(s, i))))} mm, each cut on the base beside it.",
            "The longest bridge whose underside is flat and whose lines did not droop or break is as far as this printer bridges.",
            "Bridges go better with the fan full on and a slower bridge speed: most slicers set both apart for bridges."
        ]);
    }
}

/// <summary>
/// The twenty-millimetre cube, X, Y and Z cut into the faces they are measured across, and the
/// arithmetic for putting a wrong one right said in the panel.
/// </summary>
public sealed class CalibrationCube : Generator<CalibrationCube.Settings>
{
    public override string Id => "calibration.cube";
    public override int Version => 1;
    public override string Category => "Calibration";
    public override string Title => "Calibration cube";
    public override string Summary => "The 20 mm cube with X, Y and Z on its faces, to measure the printer's scale each way.";

    public sealed record Settings(
        [Length("Size", 10, 100)] float Size = 20f,
        [Length("Letter depth", 0.2, 2, Hint = "How deep X, Y and Z are cut")] float Depth = 0.6f);

    protected override Generated Build(Settings s, Printer printer, CancellationToken token)
    {
        float a = s.Size, h = a / 2f;
        float pixel = PixelText.Fit("X", a * 0.6f, a * 0.6f, a * 0.1f);

        // X on the front, measured across it; Y on the right side, measured across that; Z on top.
        var cuts = new List<Mesh>();
        cuts.AddRange(PixelText.OnFace("X", pixel, new Vector3(0, -h, h), Vector3.UnitX, Vector3.UnitZ, s.Depth));
        cuts.AddRange(PixelText.OnFace("Y", pixel, new Vector3(h, 0, h), Vector3.UnitY, Vector3.UnitZ, s.Depth));
        cuts.AddRange(PixelText.Boxes("Z", pixel, a - s.Depth, a + 1));

        var cube = Shapes.Subtract(Shapes.Box(-h, -h, 0, h, h, a), cuts);
        return new Generated([new GeneratedPart("Calibration cube", cube, Role: "cube")],
        [
            $"Measure it with calipers across each lettered face: X across the X, Y across the Y, Z from the plate to the top. Each should read {a:0.##} mm.",
            $"If one reads, say, {a + 0.2f:0.##} instead, that axis prints {(a + 0.2f) / a * 100 - 100:0.#}% large: scale it by {a / (a + 0.2f) * 100:0.#}% in the slicer, or put the printer's steps per millimetre right.",
            "Measure at the middle of each face, away from the edges, where the first layer and the corners spread."
        ]);
    }

    protected override IEnumerable<string> Describe(Settings s, float modelScale) =>
        [$"{s.Size:0.##} mm each way when it prints true."];
}
