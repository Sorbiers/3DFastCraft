using System.Numerics;
using FastCraft3D.Geometry;

namespace FastCraft3D.Generators.Mechanisms;

/// <summary>What the gear sets here are built from: a plain spur gear standing at the origin, and how two mesh.</summary>
internal static class Spur
{
    public static Mesh Gear(float module, int teeth, float thickness, float bore, float turn = 0f)
    {
        var made = Gears.Build(new GearOptions
        {
            Kind = GearKind.Gear, Module = module, Teeth = teeth, Thickness = thickness,
            Bore = bore > 0 ? BoreShape.Round : BoreShape.None, BoreSize = MathF.Max(bore, 0.5f)
        });

        if (made.Parts.Count == 0) throw new Refusal(made.Refusal ?? $"A {teeth}-tooth gear could not be made.");
        return turn == 0f ? made.Parts[0].Mesh : Shapes.Turned(made.Parts[0].Mesh, turn);
    }

    /// <summary>
    /// How far round a gear of <paramref name="teeth"/> is turned to mesh with one at the origin at
    /// no turn, its centre along plus X: half a turn less half a tooth, so a gap faces the other's
    /// tooth. It is the rule the gear tool's own pairs are laid by.
    /// </summary>
    public static float Meshing(int teeth) => MathF.PI - MathF.PI / teeth;

    public static float Apart(float module, int a, int b) => module * (a + b) / 2f;
}

/// <summary>
/// A compound gear train to a ratio: a pinion driving a larger gear on each shaft, the larger one
/// carrying the next pinion stacked on it. The tooth counts are searched for, and what the train
/// comes to is said beside the ratio asked for.
/// </summary>
public sealed class GearTrain : Generator<GearTrain.Settings>
{
    public override string Id => "mechanism.gear-train";
    public override int Version => 1;
    public override string Category => "Mechanisms";
    public override string Title => "Gear train";
    public override string Summary => "Compound spur gears to a ratio, the tooth counts found for you.";

    public sealed record Settings(
        [Number("Ratio", 1.1, 2000, UnitText = "to 1", Hint = "Turns of the first shaft to one of the last")] float Ratio = 12f,
        [Count("Stages", 1, 4, Hint = "Pairs of gears. A stage much over 6 to 1 wants a very large gear.")] int Stages = 2,
        [Length("Module", 0.5, 4)] float Module = 1.5f,
        [Count("Pinion", 8, 30, UnitText = "teeth", Hint = "The small gear of every stage")] int Pinion = 12,
        [Count("Largest", 20, 150, UnitText = "teeth", Hint = "The most teeth any gear may have")] int Largest = 60,
        [Length("Thickness", 3, 20)] float Thickness = 6f,
        [Length("Bore", 0, 10, Hint = "For the shafts. Nought for none.")] float Bore = 3.2f,
        [Toggle("Base", Hint = "A plate with a pin standing in each shaft's bore, to turn the train by hand")] bool Base = false);

    protected override bool Shows(Settings s, string parameter) => parameter != nameof(Settings.Base) || s.Bore > 0;

    protected override IEnumerable<(string Name, Settings Settings)> Shipped =>
    [
        ("Clock, 12 to 1", Default with { Ratio = 12, Stages = 2 }),
        ("Winch, 50 to 1", Default with { Ratio = 50, Stages = 3, Module = 1 })
    ];

    protected override IEnumerable<string> Check(Settings s, Printer printer)
    {
        double each = s.Pinion * Math.Pow(s.Ratio, 1.0 / s.Stages);
        if (each > s.Largest)
            yield return $"{s.Stages} stages would each need a gear of {each:0} teeth, more than {s.Largest}. More stages, or allow larger gears.";
        if (s.Largest <= s.Pinion) yield return "The largest gear has to have more teeth than the pinion.";
        if (s.Bore > s.Module * (s.Pinion - 2.5f) - 2) yield return "The bore is wider than the pinion's roots allow.";
    }

    /// <summary>The larger gear of each stage, for the ratio nearest the one asked for.</summary>
    public static int[] Teeth(Settings s)
    {
        int p = s.Pinion;
        double target = s.Ratio;
        int guess = (int)Math.Round(p * Math.Pow(target, 1.0 / s.Stages));

        int[]? best = null;
        double bestError = double.MaxValue;
        var chosen = new int[s.Stages];

        // Every stage but the last within a few teeth of an even share; the last fills in the rest.
        void Search(int stage, double sofar)
        {
            if (stage == s.Stages - 1)
            {
                int last = Math.Clamp((int)Math.Round(target * p / sofar), p + 1, s.Largest);
                chosen[stage] = last;
                double ratio = sofar * last / p;
                double error = Math.Abs(ratio - target) / target;
                if (error < bestError - 1e-12 || (Math.Abs(error - bestError) < 1e-12 && chosen.Sum() < best!.Sum()))
                {
                    bestError = error;
                    best = chosen.ToArray();
                }

                return;
            }

            for (int b = Math.Max(p + 1, guess - 6); b <= Math.Min(s.Largest, guess + 6); b++)
            {
                chosen[stage] = b;
                Search(stage + 1, sofar * b / p);
            }
        }

        Search(0, 1.0);
        return best ?? Enumerable.Repeat(Math.Clamp(guess, p + 1, s.Largest), s.Stages).ToArray();
    }

    protected override Generated Build(Settings s, Printer printer, CancellationToken token)
    {
        var driven = Teeth(s);
        float m = s.Module, t = s.Thickness;

        var shafts = new List<float> { 0f };
        for (int k = 0; k < s.Stages; k++) shafts.Add(shafts[^1] + Spur.Apart(m, s.Pinion, driven[k]));

        var pinion = Spur.Gear(m, s.Pinion, t, s.Bore);
        var parts = new List<(string, string, Mesh, Matrix4x4?)>();
        bool based = s.Base && s.Bore > 0;
        float lift = based ? Mechanisms.Base.Lift : 0f;

        for (int j = 0; j <= s.Stages; j++)
        {
            token.ThrowIfCancellationRequested();

            // A shaft carries the larger gear of the stage before it and the pinion of the next,
            // stacked, the larger one underneath; each part printed from its lowest gear.
            var gears = new List<Mesh>();
            int lowest = j == 0 ? 0 : j - 1;
            if (j > 0) gears.Add(Spur.Gear(m, driven[j - 1], t, s.Bore, Spur.Meshing(driven[j - 1])));
            if (j < s.Stages) gears.Add(Shapes.Moved(pinion, 0, 0, (j - lowest) * t));

            string name = j == 0 ? "Input pinion" : j == s.Stages ? "Output gear" : $"Shaft {j + 1} gears";
            parts.Add((name, $"shaft {j + 1}", Shapes.Union(gears), Matrix4x4.CreateTranslation(shafts[j], 0, lift + lowest * t)));
        }

        if (based)
            parts.Add(("Base", "base", Mechanisms.Base.Plate(
                shafts.Select((x, j) => (new Vector2(x, 0), s.Bore, lift + (j == 0 ? t : j == s.Stages ? s.Stages * t : (j + 1) * t))).ToList(),
                printer), Matrix4x4.Identity));

        double ratio = driven.Aggregate(1.0, (r, b) => r * b / s.Pinion);
        var notes = new List<string>
        {
            $"{ratio:0.###} to 1, {(ratio - s.Ratio) / s.Ratio * 100:+0.##;-0.##;0}% from {s.Ratio:0.###}.",
            $"Stages {string.Join(", ", driven.Select(b => $"{s.Pinion}:{b}"))}; shafts {string.Join(", ", shafts.Select(x => $"{x:0.##}"))} mm along."
        };

        var laid = Shapes.InARow(parts);

        // Every shaft turns about its own axis, where the train goes together; the first drives.
        var shaftsTurning = laid.Take(s.Stages + 1).Select((p, j) =>
        {
            var axis = Vector3.Transform(p.Pivot, p.Assembled ?? Matrix4x4.Identity);
            return new MovingPart(j, Geometry.Motion.Joint.Revolute, new Vector2(axis.X, axis.Y));
        }).ToList();

        return new Generated(laid, notes)
        {
            Motion = new Mechanism(shaftsTurning, 0, Enumerable.Range(0, s.Stages).Select(k => lift + k * t + t / 2f).ToList())
        };
    }
}

/// <summary>
/// A planetary set: sun, planets, ring, and a carrier with pins for the planets. The planets only
/// all mesh at once when the sun's and ring's teeth together divide by the number of planets, so
/// that is checked, with a sun size that works offered in its place.
/// </summary>
public sealed class Planetary : Generator<Planetary.Settings>
{
    public override string Id => "mechanism.planetary";
    public override int Version => 1;
    public override string Category => "Mechanisms";
    public override string Title => "Planetary gear set";
    public override string Summary => "Sun, planets, ring and carrier, with the tooth counts checked so every planet meshes.";

    public sealed record Settings(
        [Length("Module", 0.5, 3)] float Module = 1.5f,
        [Count("Sun", 8, 60, UnitText = "teeth")] int Sun = 12,
        [Count("Planets' teeth", 8, 60, UnitText = "teeth")] int Planet = 12,
        [Count("Planets", 2, 8)] int Planets = 3,
        [Length("Thickness", 3, 20)] float Thickness = 8f,
        [Length("Ring rim", 2, 10)] float Rim = 3f,
        [Length("Sun bore", 0, 10)] float Bore = 5f,
        [Length("Planet pins", 1.5, 8, Hint = "The carrier's pins, and the planets' bores round them")] float Pin = 3f,
        [Clearance("Fit", 0.05, 1, Hint = "Round each planet's pin, and under the carrier")] float Fit = 0.25f);

    public static int Ring(Settings s) => s.Sun + 2 * s.Planet;

    protected override IEnumerable<string> Check(Settings s, Printer printer)
    {
        if ((s.Sun + Ring(s)) % s.Planets != 0)
        {
            int better = Enumerable.Range(8, 60).OrderBy(z => Math.Abs(z - s.Sun)).First(z => (2 * z + 2 * s.Planet) % s.Planets == 0);
            yield return $"The planets would not all mesh: sun and ring teeth together, {s.Sun + Ring(s)}, have to divide by {s.Planets}. Try a sun of {better}.";
        }

        float apart = Spur.Apart(s.Module, s.Sun, s.Planet);
        if ((s.Planet + 2) * s.Module >= 2 * apart * MathF.Sin(MathF.PI / s.Planets))
            yield return "The planets would run into each other. Fewer planets, or a bigger sun.";

        if (s.Pin + 2 * s.Fit > s.Module * (s.Planet - 2.5f) - 2) yield return "The planets are too small for their pins.";
        if (s.Bore > s.Module * (s.Sun - 2.5f) - 2) yield return "The sun's bore is wider than its roots allow.";
    }

    protected override Generated Build(Settings s, Printer printer, CancellationToken token)
    {
        float m = s.Module, t = s.Thickness;
        int ring = Ring(s);
        float apart = Spur.Apart(m, s.Sun, s.Planet);

        var parts = new List<(string, string, Mesh, Matrix4x4?)>
        {
            ("Sun", "sun", Spur.Gear(m, s.Sun, t, s.Bore), Matrix4x4.Identity)
        };

        for (int i = 0; i < s.Planets; i++)
        {
            token.ThrowIfCancellationRequested();

            // Planet i is the first carried round by its angle, then turned by as much as the sun
            // held still turns it: that is what keeps every planet in mesh with the same sun.
            float angle = 2f * MathF.PI * i / s.Planets;
            float turn = Spur.Meshing(s.Planet) + angle * (1f + (float)s.Sun / s.Planet);
            var at = new Vector2(apart * MathF.Cos(angle), apart * MathF.Sin(angle));
            parts.Add(($"Planet {i + 1}", $"planet {i + 1}", Spur.Gear(m, s.Planet, t, s.Pin + 2 * s.Fit, turn),
                Matrix4x4.CreateTranslation(at.X, at.Y, 0)));
        }

        var ringGear = Gears.Build(new GearOptions { Kind = GearKind.Ring, Module = m, Teeth = ring, Thickness = t, Rim = s.Rim });
        if (ringGear.Parts.Count == 0) throw new Refusal(ringGear.Refusal ?? "The ring could not be made.");

        // Turned so the planet at nought, turned as it is, sits in the ring's teeth.
        float ringTurn = MathF.PI * (s.Planet - 1) / ring;
        parts.Add(("Ring", "ring", Shapes.Turned(ringGear.Parts[0].Mesh, ringTurn), Matrix4x4.Identity));

        // The carrier: a disc over the planets, their pins hanging from it. Printed disc down.
        const float disc = 3f;
        float lift = t + s.Fit;
        var carrierPieces = new List<Mesh> { Shapes.Tube(apart + s.Pin + 2f, s.Bore / 2f + 1f, 0, disc) };
        for (int i = 0; i < s.Planets; i++)
        {
            float angle = 2f * MathF.PI * i / s.Planets;
            carrierPieces.Add(Shapes.Cylinder(s.Pin / 2f, disc - 0.01f, disc + t - 0.5f, new Vector2(apart * MathF.Cos(angle), apart * -MathF.Sin(angle))));
        }

        var carrier = Shapes.Union(carrierPieces);
        parts.Add(("Carrier", "carrier", carrier, Matrix4x4.CreateRotationX(MathF.PI) * Matrix4x4.CreateTranslation(0, 0, lift + disc)));

        double reduction = 1.0 + (double)ring / s.Sun;

        // Shown with the carrier held: the sun drives, each planet turns on its pin, the ring
        // turns the other way. Held rather than the ring, since a held carrier is what the
        // motion check can turn - nothing here pushes a carrier round.
        var moving = new List<MovingPart> { new(0, Geometry.Motion.Joint.Revolute, Vector2.Zero) };
        for (int i = 0; i < s.Planets; i++)
        {
            float angle = 2f * MathF.PI * i / s.Planets;
            moving.Add(new(1 + i, Geometry.Motion.Joint.Revolute, new Vector2(apart * MathF.Cos(angle), apart * MathF.Sin(angle))));
        }

        moving.Add(new(1 + s.Planets, Geometry.Motion.Joint.Revolute, Vector2.Zero));

        return new Generated(Shapes.InARow(parts),
            [$"Ring {ring} teeth. Ring held, sun driving: the carrier turns once for every {reduction:0.##} turns of the sun."])
        {
            Motion = new Mechanism(moving, 0, [t / 2f])
        };
    }
}
