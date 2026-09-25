using System.Numerics;
using FastCraft3D.Geometry;

namespace FastCraft3D.Generators.Mechanisms;

/// <summary>The bars and pins the linkages here are made of: flat links with a hole at each end.</summary>
internal static class Links
{
    /// <summary>A link with holes <paramref name="length"/> apart, lying flat, centred on the origin along X.</summary>
    public static Mesh Bar(float length, float width, float thickness, float hole) =>
        Shapes.Prism(Shapes.RoundedRect(length + width, width, width / 2f),
            [Shapes.Circle(hole / 2f, new Vector2(-length / 2f, 0)), Shapes.Circle(hole / 2f, new Vector2(length / 2f, 0))],
            0, thickness);

    /// <summary>A pin standing up, long enough to go through two links with a little to spare.</summary>
    public static Mesh Pin(float diameter, float thickness) => Shapes.Cylinder(diameter / 2f, 0, 2 * thickness + 1.5f);
}

/// <summary>
/// A four-bar linkage: ground, crank, coupler and rocker, with pins. The Grashof condition says
/// which of them can turn all the way round, and the rocker's swing and the coupler's path are
/// worked out in closed form - a four-bar has one, so there is nothing to solve by steps.
/// </summary>
public sealed class FourBar : Generator<FourBar.Settings>
{
    public override string Id => "mechanism.four-bar";
    public override int Version => 1;
    public override string Category => "Mechanisms";
    public override string Title => "Four-bar linkage";
    public override string Summary => "Four links and their pins, with what the linkage does worked out beside them.";

    public sealed record Settings(
        [Length("Ground", 10, 200, Group = "Lengths", Hint = "Between the two fixed pivots")] float Ground = 80f,
        [Length("Crank", 5, 150, Group = "Lengths", Hint = "The link that is turned")] float Crank = 25f,
        [Length("Coupler", 10, 200, Group = "Lengths")] float Coupler = 70f,
        [Length("Rocker", 10, 200, Group = "Lengths")] float Rocker = 60f,
        [Length("Link width", 4, 20, Group = "Links")] float Width = 8f,
        [Length("Link thickness", 2, 10, Group = "Links")] float Thickness = 4f,
        [Length("Pin", 2, 10, Group = "Links")] float Pin = 4f,
        [Clearance("Fit", 0.05, 1, Group = "Links", Hint = "Round each pin in its holes")] float Fit = 0.2f);

    protected override IEnumerable<string> Check(Settings s, Printer printer)
    {
        if (s.Pin + 2 * s.Fit > s.Width - 2 * 1.2f) yield return "The pins leave less than 1.2 mm of link round them.";

        var lengths = new[] { s.Ground, s.Crank, s.Coupler, s.Rocker };
        if (lengths.Max() >= lengths.Sum() - lengths.Max()) yield return "No linkage closes: the longest link is as long as the other three together.";
    }

    /// <summary>What kind of four-bar it is, by Grashof: whether the shortest link can turn right round.</summary>
    public static string Kind(Settings s)
    {
        var sorted = new[] { s.Ground, s.Crank, s.Coupler, s.Rocker }.OrderBy(x => x).ToArray();
        bool grashof = sorted[0] + sorted[3] <= sorted[1] + sorted[2];
        float shortest = sorted[0];

        if (!grashof) return "No link turns all the way round: a triple rocker.";
        if (shortest == s.Crank) return "A crank-rocker: the crank turns right round and the rocker swings.";
        if (shortest == s.Ground) return "A double crank: both the crank and the rocker turn right round.";
        if (shortest == s.Coupler) return "A double rocker whose coupler turns right round.";
        return "A double rocker: neither the crank nor the rocker turns right round.";
    }

    protected override Generated Build(Settings s, Printer printer, CancellationToken token)
    {
        float hole = s.Pin + 2 * s.Fit;
        var parts = new List<(string, string, Mesh, Matrix4x4?)>
        {
            ("Ground link", "ground", Links.Bar(s.Ground, s.Width, s.Thickness, hole), null),
            ("Crank", "crank", Links.Bar(s.Crank, s.Width, s.Thickness, hole), null),
            ("Coupler", "coupler", Links.Bar(s.Coupler, s.Width, s.Thickness, hole), null),
            ("Rocker", "rocker", Links.Bar(s.Rocker, s.Width, s.Thickness, hole), null)
        };

        for (int i = 0; i < 4; i++) parts.Add(($"Pin {i + 1}", $"pin {i + 1}", Links.Pin(s.Pin, s.Thickness), null));

        // The rocker's swing and the coupler's middle, over a turn of the crank.
        double least = double.MaxValue, most = double.MinValue;
        int reached = 0;
        var path = new List<Vector2>();
        for (int i = 0; i < 360; i++)
        {
            double theta = i * Math.PI / 180;
            var b = new Vector2((float)(s.Crank * Math.Cos(theta)), (float)(s.Crank * Math.Sin(theta)));
            var d = new Vector2(s.Ground, 0);
            float f = Vector2.Distance(b, d);
            if (f > s.Coupler + s.Rocker || f < MathF.Abs(s.Coupler - s.Rocker)) continue;

            reached++;
            float along = (f * f + s.Rocker * s.Rocker - s.Coupler * s.Coupler) / (2 * f);
            float up = MathF.Sqrt(MathF.Max(s.Rocker * s.Rocker - along * along, 0));
            var toward = Vector2.Normalize(b - d);
            var c = d + toward * along + new Vector2(-toward.Y, toward.X) * up;

            double phi = Math.Atan2(c.Y - d.Y, c.X - d.X) * 180 / Math.PI;
            least = Math.Min(least, phi);
            most = Math.Max(most, phi);
            path.Add((b + c) / 2f);
        }

        var notes = new List<string> { Kind(s) };
        if (reached == 360) notes.Add($"Turned right round, the rocker swings {most - least:0} degrees.");
        else if (reached > 0) notes.Add($"The crank reaches only {reached} degrees of its turn before the linkage locks.");

        if (path.Count > 0)
        {
            var box = (Min: path.Aggregate(Vector2.Min), Max: path.Aggregate(Vector2.Max));
            notes.Add($"The coupler's middle traces a path {box.Max.X - box.Min.X:0} by {box.Max.Y - box.Min.Y:0} mm.");
        }

        notes.Add("Plain pins: a drop of glue, or a washer melted over each end, keeps them in.");
        return new Generated(Shapes.InARow(parts), notes);
    }
}

/// <summary>A crank, a connecting rod and a slider, turning round into back and forth, with the stroke worked out.</summary>
public sealed class CrankSlider : Generator<CrankSlider.Settings>
{
    public override string Id => "mechanism.crank-slider";
    public override int Version => 1;
    public override string Category => "Mechanisms";
    public override string Title => "Crank and slider";
    public override string Summary => "A crank, a rod and a slider block, with the stroke worked out.";

    public sealed record Settings(
        [Length("Crank", 5, 80)] float Crank = 20f,
        [Length("Rod", 15, 250)] float Rod = 70f,
        [Length("Offset", 0, 40, Hint = "How far the slider's line runs beside the crank's axis")] float Offset = 0f,
        [Length("Link width", 4, 20)] float Width = 8f,
        [Length("Link thickness", 2, 10)] float Thickness = 4f,
        [Length("Pin", 2, 10)] float Pin = 4f,
        [Clearance("Fit", 0.05, 1)] float Fit = 0.2f);

    protected override IEnumerable<string> Check(Settings s, Printer printer)
    {
        if (s.Rod <= s.Crank + s.Offset) yield return "The rod has to be longer than the crank and the offset together, or the crank cannot turn right round.";
        if (s.Pin + 2 * s.Fit > s.Width - 2 * 1.2f) yield return "The pins leave less than 1.2 mm of link round them.";
    }

    public static double Stroke(Settings s)
    {
        double least = double.MaxValue, most = double.MinValue;
        for (int i = 0; i < 720; i++)
        {
            double theta = i * Math.PI / 360;
            double y = s.Crank * Math.Sin(theta) - s.Offset;
            double x = s.Crank * Math.Cos(theta) + Math.Sqrt(s.Rod * s.Rod - y * y);
            least = Math.Min(least, x);
            most = Math.Max(most, x);
        }

        return most - least;
    }

    protected override Generated Build(Settings s, Printer printer, CancellationToken token)
    {
        float hole = s.Pin + 2 * s.Fit;
        float block = s.Width * 2f;
        var slider = Shapes.Prism(Shapes.Rect(-block / 2f, -s.Width, block / 2f, s.Width), [Shapes.Circle(hole / 2f)], 0, s.Thickness * 2f);

        var parts = new List<(string, string, Mesh, Matrix4x4?)>
        {
            ("Crank", "crank", Links.Bar(s.Crank, s.Width, s.Thickness, hole), null),
            ("Rod", "rod", Links.Bar(s.Rod, s.Width, s.Thickness, hole), null),
            ("Slider", "slider", slider, null),
            ("Pin 1", "pin 1", Links.Pin(s.Pin, s.Thickness), null),
            ("Pin 2", "pin 2", Links.Pin(s.Pin, s.Thickness), null)
        };

        return new Generated(Shapes.InARow(parts),
            [$"A stroke of {Stroke(s):0.#} mm.", "The slider runs in a guide of your own; plain pins, glued or capped."]);
    }
}

/// <summary>
/// A ball bearing printed in place: two races and the balls between them, each its own part,
/// the gaps the printer's clearance. The balls stand on the plate, so nothing prints in the air
/// but the top of each ball.
/// </summary>
public sealed class Bearing : Generator<Bearing.Settings>
{
    public override string Id => "mechanism.bearing";
    public override int Version => 1;
    public override string Category => "Mechanisms";
    public override string Title => "Print-in-place bearing";
    public override string Summary => "Two races and the balls between them, printed together in place.";

    public sealed record Settings(
        [Length("Bore", 3, 60)] float Bore = 8f,
        [Length("Outside", 15, 120)] float Outside = 30f,
        [Length("Width", 5, 30)] float Width = 9f,
        [Clearance("Clearance", 0.1, 1, Hint = "Round each ball. Print-in-place wants a little more than a sliding fit: raise it if the balls fuse.")] float Clearance = 0.35f);

    private static float Ball(Settings s) => MathF.Min(s.Width / 2f, ((s.Outside - s.Bore) / 2f - 2 * 1.6f) / 2f);

    protected override IEnumerable<string> Check(Settings s, Printer printer)
    {
        if (Ball(s) < 1.5f) yield return "No room for balls between the races: a bigger outside or a smaller bore.";
    }

    protected override Generated Build(Settings s, Printer printer, CancellationToken token)
    {
        float rb = Ball(s), c = s.Clearance;
        float pitch = (s.Bore / 2f + s.Outside / 2f) / 2f;
        float height = MathF.Max(s.Width, 2 * rb);
        int count = Math.Max(3, (int)MathF.Floor(2 * MathF.PI * pitch / (2 * rb + 1.2f)));

        var groove = Shapes.Moved(Primitives.Torus(pitch, rb + c, Shapes.Sides(pitch), 32), 0, 0, rb);

        var inner = Shapes.Subtract(Shapes.Turned([new(s.Bore / 2f, 0), new(pitch - c, 0), new(pitch - c, height), new(s.Bore / 2f, height)]), groove);
        var outer = Shapes.Subtract(Shapes.Turned([new(pitch + c, 0), new(s.Outside / 2f, 0), new(s.Outside / 2f, height), new(pitch + c, height)]), groove);

        token.ThrowIfCancellationRequested();
        var parts = new List<GeneratedPart>
        {
            new("Inner race", inner, Role: "inner"),
            new("Outer race", outer, Role: "outer")
        };

        var ball = Shapes.Moved(Primitives.Sphere(rb, 24, 12), 0, 0, rb);
        for (int i = 0; i < count; i++)
        {
            float a = 2f * MathF.PI * i / count;
            var at = new Vector3(pitch * MathF.Cos(a), pitch * MathF.Sin(a), 0);
            parts.Add(new($"Ball {i + 1}", Shapes.Moved(ball, at.X, at.Y, 0), Role: $"ball {i + 1}", Pivot: at));
        }

        return new Generated(parts, [$"{count} balls of {2 * rb:0.#} mm. Print it as it lies, all together; a turn by hand frees it."]);
    }
}
