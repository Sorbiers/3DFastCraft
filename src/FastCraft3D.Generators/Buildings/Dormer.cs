using System.Numerics;
using FastCraft3D.Geometry;

namespace FastCraft3D.Generators.Buildings;

/// <summary>
/// A gabled dormer to stand on a roof slope: a front wall with a window, cheeks that run back
/// into the roof, and a little gable roof of its own - its underside cut to the pitch of the roof
/// it sits on, so it goes down flat on the slope and is Merged with it. Glass as a part of its
/// own, as the window's is.
/// </summary>
public sealed class Dormer : Generator<Dormer.Settings>
{
    public override string Id => "building.dormer";
    public override int Version => 1;
    public override string Category => "Buildings";
    public override string Title => "Dormer";
    public override string Summary => "A gabled dormer with a window, its underside cut to the roof's pitch, to stand on a roof and Merge.";

    public sealed record Settings(
        [Length("Width", 4, 100, Group = "Size", Hint = "Across the front")] float Width = 14f,
        [Length("Front height", 3, 100, Group = "Size", Hint = "From the roof up to its eaves")] float Height = 12f,
        [Angle("Roof pitch", 15, 70, Group = "Size", Hint = "The pitch of the roof it stands on: the same as the Roof's")] float Pitch = 40f,
        [Angle("Its own pitch", 20, 70, Group = "Its roof")] float OwnPitch = 45f,
        [Length("Overhang", 0, 5, Group = "Its roof")] float Overhang = 1f,
        [Length("Frame", 0.4, 10, Group = "Window", Hint = "Round the window, seen from the front")] float Frame = 1f,
        [Count("Panes across", 1, 6, Group = "Window")] int Columns = 2,
        [Count("Panes up", 1, 6, Group = "Window")] int Rows = 2,
        [Length("Glazing bars", 0.2, 3, Group = "Window")] float Bar = 0.4f,
        [Length("Window recess", 0.4, 5, Group = "Window", Hint = "How far the glass sits back from the front")] float Recess = 1.2f,
        [Toggle("Glass", Group = "Window")] bool Glass = true,
        [Length("Glass thickness", 0.1, 2, Group = "Window"), ShowWhen(nameof(Glass), true)] float GlassThickness = 0.4f);

    protected override IEnumerable<(string Name, Settings Settings)> Shipped =>
    [
        ("Two by two", Default),
        ("Narrow, one by two", Default with { Width = 9, Columns = 1, Rows = 2 }),
        ("Wide, three panes", Default with { Width = 20, Columns = 3, Rows = 1 })
    ];

    private static List<(float X0, float Z0, float X1, float Z1)> Panes(Settings s)
    {
        // The sill clear of the slope behind it, where the recess reaches back into the roof.
        float left = -s.Width / 2f + s.Frame, bottom = MathF.Max(s.Frame, s.Recess * MathF.Tan(s.Pitch * MathF.PI / 180f) + 0.6f);
        float wide = (s.Width - 2 * s.Frame - (s.Columns - 1) * s.Bar) / s.Columns;
        float tall = (s.Height - bottom - s.Frame - (s.Rows - 1) * s.Bar) / s.Rows;
        var panes = new List<(float, float, float, float)>();
        for (int c = 0; c < s.Columns; c++)
            for (int r = 0; r < s.Rows; r++)
            {
                float x = left + c * (wide + s.Bar), z = bottom + r * (tall + s.Bar);
                panes.Add((x, z, x + wide, z + tall));
            }

        return panes;
    }

    protected override IEnumerable<string> Check(Settings s, Printer printer)
    {
        var pane = Panes(s)[0];
        if (pane.X1 - pane.X0 < 0.5f || pane.Z1 - pane.Z0 < 0.5f)
            yield return "The frame and bars leave panes too small to print. Fewer panes, thinner bars, or a bigger dormer.";
        if (s.Glass && s.GlassThickness >= s.Recess) yield return "The glass is thicker than the window is recessed.";
        if (s.Recess > s.Height / MathF.Tan(s.Pitch * MathF.PI / 180f) - 0.5f)
            yield return "The window is recessed deeper than the dormer reaches back into the roof.";
    }

    protected override Generated Build(Settings s, Printer printer, CancellationToken token)
    {
        float w = s.Width / 2f, h = s.Height, o = s.Overhang;
        float slope = MathF.Tan(s.Pitch * MathF.PI / 180f), own = MathF.Tan(s.OwnPitch * MathF.PI / 180f);
        float gable = (w + o) * own, back = (h + gable) / slope + 1f;

        // Across, front to back, up: outlines drawn side on or end on and run through.
        var sideOn = new Matrix4x4(0, 1, 0, 0, 0, 0, 1, 0, 1, 0, 0, 0, 0, 0, 0, 1);
        var endOn = new Matrix4x4(1, 0, 0, 0, 0, 0, 1, 0, 0, 1, 0, 0, 0, 0, 0, 1);

        // The body: from the front back to where the slope comes up to the eaves.
        var body = MeshTransform.Transformed(Shapes.Prism([new(0, 0), new(h / slope, h), new(0, h)], -w, w), sideOn);

        // Its roof, a gable from over the front back into the main roof.
        var roof = MeshTransform.Transformed(Shapes.Prism([new(-w - o, h - 0.01f), new(w + o, h - 0.01f), new(0, h + gable)], -o, back), endOn);

        // All of it kept above the main roof's slope, so its underside is the slope.
        float c = MathF.Cos(MathF.Atan(slope)), sn = MathF.Sin(MathF.Atan(slope)), reach = back + h + gable + 10f;
        var above = MeshTransform.Transformed(Shapes.Box(-reach, -reach, 0, reach, reach, reach),
            new Matrix4x4(1, 0, 0, 0, 0, c, sn, 0, 0, -sn, c, 0, 0, 0, 0, 1));

        token.ThrowIfCancellationRequested();
        var dormer = Shapes.Intersect(Shapes.Union(body, roof), above);

        var panes = Panes(s);
        dormer = Shapes.Subtract(dormer, panes.Select(p => Shapes.Box(p.X0, -1, p.Z0, p.X1, s.Recess, p.Z1)).ToList());

        var parts = new List<GeneratedPart> { new("Dormer", dormer, Role: "dormer") };
        var notes = new List<string>();
        if (s.Glass)
        {
            parts.Add(new GeneratedPart("Glass", Mesh.Combine(panes.Select(p => Shapes.Box(p.X0, s.Recess - s.GlassThickness, p.Z0, p.X1, s.Recess, p.Z1))), Role: "glass")
            {
                Filament = Glazing.Filament,
                Colour = Glazing.Colour
            });
            notes.AddRange(Glazing.Notes(s.GlassThickness, printer));
        }

        notes.Add($"Its underside slopes at {s.Pitch:0} degrees: stand it on a roof of that pitch, the front towards the eaves, and Merge.");
        return new Generated(parts, notes);
    }

    protected override IEnumerable<string> Describe(Settings s, float modelScale)
    {
        if (Glazing.Real(s.Width, s.Height, modelScale, "dormer front") is { } real) yield return real;
    }
}
