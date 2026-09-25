using FastCraft3D.Geometry;

namespace FastCraft3D.Generators.Boxes;

/// <summary>
/// A round jar with a thread on its neck and a lid that screws onto it, on the thread code the
/// Thread generator uses. Printed side by side: two threads made separately start wherever their
/// helices start, so shown screwed together they would run into each other where no real pair does.
/// </summary>
public sealed class ScrewJar : Generator<ScrewJar.Settings>
{
    public override string Id => "box.jar";
    public override int Version => 1;
    public override string Category => "Boxes";
    public override string Title => "Screw-top jar";
    public override string Summary => "A round jar and a lid that screws onto its neck.";

    public sealed record Settings(
        [Length("Diameter", 30, 140, Hint = "Outside, the jar and the lid alike")] float Diameter = 60f,
        [Length("Height", 20, 150, Hint = "The jar, with its neck, without the lid")] float Height = 60f,
        [Wall("Wall", 1.2, 5)] float Wall = 2f,
        [Length("Floor", 1, 5, Hint = "The jar's floor, and the lid's top")] float Floor = 2f,
        [Length("Pitch", 1.5, 5, Group = "Thread", Hint = "From one crest to the next. Coarse prints well: 3 mm is a good start.")] float Pitch = 3f,
        [Length("Thread length", 4, 20, Group = "Thread")] float ThreadLength = 9f,
        [Length("Clearance", 0.1, 1, Group = "Thread", Hint = "On the diameter, between the neck's thread and the lid's")] float Clearance = 0.5f);

    protected override IEnumerable<(string Name, Settings Settings)> Shipped =>
    [
        ("Spice jar", Default with { Diameter = 45, Height = 70 }),
        ("Stash jar", Default with { Diameter = 90, Height = 60, ThreadLength = 10 })
    ];

    private static float ThreadDiameter(Settings s) => s.Diameter - 2 * s.Wall;

    private static float Cavity(Settings s) => ThreadDiameter(s) / 2f - Threads.DepthPerPitch * s.Pitch - s.Wall;

    protected override IEnumerable<string> Check(Settings s, Printer printer)
    {
        if (Cavity(s) < 5)
            yield return "The walls and the thread leave no room inside. A bigger jar, a thinner wall or a finer pitch.";

        if (s.Height < s.ThreadLength + s.Floor + 5)
            yield return "The jar is too short for its thread.";
    }

    protected override Generated Build(Settings s, Printer printer, CancellationToken token)
    {
        float neck = ThreadDiameter(s);
        float body = s.Height - s.ThreadLength;

        var rod = Threads.Build(new ThreadOptions(ThreadKind.Rod, neck, s.Pitch, s.ThreadLength + 0.5f, s.Clearance, NutBody.Hexagon, neck + 4, 5));
        var rodBounds = rod.ComputeBounds();
        rod = Shapes.Moved(rod, 0, 0, body - 0.5f - rodBounds.Min.Z);

        token.ThrowIfCancellationRequested();
        var jar = Shapes.Subtract(
            Shapes.Union(Shapes.Cylinder(s.Diameter / 2f, 0, body), rod),
            Shapes.Cylinder(Cavity(s), s.Floor, s.Height + 1));

        // The lid, top down on the plate, its thread cut up into it from the open end.
        float lidHeight = s.Floor + s.ThreadLength + 1;
        var cutter = Threads.Build(new ThreadOptions(ThreadKind.HoleCutter, neck, s.Pitch, s.ThreadLength + 2, s.Clearance, NutBody.Hexagon, neck + 4, 5));
        var cutterBounds = cutter.ComputeBounds();
        cutter = Shapes.Moved(cutter, 0, 0, s.Floor - cutterBounds.Min.Z);

        token.ThrowIfCancellationRequested();
        var lid = Shapes.Subtract(Shapes.Cylinder(s.Diameter / 2f, 0, lidHeight), cutter);

        var parts = Shapes.InARow([("Jar", "jar", jar, null), ("Lid", "lid", lid, null)]);
        return new Generated(parts, [$"Holds about {MathF.PI * Cavity(s) * Cavity(s) * (s.Height - s.Floor) / 1000f:0} ml."]);
    }
}
