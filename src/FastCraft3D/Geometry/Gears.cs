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
    Rack
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

    /// <summary>A ring's material outside the roots of its teeth; a rack's below them.</summary>
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

    /// <summary>Everything held to what can be built.</summary>
    public GearOptions Sane()
    {
        float module = Clamp(Module, 0.2f, 20f, 1.5f);
        float thickness = Clamp(Thickness, 0.5f, 1000f, 8f);
        int least = Kind switch { GearKind.Rack => 1, GearKind.Ring => 12, _ => 6 };

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
            PartnerTeeth = PartnerTeeth <= 0 ? 0 : Math.Clamp(PartnerTeeth, 6, 400)
        };

        static float Clamp(float value, float low, float high, float otherwise) =>
            float.IsFinite(value) ? Math.Clamp(value, low, high) : otherwise;
    }
}

/// <param name="Name">What the part is called in the scene.</param>
/// <param name="Mesh">The part where it goes: a pair comes out already in mesh.</param>
public sealed record GearPart(string Name, Mesh Mesh);

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
public static class Gears
{
    /// <summary>Points up each side of a tooth. The flank is a gentle curve, and chords this short are well under a hundredth of a millimetre off it.</summary>
    private const int Levels = 20;

    /// <summary>The narrowest a tooth tip or a root is let become, as a share of the module, before it is cut off flat.</summary>
    private const float Flat = 0.1f;

    /// <summary>The least material left between a bore and the roots, or a hub.</summary>
    public const float LeastWall = 0.8f;

    /// <summary>The most a helical tooth's tip moves between one layer and the next.</summary>
    private const float LayerShift = 0.4f;

    public static GearResult Build(GearOptions options, CancellationToken token = default)
    {
        var o = options.Sane();
        var notes = new List<string>();
        var parts = new List<GearPart>();
        float m = o.Module;

        if (m < 0.8f)
            notes.Add("Teeth this small are finer than a 0.4 mm nozzle prints well; a module of 1 or more comes out cleaner.");

        switch (o.Kind)
        {
            case GearKind.Gear:
            {
                var gear = External(o, o.Teeth, 1, 0f, notes, out string? refusal, token);
                if (gear is null) return new([], notes, refusal);
                parts.Add(new($"Gear {o.Teeth}T", gear));

                if (o.PartnerTeeth > 0)
                {
                    // Their tooth lines have to lie together where they touch, and the two turn opposite
                    // ways - so the partner leans the other way.
                    float apart = m * (o.Teeth + o.PartnerTeeth) / 2f;
                    var partner = Partner(o, o.PartnerTeeth, -1, MathF.PI - MathF.PI / o.PartnerTeeth, notes, token);
                    if (partner is not null)
                        parts.Add(new($"Gear {o.PartnerTeeth}T", Moved(partner, new Vector3(apart, 0, 0))));
                }
                break;
            }

            case GearKind.Ring:
            {
                var ring = Ring(o, 1, notes, out string? refusal);
                if (ring is null) return new([], notes, refusal);
                parts.Add(new($"Ring gear {o.Teeth}T", ring));

                if (o.PartnerTeeth > 0)
                {
                    if (o.PartnerTeeth > o.Teeth - 3)
                    {
                        notes.Add("A gear inside a ring has to have fewer teeth than the ring, so none was made.");
                    }
                    else
                    {
                        if (o.Teeth - o.PartnerTeeth < 12)
                            notes.Add("A ring gear wants about 12 more teeth than the gear inside it, or the tips can catch.");

                        // Inside a ring both turn the same way, so the pinion leans the same way.
                        float apart = m * (o.Teeth - o.PartnerTeeth) / 2f;
                        var pinion = Partner(o, o.PartnerTeeth, 1, 0f, notes, token);
                        if (pinion is not null)
                            parts.Add(new($"Gear {o.PartnerTeeth}T", Moved(pinion, new Vector3(apart, 0, 0))));
                    }
                }
                break;
            }

            default:
            {
                parts.Add(new($"Rack {o.Teeth}T", Rack(o, 1)));

                if (o.PartnerTeeth > 0)
                {
                    float pitchLine = o.Rim + 1.25f * m;
                    var gear = Partner(o, o.PartnerTeeth, 1, -MathF.PI / 2f, notes, token);
                    if (gear is not null)
                        parts.Add(new($"Gear {o.PartnerTeeth}T",
                            Moved(gear, new Vector3(0, pitchLine + m * o.PartnerTeeth / 2f, 0))));
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
    private static List<Vector2> Band(Vector2[] loop, Profile p, int teeth)
    {
        int n = p.Top, per = loop.Length / teeth;
        int lastCorner = 2 * n + 1 + p.TipArc;
        var band = new List<Vector2>(teeth * (2 + p.RootArc));

        for (int k = 0; k < teeth; k++)
        {
            int b = k * per;
            band.Add(loop[b]);
            band.Add(loop[b + lastCorner]);
            for (int t = 0; t < p.RootArc; t++) band.Add(loop[b + lastCorner + 1 + t]);
        }

        return band;
    }

    // --- Solids ------------------------------------------------------------------------

    private static Mesh? Partner(GearOptions o, int teeth, int hand, float phase, List<string> notes, CancellationToken token)
    {
        var gear = External(o, teeth, hand, phase, notes, out _, token);
        if (gear is not null) return gear;

        // A pinion is often too small for the bore the bigger gear was given; it is still worth
        // having, plain, rather than not at all.
        var plain = o with { Bore = BoreShape.None, HubHeight = 0f, SetScrew = 0f };
        gear = External(plain, teeth, hand, phase, notes, out string? refusal, token);
        if (gear is not null)
            notes.Add($"The {teeth}-tooth gear is too small for the bore and hub, so it has none - drill it, or give it its own.");
        else if (refusal is not null)
            notes.Add(refusal);

        return gear;
    }

    private static Mesh? External(GearOptions o, int teeth, int hand, float phase, List<string> notes,
                                  out string? refusal, CancellationToken token)
    {
        refusal = null;
        double m = o.Module, r = m * teeth / 2.0;

        var profile = ExternalProfile(o, teeth, 0.0, null);
        var chamfered = o.Chamfer > 0 ? ExternalProfile(o, teeth, o.Chamfer, profile) : null;

        if (teeth < 17 && !notes.Any(n => n.StartsWith("Under 17", StringComparison.Ordinal)))
            notes.Add("Under 17 teeth the roots are undercut, as a cut gear's are: it meshes properly, but each tooth is thinner at its base.");

        var bore = BoreLoop(o);
        double boreReach = bore?.Max(p => p.Length()) ?? 0.0;
        double band = profile.BandReach;
        bool hub = o.HubDiameter > 0 && o.HubHeight > 0;
        double hubRadius = hub ? o.HubDiameter / 2.0 : 0.0;

        if (hub && hubRadius > band - 0.2)
        {
            refusal = $"The hub is wider than the roots of a {teeth}-tooth gear - make it under {(band - 0.2) * 2.0:0.#} mm across.";
            return null;
        }

        if (bore is not null && boreReach + LeastWall > (hub ? hubRadius : band))
        {
            refusal = hub
                ? $"The bore leaves less than {LeastWall} mm of hub round it."
                : $"The bore leaves less than {LeastWall} mm between it and the roots of a {teeth}-tooth gear.";
            return null;
        }

        float h = o.Thickness;
        float top = hub ? h + o.HubHeight : h;
        var hubCircle = hub ? Circle(hubRadius) : null;

        var layers = Layers(o, (float)(hand * Math.Tan(Radians(o.HelixAngle)) / r), (float)profile.Radii[^1]);
        var loops = layers.Select(l => ExternalLoop(l.Chamfered ? chamfered! : profile, teeth, phase + l.Twist)).ToList();

        var mesh = new Mesh();
        for (int i = 0; i + 1 < layers.Count; i++)
            Wall(mesh, loops[i], layers[i].Z, loops[i + 1], layers[i + 1].Z, outward: true);

        if (bore is not null) Wall(mesh, bore, 0f, bore, top, outward: false);
        if (hubCircle is not null) Wall(mesh, hubCircle, h, hubCircle, top, outward: true);

        var bottom = layers[0].Chamfered ? chamfered! : profile;
        Zip(mesh, Band(loops[0], bottom, teeth), bore, 0f, up: false);
        ExternalTeeth(mesh, loops[0], bottom, teeth, 0f, up: false);

        Zip(mesh, Band(loops[^1], profile, teeth), hubCircle ?? bore, h, up: true);
        ExternalTeeth(mesh, loops[^1], profile, teeth, h, up: true);

        if (hubCircle is not null) Zip(mesh, hubCircle, bore, top, up: true);

        mesh = mesh.Welded();

        if (o.SetScrew > 0)
        {
            if (!hub)
                notes.Add("A set screw needs a hub to go through, so there is no hole for one.");
            else if (o.HubHeight < o.SetScrew + 1f)
                notes.Add($"The hub is too short for a {o.SetScrew:0.#} mm set screw, so there is no hole for one.");
            else if (hubRadius - boreReach < 1f)
                notes.Add("The hub wall is too thin for a set screw, so there is no hole for one.");
            else
            {
                double inner = bore is null ? 0.0 : bore.Min(p => p.Length()) * 0.5;
                double outer = hubRadius + 1.0;
                var rod = MeshTransform.Transformed(
                    Primitives.Prism(o.SetScrew / 2f, (float)(outer - inner), 24),
                    Matrix4x4.CreateRotationY(MathF.PI / 2f)
                    * Matrix4x4.CreateTranslation((float)((inner + outer) / 2.0), 0f, h + o.HubHeight / 2f));

                // Turned so the hole meets a D-shaped bore's flat, which sits on +X.
                var drilled = LocalCsg.Subtract(mesh, rod, token);
                if (drilled.CheckHealth().IsWatertight) mesh = drilled;
                else notes.Add("The set-screw hole would not cut cleanly, so it was left out.");
            }
        }

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

    private static void ExternalTeeth(Mesh mesh, Vector2[] loop, Profile p, int teeth, float z, bool up)
    {
        int n = p.Top, per = loop.Length / teeth;
        for (int k = 0; k < teeth; k++)
        {
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

    private static Vector2 Polar(double radius, double angle) =>
        new((float)(radius * Math.Cos(angle)), (float)(radius * Math.Sin(angle)));

    private static float Radians(float degrees) => degrees * MathF.PI / 180f;
}
