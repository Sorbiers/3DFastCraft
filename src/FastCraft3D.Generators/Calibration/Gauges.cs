using System.Numerics;
using FastCraft3D.Generators.Fasteners;
using FastCraft3D.Geometry;

namespace FastCraft3D.Generators.Calibration;

/// <summary>
/// Holes and pegs of the same sizes side by side - holes through the plate, pegs standing on it,
/// and holes through an upright wall - each size cut beside it. Measured, or tried with a drill
/// bit and a bought bolt, they say how far this printer makes a hole small and a peg large, and
/// how much rounder a hole printed upright is than one printed on its side.
/// </summary>
public sealed class HoleGauge : Generator<HoleGauge.Settings>
{
    public override string Id => "calibration.holes";
    public override int Version => 1;
    public override string Category => "Calibration";
    public override string Title => "Hole and peg gauge";
    public override string Summary => "Holes and pegs at the same sizes, upright and on their side: how far off true this printer makes them.";

    public sealed record Settings(
        [Length("Smallest", 2, 20)] float From = 3f,
        [Length("Step", 0.5, 10)] float Step = 2f,
        [Count("Sizes", 2, 8)] int Sizes = 5,
        [Length("Plate", 2, 10, Hint = "How thick the plate is, and so how deep the upright holes")] float Thickness = 3f,
        [Toggle("Pegs")] bool Pegs = true,
        [Toggle("Holes on their side", Hint = "Through an upright wall, as a horizontal hole prints")] bool Sideways = true);

    private static float Size(Settings s, int i) => s.From + i * s.Step;

    protected override IEnumerable<string> Check(Settings s, Printer printer)
    {
        if (Size(s, s.Sizes - 1) > 40) yield return "The largest would be over 40 mm. Fewer sizes, or a smaller step.";
    }

    protected override Generated Build(Settings s, Printer printer, CancellationToken token)
    {
        float largest = Size(s, s.Sizes - 1), pitch = largest + 5f, t = s.Thickness;
        float first = -(s.Sizes - 1) * pitch / 2f, half = s.Sizes * pitch / 2f + 1f;

        // Front to back: the sizes, the upright holes, the pegs, the wall with the holes on their side.
        float labelY = -largest / 2f - 4f, pegY = largest + 5f, wallY = pegY + largest / 2f + 4f, wallDepth = 6f;
        float front = labelY - 3.5f;
        float back = s.Sideways ? wallY + wallDepth : s.Pegs ? pegY + largest / 2f + 3f : largest / 2f + 3f;
        float axis = t + 2.5f + largest / 2f;

        var solid = new List<Mesh> { Shapes.Box(-half, front, 0, half, back, t) };
        if (s.Sideways) solid.Add(Shapes.Box(-half, wallY, t - 0.01f, half, wallY + wallDepth, axis + largest / 2f + 2.5f));

        var cuts = new List<Mesh>();
        for (int i = 0; i < s.Sizes; i++)
        {
            token.ThrowIfCancellationRequested();
            float d = Size(s, i), x = first + i * pitch;
            int sides = Shapes.Sides(d / 2f);

            cuts.Add(Shapes.Cylinder(d / 2f, -1, t + 1, new Vector2(x, 0), sides));
            if (s.Pegs) solid.Add(Shapes.Cylinder(d / 2f, t - 0.01f, t + 8f, new Vector2(x, pegY), sides));
            if (s.Sideways) cuts.Add(Shapes.RodAlongY(d / 2f, wallY - 1, wallY + wallDepth + 1, x, axis, sides));

            string text = PixelText.Number(d);
            float pixel = PixelText.Fit(text, pitch - 1.5f, 5f, 0.6f);
            cuts.AddRange(PixelText.Boxes(text, pixel, t - 0.4f, t + 1).Select(b => Shapes.Moved(b, x, labelY, 0)));
        }

        var mesh = Shapes.Subtract(Shapes.Union(solid), cuts);
        return new Generated([new GeneratedPart("Hole and peg gauge", mesh, Role: "gauge")],
        [
            $"Sizes {string.Join(", ", Enumerable.Range(0, s.Sizes).Select(i => PixelText.Number(Size(s, i))))} mm, each cut in front of its hole.",
            "Measure each hole and peg with calipers, or try a drill bit in the holes. Printed holes usually come out a little small and pegs a little large, by about the same: that is how much to open a hole that must fit.",
            "The holes on their side come out oval, flatter at the top where they bridge: measure them both ways."
        ]);
    }
}

/// <summary>
/// Bolts and nuts of one size in pairs, each pair at a thread clearance of its own written on it,
/// so the pair that screws together easily and without wobble says what to set the Thread tool's
/// clearance to.
/// </summary>
public sealed class ThreadFitTest : Generator<ThreadFitTest.Settings>
{
    public override string Id => "calibration.threads";
    public override int Version => 1;
    public override string Category => "Calibration";
    public override string Title => "Thread fit test";
    public override string Summary => "Bolt and nut pairs at a run of thread clearances: the pair that screws together cleanly sets Thread's clearance.";

    public sealed record Settings(
        [Choice("Size")] ThreadSize Size = ThreadSize.M8,
        [Length("Diameter", 3, 30), ShowWhen(nameof(Size), ThreadSize.Custom)] float Diameter = 8f,
        [Length("Pitch", 0.5, 4), ShowWhen(nameof(Size), ThreadSize.Custom)] float Pitch = 1.25f,
        [Number("First clearance", 0, 1, UnitText = "mm", Hint = "On the diameter, between bolt and nut")] float From = 0.1f,
        [Number("Step", 0.05, 0.5, UnitText = "mm")] float Step = 0.1f,
        [Count("Pairs", 2, 10)] int Pairs = 4);

    protected override IEnumerable<(string Name, Settings Settings)> Shipped =>
    [
        ("M8", Default),
        ("M5", Default with { Size = ThreadSize.M5 }),
        ("M12", Default with { Size = ThreadSize.M12, Pairs = 3 })
    ];

    private static float Clearance(Settings s, int i) => MathF.Round(s.From + i * s.Step, 3);

    private static (float Diameter, float Pitch) Thread(Settings s)
    {
        foreach (var m in Threads.Metric)
            if (m.Name == s.Size.ToString()) return (m.Diameter, m.Pitch);
        return (s.Diameter, s.Pitch);
    }

    protected override IEnumerable<string> Check(Settings s, Printer printer)
    {
        var (d, pitch) = Thread(s);
        if (pitch > d / 3f) yield return $"A pitch over {d / 3f:0.##} mm is too coarse for a {d:0.##} mm thread.";
        if (Clearance(s, s.Pairs - 1) > ThreadOptions.MaximumClearance)
            yield return $"The clearances run past {ThreadOptions.MaximumClearance:0.#} mm. A smaller step.";
    }

    protected override Generated Build(Settings s, Printer printer, CancellationToken token)
    {
        var (d, pitch) = Thread(s);

        // Round bodies wide enough to write the clearance on, round the bolt's shank and the nut's hole.
        float body = d + 2 * MathF.Max(4f, d * 0.5f);
        float ring = (body - d) / 2f;
        var parts = new List<(string, string, Mesh, Matrix4x4?)>();
        string size = s.Size == ThreadSize.Custom ? $"M{d:0.#}" : s.Size.ToString();

        for (int i = 0; i < s.Pairs; i++)
        {
            token.ThrowIfCancellationRequested();
            float c = Clearance(s, i);
            string text = PixelText.Number(c);
            float pixel = PixelText.Fit(text, body * 0.6f, ring - 1.2f, 0.5f);

            foreach (var kind in new[] { ThreadKind.Bolt, ThreadKind.Nut })
            {
                var options = new ThreadOptions(kind, d, pitch, MathF.Max(8f, 1.5f * d), c, NutBody.Round, body, MathF.Max(4f, 0.8f * d))
                    { HeadHeight = MathF.Max(3f, 0.6f * d) }.Sane();
                var mesh = Threads.Build(options);
                if (mesh.TriangleCount == 0 || !mesh.CheckHealth().IsWatertight)
                    throw new Refusal($"The {(kind == ThreadKind.Nut ? "nut" : "bolt")} at {text} mm does not close. A slightly different size.");
                mesh = Shapes.Moved(mesh, 0, 0, -mesh.ComputeBounds().Min.Z);

                // On the nut's top, or the top of the bolt's head, beside the thread.
                float face = kind == ThreadKind.Nut ? options.NutHeight : options.HeadHeight;
                if (pixel >= 0.3f)
                    mesh = Shapes.Subtract(mesh, PixelText.Boxes(text, pixel, face - 0.4f, face + 1f)
                        .Select(b => Shapes.Moved(b, 0, -(d / 2f + ring / 2f + 0.2f), 0)).ToList());

                string name = $"{size} {(kind == ThreadKind.Nut ? "nut" : "bolt")} {text}";
                parts.Add((name, $"{(kind == ThreadKind.Nut ? "nut" : "bolt")} {i + 1}", mesh, null));
            }
        }

        return new Generated(Shapes.InARow(parts, gap: 4f),
        [
            $"Clearances {string.Join(", ", Enumerable.Range(0, s.Pairs).Select(i => PixelText.Number(Clearance(s, i))))} mm, each written on its bolt's head and its nut.",
            "Screw each bolt into its own nut. The tightest pair that turns by hand without wobbling is the clearance to give the Thread tool.",
            "Then try a printed bolt in a bought nut, and a bought bolt in a printed nut: half the clearance is on each, so both should fit too."
        ]);
    }
}
