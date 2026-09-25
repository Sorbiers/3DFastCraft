using System.Numerics;
using FastCraft3D.Geometry;

namespace FastCraft3D.Generators.Hinges;

public enum PinKind
{
    /// <summary>The pin is part of one leaf, running through the other's knuckles with a gap round it.</summary>
    [ShownAs("Print in place")] PrintInPlace,

    /// <summary>Both leaves are bored, and the pin is printed on its own and pushed through.</summary>
    [ShownAs("Separate pin")] SeparatePin
}

/// <summary>
/// A knuckle hinge: two leaves, their knuckles taking turns along the axis, and a pin through
/// them. Printed flat, the knuckles lying on the plate; printed in place, the two leaves come off
/// the plate already joined, the gaps between them the printer's clearance.
///
/// The leaves never touch - the sweep checks that - so a hinge that fuses on the printer has too
/// small a clearance for that printer, not too small a gap in the model.
/// </summary>
public sealed class Hinge : Generator<Hinge.Settings>
{
    public override string Id => "hinge.knuckle";
    public override int Version => 1;
    public override string Category => "Hinges";
    public override string Title => "Knuckle hinge";
    public override string Summary => "Two leaves and a pin, printed in place or with the pin separate.";

    public sealed record Settings(
        [Choice("Pin")] PinKind Pin = PinKind.PrintInPlace,
        [Length("Width", 10, 150, Hint = "Along the pin")] float Width = 40f,
        [Length("Leaf", 5, 100, Hint = "How far each leaf reaches from the knuckles")] float Leaf = 20f,
        [Length("Leaf thickness", 1, 6)] float Thickness = 2.4f,
        [Count("Knuckles", 3, 11, Hint = "Odd, so the same leaf has both ends")] int Knuckles = 5,
        [Length("Knuckle diameter", 4, 20)] float Diameter = 8f,
        [Length("Pin diameter", 1.5, 12)] float PinDiameter = 3.4f,
        [Clearance("Clearance", 0.1, 1, Hint = "Round the pin and between the knuckles. If the leaves fuse, raise it.")] float Clearance = 0.3f);

    protected override IEnumerable<string> Check(Settings s, Printer printer)
    {
        if (s.Knuckles % 2 == 0) yield return "Take an odd number of knuckles, so the same leaf has both ends.";

        if (Knuckle(s) < 2) yield return "The knuckles would be under 2 mm long. Fewer of them, or a wider hinge.";

        if (s.PinDiameter + 2 * s.Clearance > s.Diameter - 2 * 1.2f)
            yield return "The pin leaves less than 1.2 mm of knuckle round it.";

        if (s.Thickness > s.Diameter / 2f) yield return "The leaves are thicker than half the knuckle.";
    }

    private static float Knuckle(Settings s) => (s.Width - (s.Knuckles - 1) * s.Clearance) / s.Knuckles;

    protected override Generated Build(Settings s, Printer printer, CancellationToken token)
    {
        float r = s.Diameter / 2f, c = s.Clearance, t = s.Thickness, k = Knuckle(s);
        float pin = s.PinDiameter / 2f;

        Mesh Leaf(int side)
        {
            float near = r + c, far = r + c + s.Leaf;
            var pieces = new List<Mesh>
            {
                side < 0 ? Shapes.Box(-s.Width / 2f, -far, 0, s.Width / 2f, -near, t) : Shapes.Box(-s.Width / 2f, near, 0, s.Width / 2f, far, t)
            };

            for (int i = side < 0 ? 0 : 1; i < s.Knuckles; i += 2)
            {
                float x0 = -s.Width / 2f + i * (k + c), x1 = x0 + k;
                pieces.Add(Shapes.RodAlongX(r, x0, x1, 0, r));

                // The web that carries the knuckle to its leaf, under it.
                pieces.Add(side < 0 ? Shapes.Box(x0, -near - 0.01f, 0, x1, 0, t) : Shapes.Box(x0, 0, 0, x1, near + 0.01f, t));
            }

            return Shapes.Union(pieces);
        }

        var a = Leaf(-1);
        var b = Leaf(+1);
        token.ThrowIfCancellationRequested();

        var parts = new List<(string, string, Mesh, Matrix4x4?)>();
        if (s.Pin == PinKind.PrintInPlace)
        {
            a = Shapes.Union(a, Shapes.RodAlongX(pin, -s.Width / 2f, s.Width / 2f, 0, r));
            b = Shapes.Subtract(b, Shapes.RodAlongX(pin + c, -s.Width / 2f - 1, s.Width / 2f + 1, 0, r));

            // One print, two parts: both stay where they are, already joined.
            return new Generated(
            [
                new GeneratedPart("Hinge leaf A", a, Role: "leaf a", Pivot: new Vector3(0, 0, 0)),
                new GeneratedPart("Hinge leaf B", b, Role: "leaf b", Pivot: new Vector3(0, 0, 0))
            ], ["Print both leaves together as they lie. If they come off fused, raise the clearance."]);
        }

        var bore = Shapes.RodAlongX(pin, -s.Width / 2f - 1, s.Width / 2f + 1, 0, r);
        a = Shapes.Subtract(a, bore);
        b = Shapes.Subtract(b, bore);

        // The pin lying on the plate; in its bore, turned to the axis.
        float pinRadius = pin - c;
        var rod = Shapes.RodAlongX(pinRadius, -s.Width / 2f, s.Width / 2f, 0, pinRadius);

        return new Generated(
        [
            new GeneratedPart("Hinge leaf A", a, Role: "leaf a"),
            new GeneratedPart("Hinge leaf B", b, Role: "leaf b"),
            new GeneratedPart("Hinge pin", Shapes.Moved(rod, 0, -(r + c + s.Leaf + 5 + pinRadius), 0),
                Matrix4x4.CreateTranslation(0, r + c + s.Leaf + 5 + pinRadius, r - pinRadius), Role: "pin")
        ], ["Push the pin through once printed. A drop of glue at one end keeps it."]);
    }
}

/// <summary>
/// Two plates joined by a web thin enough to bend. Only in a material that takes bending over
/// and over - PP or PETG, not PLA, which cracks within a few folds.
/// </summary>
public sealed class LivingHinge : Generator<LivingHinge.Settings>
{
    public override string Id => "hinge.living";
    public override int Version => 1;
    public override string Category => "Hinges";
    public override string Title => "Living hinge";
    public override string Summary => "Two plates joined by a thin web that bends. PP or PETG; PLA cracks.";

    public sealed record Settings(
        [Length("Width", 5, 150, Hint = "Along the fold")] float Width = 40f,
        [Length("Leaf", 5, 100, Hint = "How far each plate reaches from the web")] float Leaf = 20f,
        [Length("Thickness", 1, 6)] float Thickness = 2f,
        [Length("Web", 0.2, 1.2, Hint = "The web's thickness: two or three layers")] float Web = 0.4f,
        [Length("Web length", 1, 10, Hint = "Across the fold. Longer bends more gently.")] float WebLength = 3f);

    protected override IEnumerable<string> Check(Settings s, Printer printer)
    {
        if (s.Web >= s.Thickness) yield return "The web is as thick as the plates, so nothing would bend.";
    }

    protected override Generated Build(Settings s, Printer printer, CancellationToken token)
    {
        float half = s.WebLength / 2f;
        var hinge = Shapes.Union(
            Shapes.Box(-s.Width / 2f, -half - s.Leaf, 0, s.Width / 2f, -half + 0.01f, s.Thickness),
            Shapes.Box(-s.Width / 2f, -half, 0, s.Width / 2f, half, s.Web),
            Shapes.Box(-s.Width / 2f, half - 0.01f, 0, s.Width / 2f, half + s.Leaf, s.Thickness));

        var notes = new List<string> { "Print in PP or PETG, with the web's layers along the fold. PLA cracks within a few folds." };
        if (s.Web < 2 * printer.Layer) notes.Add($"The web is under two layers of {printer.Layer:0.##} mm, and may not print whole.");

        return new Generated([new GeneratedPart("Living hinge", hinge, Role: "hinge")], notes);
    }
}
