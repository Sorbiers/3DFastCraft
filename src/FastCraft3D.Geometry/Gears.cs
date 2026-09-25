using System.Globalization;
using System.Numerics;
using FastCraft3D.Geometry.Csg;

namespace FastCraft3D.Geometry;

public enum GearKind
{
    /// <summary>Teeth round the outside.</summary>
    Gear,

    /// <summary>Teeth round the inside of a ring - the outer part of a planetary gearbox.</summary>
    Ring,

    /// <summary>Teeth along a straight bar.</summary>
    Rack,

    /// <summary>Teeth on a cone, for a pair that turns a corner.</summary>
    Bevel,

    /// <summary>A screw and the wheel it drives: a large reduction in one step.</summary>
    Worm,

    /// <summary>A sawtooth wheel, free one way and held the other by its pawl.</summary>
    Ratchet
}

public enum ToothForm
{
    Straight,

    /// <summary>Teeth that wind round as they rise: quieter, and more than one tooth in contact at once.</summary>
    Helical,

    /// <summary>Helical one way to half the thickness and back the other: no end thrust, and it cannot slide apart.</summary>
    Herringbone
}

public enum BoreShape
{
    None,
    Round,

    /// <summary>A round hole with one flat side, for a motor shaft.</summary>
    DShaft,

    /// <summary>A hexagon, measured across its flats: a hex shaft, or a nut pressed in.</summary>
    Hex
}

/// <summary>
/// A gear to make, and what goes with it.
///
/// A record class rather than a record struct, so a new one comes with the defaults below instead
/// of a gear with no teeth.
/// </summary>
public sealed record GearOptions
{
    public GearKind Kind { get; init; } = GearKind.Gear;
    public ToothForm Form { get; init; } = ToothForm.Straight;

    /// <summary>The size of a tooth, in millimetres: the pitch diameter is this times the teeth.</summary>
    public float Module { get; init; } = 1.5f;

    /// <summary>Teeth on the gear or ring; on a rack, how many it is long.</summary>
    public int Teeth { get; init; } = 20;

    /// <summary>The face width: how thick the gear is.</summary>
    public float Thickness { get; init; } = 8f;

    public float PressureAngle { get; init; } = 20f;

    /// <summary>How far a helical tooth leans from straight, in degrees.</summary>
    public float HelixAngle { get; init; } = 20f;

    /// <summary>
    /// The play between two meshing gears, along the pitch circle, in millimetres. Each gear's
    /// teeth are thinned by half of it, so any two made here come out with this much between them.
    /// </summary>
    public float Backlash { get; init; } = 0.15f;

    /// <summary>A ring's material outside the roots of its teeth; a rack's below them; a frame's outside its inside.</summary>
    public float Rim { get; init; } = 3f;

    public BoreShape Bore { get; init; } = BoreShape.Round;

    /// <summary>The bore's diameter, or across the flats of a hexagon.</summary>
    public float BoreSize { get; init; } = 5f;

    /// <summary>For a D-shaped bore: from the flat to the far side of the hole.</summary>
    public float BoreFlat { get; init; } = 4.5f;

    /// <summary>A collar standing on top of a gear round its bore. No hub when either is nought.</summary>
    public float HubDiameter { get; init; }

    public float HubHeight { get; init; }

    /// <summary>A hole across the hub for a grub screw, this wide; nought for none.</summary>
    public float SetScrew { get; init; }

    /// <summary>How far the bottom edge of the teeth is drawn in, against the first layer spreading.</summary>
    public float Chamfer { get; init; }

    /// <summary>Teeth on a gear made to mesh with this one, placed in mesh beside it; nought for none.</summary>
    public int PartnerTeeth { get; init; }

    /// <summary>
    /// How many teeth are left on a gear cut away for a reciprocating drive; nought for a whole
    /// one. The rest of the rim comes down to the root circle, so the rack runs clear of it.
    /// </summary>
    public int KeptTeeth { get; init; }

    /// <summary>
    /// Makes the cut-away gear's mate the closed frame it drives back and forth rather than
    /// another gear. See <see cref="Gears.PlanFrame"/>.
    /// </summary>
    public bool Frame { get; init; }

    /// <summary>The frame's outside; its inside is made from the gear's path either way.</summary>
    public FrameEnds FrameEnds { get; init; } = FrameEnds.Round;

    /// <summary>
    /// The gap left all round between a frame and its gear - flanks, tips, ends and lock alike -
    /// for the printer's slop. The gear's own backlash is on top of this.
    /// </summary>
    public float FrameClearance { get; init; } = 0.25f;

    /// <summary>
    /// How tall the lock is that stands on top of a framed gear and holds the frame while it is
    /// parked; nought for none. The frame gets a matching layer of the same height.
    /// </summary>
    public float LockHeight { get; init; } = 3f;

    /// <summary>A bevel's pitch cone angle. A pair whose angles add up to 90 meets at a right angle.</summary>
    public float ConeAngle { get; init; } = 45f;

    /// <summary>A worm's pitch diameter; nought for ten times the module, which is the usual choice.</summary>
    public float WormDiameter { get; init; }

    // --- What the mate is made to, where that is not simply the gear's own -------------

    /// <summary>
    /// The mate's width and shaft, each null for the gear's own. A pair is usually two of a kind
    /// and only the odd one needs saying - but a pinion on a 3 mm arbor often drives a wheel on a
    /// 5 mm one, and a worm's wheel is never as wide as the worm is long.
    /// </summary>
    public float? MateThickness { get; init; }

    public BoreShape? MateBore { get; init; }

    public float? MateBoreSize { get; init; }

    public float? MateBoreFlat { get; init; }

    public float? MateHubDiameter { get; init; }

    public float? MateHubHeight { get; init; }

    public float? MateSetScrew { get; init; }

    /// <summary>The same options with nothing of the mate's own left on them.</summary>
    public GearOptions WithoutMate() => this with
    {
        MateThickness = null, MateBore = null, MateBoreSize = null, MateBoreFlat = null,
        MateHubDiameter = null, MateHubHeight = null, MateSetScrew = null
    };

    /// <summary>
    /// What the mate is built from: its own numbers where it has them, the gear's where it has
    /// not. <paramref name="width"/> is what its width falls back to before the gear's own does,
    /// which is how a worm wheel gets the width the worm asks for rather than the worm's length.
    /// </summary>
    public GearOptions ForMate(float? width = null) => (this with
    {
        Thickness = MateThickness ?? width ?? Thickness,
        Bore = MateBore ?? Bore,
        BoreSize = MateBoreSize ?? BoreSize,
        BoreFlat = MateBoreFlat ?? BoreFlat,
        HubDiameter = MateHubDiameter ?? HubDiameter,
        HubHeight = MateHubHeight ?? HubHeight,
        SetScrew = MateSetScrew ?? SetScrew
    }).WithoutMate().Sane();

    /// <summary>How far a ratchet's catching face leans back from radial, so its pawl cannot ride out.</summary>
    public float Undercut { get; init; } = 4f;

    /// <summary>A pawl to go with a ratchet wheel.</summary>
    public bool WithPawl { get; init; }

    /// <summary>Everything held to what can be built.</summary>
    public GearOptions Sane()
    {
        float module = Clamp(Module, 0.2f, 20f, 1.5f);
        float thickness = Clamp(Thickness, 0.5f, 1000f, 8f);
        int least = Kind switch { GearKind.Rack => 1, GearKind.Ring => 12, GearKind.Ratchet => 4, _ => 6 };

        return this with
        {
            Module = module,
            Teeth = Math.Clamp(Teeth, least, Kind == GearKind.Rack ? 500 : 400),
            Thickness = thickness,
            PressureAngle = Clamp(PressureAngle, 10f, 30f, 20f),
            HelixAngle = Clamp(HelixAngle, 0f, 45f, 20f),
            Backlash = Clamp(Backlash, 0f, module, 0.15f),
            Rim = Clamp(Rim, 0.5f, 500f, 3f),
            BoreSize = Clamp(BoreSize, 0.5f, 1000f, 5f),
            BoreFlat = Clamp(BoreFlat, 0.1f, Clamp(BoreSize, 0.5f, 1000f, 5f), 4.5f),
            HubDiameter = Clamp(HubDiameter, 0f, 2000f, 0f),
            HubHeight = Clamp(HubHeight, 0f, 1000f, 0f),
            SetScrew = Clamp(SetScrew, 0f, 50f, 0f),
            Chamfer = Clamp(Chamfer, 0f, MathF.Min(0.5f * module, thickness / 3f), 0f),
            PartnerTeeth = PartnerTeeth <= 0 ? 0 : Math.Clamp(PartnerTeeth, 6, 400),
            KeptTeeth = KeptTeeth <= 0 ? 0 : Math.Clamp(KeptTeeth, 1, Math.Clamp(Teeth, least, 400)),
            ConeAngle = Clamp(ConeAngle, 5f, 85f, 45f),
            FrameClearance = Clamp(FrameClearance, 0.1f, 1f, 0.25f),
            LockHeight = Clamp(LockHeight, 0f, 50f, 3f),
            WormDiameter = Clamp(WormDiameter, 0f, 500f, 0f),
            Undercut = Clamp(Undercut, 0f, 20f, 4f)
        };

        static float Clamp(float value, float low, float high, float otherwise) =>
            float.IsFinite(value) ? Math.Clamp(value, low, high) : otherwise;
    }
}

/// <param name="Name">What the part is called in the scene.</param>
/// <param name="Mesh">
/// The part where it goes to be printed: flat on the bed, and for most pairs that is also in mesh.
/// </param>
/// <param name="InMesh">
/// Where the part goes when the pair is being looked at rather than printed, or null when the two
/// are the same place. A bevel pair meshes at a right angle, with one of them standing on its edge
/// - which is how it is shown and not how it prints.
/// </param>
/// <param name="Anchors">
/// What this part's own tool knows about it - where its shaft hole is, and the like - so a shaft
/// can be fitted to it afterwards without anyone measuring. See <see cref="Anchor"/>.
/// </param>
public sealed record GearPart(
    string Name, Mesh Mesh, Matrix4x4? InMesh = null, IReadOnlyList<Anchor>? Anchors = null);

/// <param name="Parts">What was made - none when it was refused.</param>
/// <param name="Notes">Things worth knowing about what was made.</param>
/// <param name="Refusal">Why nothing was made.</param>
public sealed record GearResult(IReadOnlyList<GearPart> Parts, IReadOnlyList<string> Notes, string? Refusal = null);

/// <summary>
/// Involute gears, ring gears and racks, built straight to a closed solid.
///
/// The tooth is not drawn from the involute formula but generated the way a gear is cut: by rolling
/// the standard rack along the pitch circle and keeping what it never reaches. Above the base
/// circle that gives the involute anyway; below it, it gives the curved root a real cutter leaves,
/// and on a small gear the undercut that lets a partner's tips pass. A gear drawn with the formula
/// alone has straight roots, and a pinion of a dozen teeth jams against them.
///
/// Nothing is cut with a boolean except a set-screw hole. The outline is a loop of points, the
/// faces are laid between them - the teeth as strips from root to tip, the middle as a band out to
/// the roots - so the solid is closed by construction. Helical teeth are the same outline turned a
/// little for each layer; the bore and any rim stay put, so a shaft still goes straight through.
/// </summary>
public static partial class Gears
{
    /// <summary>Points up each side of a tooth. The flank is a gentle curve, and chords this short are well under a hundredth of a millimetre off it.</summary>
    private const int Levels = 20;

    /// <summary>The narrowest a tooth tip or a root is let become, as a share of the module, before it is cut off flat.</summary>
    private const float Flat = 0.1f;

    /// <summary>The least material left between a bore and the roots, or a hub.</summary>
    public const float LeastWall = 0.8f;

    /// <summary>The most a helical tooth's tip moves between one layer and the next.</summary>
    private const float LayerShift = 0.4f;

    /// <summary>
    /// How much shorter the first and last tooth of a cut-away gear are, as a share of the module.
    /// They are the two that come back into mesh, and a tooth at full height meets a rack's tip
    /// head-on and jams; taking a little off the tip lets it find the space instead.
    /// </summary>
    private const double Relief = 0.15;

    public static GearResult Build(GearOptions options, CancellationToken token = default)
    {
        var o = options.Sane();
        var notes = new List<string>();
        var parts = new List<GearPart>();
        float m = o.Module;

        if (m < 0.8f)
            notes.Add("Module under 0.8 mm is finer than a 0.4 mm nozzle prints well.");

        switch (o.Kind)
        {
            case GearKind.Gear:
            {
                bool framed = o.Frame && o.KeptTeeth > 0 && o.KeptTeeth < o.Teeth;
                string? why = null;
                var plan = framed ? PlanFrame(o, out why) : null;

                // A framed gear is built turned to where it is shown parked, with its lock on top.
                var lockDisc = plan?.Lock?.Select(p => Rotated(p, (float)plan.Shown)).ToList();
                float lockHeight = lockDisc is null ? 0f : o.LockHeight;

                var gear = External(o, o.Teeth, 1, (float)(plan?.Shown ?? 0.0), notes, out string? refusal, token,
                                    partial: true, lockDisc: lockDisc, lockHeight: lockHeight);
                if (gear is null) return new([], notes, refusal);
                parts.Add(new($"Gear {o.Teeth}T", gear, Anchors: Bore(o, gear)));

                if (framed)
                {
                    if (plan is null) notes.Add(why!);
                    else
                    {
                        // Beside the gear to print; round it to be looked at, which is where it
                        // works: the gear turns on the spot and the frame slides past it. Shown
                        // parked at one end rather than halfway along, since halfway is a place it
                        // passes through and never stops at.
                        var (print, inMesh) = FrameSolid(plan, o, lockHeight, m * (o.Teeth + 2) / 2f);
                        parts.Add(new("Frame", print, inMesh));
                        notes.AddRange(plan.Notes);
                    }
                }
                else if (o.PartnerTeeth > 0)
                {
                    // Their tooth lines have to lie together where they touch, and the two turn opposite
                    // ways - so the partner leans the other way.
                    float apart = m * (o.Teeth + o.PartnerTeeth) / 2f;
                    var partner = Partner(o.ForMate(), o.PartnerTeeth, -1, MathF.PI - MathF.PI / o.PartnerTeeth, notes, token);
                    if (partner is not null)
                    {
                        var placed = Moved(partner, new Vector3(apart, 0, 0));
                        parts.Add(new($"Gear {o.PartnerTeeth}T", placed,
                                      Anchors: Bore(o.ForMate(), placed, new Vector2(apart, 0f))));
                    }
                }
                break;
            }

            case GearKind.Ring:
            {
                var ring = Ring(o, 1, notes, out string? refusal);
                if (ring is null) return new([], notes, refusal);
                parts.Add(new($"Ring gear {o.Teeth}T", ring, Anchors: Bore(o, ring)));

                if (o.PartnerTeeth > 0)
                {
                    if (o.PartnerTeeth > o.Teeth - 3)
                    {
                        notes.Add("No pinion: it needs fewer teeth than the ring.");
                    }
                    else
                    {
                        if (o.Teeth - o.PartnerTeeth < 12)
                            notes.Add("A ring wants 12 teeth more than its pinion, or the tips catch.");

                        // Inside a ring both turn the same way, so the pinion leans the same way.
                        float apart = m * (o.Teeth - o.PartnerTeeth) / 2f;
                        var pinion = Partner(o.ForMate(), o.PartnerTeeth, 1, 0f, notes, token);
                        if (pinion is not null)
                        {
                            var placed = Moved(pinion, new Vector3(apart, 0, 0));
                            parts.Add(new($"Gear {o.PartnerTeeth}T", placed,
                                          Anchors: Bore(o.ForMate(), placed, new Vector2(apart, 0f))));
                        }
                    }
                }
                break;
            }

            case GearKind.Bevel:
            {
                bool pair = o.PartnerTeeth > 0;
                float cone = pair ? (float)(Math.Atan2(o.Teeth, o.PartnerTeeth) * 180.0 / Math.PI) : o.ConeAngle;

                var bevel = BevelGear(o, o.Teeth, cone, notes, out string? refusal, token);
                if (bevel is null) return new([], notes, refusal);
                parts.Add(new($"Bevel {o.Teeth}T", bevel, Anchors: Bore(o, bevel)));

                if (pair)
                {
                    var mate = BevelGear(o.ForMate(), o.PartnerTeeth, 90f - cone, notes, out _, token);
                    if (mate is not null)
                    {
                        // Side by side, both on their backs. A bevel pair in mesh has one of them
                        // standing on its edge, which is no way to print it - so that is kept for
                        // the preview and the parts themselves are laid out to go on the bed.
                        float apart = m * (o.Teeth + o.PartnerTeeth + 4) / 2f + 2f;
                        var placed = Moved(mate, new Vector3(apart, 0, 0));

                        parts.Add(new($"Bevel {o.PartnerTeeth}T", placed,
                                      MatingBevel(o, cone, apart),
                                      Bore(o.ForMate(), placed, new Vector2(apart, 0f))));
                    }

                    notes.Add($"Right-angled pair: {cone:0.#} and {90f - cone:0.#} degree cones, {o.Teeth}:{o.PartnerTeeth}.");
                    notes.Add("Made side by side to print, shown in mesh. A bevel stands as high as "
                            + "its face leaning at its own cone, so the flatter of the two is the shallower.");
                }
                else
                {
                    notes.Add($"Its mate wants a {90f - cone:0.#} degree cone.");
                }
                break;
            }

            case GearKind.Worm:
            {
                double d = o.WormDiameter > 0 ? o.WormDiameter : 10.0 * m;
                double leadAngle = Math.Atan2(m, d) * 180.0 / Math.PI;

                var screw = WormScrew(o, d, notes, out string? wormRefusal, token);
                if (screw is null) return new([], notes, wormRefusal);

                if (o.PartnerTeeth <= 0)
                {
                    parts.Add(new("Worm", screw, Anchors: Bore(o, screw)));
                }
                else
                {
                    // Left to itself the wheel is as wide as the worm asks for, which has nothing
                    // to do with how long the worm is: two modules for every root of the diameter
                    // quotient and one. A long worm only lets the shafts slide along one another.
                    var mate = o.ForMate((float)(2.0 * m * Math.Sqrt(d / m + 1.0)))
                                with { Form = ToothForm.Helical, HelixAngle = (float)leadAngle };
                    float width = mate.Thickness;

                    var wheel = External(mate, o.PartnerTeeth, 1, 0f, notes, out string? refusal, token);
                    if (wheel is null) return new([], notes, refusal);

                    // The worm stands on its end to print, which is how a thread wants to be
                    // printed; the wheel lies flat beside it. In mesh neither is where it prints:
                    // the worm lies down across the wheel, a quarter turn from it.
                    double centres = d / 2.0 + m * o.PartnerTeeth / 2.0;
                    float apart = (float)(d / 2.0 + m + m * (o.PartnerTeeth + 2) / 2f + 2f);

                    var seated = Moved(wheel, new Vector3(apart, 0, 0));

                    parts.Add(new("Worm", screw,
                                  AcrossTheWheel(o, screw, width, centres, Radians((float)leadAngle)),
                                  Bore(o, screw)));
                    parts.Add(new($"Worm wheel {o.PartnerTeeth}T", seated,
                                  Matrix4x4.CreateTranslation(-apart, 0f, 0f),
                                  Bore(o.ForMate(), seated, new Vector2(apart, 0f))));

                    notes.Add("Made side by side to print, shown in mesh. Both right-handed.");
                    notes.Add("The wheel is helical, not throated: it touches at a point.");
                }

                notes.Add($"Single start, {d:0.##} mm across the pitch, thread leaning {leadAngle:0.#} degrees"
                        + (leadAngle < 5.0 ? " - under 5, so the wheel cannot drive it backwards." : "."));
                break;
            }

            case GearKind.Ratchet:
            {
                double tipRadius = m * o.Teeth / 2.0;
                var wheel = RatchetWheel(o, notes, out string? refusal, token);
                if (wheel is null) return new([], notes, refusal);

                parts.Add(new($"Ratchet {o.Teeth}T", wheel, Anchors: Bore(o, wheel)));

                if (o.WithPawl)
                {
                    var (pawl, reach) = Pawl(o, tipRadius);

                    // Clear of the wheel to print, back into a tooth to be looked at.
                    float apart = (float)tipRadius + 2f - pawl.ComputeBounds().Min.X;
                    var (against, pivot) = AgainstTheRatchet(o, Moved(pawl, new Vector3(apart, 0f, 0f)), apart, reach);

                    parts.Add(new("Pawl", Moved(pawl, new Vector3(apart, 0f, 0f)), against));
                    notes.Add($"The pawl's pivot goes {pivot.Length():0.#} mm from the centre, as shown.");
                }

                break;
            }

            default:
            {
                parts.Add(new($"Rack {o.Teeth}T", Rack(o, 1)));

                if (o.PartnerTeeth > 0)
                {
                    float pitchLine = o.Rim + 1.25f * m;
                    var gear = Partner(o.ForMate(), o.PartnerTeeth, 1, -MathF.PI / 2f, notes, token);
                    if (gear is not null)
                    {
                        float up = pitchLine + m * o.PartnerTeeth / 2f;
                        var placed = Moved(gear, new Vector3(0, up, 0));

                        parts.Add(new($"Gear {o.PartnerTeeth}T", placed,
                                      Anchors: Bore(o.ForMate(), placed, new Vector2(0f, up))));
                    }
                }
                break;
            }
        }

        return new(parts, notes);
    }

    /// <summary>The numbers a gear is chosen by, as lines to show beside it.</summary>
    public static List<string> Describe(GearOptions options)
    {
        var o = options.Sane();
        float m = o.Module;
        var lines = new List<string>();

        switch (o.Kind)
        {
            case GearKind.Gear:
                lines.Add($"Pitch diameter {F(m * o.Teeth)} mm, outside {F(m * (o.Teeth + 2))} mm, roots {F(m * (o.Teeth - 2.5f))} mm.");
                if (o.PartnerTeeth > 0)
                    lines.Add($"Ratio 1 : {F(o.PartnerTeeth / (float)o.Teeth)} - centres {F(m * (o.Teeth + o.PartnerTeeth) / 2f)} mm apart.");
                break;
            case GearKind.Ring:
                lines.Add($"Pitch diameter {F(m * o.Teeth)} mm, inside the teeth {F(m * (o.Teeth - 2))} mm, outside {F(m * (o.Teeth + 2.5f) + 2f * o.Rim)} mm.");
                if (o.PartnerTeeth > 0 && o.PartnerTeeth <= o.Teeth - 3)
                    lines.Add($"Ratio 1 : {F(o.Teeth / (float)o.PartnerTeeth)} - the gear's centre {F(m * (o.Teeth - o.PartnerTeeth) / 2f)} mm from the ring's.");
                break;
            case GearKind.Bevel:
            {
                float cone = o.PartnerTeeth > 0
                    ? (float)(Math.Atan2(o.Teeth, o.PartnerTeeth) * 180.0 / Math.PI)
                    : o.ConeAngle;
                float distance = m * o.Teeth / 2f / MathF.Sin(Radians(cone));
                lines.Add($"Pitch diameter {F(m * o.Teeth)} mm at the back, a {F(cone)} degree cone {F(distance)} mm long.");
                lines.Add($"Face kept to {F(MathF.Min(o.Thickness, distance / 3f))} mm, a third of the cone at the most.");
                break;
            }
            case GearKind.Worm:
            {
                float d = o.WormDiameter > 0 ? o.WormDiameter : 10f * m;
                lines.Add($"Worm {F(d + 2f * m)} mm across, {F(o.Thickness)} mm long, a thread every {F(MathF.PI * m)} mm.");
                if (o.PartnerTeeth > 0)
                {
                    float width = o.ForMate((float)(2.0 * m * Math.Sqrt(d / m + 1.0))).Thickness;
                    lines.Add($"Wheel {F(m * (o.PartnerTeeth + 2))} mm across, {F(width)} mm wide"
                            + (o.MateThickness is null ? " - as the worm asks." : "."));
                    lines.Add($"Ratio {o.PartnerTeeth}:1 - shafts {F(d / 2f + m * o.PartnerTeeth / 2f)} mm apart, at right angles.");
                }
                break;
            }
            case GearKind.Ratchet:
                lines.Add($"{F(m * o.Teeth)} mm across the tips, teeth {F(m)} mm deep, one every {F(360f / o.Teeth)} degrees.");
                break;
            default:
                lines.Add($"{F(MathF.PI * m * o.Teeth)} mm long, {F(o.Rim + 2.25f * m)} mm tall, a tooth every {F(MathF.PI * m)} mm.");
                if (o.PartnerTeeth > 0)
                    lines.Add($"One turn of the gear moves the rack {F(MathF.PI * m * o.PartnerTeeth)} mm.");
                break;
        }

        return lines;

        static string F(float value) => value.ToString("0.##", CultureInfo.CurrentCulture);
    }

    // --- Tooth shapes ------------------------------------------------------------------

    /// <summary>
    /// Half the angle a gear's tooth space takes up at a radius, found by rolling the rack.
    ///
    /// The gear turns by some angle while the rack slides the pitch circle's length of it. A point
    /// of the gear at <paramref name="radius"/> is cut away if a rack tooth covers it at any point
    /// in that roll. Written out, the point at an angle d from the middle of the space is covered
    /// when r d lies within a rack tooth's half width of r u - rho sin u, for the turn u that brings
    /// it to height rho cos u - so the space reaches as far as the largest that ever gets.
    /// </summary>
    public static double SpaceHalfAngle(double module, int teeth, double pressureRadians, double backlash, double radius)
    {
        double r = module * teeth / 2.0;
        double tipLine = r - 1.25 * module;
        double tan = Math.Tan(pressureRadians);
        double halfAtPitch = Math.PI * module / 4.0 + backlash / 4.0;
        double reach = radius <= tipLine ? 0.0 : Math.Acos(Math.Min(1.0, tipLine / radius));

        double Covered(double u)
        {
            double height = radius * Math.Cos(u);
            double half = halfAtPitch + (height - r) * tan;
            return r * u - radius * Math.Sin(u) + half;
        }

        const int samples = 240;
        double step = 2.0 * reach / samples;
        double best = Covered(0.0), bestU = 0.0;
        for (int i = 0; i <= samples && step > 0; i++)
        {
            double u = -reach + i * step;
            double value = Covered(u);
            if (value > best) (best, bestU) = (value, u);
        }

        // The sampled best is within a step of the true one; a golden-section search closes the rest.
        if (step > 0)
        {
            double low = Math.Max(-reach, bestU - step), high = Math.Min(reach, bestU + step);
            const double golden = 0.6180339887498949;
            for (int i = 0; i < 40; i++)
            {
                double a = high - golden * (high - low), b = low + golden * (high - low);
                if (Covered(a) > Covered(b)) high = b; else low = a;
            }
            best = Math.Max(best, Covered((low + high) / 2.0));
        }

        return best / r;
    }

    /// <summary>Half the angle an external gear's tooth spans at a radius.</summary>
    public static double ToothHalfAngle(double module, int teeth, double pressureRadians, double backlash, double radius) =>
        Math.PI / teeth - SpaceHalfAngle(module, teeth, pressureRadians, backlash, radius);

    /// <param name="Radii">From the root out to the tip for a gear; from the root in to the tip for a ring.</param>
    /// <param name="Half">Half the angle the tooth spans at each of those radii.</param>
    /// <param name="TipArc">Points along the tip between the two flanks.</param>
    /// <param name="RootArc">Points along the root between one tooth and the next.</param>
    private sealed record Profile(double[] Radii, double[] Half, int TipArc, int RootArc)
    {
        public int Top => Radii.Length - 1;

        /// <summary>How near the middle the band inside the teeth comes: the chord across a tooth's root.</summary>
        public double BandReach => Radii[0] * Math.Cos(Half[0]);
    }

    private static Profile ExternalProfile(GearOptions o, int teeth, double shrink, Profile? like)
    {
        double m = o.Module, r = m * teeth / 2.0, pressure = Radians(o.PressureAngle);
        double Half(double rho) => ToothHalfAngle(m, teeth, pressure, o.Backlash, rho) - shrink / rho;
        double Least(double rho) => Flat * m / 2.0 / rho;

        double root = r - 1.25 * m, tip = r + m - shrink;
        if (Half(tip) < Least(tip)) tip = Bisect(r, tip, rho => Half(rho) - Least(rho));
        if (Half(root) < Least(root)) root = Bisect(root, r, rho => Half(rho) - Least(rho));

        var radii = new double[Levels + 1];
        var half = new double[Levels + 1];
        for (int j = 0; j <= Levels; j++)
        {
            radii[j] = root + (tip - root) * j / Levels;
            half[j] = Half(radii[j]);
        }

        return new(radii, half,
            like?.TipArc ?? Math.Max(0, (int)Math.Ceiling(2.0 * half[Levels] * tip / (0.25 * m)) - 1),
            like?.RootArc ?? Math.Max(0, (int)Math.Ceiling((2.0 * Math.PI / teeth - 2.0 * half[0]) * root / (0.25 * m)) - 1));
    }

    /// <summary>
    /// A ring gear's teeth. The space between two of them is the shape of an ordinary gear's tooth
    /// with the same count - the involute from the same base circle - so it is drawn from the
    /// formula; there is no rack to roll inside a ring. Below the base circle, which only a small
    /// ring reaches, the flank goes straight in.
    /// </summary>
    private static Profile RingProfile(GearOptions o, double shrink, Profile? like)
    {
        int teeth = o.Teeth;
        double m = o.Module, r = m * teeth / 2.0, pressure = Radians(o.PressureAngle);
        double baseRadius = r * Math.Cos(pressure);
        double involute = Math.Tan(pressure) - pressure;

        double Space(double rho)
        {
            double at = Math.Acos(Math.Min(1.0, baseRadius / Math.Max(rho, baseRadius)));
            return Math.PI / (2.0 * teeth) + o.Backlash / (4.0 * r) + involute - (Math.Tan(at) - at) + shrink / rho;
        }

        double Half(double rho) => Math.PI / teeth - Space(rho);
        double Least(double rho) => Flat * m / 2.0 / rho;

        double root = r + 1.25 * m, tip = r - m + shrink;
        if (Space(root) < Least(root)) root = Bisect(r, root, rho => Space(rho) - Least(rho));
        if (Half(tip) < Least(tip)) tip = Bisect(tip, r, rho => Half(rho) - Least(rho));

        var radii = new double[Levels + 1];
        var half = new double[Levels + 1];
        for (int j = 0; j <= Levels; j++)
        {
            radii[j] = root + (tip - root) * j / Levels;
            half[j] = Half(radii[j]);
        }

        return new(radii, half, 0,
            like?.RootArc ?? Math.Max(0, (int)Math.Ceiling(2.0 * Space(root) * root / (0.25 * m)) - 1));
    }

    /// <summary>Where a function crosses nought between two radii it has opposite signs at.</summary>
    private static double Bisect(double low, double high, Func<double, double> f)
    {
        bool lowPositive = f(low) > 0;
        for (int i = 0; i < 60; i++)
        {
            double middle = (low + high) / 2.0;
            if (f(middle) > 0 == lowPositive) low = middle; else high = middle;
        }

        // The side of the crossing where the tooth still has its width.
        return lowPositive ? low : high;
    }

    // --- Outlines --------------------------------------------------------------------

    /// <summary>
    /// A gear's outline, anticlockwise, turned by <paramref name="turn"/>. Each tooth is its root
    /// corner and up one flank, the tip, down the other flank, then the root to the next tooth; the
    /// tooth's middle sits at the turn plus a whole number of pitches.
    /// </summary>
    private static Vector2[] ExternalLoop(Profile p, int teeth, double turn)
    {
        int n = p.Top, per = 2 * (n + 1) + p.TipArc + p.RootArc;
        var points = new Vector2[teeth * per];

        for (int k = 0; k < teeth; k++)
        {
            double middle = turn + 2.0 * Math.PI * k / teeth;
            int b = k * per;

            for (int j = 0; j <= n; j++)
            {
                points[b + j] = Polar(p.Radii[j], middle - p.Half[j]);
                points[b + n + 1 + p.TipArc + (n - j)] = Polar(p.Radii[j], middle + p.Half[j]);
            }

            for (int t = 1; t <= p.TipArc; t++)
                points[b + n + t] = Polar(p.Radii[n], middle - p.Half[n] + 2.0 * p.Half[n] * t / (p.TipArc + 1));

            double from = middle + p.Half[0], to = middle + 2.0 * Math.PI / teeth - p.Half[0];
            for (int t = 1; t <= p.RootArc; t++)
                points[b + 2 * (n + 1) + p.TipArc + t - 1] = Polar(p.Radii[0], from + (to - from) * t / (p.RootArc + 1));
        }

        return points;
    }

    /// <summary>A ring gear's toothed inside, anticlockwise: the teeth's middles half a pitch off the turn, so a space faces it.</summary>
    private static Vector2[] RingLoop(Profile p, int teeth, double turn)
    {
        int n = p.Top, per = 2 * (n + 1) + p.RootArc;
        var points = new Vector2[teeth * per];

        for (int k = 0; k < teeth; k++)
        {
            double middle = turn + (2.0 * k + 1.0) * Math.PI / teeth;
            int b = k * per;

            for (int j = 0; j <= n; j++)
            {
                points[b + j] = Polar(p.Radii[j], middle - p.Half[j]);
                points[b + n + 1 + (n - j)] = Polar(p.Radii[j], middle + p.Half[j]);
            }

            double from = middle + p.Half[0], to = middle + 2.0 * Math.PI / teeth - p.Half[0];
            for (int t = 1; t <= p.RootArc; t++)
                points[b + 2 * (n + 1) + t - 1] = Polar(p.Radii[0], from + (to - from) * t / (p.RootArc + 1));
        }

        return points;
    }

    /// <summary>
    /// The band inside a gear's teeth, or outside a ring's: the root corners of every tooth and the
    /// roots between them, all on one circle. A tooth's own root is left as a chord, which is the
    /// edge the tooth's strips start from.
    /// </summary>
    private static List<Vector2> Band(Vector2[] loop, Profile p, int teeth, Func<int, Tooth>? what = null)
    {
        int n = p.Top, per = loop.Length / teeth;
        int lastCorner = 2 * n + 1 + p.TipArc;
        var band = new List<Vector2>(teeth * (2 + p.RootArc));

        for (int k = 0; k < teeth; k++)
        {
            int b = k * per;

            // Where the tooth has been taken away the whole of it lies on the root circle, so the
            // band follows it round rather than cutting the chord a tooth's root would leave.
            if (what?.Invoke(k) == Tooth.Gone)
            {
                for (int t = 0; t < per; t++) band.Add(loop[b + t]);
                continue;
            }

            band.Add(loop[b]);
            band.Add(loop[b + lastCorner]);
            for (int t = 0; t < p.RootArc; t++) band.Add(loop[b + lastCorner + 1 + t]);
        }

        return band;
    }

    /// <summary>What is left of a tooth on a gear cut away for a reciprocating drive.</summary>
    private enum Tooth
    {
        /// <summary>The tooth as it was cut.</summary>
        Whole,

        /// <summary>A little short, so it can come back into mesh without meeting a tip head-on.</summary>
        Relieved,

        /// <summary>Taken away: the rim lies on the root circle here.</summary>
        Gone
    }

    /// <summary>
    /// A cut-away gear's outline. The same points as a whole one's, so everything laid on it
    /// counts the same way; a tooth that has gone has them all on the root circle, which leaves
    /// the rim a plain arc there.
    /// </summary>
    private static Vector2[] PartialLoop(Profile whole, Profile relieved, int teeth, double turn, Func<int, Tooth> what)
    {
        int n = whole.Top, per = 2 * (n + 1) + whole.TipArc + whole.RootArc;
        var points = new Vector2[teeth * per];
        double root = whole.Radii[0];

        for (int k = 0; k < teeth; k++)
        {
            var tooth = what(k);
            var p = tooth == Tooth.Relieved ? relieved : whole;
            double middle = turn + 2.0 * Math.PI * k / teeth;
            int b = k * per;

            double Radius(int j) => tooth == Tooth.Gone ? root : p.Radii[j];

            for (int j = 0; j <= n; j++)
            {
                points[b + j] = Polar(Radius(j), middle - p.Half[j]);
                points[b + n + 1 + p.TipArc + (n - j)] = Polar(Radius(j), middle + p.Half[j]);
            }

            for (int t = 1; t <= p.TipArc; t++)
                points[b + n + t] = Polar(Radius(n), middle - p.Half[n] + 2.0 * p.Half[n] * t / (p.TipArc + 1));

            double from = middle + p.Half[0], to = middle + 2.0 * Math.PI / teeth - p.Half[0];
            for (int t = 1; t <= p.RootArc; t++)
                points[b + 2 * (n + 1) + p.TipArc + t - 1] = Polar(root, from + (to - from) * t / (p.RootArc + 1));
        }

        return points;
    }

    /// <summary>
    /// The shaft hole of a part that has one.
    ///
    /// Every round part here is built about the Z axis with its bore on that axis and then moved
    /// sideways to stand beside its mate, so <paramref name="moved"/> is where the axis ended up.
    /// It has to be handed over: taking it as the middle of the part's own box is right for a
    /// whole gear and wrong for a cut-away one, which is a disc with teeth over part of its rim
    /// and whose box therefore sits a couple of millimetres off the shaft it turns on. That put
    /// every fitted shaft exactly that far out.
    ///
    /// The height still comes off the box, which is exact: the hole runs the whole of the part.
    ///
    /// Empty rather than null for a part with no bore, so the caller never has to ask twice.
    /// </summary>
    private static IReadOnlyList<Anchor> Bore(GearOptions o, Mesh placed, Vector2 moved = default)
    {
        if (o.Bore == BoreShape.None || o.BoreSize <= 0) return [];

        var box = placed.ComputeBounds();
        if (box.IsEmpty) return [];

        return [new Anchor(
            AnchorKind.Bore, "Shaft hole",
            new Vector3(moved.X, moved.Y, box.Min.Z),
            Vector3.UnitZ,

            // BoreLoop cuts the flat of a D on +X and never turns it, whatever phase the teeth
            // are given, so that is where it still is.
            o.Bore == BoreShape.Round ? Vector3.Zero : Vector3.UnitX,
            o.BoreSize, box.Size.Z, o.Bore,
            o.Bore == BoreShape.DShaft ? o.BoreFlat : 0f)];
    }

    // --- Solids ------------------------------------------------------------------------

    /// <summary>
    /// A straight bevel gear: the tooth section at the heel, run in straight lines towards the
    /// cone's apex, so the teeth shrink along the face exactly as the pitch cone does.
    ///
    /// A bevel tooth is properly a spherical involute. This is Tredgold's approximation, which is
    /// what every printed bevel is: true where the teeth touch, and wandering off it further out.
    /// The face is held to a third of the cone's length, as a cut bevel's is - past that the
    /// approximation is worth nothing and the teeth are too thin at the toe to print anyway.
    /// </summary>
    private static Mesh? BevelGear(GearOptions o, int teeth, float cone, List<string> notes,
                                   out string? refusal, CancellationToken token)
    {
        double m = o.Module, gamma = Radians(cone);
        double pitchRadius = m * teeth / 2.0;
        double distance = pitchRadius / Math.Sin(gamma);
        double face = Math.Min(o.Thickness, distance / 3.0);

        if (face < o.Thickness - 1e-3)
            notes.Add($"Face cut to {face:0.#} mm from {o.Thickness:0.#}: a third of the cone, as a cut bevel's is.");

        var profile = ExternalProfile(o, teeth, 0.0, null);
        double shrink = 1.0 - face / distance;

        var middle = Fitting(o with { HubDiameter = 0f }, profile.BandReach * shrink,
                             $"the roots of a {teeth}-tooth bevel at its toe", out refusal);
        if (middle is null) return null;

        if (o.HubDiameter > 0 && o.HubHeight > 0)
            notes.Add("No hub on a bevel: its back is the face that seats.");

        var heel = ExternalLoop(profile, teeth, 0.0);
        var toe = heel.Select(p => p * (float)shrink).ToArray();
        float top = (float)(face * Math.Cos(gamma));

        var mesh = new Mesh();
        Wall(mesh, heel, 0f, toe, top, outward: true);
        if (middle.Bore is not null) Wall(mesh, middle.Bore, 0f, middle.Bore, top, outward: false);

        Zip(mesh, Band(heel, profile, teeth), middle.Bore, 0f, up: false);
        ExternalTeeth(mesh, heel, profile, teeth, 0f, up: false);

        Zip(mesh, Band(toe, profile, teeth), middle.Bore, top, up: true);
        ExternalTeeth(mesh, toe, profile, teeth, top, up: true);

        return SetScrewed(mesh.Welded(), o with { HubDiameter = 0f }, middle, notes, token);
    }

    /// <summary>
    /// Where the worm goes when the drive is shown as it goes together: laid across the wheel a
    /// quarter turn from it, its axis at the wheel's half height and the centre distance away.
    ///
    /// It is also turned about its own axis so the two interleave where they are closest, rather
    /// than being drawn driven into one another. Which way round that is depends on whether the
    /// wheel has a tooth or a space facing the worm, and the wheel's teeth lean - so the tooth at
    /// the bottom face is not the tooth at half height. Both are worked out here: the wheel's from
    /// its twist, the worm's by looking at where the thread's own corners fall round the middle of
    /// its length. It is near enough for something being looked at; the two are not being run.
    /// </summary>
    private static Matrix4x4 AcrossTheWheel(GearOptions o, Mesh worm, float width, double centres, double leadAngle)
    {
        double radius = o.Module * o.PartnerTeeth / 2.0;
        double pitchAngle = 2.0 * Math.PI / o.PartnerTeeth;

        // How far the wheel's teeth have wound round by the height the worm touches them.
        double twist = Math.Tan(leadAngle) / radius * (width / 2.0);
        double nearest = twist - Math.Round(twist / pitchAngle) * pitchAngle;
        bool toothFacing = Math.Abs(nearest) < pitchAngle / 4.0;

        float middle = o.Thickness / 2f;   // the worm's own middle, along its length
        double slab = Math.PI * o.Module / 40.0;
        double deepest = double.MaxValue, highest = 0.0, atRoot = 0.0, atCrest = 0.0;
        bool found = false;

        foreach (var p in worm.Positions)
        {
            if (Math.Abs(p.Z - middle) > slab) continue;

            double from = Math.Sqrt(p.X * p.X + p.Y * p.Y);
            double round = Math.Atan2(p.Y, p.X);
            found = true;

            if (from < deepest) { deepest = from; atRoot = round; }
            if (from > highest) { highest = from; atCrest = round; }
        }

        // The wheel lies on -X of the worm once the worm is moved out to the centre distance.
        double facing = found ? Math.PI - (toothFacing ? atRoot : atCrest) : 0.0;

        return Matrix4x4.CreateTranslation(0f, 0f, -o.Thickness / 2f)
             * Matrix4x4.CreateRotationZ((float)facing)
             * Matrix4x4.CreateRotationX(MathF.PI / 2f)
             * Matrix4x4.CreateTranslation((float)centres, 0f, width / 2f);
    }

    /// <summary>
    /// Where the mate of a bevel pair goes when the two are shown together: turned a quarter turn
    /// so the shafts are at right angles, and slid so the two cones share an apex.
    ///
    /// Half a tooth of turn goes with it when the first gear has a tooth where they touch - which
    /// it has whenever its teeth are an even number - so the two interleave rather than meeting
    /// tip to tip. The pair is only being looked at, but a pair drawn jammed together looks broken.
    /// </summary>
    private static Matrix4x4 MatingBevel(GearOptions o, float cone, float apart)
    {
        double gamma = Radians(cone), mate = Radians(90f - cone);
        double distance = o.Module * o.Teeth / 2.0 / Math.Sin(gamma);
        double half = o.Teeth % 2 == 0 ? Math.PI / o.PartnerTeeth : 0.0;

        return Matrix4x4.CreateTranslation(-apart, 0f, 0f)
             * Matrix4x4.CreateRotationZ((float)half)
             * Matrix4x4.CreateRotationY(MathF.PI / 2f)
             * Matrix4x4.CreateTranslation((float)(-distance * Math.Cos(mate)), 0f, (float)(distance * Math.Cos(gamma)));
    }

    /// <summary>
    /// A worm: a single start, its thread the shape of the rack it stands for - flanks leaning at
    /// the pressure angle, a module above the pitch line and 1.25 below.
    ///
    /// Built on the same helical surface as a threaded rod, which already closes itself and fades
    /// its ends away into the core, with the corners of the profile put where a worm's are rather
    /// than where a screw thread's are.
    /// </summary>
    private static Mesh? WormScrew(GearOptions o, double diameter, List<string> notes,
                                   out string? refusal, CancellationToken token)
    {
        refusal = null;
        double m = o.Module, pitch = Math.PI * m, length = o.Thickness;
        double tip = diameter / 2.0 + m, root = Math.Max(diameter / 2.0 - 1.25 * m, 0.2);

        var bore = BoreLoop(o);
        double boreReach = bore?.Max(p => p.Length()) ?? 0.0;
        bool hub = o.HubDiameter > 0 && o.HubHeight > 0;
        double hubRadius = hub ? o.HubDiameter / 2.0 : 0.0;

        if (bore is not null && boreReach + LeastWall > root)
        {
            refusal = $"The bore leaves less than {LeastWall} mm between it and the roots of the worm"
                    + $" - make it under {(root - LeastWall) * 2.0:0.#} mm across.";
            return null;
        }

        if (hub && bore is not null && boreReach + LeastWall > hubRadius)
        {
            refusal = $"The bore leaves less than {LeastWall} mm of hub round it.";
            return null;
        }

        var (corners, share) = WormProfile(Radians(o.PressureAngle));
        int segments = Math.Clamp((int)Math.Ceiling(2.0 * Math.PI * tip / 0.4), 24, 360);
        double lead = Math.Min(pitch, length / 3.0);

        double Fade(double z) => Math.Clamp(Math.Min(z, length - z) / lead, 0.0, 1.0);

        var surface = new Threads.HelicalSurface(pitch, length, lead, segments,
            (t, z) => root + (tip - root) * (1.0 - share(t)) * Fade(z), corners);

        surface.Build(outward: true);
        surface.Disc(top: false);
        surface.Disc(top: true);

        // Stood on the bed like every other part here. The helical surface builds it about its
        // own middle, as a threaded rod is built.
        var mesh = Moved(surface.ToMesh().Welded(), new Vector3(0f, 0f, (float)(length / 2.0)));

        // The collar and the hole are cut rather than built into the surface: the surface lays
        // its levels out along the thread's own lead-in and has nowhere to put a step. Both are
        // plain shapes on the axis, well clear of the thread, which is the easy case for a
        // boolean - and either is left off rather than kept if it comes back with a hole in it.
        if (hub)
        {
            var collar = Rod(Circle(hubRadius), (float)length - 0.2f, (float)(length + o.HubHeight));
            var joined = LocalCsg.Union(mesh, collar, token);

            if (joined.CheckHealth().IsWatertight) mesh = joined;
            else notes.Add("Hub left off: it would not join cleanly.");
        }

        if (bore is not null)
        {
            var hole = Rod(bore, -1f, (float)(length + o.HubHeight) + 1f);
            var drilled = LocalCsg.Subtract(mesh, hole, token);

            if (drilled.CheckHealth().IsWatertight) mesh = drilled;
            else notes.Add("Bore left out: it would not cut cleanly.");
        }

        var middle = new Middle(bore, boreReach, hubRadius, hub ? Circle(hubRadius) : null,
                                (float)(length + o.HubHeight), (float)length);

        return SetScrewed(mesh, o, middle, notes, token);
    }

    /// <summary>A closed prism standing on a 2D loop: a bore of whatever shape, or a collar.</summary>
    private static Mesh Rod(IReadOnlyList<Vector2> loop, float from, float to)
    {
        var mesh = new Mesh();

        Wall(mesh, loop, from, loop, to, outward: true);
        Zip(mesh, loop, null, from, up: false);
        Zip(mesh, loop, null, to, up: true);

        return mesh.Welded();
    }

    /// <summary>
    /// Where a worm's thread turns its corners along one pitch, and how deep it is between them:
    /// the crest, down a flank, along the root, and back up again. The tooth is half the pitch
    /// thick at the pitch line and each flank runs out by the whole depth times the tangent of the
    /// pressure angle, which is the rack the wheel is cut to.
    /// </summary>
    private static (double[] Corners, Func<double, double> Share) WormProfile(double pressure)
    {
        double tan = Math.Tan(pressure);
        double flank = Math.Max(2.25 * tan / Math.PI, 0.02);
        double crest = Math.Max((0.5 * Math.PI - 2.0 * tan) / Math.PI, 0.05);
        double root = Math.Max(1.0 - crest - 2.0 * flank, 0.05);

        // Held to exactly one pitch between them, whatever the pressure angle made of it.
        double whole = crest + 2.0 * flank + root;
        crest /= whole;
        flank /= whole;
        root /= whole;

        return ([0.0, crest, crest + flank, crest + flank + root], Share);

        double Share(double t)
        {
            t -= Math.Floor(t);
            if (t <= crest) return 0.0;
            if (t <= crest + flank) return (t - crest) / flank;
            if (t <= crest + flank + root) return 1.0;
            return (1.0 - t) / flank;
        }
    }

    /// <summary>
    /// A ratchet wheel: a long ramp up to each tip and a steep face back down to the next root, so
    /// it turns freely one way and is caught the other.
    ///
    /// The catching face leans back from radial by <see cref="GearOptions.Undercut"/>. A face left
    /// radial lets the pawl ride out of the tooth under load, which is how a printed ratchet
    /// usually fails; leaning it back pulls the pawl further in instead. Three to five degrees is
    /// the usual choice, and more than the pitch has room for is quietly held to what it has.
    /// </summary>
    private static Mesh? RatchetWheel(GearOptions o, List<string> notes, out string? refusal, CancellationToken token)
    {
        double m = o.Module, tip = m * o.Teeth / 2.0, root = tip - m;
        double pitch = 2.0 * Math.PI / o.Teeth;
        double lean = Math.Min(Radians(o.Undercut), pitch / 3.0);

        var middle = Fitting(o, root, $"the roots of a {o.Teeth}-tooth ratchet", out refusal);
        if (middle is null) return null;

        var loop = new List<Vector2>(2 * o.Teeth);
        for (int k = 0; k < o.Teeth; k++)
        {
            double at = pitch * k;
            loop.Add(Polar(root, at + lean));   // the foot of the catching face
            loop.Add(Polar(tip, at + pitch));   // the tip, at the top of the ramp
        }

        float h = o.Thickness, top = middle.Top;
        var mesh = new Mesh();

        Wall(mesh, loop, 0f, loop, h, outward: true);
        if (middle.Bore is not null) Wall(mesh, middle.Bore, 0f, middle.Bore, top, outward: false);
        if (middle.HubCircle is not null) Wall(mesh, middle.HubCircle, h, middle.HubCircle, top, outward: true);

        Zip(mesh, loop, middle.Bore, 0f, up: false);
        Zip(mesh, loop, middle.HubCircle ?? middle.Bore, h, up: true);
        if (middle.HubCircle is not null) Zip(mesh, middle.HubCircle, middle.Bore, top, up: true);

        if (lean < Radians(o.Undercut) - 1e-4)
            notes.Add($"The face leans {lean * 180.0 / Math.PI:0.#} degrees: the pitch has room for no more.");

        notes.Add("Free clockwise from above, caught anticlockwise.");

        return SetScrewed(mesh.Welded(), o, middle, notes, token);
    }

    /// <summary>
    /// The pawl that catches a ratchet: an arm with its pivot at one end and a point at the other,
    /// lying flat as the wheel does. Where the pivot goes is said in a note rather than built in,
    /// since it is a hole in whatever the pair is mounted on.
    /// </summary>
    /// <summary>
    /// Where the pawl goes when the two are shown together: its point in the notch nearest the top
    /// of the wheel, and its arm laid along the way the wheel pushes it when it is caught, so the
    /// push runs down the arm into the pivot instead of trying to lift the point out. Hands back
    /// where the pivot lands as well, since that is a hole in whatever the pair is mounted on.
    /// </summary>
    private static (Matrix4x4 Where, Vector2 Pivot) AgainstTheRatchet(GearOptions o, Mesh pawl, float apart, float reach)
    {
        double m = o.Module, tip = m * o.Teeth / 2.0, root = tip - m;
        double pitch = 2.0 * Math.PI / o.Teeth;
        double lean = Math.Min(Radians(o.Undercut), pitch / 3.0);

        // The foot of a catching face, which is where a pawl sits, nearest to straight up.
        double at = Math.Round((Math.PI / 2.0 - lean) / pitch) * pitch + lean;
        var contact = Polar(root + 0.2, at);

        // Round the wheel the way it is caught: the face is a hair behind this point, so the arm
        // running on from here is the arm the face pushes along.
        var along = new Vector2((float)-Math.Sin(at), (float)Math.Cos(at));
        var pivot = contact + along * reach;

        Matrix4x4 At(Vector2 where) =>
            Matrix4x4.CreateTranslation(-apart, 0f, 0f)
            * Matrix4x4.CreateRotationZ((float)(at - Math.PI / 2.0))
            * Matrix4x4.CreateTranslation(where.X, where.Y, 0f);

        // Then pushed out until the deepest part of it - the back of the hook, not its point -
        // sits just clear of the roots. Seated by the point alone it buried its own hook in the
        // wheel, which is the one thing a pawl must not do.
        var outward = new Vector2((float)Math.Cos(at), (float)Math.Sin(at));

        for (int settle = 0; settle < 3; settle++)
        {
            var placed = At(pivot);
            float deepest = float.MaxValue;

            foreach (var p in pawl.Positions)
            {
                var q = Vector3.Transform(p, placed);
                deepest = MathF.Min(deepest, new Vector2(q.X, q.Y).Length());
            }

            float clear = (float)(root + 0.1) - deepest;
            if (clear <= 0.001f) break;

            pivot += outward * clear;
        }

        return (At(pivot), pivot);
    }

    private static (Mesh Mesh, float Reach) Pawl(GearOptions o, double tip)
    {
        double boss = Math.Max(o.BoreSize / 2.0 + 1.6, 3.0);

        // The hook has to lie inside a tooth, and a tooth is only a module deep - so this is held
        // to half of one however big the boss round the pivot has to be.
        double nose = Math.Min(Math.Max(boss * 0.45, 1.2), 0.5 * o.Module);
        double arm = Math.Max(1.2 * tip, 4.0 * boss);
        double point = arm + nose * 1.6;

        var outline = new List<Vector2>();
        int steps = Math.Max(12, (int)Math.Ceiling(Math.PI * boss / 0.4));
        for (int i = 0; i <= steps; i++)
            outline.Add(Polar(boss, Math.PI / 2.0 + Math.PI * i / steps));

        outline.Add(new Vector2((float)arm, (float)-nose));
        outline.Add(new Vector2((float)point, (float)(-nose * 0.15)));

        var loop = Densified(outline);
        var bore = Circle(o.BoreSize / 2.0);
        float h = o.Thickness;

        var mesh = new Mesh();
        Wall(mesh, loop, 0f, loop, h, outward: true);
        Wall(mesh, bore, 0f, bore, h, outward: false);
        Zip(mesh, loop, bore, 0f, up: false);
        Zip(mesh, loop, bore, h, up: true);

        return (mesh.Welded(), (float)point);
    }

    private static Mesh? Partner(GearOptions o, int teeth, int hand, float phase, List<string> notes, CancellationToken token)
    {
        var gear = External(o, teeth, hand, phase, notes, out _, token);
        if (gear is not null) return gear;

        // A pinion is often too small for the bore the bigger gear was given; it is still worth
        // having, plain, rather than not at all.
        var plain = o with { Bore = BoreShape.None, HubHeight = 0f, SetScrew = 0f };
        gear = External(plain, teeth, hand, phase, notes, out string? refusal, token);
        if (gear is not null)
            notes.Add($"The {teeth}-tooth gear is too small for that bore, so it was made plain.");
        else if (refusal is not null)
            notes.Add(refusal);

        return gear;
    }

    /// <param name="lockDisc">A framed gear's lock, standing <paramref name="lockHeight"/> on top of the teeth; see <see cref="PlanFrame"/>.</param>
    private static Mesh? External(GearOptions o, int teeth, int hand, float phase, List<string> notes,
                                  out string? refusal, CancellationToken token, bool partial = false,
                                  IReadOnlyList<Vector2>? lockDisc = null, float lockHeight = 0f)
    {
        refusal = null;
        double m = o.Module, r = m * teeth / 2.0;

        var profile = ExternalProfile(o, teeth, 0.0, null);
        var chamfered = o.Chamfer > 0 ? ExternalProfile(o, teeth, o.Chamfer, profile) : null;

        // Cut away for a reciprocating drive: a sector of teeth, the rest of the rim at the roots.
        int kept = partial && o.KeptTeeth > 0 && o.KeptTeeth < teeth ? o.KeptTeeth : teeth;
        bool mutilated = kept < teeth;
        var relieved = mutilated ? ExternalProfile(o, teeth, Relief * m, profile) : null;
        var relievedChamfer = mutilated && o.Chamfer > 0
            ? ExternalProfile(o, teeth, o.Chamfer + Relief * m, profile)
            : null;

        Tooth What(int k) => k >= kept ? Tooth.Gone : k == 0 || k == kept - 1 ? Tooth.Relieved : Tooth.Whole;

        if (mutilated)
        {
            notes.Add($"{kept} of {teeth} teeth, a {360.0 * kept / teeth:0.#} degree sector; the rest at the roots.");
            notes.Add("The first and last are shortened, to come back into mesh cleanly.");
            if (lockDisc is null)
                notes.Add("No locking arc: nothing holds the follower between engagements.");
        }

        if (teeth < 17 && !notes.Any(n => n.StartsWith("Under 17", StringComparison.Ordinal)))
            notes.Add("Under 17 teeth the roots undercut, as a cut gear's do.");

        if (lockDisc is null) lockHeight = 0f;
        var middle = Fitting(o, profile.BandReach, $"the roots of a {teeth}-tooth gear", out refusal, lockHeight);
        if (middle is null) return null;

        var bore = middle.Bore;
        var hubCircle = middle.HubCircle;
        float h = o.Thickness;
        float top = middle.Top;

        var layers = Layers(o, (float)(hand * Math.Tan(Radians(o.HelixAngle)) / r), (float)profile.Radii[^1]);
        var loops = layers.Select(l => mutilated
            ? PartialLoop(l.Chamfered ? chamfered! : profile, l.Chamfered ? relievedChamfer! : relieved!,
                          teeth, phase + l.Twist, What)
            : ExternalLoop(l.Chamfered ? chamfered! : profile, teeth, phase + l.Twist)).ToList();

        var mesh = new Mesh();
        for (int i = 0; i + 1 < layers.Count; i++)
            Wall(mesh, loops[i], layers[i].Z, loops[i + 1], layers[i + 1].Z, outward: true);

        if (bore is not null) Wall(mesh, bore, 0f, bore, top, outward: false);
        if (hubCircle is not null) Wall(mesh, hubCircle, middle.HubBase, hubCircle, top, outward: true);

        var gone = mutilated ? What : (Func<int, Tooth>?)null;
        var bottom = layers[0].Chamfered ? chamfered! : profile;
        Zip(mesh, Band(loops[0], bottom, teeth, gone), bore, 0f, up: false);
        ExternalTeeth(mesh, loops[0], bottom, teeth, 0f, up: false, gone);

        var band = Band(loops[^1], profile, teeth, gone);
        if (lockDisc is null)
        {
            Zip(mesh, band, hubCircle ?? bore, h, up: true);
        }
        else
        {
            Zip(mesh, band, lockDisc, h, up: true);
            Wall(mesh, lockDisc, h, lockDisc, middle.HubBase, outward: true);
            Zip(mesh, lockDisc, hubCircle ?? bore, middle.HubBase, up: true);
        }

        ExternalTeeth(mesh, loops[^1], profile, teeth, h, up: true, gone);

        if (hubCircle is not null) Zip(mesh, hubCircle, bore, top, up: true);

        return SetScrewed(mesh.Welded(), o, middle, notes, token);
    }

    /// <param name="Top">The height of the whole part: its thickness, anything standing on it, and its hub on top of that.</param>
    /// <param name="HubBase">Where the hub starts: the top of the teeth, or of a lock standing on them.</param>
    private sealed record Middle(
        List<Vector2>? Bore, double BoreReach, double HubRadius, List<Vector2>? HubCircle, float Top, float HubBase)
    {
        public bool HasHub => HubCircle is not null;
    }

    /// <summary>
    /// The hole through a disc and the collar round it, with what would leave the part too thin to
    /// hold refused. <paramref name="band"/> is how near the middle the material reaches - the
    /// roots of a gear, the roots of a ratchet - and <paramref name="what"/> names it in the
    /// refusal. <paramref name="raise"/> is anything standing between the teeth and the hub.
    /// </summary>
    private static Middle? Fitting(GearOptions o, double band, string what, out string? refusal, float raise = 0f)
    {
        refusal = null;

        var bore = BoreLoop(o);
        double boreReach = bore?.Max(p => p.Length()) ?? 0.0;
        bool hub = o.HubDiameter > 0 && o.HubHeight > 0;
        double hubRadius = hub ? o.HubDiameter / 2.0 : 0.0;

        if (hub && hubRadius > band - 0.2)
        {
            refusal = $"The hub is wider than {what} - make it under {(band - 0.2) * 2.0:0.#} mm across.";
            return null;
        }

        if (bore is not null && boreReach + LeastWall > (hub ? hubRadius : band))
        {
            refusal = hub
                ? $"The bore leaves less than {LeastWall} mm of hub round it."
                : $"The bore leaves less than {LeastWall} mm between it and {what}.";
            return null;
        }

        return new(bore, boreReach, hubRadius, hub ? Circle(hubRadius) : null,
                   o.Thickness + raise + (hub ? o.HubHeight : 0f), o.Thickness + raise);
    }

    /// <summary>The hole across a hub for a grub screw, or a note saying why there is none.</summary>
    private static Mesh SetScrewed(Mesh mesh, GearOptions o, Middle middle, List<string> notes, CancellationToken token)
    {
        if (o.SetScrew <= 0) return mesh;

        if (!middle.HasHub)
        {
            notes.Add("No set screw: it needs a hub to go through.");
            return mesh;
        }

        if (o.HubHeight < o.SetScrew + 1f)
        {
            notes.Add($"No set screw: the hub is under {o.SetScrew + 1f:0.#} mm tall.");
            return mesh;
        }

        if (middle.HubRadius - middle.BoreReach < 1f)
        {
            notes.Add("No set screw: the hub wall is under 1 mm.");
            return mesh;
        }

        double inner = middle.Bore is null ? 0.0 : middle.Bore.Min(p => p.Length()) * 0.5;
        double outer = middle.HubRadius + 1.0;
        var rod = MeshTransform.Transformed(
            Primitives.Prism(o.SetScrew / 2f, (float)(outer - inner), 24),
            Matrix4x4.CreateRotationY(MathF.PI / 2f)
            * Matrix4x4.CreateTranslation((float)((inner + outer) / 2.0), 0f, middle.HubBase + o.HubHeight / 2f));

        // Turned so the hole meets a D-shaped bore's flat, which sits on +X.
        var drilled = LocalCsg.Subtract(mesh, rod, token);
        if (drilled.CheckHealth().IsWatertight) return drilled;

        notes.Add("Set-screw hole left out: it would not cut cleanly.");
        return mesh;
    }

    private static Mesh? Ring(GearOptions o, int hand, List<string> notes, out string? refusal)
    {
        refusal = null;
        int teeth = o.Teeth;
        double m = o.Module, r = m * teeth / 2.0;

        var profile = RingProfile(o, 0.0, null);
        var chamfered = o.Chamfer > 0 ? RingProfile(o, o.Chamfer, profile) : null;
        var rim = Circle(r + 1.25 * m + o.Rim);

        float h = o.Thickness;
        var layers = Layers(o, (float)(hand * Math.Tan(Radians(o.HelixAngle)) / r), (float)profile.Radii[0]);
        var loops = layers.Select(l => RingLoop(l.Chamfered ? chamfered! : profile, teeth, l.Twist)).ToList();

        var mesh = new Mesh();
        for (int i = 0; i + 1 < layers.Count; i++)
            Wall(mesh, loops[i], layers[i].Z, loops[i + 1], layers[i + 1].Z, outward: false);
        Wall(mesh, rim, 0f, rim, h, outward: true);

        var bottom = layers[0].Chamfered ? chamfered! : profile;
        Zip(mesh, rim, RingBand(loops[0], bottom, teeth), 0f, up: false);
        RingTeeth(mesh, loops[0], bottom, teeth, 0f, up: false);

        Zip(mesh, rim, RingBand(loops[^1], profile, teeth), h, up: true);
        RingTeeth(mesh, loops[^1], profile, teeth, h, up: true);

        return mesh.Welded();
    }

    private static List<Vector2> RingBand(Vector2[] loop, Profile p, int teeth)
    {
        int n = p.Top, per = loop.Length / teeth;
        var band = new List<Vector2>(teeth * (2 + p.RootArc));

        for (int k = 0; k < teeth; k++)
        {
            int b = k * per;
            band.Add(loop[b]);
            band.Add(loop[b + 2 * n + 1]);
            for (int t = 0; t < p.RootArc; t++) band.Add(loop[b + 2 * (n + 1) + t]);
        }

        return band;
    }

    /// <summary>
    /// A rack lying flat, teeth towards +Y, its pitch line <see cref="GearOptions.Rim"/> plus a
    /// root's depth up from the bottom edge. A space sits on X = 0, so a gear with a tooth pointing
    /// down meshes there.
    /// </summary>
    private static Mesh Rack(GearOptions o, int hand)
    {
        float m = o.Module, h = o.Thickness;
        var layers = Layers(o, (float)(hand * Math.Tan(Radians(o.HelixAngle))), 1f);
        var loops = layers.Select(l => RackLoop(o, l.Chamfered ? o.Chamfer : 0f, l.Twist)).ToList();

        var mesh = new Mesh();
        for (int i = 0; i + 1 < layers.Count; i++)
            Wall(mesh, loops[i], layers[i].Z, loops[i + 1], layers[i + 1].Z, outward: true);

        RackCap(mesh, loops[0], 0f, up: false);
        RackCap(mesh, loops[^1], h, up: true);

        return mesh.Welded();
    }

    /// <summary>Along the bottom edge left to right, then back along the teeth: the same points above and below, so the cap is a row of slabs.</summary>
    private static Vector2[] RackLoop(GearOptions o, float shrink, float shift)
    {
        float m = o.Module, pitch = MathF.PI * m;
        float tan = MathF.Tan(Radians(o.PressureAngle));
        float rootLine = o.Rim, pitchLine = rootLine + 1.25f * m, tipLine = pitchLine + m - shrink;
        float halfAtPitch = pitch / 4f - o.Backlash / 4f - shrink;
        float halfRoot = halfAtPitch + 1.25f * m * tan;
        float halfTip = halfAtPitch - (m - shrink) * tan;

        float start = -(o.Teeth / 2) * pitch;
        var chain = new List<Vector2> { new(start, rootLine) };
        for (int k = 0; k < o.Teeth; k++)
        {
            float middle = start + (k + 0.5f) * pitch;
            chain.Add(new(middle - halfRoot, rootLine));
            chain.Add(new(middle - halfTip, tipLine));
            chain.Add(new(middle + halfTip, tipLine));
            chain.Add(new(middle + halfRoot, rootLine));
        }
        chain.Add(new(start + o.Teeth * pitch, rootLine));

        int count = chain.Count;
        var loop = new Vector2[2 * count];
        for (int i = 0; i < count; i++)
        {
            loop[i] = new(chain[i].X + shift, 0f);
            loop[2 * count - 1 - i] = new(chain[i].X + shift, chain[i].Y);
        }

        return loop;
    }

    private static void RackCap(Mesh mesh, Vector2[] loop, float z, bool up)
    {
        int count = loop.Length / 2;
        for (int i = 0; i + 1 < count; i++)
        {
            var lowA = loop[i];
            var lowB = loop[i + 1];
            var highB = loop[2 * count - 2 - i];
            var highA = loop[2 * count - 1 - i];
            Face(mesh, lowA, lowB, highB, z, up);
            Face(mesh, lowA, highB, highA, z, up);
        }
    }

    /// <summary>
    /// The height of each layer, how far it is turned or slid, and whether it is the chamfered
    /// bottom. A straight gear needs only its two faces; a helical one enough between them that a
    /// tip moves under <see cref="LayerShift"/> from one to the next.
    /// </summary>
    private static List<(float Z, float Twist, bool Chamfered)> Layers(GearOptions o, float perHeight, float reach)
    {
        float h = o.Thickness, chamfer = o.Chamfer;
        bool winds = o.Form != ToothForm.Straight && o.HelixAngle > 0f;
        var heights = new List<float> { 0f };

        if (!winds)
        {
            heights.Add(h);
        }
        else
        {
            float span = o.Form == ToothForm.Herringbone ? h / 2f : h;
            int steps = Math.Clamp((int)MathF.Ceiling(MathF.Abs(perHeight) * span * reach / LayerShift), 1, 200);
            for (int i = 1; i <= steps; i++) heights.Add(span * i / steps);
            if (o.Form == ToothForm.Herringbone)
                for (int i = 1; i <= steps; i++) heights.Add(span + span * i / steps);
        }

        if (chamfer > 0f)
        {
            heights.RemoveAll(z => z > 0f && z < chamfer + 1e-3f);
            heights.Add(chamfer);
            heights.Sort();
        }

        return heights.Select(z => (z, Twist(z), chamfer > 0f && z == 0f)).ToList();

        float Twist(float z) => !winds ? 0f
            : o.Form == ToothForm.Herringbone ? perHeight * MathF.Min(z, h - z)
            : perHeight * z;
    }

    private static List<Vector2>? BoreLoop(GearOptions o)
    {
        float radius = o.BoreSize / 2f;
        switch (o.Bore)
        {
            case BoreShape.Round:
                return Circle(radius);

            case BoreShape.Hex:
            {
                float corner = o.BoreSize / MathF.Sqrt(3f);
                var corners = Enumerable.Range(0, 6)
                    .Select(k => Polar(corner, Math.PI / 6.0 + k * Math.PI / 3.0)).ToList();
                return Densified(corners);
            }

            case BoreShape.DShaft:
            {
                // The flat on +X, as far from the far side as asked.
                float flat = o.BoreFlat - radius;
                var circle = Circle(radius);
                var clipped = new List<Vector2>();
                for (int i = 0; i < circle.Count; i++)
                {
                    var a = circle[i];
                    var b = circle[(i + 1) % circle.Count];
                    bool aIn = a.X <= flat, bIn = b.X <= flat;
                    if (aIn) clipped.Add(a);
                    if (aIn != bIn)
                        clipped.Add(Vector2.Lerp(a, b, (flat - a.X) / (b.X - a.X)));
                }
                return Densified(clipped);
            }

            default:
                return null;
        }
    }

    /// <summary>Straight edges broken into short pieces, so the band laid round a bore follows it closely.</summary>
    private static List<Vector2> Densified(List<Vector2> loop)
    {
        var dense = new List<Vector2>();
        for (int i = 0; i < loop.Count; i++)
        {
            var a = loop[i];
            var b = loop[(i + 1) % loop.Count];
            int pieces = Math.Max(1, (int)MathF.Ceiling(Vector2.Distance(a, b) / 0.4f));
            for (int k = 0; k < pieces; k++) dense.Add(Vector2.Lerp(a, b, k / (float)pieces));
        }

        return dense;
    }

    private static List<Vector2> Circle(double radius)
    {
        int sides = Math.Clamp((int)Math.Ceiling(2.0 * Math.PI * radius / 0.4), 24, 720);
        return Enumerable.Range(0, sides).Select(k => Polar(radius, 2.0 * Math.PI * k / sides)).ToList();
    }

    // --- Faces -------------------------------------------------------------------------

    private static void ExternalTeeth(Mesh mesh, Vector2[] loop, Profile p, int teeth, float z, bool up,
                                      Func<int, Tooth>? what = null)
    {
        int n = p.Top, per = loop.Length / teeth;
        for (int k = 0; k < teeth; k++)
        {
            if (what?.Invoke(k) == Tooth.Gone) continue;

            int b = k * per;
            Vector2 Right(int j) => loop[b + j];
            Vector2 Left(int j) => loop[b + n + 1 + p.TipArc + (n - j)];

            for (int j = 0; j < n; j++)
            {
                Face(mesh, Right(j), Right(j + 1), Left(j + 1), z, up);
                Face(mesh, Right(j), Left(j + 1), Left(j), z, up);
            }

            // The tip: a fan from one corner across the arc to the other.
            if (p.TipArc == 0) continue;
            var previous = loop[b + n + 1];
            for (int t = 2; t <= p.TipArc; t++)
            {
                Face(mesh, Right(n), previous, loop[b + n + t], z, up);
                previous = loop[b + n + t];
            }
            Face(mesh, Right(n), previous, Left(n), z, up);
        }
    }

    private static void RingTeeth(Mesh mesh, Vector2[] loop, Profile p, int teeth, float z, bool up)
    {
        int n = p.Top, per = loop.Length / teeth;
        for (int k = 0; k < teeth; k++)
        {
            int b = k * per;
            Vector2 Right(int j) => loop[b + j];
            Vector2 Left(int j) => loop[b + n + 1 + (n - j)];

            // From the root inward; the tip is left flat, since an arc there would bulge into the
            // space inside the ring rather than into the tooth.
            for (int j = 0; j < n; j++)
            {
                Face(mesh, Right(j + 1), Right(j), Left(j), z, up);
                Face(mesh, Right(j + 1), Left(j), Left(j + 1), z, up);
            }
        }
    }

    /// <summary>
    /// The faces between an outer loop and an inner one, both going round the middle: stepping
    /// along whichever has the nearer next point by angle, so every triangle spans a thin wedge.
    /// With no inner loop, a fan from the middle.
    /// </summary>
    private static void Zip(Mesh mesh, IReadOnlyList<Vector2> outer, IReadOnlyList<Vector2>? inner, float z, bool up)
    {
        if (inner is null)
        {
            for (int i = 0; i < outer.Count; i++)
                Face(mesh, Vector2.Zero, outer[i], outer[(i + 1) % outer.Count], z, up);
            return;
        }

        var (o, oa) = ByAngle(outer);
        var (q, qa) = ByAngle(inner);

        int a = 0, c = 0;
        while (a < o.Count - 1 || c < q.Count - 1)
        {
            bool alongOuter = c == q.Count - 1 || (a < o.Count - 1 && oa[a + 1] <= qa[c + 1]);
            if (alongOuter)
            {
                Face(mesh, q[c], o[a], o[a + 1], z, up);
                a++;
            }
            else
            {
                Face(mesh, q[c], o[a], q[c + 1], z, up);
                c++;
            }
        }
    }

    /// <summary>A loop started from its point nearest angle nought, angles climbing, the first point repeated at the end a turn on.</summary>
    private static (List<Vector2> Points, List<double> Angles) ByAngle(IReadOnlyList<Vector2> loop)
    {
        double Angle(Vector2 p)
        {
            double a = Math.Atan2(p.Y, p.X);
            return a < 0 ? a + 2.0 * Math.PI : a;
        }

        int start = 0;
        for (int i = 1; i < loop.Count; i++)
            if (Angle(loop[i]) < Angle(loop[start])) start = i;

        var points = new List<Vector2>(loop.Count + 1);
        var angles = new List<double>(loop.Count + 1);
        double previous = -1.0, lap = 0.0;

        for (int k = 0; k <= loop.Count; k++)
        {
            var p = loop[(start + k) % loop.Count];
            double a = Angle(p) + lap;
            if (a < previous - Math.PI) { lap += 2.0 * Math.PI; a += 2.0 * Math.PI; }
            if (k == loop.Count && a <= previous) a += 2.0 * Math.PI;
            points.Add(p);
            angles.Add(a);
            previous = a;
        }

        return (points, angles);
    }

    private static void Wall(Mesh mesh, IReadOnlyList<Vector2> lower, float lowZ, IReadOnlyList<Vector2> upper, float highZ, bool outward)
    {
        for (int i = 0; i < lower.Count; i++)
        {
            int next = (i + 1) % lower.Count;
            var a = new Vector3(lower[i], lowZ);
            var b = new Vector3(lower[next], lowZ);
            var c = new Vector3(upper[next], highZ);
            var d = new Vector3(upper[i], highZ);

            if (outward)
            {
                mesh.AddTriangle(a, b, c);
                mesh.AddTriangle(a, c, d);
            }
            else
            {
                mesh.AddTriangle(a, c, b);
                mesh.AddTriangle(a, d, c);
            }
        }
    }

    private static void Face(Mesh mesh, Vector2 a, Vector2 b, Vector2 c, float z, bool up)
    {
        if (up) mesh.AddTriangle(new(a, z), new(b, z), new(c, z));
        else mesh.AddTriangle(new(a, z), new(c, z), new(b, z));
    }

    private static Mesh Moved(Mesh mesh, Vector3 by) =>
        MeshTransform.Transformed(mesh, Matrix4x4.CreateTranslation(by));

    private static Vector2 Rotated(Vector2 p, float angle) =>
        new(p.X * MathF.Cos(angle) - p.Y * MathF.Sin(angle), p.X * MathF.Sin(angle) + p.Y * MathF.Cos(angle));

    private static Vector2 Polar(double radius, double angle) =>
        new((float)(radius * Math.Cos(angle)), (float)(radius * Math.Sin(angle)));

    private static float Radians(float degrees) => degrees * MathF.PI / 180f;
}
