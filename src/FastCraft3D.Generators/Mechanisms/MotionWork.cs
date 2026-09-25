using System.Numerics;
using FastCraft3D.Geometry;

namespace FastCraft3D.Generators.Mechanisms;

public enum MotionTeeth
{
    [ShownAs("10 : 30 and 8 : 32")] Small,
    [ShownAs("15 : 45 and 12 : 48")] Standard,
    [ShownAs("20 : 60 and 16 : 64")] Large
}

/// <summary>
/// A clock's motion work: the wheels behind the dial that turn the hour hand once for every
/// twelve turns of the minute hand. The cannon pinion, on the centre arbor with the minute hand,
/// drives the minute wheel on its stud; the minute wheel's pinion drives the hour wheel, whose
/// pipe rides on the cannon pinion's and carries the hour hand.
///
/// Twelve to one in two stages whose centres are the same distance apart, so the hour wheel comes
/// back round onto the centre: three to one then four to one, with the teeth of each pair adding
/// up to the same. Hence the tooth counts come in sets rather than one at a time.
///
/// The hands sit on a flat on each pipe, as a real minute hand sits on its square, so turning the
/// cannon pinion carries them round rather than leaving them where they were printed.
/// </summary>
public sealed class MotionWork : Generator<MotionWork.Settings>
{
    public override string Id => "mechanism.motion-work";
    public override int Version => 1;
    public override string Category => "Mechanisms";
    public override string Title => "Clock motion work";
    public override string Summary => "The wheels behind a clock's hands: the hour hand turned once for every twelve turns of the minute hand.";

    /// <summary>Between the cannon pinion and the hour wheel above it.</summary>
    private const float Gap = 0.5f;

    /// <summary>The stand's plate.</summary>
    private const float Plate = 3f;

    public sealed record Settings(
        [Choice("Wheels", Hint = "Cannon pinion and minute wheel, then minute pinion and hour wheel")] MotionTeeth Teeth = MotionTeeth.Standard,
        [Length("Module", 0.5, 3)] float Module = 1f,
        [Length("Thickness", 2, 12, Hint = "The face width of every wheel")] float Thickness = 4f,
        [Length("Centre arbor", 1, 10, Group = "Shafts", Hint = "The shaft the cannon pinion turns on")] float Arbor = 3f,
        [Length("Stud", 1, 10, Group = "Shafts", Hint = "The pin the minute wheel turns on")] float Stud = 3f,
        [Clearance("Fit", 0.05, 1, Group = "Shafts", Hint = "Round every shaft and pipe, and in the hands")] float Fit = 0.2f,
        [Wall("Pipe wall", 0.8, 5, Group = "Pipes")] float Wall = 1.2f,
        [Length("Hour pipe", 1.5, 30, Group = "Pipes", Hint = "How far the hour wheel's pipe stands above it")] float HourPipe = 5f,
        [Length("Minute pipe", 1.5, 30, Group = "Pipes", Hint = "How far the cannon pinion's pipe stands above the hour pipe")] float MinutePipe = 4f,
        [Toggle("Hands", Group = "Hands")] bool Hands = true,
        [Length("Minute hand", 8, 150, Group = "Hands", Hint = "From the centre to the tip"), ShowWhen(nameof(Hands), true)] float MinuteHand = 30f,
        [Length("Hour hand", 6, 120, Group = "Hands"), ShowWhen(nameof(Hands), true)] float HourHand = 20f,
        [Length("Hand thickness", 0.6, 4, Group = "Hands"), ShowWhen(nameof(Hands), true)] float HandThickness = 1.2f,
        [Toggle("Stand", Group = "Stand", Hint = "A plate with pins for the arbor and the stud, to turn it by hand")] bool Stand = true);

    protected override IEnumerable<(string Name, Settings Settings)> Shipped =>
    [
        ("Wall clock", Default),
        ("Small, wheels only", Default with { Teeth = MotionTeeth.Small, Hands = false, Stand = false }),
        ("Large, for a big dial", Default with { Teeth = MotionTeeth.Large, Module = 1.25f, Thickness = 5, MinuteHand = 80, HourHand = 55, HandThickness = 1.6f })
    ];

    /// <summary>Cannon pinion, minute wheel, minute pinion, hour wheel.</summary>
    internal static (int Cannon, int Minute, int Pinion, int Hour) Teeth(MotionTeeth set) => set switch
    {
        MotionTeeth.Small => (10, 30, 8, 32),
        MotionTeeth.Large => (20, 60, 16, 64),
        _ => (15, 45, 12, 48)
    };

    /// <summary>The sizes everything else follows from.</summary>
    private readonly record struct Layout(
        float Apart, float Bore, float Pipe, float PipeFlat, float HourBore, float HourOuter, float HourFlat, float StudBore,
        float HourTop, float CannonTop, float Base);

    private static Layout Measure(Settings s)
    {
        var (cannon, minute, _, _) = Teeth(s.Teeth);
        float t = s.Thickness, c = s.Fit;

        float bore = s.Arbor / 2f + c, pipe = bore + s.Wall;
        float hourBore = pipe + c, hourOuter = hourBore + s.Wall;
        float hourTop = 2 * t + Gap + s.HourPipe;

        // Each flat halfway into its pipe's wall: well clear of the bore, and deep enough to drive a hand.
        return new Layout(s.Module * (cannon + minute) / 2f, bore, pipe, bore + s.Wall / 2f, hourBore, hourOuter, hourBore + s.Wall / 2f,
            s.Stud / 2f + c, hourTop, hourTop + s.MinutePipe, s.Stand ? Plate + Gap : 0f);
    }

    private static float Boss(float hole) => hole + 1.5f;

    protected override IEnumerable<string> Check(Settings s, Printer printer)
    {
        var (cannon, _, pinion, _) = Teeth(s.Teeth);
        var l = Measure(s);
        float m = s.Module;

        if (2 * l.Pipe > m * (cannon - 2.5f) - 1)
            yield return "The cannon pinion is too small for its pipe. A larger module, larger wheels, or a thinner arbor.";
        if (2 * l.StudBore + 2 > m * (pinion - 2.5f))
            yield return "The minute pinion is too small for its stud. A larger module, larger wheels, or a thinner stud.";

        if (s.Hands)
        {
            if (s.HourPipe < s.HandThickness + 0.6f) yield return "The hour pipe is too short to carry the hour hand.";
            if (s.MinutePipe < s.HandThickness + 0.6f) yield return "The minute pipe is too short to carry the minute hand clear of the hour hand.";
            if (s.MinuteHand < Boss(l.Pipe + s.Fit) + 2) yield return "The minute hand is shorter than the boss it turns on.";
            if (s.HourHand < Boss(l.HourOuter + s.Fit) + 2) yield return "The hour hand is shorter than the boss it turns on.";
        }
    }

    /// <summary>
    /// A pipe's outline: round, less a flat on its lower side at <paramref name="flat"/> from the
    /// centre, for a hand to be driven by. The hand's hole is the same, grown by the fit.
    /// </summary>
    internal static List<Vector2> Flatted(float radius, float flat)
    {
        float half = MathF.Sqrt(radius * radius - flat * flat);
        float from = MathF.Atan2(-flat, half), to = MathF.PI - from;
        int n = Shapes.Sides(radius);

        var loop = new List<Vector2>(n + 1);
        for (int i = 0; i <= n; i++)
        {
            float a = from + (to - from) * i / n;
            loop.Add(new Vector2(radius * MathF.Cos(a), radius * MathF.Sin(a)));
        }

        return loop;
    }

    /// <summary>A hand pointing at twelve, flat on the plate, with a boss round its hole.</summary>
    private static Mesh Hand(float length, float root, List<Vector2> hole, float boss, float thick)
    {
        root = MathF.Min(root, 2 * boss);
        float tip = MathF.Max(0.8f, root * 0.3f);

        var blade = Shapes.Prism([new(-root / 2, 0), new(root / 2, 0), new(tip / 2, length), new(-tip / 2, length)], 0, thick);
        return Shapes.Subtract(Shapes.Union(blade, Shapes.Cylinder(boss, 0, thick)), Shapes.Prism(hole, -1, thick + 1));
    }

    protected override Generated Build(Settings s, Printer printer, CancellationToken token)
    {
        var (z1, z2, z3, z4) = Teeth(s.Teeth);
        var l = Measure(s);
        float m = s.Module, t = s.Thickness, c = s.Fit;

        // Every wheel made whole and bored after, so a bore and the pipe round it are one cut
        // rather than two circles of different facets meeting.
        var cannon = Shapes.Subtract(
            Shapes.Union(Spur.Gear(m, z1, t, 0), Shapes.Prism(Flatted(l.Pipe, l.PipeFlat), 0, l.CannonTop)),
            Shapes.Cylinder(l.Bore, -1, l.CannonTop + 1));

        token.ThrowIfCancellationRequested();
        var minuteWheel = Shapes.Subtract(
            Shapes.Union(Spur.Gear(m, z2, t, 0, Spur.Meshing(z2)), Shapes.Moved(Spur.Gear(m, z3, t + Gap, 0, Spur.Meshing(z3)), 0, 0, t)),
            Shapes.Cylinder(l.StudBore, -1, 2 * t + Gap + 1));

        token.ThrowIfCancellationRequested();
        var hourWheel = Shapes.Subtract(
            Shapes.Union(Spur.Gear(m, z4, t, 0), Shapes.Prism(Flatted(l.HourOuter, l.HourFlat), 0, t + s.HourPipe)),
            Shapes.Cylinder(l.HourBore, -1, t + s.HourPipe + 1));

        float lift = l.Base;
        var parts = new List<(string, string, Mesh, Matrix4x4?)>
        {
            ("Cannon pinion", "cannon pinion", cannon, Matrix4x4.CreateTranslation(0, 0, lift)),
            ("Minute wheel", "minute wheel", minuteWheel, Matrix4x4.CreateTranslation(l.Apart, 0, lift)),
            ("Hour wheel", "hour wheel", hourWheel, Matrix4x4.CreateTranslation(0, 0, lift + t + Gap))
        };

        if (s.Hands)
        {
            float ht = s.HandThickness;
            var minuteHole = Flatted(l.Pipe + c, l.PipeFlat + c);
            var hourHole = Flatted(l.HourOuter + c, l.HourFlat + c);

            parts.Add(("Minute hand", "minute hand",
                Hand(s.MinuteHand, MathF.Max(1.8f, 0.1f * s.MinuteHand), minuteHole, Boss(l.Pipe + c), ht),
                Matrix4x4.CreateTranslation(0, 0, lift + l.CannonTop - ht)));
            parts.Add(("Hour hand", "hour hand",
                Hand(s.HourHand, MathF.Max(2.4f, 0.2f * s.HourHand), hourHole, Boss(l.HourOuter + c), ht),
                Matrix4x4.CreateTranslation(0, 0, lift + l.HourTop - ht)));
        }

        if (s.Stand)
        {
            // Pins with the same facets as the bores they stand in, so they sit inside them all round.
            float end = MathF.Max(l.Bore, l.StudBore) + 4f;
            var plate = Shapes.Prism(Shapes.RoundedRect(l.Apart + 2 * end, 2 * end, end, new Vector2(l.Apart / 2f, 0)), 0, Plate);
            var arbor = Shapes.Cylinder(s.Arbor / 2f, Plate - 0.01f, lift + l.CannonTop - 1, sides: Shapes.Sides(l.Bore));
            var stud = Shapes.Cylinder(s.Stud / 2f, Plate - 0.01f, lift + 2 * t + Gap - 0.5f, new Vector2(l.Apart, 0), Shapes.Sides(l.StudBore));
            parts.Add(("Stand", "stand", Shapes.Union(plate, arbor, stud), Matrix4x4.Identity));
        }

        var laid = Shapes.InARow(parts);

        var moving = new List<MovingPart>
        {
            new(0, Geometry.Motion.Joint.Revolute, Vector2.Zero),
            new(1, Geometry.Motion.Joint.Revolute, new Vector2(l.Apart, 0)),
            new(2, Geometry.Motion.Joint.Revolute, Vector2.Zero)
        };
        var layers = new List<float> { lift + t / 2f, lift + t + Gap + t / 2f };

        if (s.Hands)
        {
            moving.Add(new(3, Geometry.Motion.Joint.Revolute, Vector2.Zero));
            moving.Add(new(4, Geometry.Motion.Joint.Revolute, Vector2.Zero));
            layers.Add(lift + l.HourTop - s.HandThickness / 2f);
            layers.Add(lift + l.CannonTop - s.HandThickness / 2f);
        }

        var notes = new List<string>
        {
            $"Wheels {z1}:{z2} and {z3}:{z4}, twelve to one; the minute wheel's stud {l.Apart:0.##} mm from the centre.",
            "Turned clockwise from the cannon pinion, as the going train turns it. The cannon pinion should grip its arbor just enough to be turned by hand to set the time."
        };
        if (s.Hands) notes.Add("The hands sit on a flat on each pipe; press them on, pointing at twelve together.");
        if (s.Stand) notes.Add("The stand's pins print standing up; a length of rod through the plate is stronger.");

        return new Generated(laid, notes)
        {
            // Clockwise, a turn of the minute hand: the hour hand goes a twelfth of the way.
            Motion = new Mechanism(moving, 0, layers, Turns: -1)
        };
    }

    protected override IEnumerable<string> Describe(Settings s, float modelScale)
    {
        var (z1, z2, _, z4) = Teeth(s.Teeth);
        yield return $"The minute wheel {s.Module * (z2 + 2):0.#} mm across, the hour wheel {s.Module * (z4 + 2):0.#} mm; centres {s.Module * (z1 + z2) / 2f:0.#} mm apart.";
    }
}
