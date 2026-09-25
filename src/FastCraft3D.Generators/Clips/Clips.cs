using System.Numerics;
using FastCraft3D.Geometry;

namespace FastCraft3D.Generators.Clips;

public enum Material
{
    PLA,
    PETG,
    ABS,
    [ShownAs("Nylon")] Nylon
}

/// <summary>
/// A cantilever snap-fit: a beam with a hook on its end, and the catch it clips behind. The hook
/// is sized from the strain the material takes - a beam bent further than that breaks the first
/// time, or cracks by the tenth - using the usual cantilever sum: the deflection a beam of length
/// L and thickness h takes at strain e is two thirds of e L squared over h.
///
/// Printed on its side, so the beam's layers run along it and it bends across them, never
/// between them.
/// </summary>
public sealed class SnapFit : Generator<SnapFit.Settings>
{
    public override string Id => "clip.snap-fit";
    public override int Version => 1;
    public override string Category => "Clips";
    public override string Title => "Snap-fit";
    public override string Summary => "A cantilever hook and its catch, sized so the beam bends without breaking.";

    /// <summary>The strain a printed beam can be bent to more than once, at a guess on the safe side of the datasheets.</summary>
    public static float Strain(Material m) => m switch
    {
        Material.PLA => 0.018f,
        Material.PETG => 0.025f,
        Material.ABS => 0.025f,
        _ => 0.04f
    };

    public sealed record Settings(
        [Choice("Material")] Material Material = Material.PETG,
        [Length("Beam length", 5, 60)] float Length = 18f,
        [Length("Beam thickness", 0.8, 5)] float Thickness = 1.8f,
        [Length("Width", 3, 30, Hint = "Across the beam, as it lies printed")] float Width = 8f,
        [Length("Hook depth", 0.3, 5, Hint = "How far the hook stands out: how much it holds by")] float Hook = 1.2f,
        [Length("Hook length", 1, 10, Hint = "Along the beam: the ramp's run")] float HookLength = 3f,
        [Clearance("Fit", 0.05, 1)] float Fit = 0.2f);

    /// <summary>The most the beam's end can be bent without going past the material's strain.</summary>
    public static float Allowed(Settings s) => 0.67f * Strain(s.Material) * s.Length * s.Length / s.Thickness;

    protected override IEnumerable<string> Check(Settings s, Printer printer)
    {
        float allowed = Allowed(s);
        if (s.Hook > allowed)
            yield return $"A {s.Hook:0.##} mm hook bends this beam past what {s.Material} takes: at most {allowed:0.##} mm. "
                         + "A longer or thinner beam, or a shallower hook.";

        if (s.HookLength > s.Length / 2f) yield return "The hook is longer than half the beam.";
    }

    protected override Generated Build(Settings s, Printer printer, CancellationToken token)
    {
        float L = s.Length, h = s.Thickness, y = s.Hook, run = s.HookLength;
        float baseHeight = MathF.Max(h + y + 2f, 6f);

        // In the plane: the beam along X from its base, the hook standing up in Y at its end,
        // its square face towards the base and its ramp towards the tip.
        var hook = Shapes.Prism(
        [
            new(-6f, 0), new(L, 0), new(L, h), new(L - run, h + y), new(L - run, h), new(0, h), new(0, baseHeight), new(-6f, baseHeight)
        ], 0, s.Width);

        // The catch sits in front of the hook's square face, clear of the beam.
        float gap = s.Fit;
        var catcher = Shapes.Box(L - run - 4f, h + gap, 0, L - run - gap, h + y + 3f, s.Width);

        const float apart = 12f;
        var parts = new List<GeneratedPart>
        {
            new("Snap hook", hook, Role: "hook"),
            new("Snap catch", Shapes.Moved(catcher, 0, apart, 0), Matrix4x4.CreateTranslation(0, -apart, 0), Role: "catch")
        };

        return new Generated(parts,
            [$"The beam bends {y:0.##} mm of the {Allowed(s):0.##} mm {s.Material} takes. Build the catch into the part it holds."]);
    }
}

public enum ScrewHole
{
    None,
    M3,
    M4
}

/// <summary>A clip for one or more cables, open at the top to snap them in, on a strip with a screw hole if wanted.</summary>
public sealed class CableClip : Generator<CableClip.Settings>
{
    public override string Id => "clip.cable";
    public override int Version => 1;
    public override string Category => "Clips";
    public override string Title => "Cable clip";
    public override string Summary => "Snap-in clips for cables, side by side on a strip, to screw or stick down.";

    public sealed record Settings(
        [Length("Cable", 2, 20, Hint = "The cable's diameter")] float Cable = 6f,
        [Count("Cables", 1, 8)] int Count = 1,
        [Wall("Wall", 1, 4)] float Wall = 1.6f,
        [Length("Width", 4, 30, Hint = "Along the cable")] float Width = 8f,
        [Choice("Screw")] ScrewHole Screw = ScrewHole.M3);

    protected override Generated Build(Settings s, Printer printer, CancellationToken token)
    {
        float r = s.Cable / 2f + 0.15f;
        float outer = r + s.Wall;
        float pitch = 2 * outer + 1f;
        float hole = s.Screw switch { ScrewHole.M3 => 3.4f, ScrewHole.M4 => 4.5f, _ => 0f };
        float tab = hole > 0 ? hole + 6f : 0f;

        float left = -outer - tab, right = (s.Count - 1) * pitch + outer + tab;

        // In the plane: the strip along X at the bottom, the rings standing on it; up along Z by the width.
        var pieces = new List<Mesh> { Shapes.Box(left, 0, 0, right, s.Wall, s.Width) };
        var cuts = new List<Mesh>();

        for (int i = 0; i < s.Count; i++)
        {
            // Sunk a little into the strip, so trimming the underside flat cuts across each ring
            // rather than meeting it on a tangent.
            var centre = new Vector2(i * pitch, outer - 0.3f);
            pieces.Add(Shapes.Cylinder(outer, 0, s.Width, centre));
            cuts.Add(Shapes.Cylinder(r, -1, s.Width + 1, centre));

            // The mouth: narrower than the cable, so it snaps in and stays.
            float mouth = s.Cable * 0.75f / 2f;
            cuts.Add(Shapes.Box(centre.X - mouth, centre.Y, -1, centre.X + mouth, centre.Y + outer + 1, s.Width + 1));
        }

        if (hole > 0)
            foreach (float x in new[] { left + tab / 2f, right - tab / 2f })
                cuts.Add(Shapes.RodAlongY(hole / 2f, -1, s.Wall + 1, x, s.Width / 2f));

        // Nothing of the rings below the strip: the strip is what stands on the wall.
        token.ThrowIfCancellationRequested();
        var clip = Shapes.Subtract(Shapes.Intersect(Shapes.Union(pieces), Shapes.Box(left - 1, 0, -1, right + 1, 1000, s.Width + 1)), cuts);

        return new Generated([new GeneratedPart("Cable clip", clip, Role: "clip")],
            ["Printed standing, as it lies here: the strip's underside goes against the wall."]);
    }
}
