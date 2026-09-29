using System.Numerics;
using FastCraft3D.Generators.Fasteners;
using FastCraft3D.Geometry;

namespace FastCraft3D.Generators.Calibration;

/// <summary>
/// Every fit in the printer profile tried at once, on one plate: a short run of each either side
/// of the profile's own number, with each sample's value printed on it. What fits best goes back
/// into Settings as the profile's number.
/// </summary>
/// <remarks>
/// Made from the single tests rather than beside them, so a sample here is the same as in its own
/// test and says the same thing. Each is centred on the profile rather than starting from a
/// guess: once a printer is roughly right, a second print only has to look either side.
/// </remarks>
public sealed class ProfileTest : Generator<ProfileTest.Settings>
{
    public override string Id => "calibration.profile";
    public override int Version => 1;
    public override string Category => "Calibration";
    public override string Title => "Printer profile test";
    public override string Summary => "Every fit in the printer profile on one plate, a few samples either side of its number: what fits best goes into Settings.";

    public sealed record Settings(
        [Toggle("Sliding clearance", Group = "Tests", Hint = "Pins printed in place in their holes: the first that turns free")] bool Sliding = true,
        [Toggle("Hole clearance", Group = "Tests", Hint = "Holes a loose peg of the same size is tried in: the one it slides into snugly")] bool Holes = true,
        [Toggle("Brick fit", Group = "Tests", Hint = "Brick plates at a run of fits: the one that grips its twin")] bool Bricks = true,
        [Toggle("Thread clearance", Group = "Tests", Hint = "Bolt and nut pairs: the pair that screws together cleanly")] bool Threads = true,
        [Count("Samples", 3, 7, Hint = "Of each fit, the profile's own number in the middle")] int Samples = 5,
        [Number("Step", 0.02, 0.2, UnitText = "mm", Hint = "Between one sample and the next")] float Step = 0.05f);

    protected override IEnumerable<(string Name, Settings Settings)> Shipped =>
    [
        ("All four, fine", Default),
        ("All four, coarse", Default with { Step = 0.1f }),
        ("Clearances only", Default with { Bricks = false, Threads = false })
    ];

    /// <summary>The peg tried in the holes, and the holes' size before the clearance.</summary>
    private const float Peg = 6f;

    protected override IEnumerable<string> Check(Settings s, Printer printer)
    {
        if (!s.Sliding && !s.Holes && !s.Bricks && !s.Threads) yield return "Pick at least one test.";
    }

    /// <summary>The samples either side of a number, as low as they may go.</summary>
    private static float First(Settings s, float middle, float least) => MathF.Max(least, middle - (s.Samples - 1) / 2 * s.Step);

    protected override Generated Build(Settings s, Printer printer, CancellationToken token)
    {
        var rows = new List<(string Test, IReadOnlyList<GeneratedPart> Parts)>();
        var notes = new List<string>();

        if (s.Sliding)
        {
            var made = new ClearanceTest().Make(new ClearanceTest.Settings(
                From: First(s, printer.XyClearance, 0.05f), Step: s.Step, Pins: s.Samples, Diameter: 6f, Thickness: 5f), printer, token);
            rows.Add(("Sliding", made.Parts));
            notes.Add("Sliding clearance: push each pin; the smallest gap that turns free is the profile's Sliding clearance.");
        }

        if (s.Holes)
        {
            token.ThrowIfCancellationRequested();
            rows.Add(("Holes", HoleRow(s, printer)));
            notes.Add($"Hole clearance: try the loose {Peg:0} mm peg in each hole; the smallest it slides into without forcing is the profile's Hole clearance.");
        }

        if (s.Bricks)
        {
            token.ThrowIfCancellationRequested();
            var made = new FitTest().Make(new FitTest.Settings(
                From: First(s, printer.BrickFit, -0.5f), Step: s.Step, Plates: s.Samples), printer, token);
            rows.Add(("Bricks", made.Parts));
            notes.Add("Brick fit: print the row twice and press each plate onto its twin; the one that grips and still comes apart is the profile's Brick fit.");
        }

        if (s.Threads)
        {
            token.ThrowIfCancellationRequested();
            var made = new ThreadFitTest().Make(new ThreadFitTest.Settings(
                Size: ThreadSize.M6, From: First(s, printer.ThreadClearance, 0f), Step: MathF.Max(s.Step, 0.05f), Pairs: Math.Min(s.Samples, 4)), printer, token);
            rows.Add(("Threads", made.Parts));
            notes.Add("Thread clearance: screw each nut on its bolt; the smallest that turns by hand without wobble is the profile's Thread clearance.");
        }

        if (rows.Any(r => r.Parts.Count == 0)) return Generated.Refused("One of the tests could not be made with this printer.");

        // A row for each test, one behind the other, each laid out as its own test lays it out.
        var parts = new List<GeneratedPart>();
        float y = 0f;
        foreach (var (test, made) in rows)
        {
            var bounds = made.Select(p => p.Mesh.ComputeBounds()).Aggregate((a, b) => a.Union(b));
            float shift = y - bounds.Min.Y;
            foreach (var part in made)
                parts.Add(part with
                {
                    Name = $"{test}: {part.Name}",
                    Role = $"{test.ToLowerInvariant()}.{part.Role}",
                    Mesh = Shapes.Moved(part.Mesh, -bounds.Center.X, shift, 0),
                    Assembled = null,
                    Pivot = part.Pivot + new Vector3(-bounds.Center.X, shift, 0)
                });
            y += bounds.Size.Y + 8f;
        }

        // Centred on the plate front to back.
        float middle = (y - 8f) / 2f;
        parts = parts.Select(p => p with { Mesh = Shapes.Moved(p.Mesh, 0, -middle, 0), Pivot = p.Pivot - new Vector3(0, middle, 0) }).ToList();

        notes.Add("Each sample has its number printed on it. Put the winners into Settings on the File tab, in the profile they were printed for.");
        return new Generated(parts, notes);
    }

    /// <summary>
    /// A plate of holes, each the peg's size opened by a clearance of its own written in front of
    /// it, and the loose peg to try in them.
    /// </summary>
    private static List<GeneratedPart> HoleRow(Settings s, Printer printer)
    {
        float first = First(s, printer.HoleClearance, 0f);
        float[] gaps = Enumerable.Range(0, s.Samples).Select(i => MathF.Round(first + i * s.Step, 3)).ToArray();

        float pitch = Peg + 2 * gaps[^1] + 5f, t = 4f;
        float half = s.Samples * pitch / 2f + 1f, labelY = -Peg / 2f - 4f;
        var plate = Shapes.Box(-half, labelY - 3.5f, 0, half, Peg / 2f + 3f, t);

        var cuts = new List<Mesh>();
        for (int i = 0; i < s.Samples; i++)
        {
            float x = -(s.Samples - 1) * pitch / 2f + i * pitch, r = Peg / 2f + gaps[i];
            cuts.Add(Shapes.Cylinder(r, -1, t + 1, new Vector2(x, 0), Shapes.Sides(r)));

            string text = PixelText.Number(gaps[i]);
            float pixel = PixelText.Fit(text, pitch - 1.5f, 5f, 0.6f);
            cuts.AddRange(PixelText.Boxes(text, pixel, t - 0.4f, t + 1).Select(b => Shapes.Moved(b, x, labelY, 0)));
        }

        var holes = Shapes.Subtract(plate, cuts);
        var peg = Shapes.Moved(Shapes.Cylinder(Peg / 2f, 0, 12f, Vector2.Zero, Shapes.Sides(Peg / 2f)), half + 6f + Peg / 2f, 0, 0);

        return
        [
            new GeneratedPart("Hole plate", holes, Role: "plate"),
            new GeneratedPart("Peg", peg, Role: "peg")
        ];
    }
}
