using System.Numerics;
using FastCraft3D.Geometry;
using FastCraft3D.Geometry.Motion;

namespace FastCraft3D.Generators.Mechanisms;

/// <summary>
/// A gear, a ring, a rack, a bevel, a worm or a ratchet, with a partner in mesh with it or a
/// frame for a cut-away gear to drive, built by <see cref="Gears"/>.
///
/// The settings are <see cref="GearOptions"/> laid flat: a tick box and a count where the options
/// have a count that is nought for "none", and the mate's own shaft as settings of its own. A row
/// that means nothing for the kind in hand is taken away rather than greyed - six kinds share the
/// panel and no one of them uses half its rows - and <see cref="Shows"/> is where that is decided,
/// rule for rule as the gear's own panel did before it moved here.
/// </summary>
public sealed class Gear : Generator<Gear.Settings>
{
    public override string Id => "mechanism.gear";
    public override int Version => 1;
    public override string Category => "Mechanisms";
    public override string Title => "Gear";
    public override string Summary => "Involute teeth, cut as a gear cutter would cut them, with a partner made already in mesh.";
    public override bool IsBeta => false;

    public sealed record Settings(
        [Choice("Kind")] GearKind Kind = GearKind.Gear,
        [Choice("Teeth form", Hint = "Helical teeth wind round as they rise: quieter and smoother. Herringbone goes one way to the middle and back, with no sideways push.")]
        ToothForm Form = ToothForm.Straight,
        [Length("Module", 0.2, 10, Hint = "The size of a tooth. Gears only mesh with gears of the same module. The pitch diameter is the module times the teeth; 1 to 2 prints well on a 0.4 mm nozzle.")]
        float Module = 1.5f,
        [Count("Teeth", 1, 400, Hint = "On a rack, how many teeth long it is")] int Teeth = 20,
        [Length("Thickness", 0.5, 200, Hint = "The width of the teeth, along Z. For a worm, its length.")] float Thickness = 8f,
        [Angle("Pressure angle", 10, 30, Hint = "20 degrees is the standard almost everywhere. Two gears must share it to mesh.")] float PressureAngle = 20f,
        [Angle("Helix angle", 0, 45, Hint = "How far the teeth lean. The partner made here leans the other way, as it has to.")] float HelixAngle = 20f,
        [Length("Backlash", 0, 2, Hint = "Play between the teeth, along the pitch circle, shared between the two gears")] float Backlash = 0.15f,
        [Length("Rim", 0.5, 50, Hint = "Round a ring gear's teeth; under a rack's; round a frame")] float Rim = 3f,
        [Length("Chamfer", 0, 5, Hint = "Taken off the tips' edges top and bottom, so the first layer does not flare into the next tooth")] float Chamfer = 0f,

        [Toggle("Cut away", Group = "Cut away", Hint = "Teeth over only part of the rim: a sector that drives something back and forth")] bool Partial = false,
        [Count("Kept teeth", 1, 400, UnitText = "teeth", Group = "Cut away")] int KeptTeeth = 5,
        [Toggle("Frame", Group = "Cut away", Hint = "A frame round the cut-away gear that it drives back and forth")] bool Frame = false,
        [Choice("Frame ends", Group = "Cut away")] FrameEnds FrameEnds = FrameEnds.Round,
        [Length("Frame clearance", 0.1, 1, Group = "Cut away", Hint = "The gap all round between the gear and the frame")] float FrameClearance = 0.25f,
        [Length("Lock height", 0, 30, Group = "Cut away", Hint = "How tall the lock over the frame stands. Nought for none.")] float LockHeight = 3f,

        [Angle("Cone angle", 5, 85, Group = "Bevel", Hint = "Half the cone's angle. With a mate asked for, it comes from the two tooth counts instead.")] float ConeAngle = 45f,
        [Length("Worm diameter", 0, 200, Group = "Worm", Hint = "Over the worm's crests. Nought chooses one from the module.")] float WormDiameter = 0f,
        [Length("Undercut", 0, 20, Group = "Ratchet", Hint = "How far each tooth's face leans back, so the pawl is pulled in rather than pushed out")] float Undercut = 4f,
        [Toggle("Pawl", Group = "Ratchet", Hint = "A pawl to hold it, made beside it")] bool WithPawl = false,

        [Toggle("Mate", Group = "Mate", Hint = "A partner made already in mesh with it: a gear, the gear inside a ring or on a rack, a bevel's mate, a worm's wheel")] bool HasPartner = false,
        [Count("Mate teeth", 6, 400, UnitText = "teeth", Group = "Mate")] int PartnerTeeth = 40,
        [Length("Mate thickness", 0, 200, Group = "Mate", Hint = "Nought for the gear's own - or, for a worm's wheel, the width the worm asks for")] float MateThickness = 0f,
        [Choice("Mate bore", Group = "Mate")] BoreShape MateBore = BoreShape.Round,
        [Length("Mate bore size", 0.5, 100, Group = "Mate", Hint = "Across the flats of a hexagon; the diameter of anything else")] float MateBoreSize = 5f,
        [Length("Mate flat", 0.1, 100, Group = "Mate", Hint = "For a D-shaft, from the flat to the far side")] float MateBoreFlat = 4.5f,
        [Length("Mate hub", 0, 300, Group = "Mate", Hint = "The hub's diameter. Nought for none.")] float MateHubDiameter = 0f,
        [Length("Mate hub height", 0, 100, Group = "Mate")] float MateHubHeight = 0f,
        [Length("Mate set screw", 0, 20, Group = "Mate", Hint = "The set screw's hole through the hub. Nought for none.")] float MateSetScrew = 0f,

        [Choice("Bore", Group = "Shaft")] BoreShape Bore = BoreShape.Round,
        [Length("Bore size", 0.5, 100, Group = "Shaft", Hint = "Across the flats of a hexagon; the diameter of anything else")] float BoreSize = 5f,
        [Length("Flat", 0.1, 100, Group = "Shaft", Hint = "For a D-shaft, from the flat to the far side - the dimension a supplier quotes")] float BoreFlat = 4.5f,
        [Length("Hub", 0, 300, Group = "Shaft", Hint = "The hub's diameter. Nought for none.")] float HubDiameter = 0f,
        [Length("Hub height", 0, 100, Group = "Shaft")] float HubHeight = 0f,
        [Length("Set screw", 0, 20, Group = "Shaft", Hint = "The set screw's hole through the hub. Nought for none.")] float SetScrew = 0f);

    protected override IEnumerable<(string Name, Settings Settings)> Shipped =>
    [
        ("Pair, 2:1", Default with { Teeth = 15, HasPartner = true, PartnerTeeth = 30 }),
        ("Rack and pinion", Default with { Kind = GearKind.Rack, Teeth = 30, HasPartner = true, PartnerTeeth = 16 }),
        ("Bevel pair", Default with { Kind = GearKind.Bevel, HasPartner = true, PartnerTeeth = 20 }),
        ("Worm and wheel", Default with { Kind = GearKind.Worm, HasPartner = true, PartnerTeeth = 30 }),
        ("Reciprocating frame", Default with { Teeth = 24, Partial = true, KeptTeeth = 6, Frame = true })
    ];

    protected override bool Shows(Settings s, string parameter)
    {
        bool cut = s.Kind is GearKind.Gear or GearKind.Ring or GearKind.Rack;
        bool cutAway = s.Kind == GearKind.Gear && s.Partial;
        bool framed = cutAway && s.Frame;
        bool partnered = s.HasPartner && s.Kind != GearKind.Ratchet && !framed;
        bool shaft = s.Kind is GearKind.Gear or GearKind.Bevel or GearKind.Ratchet or GearKind.Worm || partnered;
        bool hubbed = shaft && s.Kind != GearKind.Bevel;

        // A frame is a mate too, but one with no shaft in it.
        bool mate = framed || partnered;
        bool mateShaft = mate && !framed && s.Kind != GearKind.Bevel;

        return parameter switch
        {
            nameof(Settings.Teeth) => s.Kind != GearKind.Worm,
            nameof(Settings.Form) or nameof(Settings.Chamfer) => cut,
            nameof(Settings.HelixAngle) => cut && s.Form != ToothForm.Straight,
            nameof(Settings.PressureAngle) or nameof(Settings.Backlash) => s.Kind != GearKind.Ratchet,
            nameof(Settings.Rim) => s.Kind is GearKind.Ring or GearKind.Rack || framed,
            nameof(Settings.Partial) => s.Kind == GearKind.Gear,
            nameof(Settings.KeptTeeth) or nameof(Settings.Frame) => cutAway,
            nameof(Settings.FrameEnds) or nameof(Settings.FrameClearance) or nameof(Settings.LockHeight) => framed,
            nameof(Settings.ConeAngle) => s.Kind == GearKind.Bevel && !partnered,
            nameof(Settings.WormDiameter) => s.Kind == GearKind.Worm,
            nameof(Settings.Undercut) or nameof(Settings.WithPawl) => s.Kind == GearKind.Ratchet,
            nameof(Settings.HasPartner) => s.Kind != GearKind.Ratchet && !framed,
            nameof(Settings.PartnerTeeth) => partnered,
            nameof(Settings.MateThickness) => mate,
            nameof(Settings.MateBore) or nameof(Settings.MateBoreSize) => mate && !framed,
            nameof(Settings.MateBoreFlat) => mate && !framed && s.MateBore == BoreShape.DShaft,
            nameof(Settings.MateHubDiameter) or nameof(Settings.MateHubHeight) => mateShaft,
            nameof(Settings.MateSetScrew) => mateShaft && s.MateHubDiameter > 0 && s.MateHubHeight > 0,
            nameof(Settings.Bore) => shaft,
            nameof(Settings.BoreSize) => shaft && s.Bore != BoreShape.None,
            nameof(Settings.BoreFlat) => shaft && s.Bore == BoreShape.DShaft,
            nameof(Settings.HubDiameter) or nameof(Settings.HubHeight) => hubbed,
            nameof(Settings.SetScrew) => hubbed && s.HubDiameter > 0 && s.HubHeight > 0,
            _ => true
        };
    }

    /// <summary>
    /// A mate starts with the gear's own shaft, so a pair that wants two of a kind needs nothing
    /// typed and a pair that does not has somewhere to type it.
    /// </summary>
    protected override Settings Adjust(Settings before, Settings after, string changed) =>
        changed == nameof(Settings.HasPartner) && after.HasPartner && !before.HasPartner
            ? after with
            {
                MateBore = after.Bore,
                MateBoreSize = after.BoreSize,
                MateBoreFlat = after.BoreFlat,
                MateHubDiameter = after.HubDiameter,
                MateHubHeight = after.HubHeight,
                MateSetScrew = after.SetScrew
            }
            : after;

    /// <summary>
    /// Past this, a gear is bigger than any printer's plate several times over and takes minutes
    /// to build. Made to a scale, it is better made small and scaled up afterwards.
    /// </summary>
    public const float LargestAcross = 600f;

    protected override IEnumerable<string> Check(Settings s, Printer printer)
    {
        // A rack's teeth run along it, so its length is the pitch's worth of each.
        float across = s.Kind == GearKind.Rack ? MathF.PI * s.Module * s.Teeth : s.Module * s.Teeth;
        if (across > LargestAcross)
            yield return $"That is {across:0} mm {(s.Kind == GearKind.Rack ? "long" : "across")}, bigger than any printer takes. "
                         + "Made to a scale, make it small and scale it up afterwards.";

        if (s.HasPartner && s.Module * s.PartnerTeeth > LargestAcross)
            yield return $"The mate would be {s.Module * s.PartnerTeeth:0} mm across, bigger than any printer takes.";
    }

    protected override Generated Build(Settings s, Printer printer, CancellationToken token)
    {
        var asked = Options(s);
        var gear = asked.Sane();

        token.ThrowIfCancellationRequested();
        var result = Gears.Build(gear);

        var notes = Gears.Describe(gear);
        if (asked.Teeth != gear.Teeth) notes.Add($"Teeth are held to {gear.Teeth}.");
        if (asked.Chamfer > gear.Chamfer + 1e-3f) notes.Add($"The chamfer is held to {gear.Chamfer:0.##} mm.");
        notes.AddRange(result.Notes);

        if (result.Parts.Count == 0) return Generated.Refused(result.Refusal ?? "No gear was made.");

        if (gear.Kind == GearKind.Gear && gear.KeptTeeth > 0 && gear.Frame && Turned(gear) is { } jam)
            return Generated.Refused(jam);

        var parts = result.Parts.Select((part, i) => new GeneratedPart(
            part.Name, part.Mesh, part.InMesh, part.Anchors,
            Role: i switch { 0 => "gear", 1 => "mate", _ => $"part {i + 1}" },
            Pivot: PivotOf(part))).ToList();

        return new Generated(parts, notes) { Motion = MotionOf(gear, parts) };
    }

    /// <summary>
    /// How the set turns, for the ones that do in the flat: a pair, a gear in a ring, a pinion on a
    /// rack, a cut-away gear in its frame. A bevel and a worm turn out of the plane, and a
    /// ratchet's pawl is held by nothing but friction here, so those have none.
    /// </summary>
    private static Mechanism? MotionOf(GearOptions gear, IReadOnlyList<GeneratedPart> parts)
    {
        if (parts.Count != 2) return null;

        float layer = MathF.Min(gear.Thickness, gear.MateThickness ?? gear.Thickness) / 2f;
        MovingPart Turning(int i, double reach = 0.3) => new(i, Joint.Revolute, new Vector2(parts[i].Pivot.X, parts[i].Pivot.Y), Reach: reach);
        MovingPart Sliding(int i) => new(i, Joint.Prismatic, default, Vector2.UnitX, Reach: 3);

        return gear.Kind switch
        {
            GearKind.Gear when gear.KeptTeeth > 0 && gear.Frame => new Mechanism([Turning(0), Sliding(1)], 0,
                gear.LockHeight > 0 ? [gear.Thickness / 2f, gear.Thickness + gear.LockHeight / 2f] : [gear.Thickness / 2f], Reach: 3),
            GearKind.Gear => new Mechanism([Turning(0), Turning(1)], 0, [layer]),
            GearKind.Ring => new Mechanism([Turning(0), Turning(1)], 1, [layer]),
            GearKind.Rack => new Mechanism([Sliding(0), Turning(1)], 1, [layer]),
            _ => null
        };
    }

    /// <summary>
    /// A reciprocating frame, turned a whole turn each way by the motion check: the gear turning,
    /// the frame sliding only when pushed. The frame's first version passed every static test and
    /// jammed both ways round the moment it was turned; this is the check that caught it, run on
    /// every frame made rather than on the one in the tests. Null when it runs clear.
    /// </summary>
    private static string? Turned(GearOptions gear)
    {
        if (Gears.PlanFrame(gear, out _) is not { } plan) return null;

        // Thinned: the outlines are sampled finely enough to cut teeth from, and every step of the
        // run tries every edge. Two thousand points to a loop took the frame seconds to check.
        static Vector2[] Thin(IReadOnlyList<Vector2> loop) => Sections.Thinned(loop.ToArray(), 0.02f);

        var turning = new List<PlanarLoop> { new(0, Thin(plan.Gear)) };
        var sliding = new List<PlanarLoop> { new(0, Thin(plan.Cavity)) };
        if (plan.Lock is { } disc && plan.LockCavity is { } rails)
        {
            turning.Add(new(1, Thin(disc)));
            sliding.Add(new(1, Thin(rails)));
        }

        var bodies = new[] { PlanarBody.Turning("the gear", Vector2.Zero, [.. turning]), PlanarBody.Sliding("the frame", Vector2.UnitX, [.. sliding]) };

        // In the frame's own coordinates the gear's centre is at minus the slide; here the gear
        // stays put and the frame moves, so the frame stands at plus the slide.
        foreach (int way in new[] { +1, -1 })
        {
            var run = PlanarMotion.Drive(bodies, 0, way, [plan.Shown, plan.Slide(plan.Shown)], stepDegrees: 1.0, reach: 2.0);
            if (run.Jammed)
                return $"The frame jams {Math.Abs(run.JammedAt!.Value):0} degrees into a turn {(way > 0 ? "anticlockwise" : "clockwise")}. Try another number of kept teeth.";
        }

        return null;
    }

    /// <summary>
    /// A gear turns about its shaft, not about the middle of its outline - a cut-away gear is a
    /// disc with teeth over a quarter of its rim, and the middle of its box is well off its axis.
    /// At the plate, so a thicker gear grows up from where it stands. Anything with no shaft, a
    /// rack or a frame, about the middle of its footprint.
    /// </summary>
    private static Vector3 PivotOf(GearPart part)
    {
        var bounds = part.Mesh.ComputeBounds();
        var axis = part.Anchors?.FirstOrDefault(a => a.Kind == AnchorKind.Bore) ?? default;

        return axis.Size > 0
            ? new Vector3(axis.At.X, axis.At.Y, bounds.Min.Z)
            : new Vector3(bounds.Center.X, bounds.Center.Y, bounds.Min.Z);
    }

    /// <summary>The options these settings ask for, before they are held to what can be made.</summary>
    public static GearOptions Options(Settings s)
    {
        bool framed = s.Kind == GearKind.Gear && s.Partial && s.Frame;
        bool partnered = s.HasPartner && s.Kind != GearKind.Ratchet && !framed;

        return new GearOptions
        {
            Kind = s.Kind,
            Form = s.Form,
            Module = s.Module,
            Teeth = s.Teeth,
            Thickness = s.Thickness,
            PressureAngle = s.PressureAngle,
            HelixAngle = s.HelixAngle,
            Backlash = s.Backlash,
            Rim = s.Rim,
            Bore = s.Bore,
            BoreSize = s.BoreSize,
            BoreFlat = s.BoreFlat,
            HubDiameter = s.HubDiameter,
            HubHeight = s.HubHeight,
            SetScrew = s.SetScrew,
            Chamfer = s.Chamfer,
            PartnerTeeth = partnered ? s.PartnerTeeth : 0,
            KeptTeeth = s.Kind == GearKind.Gear && s.Partial ? s.KeptTeeth : 0,
            Frame = s.Frame,
            FrameEnds = s.FrameEnds,
            FrameClearance = s.FrameClearance,
            LockHeight = s.LockHeight,
            ConeAngle = s.ConeAngle,
            WormDiameter = s.WormDiameter,
            Undercut = s.Undercut,
            WithPawl = s.WithPawl,
            MateThickness = s.MateThickness > 0 ? s.MateThickness : null,
            MateBore = s.MateBore,
            MateBoreSize = s.MateBoreSize,
            MateBoreFlat = s.MateBoreFlat,
            MateHubDiameter = s.MateHubDiameter,
            MateHubHeight = s.MateHubHeight,
            MateSetScrew = s.MateSetScrew
        };
    }
}
