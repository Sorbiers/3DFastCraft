using FastCraft3D.Geometry;
using FastCraft3D.Geometry.Engraving;

namespace FastCraft3D.Generators.Calibration;

/// <summary>
/// Small two-by-two brick plates at a run of fits, studs on top and the underside below, each
/// marked with its number of notches. Printed once per filament and pressed onto real bricks and
/// onto each other, they say which Fit to type into Emboss and Split with connectors - which no
/// table of tolerances can, since it is the printer and the filament that decide it.
///
/// It was a button of its own on the Insert tab; here it takes its fits as settings rather than
/// always the same four.
/// </summary>
public sealed class FitTest : Generator<FitTest.Settings>
{
    public override string Id => "calibration.brick-fit";
    public override int Version => 1;
    public override string Category => "Calibration";
    public override string Title => "Brick fit test";
    public override string Summary => "Brick plates at a run of fits, each with its fit written on top: print them and use the Fit that grips.";
    public override bool IsBeta => false;

    public sealed record Settings(
        [Number("First fit", -0.5, 0.5, UnitText = "mm", Hint = "The plate with one notch. Positive grips harder, negative looser.")] float From = -0.2f,
        [Number("Step", 0.02, 0.3, UnitText = "mm", Hint = "Between one plate's fit and the next")] float Step = 0.1f,
        [Count("Plates", 1, 8, Hint = "Each with its fit on top; the first four notched as well")] int Plates = 4);

    /// <summary>How deep the fit is cut into the top: under half the roof over the hollow.</summary>
    private const float Engrave = 0.4f;

    internal static float[] Fits(Settings s) => Enumerable.Range(0, s.Plates).Select(i => MathF.Round(s.From + i * s.Step, 3)).ToArray();

    protected override Generated Build(Settings s, Printer printer, CancellationToken token)
    {
        var fits = Fits(s);
        var plates = new List<(string, string, Mesh, System.Numerics.Matrix4x4?)>();

        for (int i = 0; i < fits.Length; i++)
        {
            token.ThrowIfCancellationRequested();
            var plate = BrickStuds.FitCoupon(fits[i], i < 4 ? i + 1 : 0, token)
                        ?? throw new Refusal($"The plate at {fits[i]:+0.00;-0.00;0.00} mm would not come out whole. A fit nearer nought.");

            // The fit written on top as well as notched on the side: counting notches on clear
            // filament proved hard. Cut along the strip between the two rows of studs, where a
            // brick pressed on top has nothing to put.
            string label = PixelText.Number(fits[i], signed: true);
            float room = BrickStuds.Pitch - 2 * BrickStuds.StudRadius(fits[i]) - 0.6f;
            float pixel = PixelText.Fit(label, 2 * BrickStuds.Pitch - 3f, room, 0.45f);
            if (pixel >= 0.3f)
                plate = Shapes.Subtract(plate, PixelText.Boxes(label, pixel, BrickStuds.PlateHeight - Engrave, BrickStuds.PlateHeight + 1));
            plates.Add(($"Fit test {fits[i]:+0.00;-0.00;0.00}", $"plate {i + 1}", plate, null));
        }

        return new Generated(Shapes.InARow(plates, gap: 6f),
        [
            "Fits " + string.Join(", ", fits.Select(f => PixelText.Number(f, signed: true))) + " mm, written on each plate; the first four notched one to four as well.",
            "Press each onto real bricks, and onto each other: a pair grips like the sum of their two Fits. "
            + "To test two halves as Split with connectors makes them, print the set twice and try each plate on its twin."
        ]);
    }
}
