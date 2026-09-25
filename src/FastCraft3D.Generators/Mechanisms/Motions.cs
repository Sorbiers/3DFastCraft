using System.Numerics;
using FastCraft3D.Geometry;
using FastCraft3D.Geometry.Motion;

namespace FastCraft3D.Generators.Mechanisms;

/// <summary>
/// An external Geneva drive: a driver with a pin and a locking disc, and a wheel with slots it
/// steps round by one slot a turn, held still between. From the standard layout - the pin enters
/// each slot square, along it, so the wheel starts and stops without a knock - and then turned
/// by the motion check both ways round before it is handed back: a drive that jams is refused.
/// </summary>
public sealed class Geneva : Generator<Geneva.Settings>
{
    public override string Id => "mechanism.geneva";
    public override int Version => 1;
    public override string Category => "Mechanisms";
    public override string Title => "Geneva drive";
    public override string Summary => "A driver and a slotted wheel that it steps round one slot a turn, checked by turning it.";

    public sealed record Settings(
        [Count("Slots", 3, 8)] int Slots = 4,
        [Length("Centre distance", 20, 120, Hint = "Between the driver's axis and the wheel's")] float Distance = 40f,
        [Length("Pin", 2, 10, Hint = "The driving pin's diameter")] float Pin = 4f,
        [Length("Thickness", 3, 15)] float Thickness = 5f,
        [Length("Bore", 0, 10, Hint = "For both shafts. Nought for none.")] float Bore = 4f,
        [Clearance("Clearance", 0.15, 1, Hint = "Round the pin in the slot, and round the locking disc. Under 0.15 a printed Geneva binds.")] float Clearance = 0.3f);

    private sealed record Layout(float Crank, float Wheel, float Lock, float SlotBottom, float Beta);

    private static Layout Plan(Settings s)
    {
        float beta = MathF.PI / s.Slots;
        float crank = s.Distance * MathF.Sin(beta);
        float rp = s.Pin / 2f;
        float wheel = MathF.Sqrt(MathF.Pow(s.Distance * MathF.Cos(beta), 2) + MathF.Pow(rp + s.Clearance, 2));
        return new(crank, wheel, 0.7f * crank, s.Distance - crank - rp - s.Clearance, beta);
    }

    protected override IEnumerable<string> Check(Settings s, Printer printer)
    {
        var p = Plan(s);
        if (s.Pin / 2f > 0.25f * p.Crank) yield return $"The pin is too big for the crank: at most {0.5f * p.Crank:0.#} mm.";
        if (s.Bore / 2f + 2f > p.SlotBottom) yield return "The wheel's bore runs into its slots. A smaller bore or a bigger drive.";
        if (s.Bore / 2f + 2f > p.Lock - p.Crank * 0.2f) yield return "The driver's bore is too big for its locking disc.";
    }

    protected override Generated Build(Settings s, Printer printer, CancellationToken token)
    {
        var p = Plan(s);
        float rp = s.Pin / 2f, c = s.Clearance, t = s.Thickness;
        const float plate = 3f;
        float gap = 0.3f;

        token.ThrowIfCancellationRequested();
        var wheel = Wheel(s, p, p.Wheel, p.Lock + c, rp + c, t);

        // The driver, pin pointing along plus X: a crank plate under, the locking disc and the pin
        // in the working layer above it. The disc is cut back by everywhere the wheel can be while
        // the pin drives it: every point of the wheel is within its outer circle, whatever its
        // turn, so the band that circle sweeps - seen from the driver, turning the other way - is
        // the relief. A single circle, centred along the pin, was tried first and is only right
        // with the pin on the line of centres; seven degrees either side the wheel's tips ran into
        // the disc. The wheel cut out at each degree of the drive was tried next: the wheel lags
        // the pin by its clearance, and the lag carried its tips past the cut.
        float entry = MathF.PI / 2f - p.Beta;
        var relief = Shapes.Prism(Band(s.Distance, p.Wheel + c, entry), plate - 1f, plate + t + 1f);
        token.ThrowIfCancellationRequested();
        var driver = Shapes.Union(
            Shapes.Cylinder(p.Crank + rp + 2f, 0, plate),
            Shapes.Subtract(Shapes.Cylinder(p.Lock, plate - 0.01f, plate + t), relief),
            Shapes.Cylinder(rp, plate - 0.01f, plate + t, new Vector2(p.Crank, 0)));
        if (s.Bore > 0) driver = Shapes.Subtract(driver, Shapes.Cylinder(s.Bore / 2f, -1, plate + t + 1));

        // Put together: the driver turned to start with its pin on the far side, the wheel beside it.
        var driverTogether = Matrix4x4.CreateRotationZ(MathF.PI);
        var wheelTogether = Matrix4x4.CreateTranslation(s.Distance, 0, plate + gap);

        var made = new Generated(
            Shapes.InARow([("Geneva driver", "driver", driver, driverTogether), ("Geneva wheel", "wheel", wheel, wheelTogether)]),
            [$"Turned by the motion check: the wheel steps {360f / s.Slots:0.#} degrees for each turn of the driver, "
             + $"moving through {180f - 360f / s.Slots:0} degrees of it and held still for the rest."])
        {
            // One way only: the drive is its own mirror image across the line of centres - the
            // slots, the arcs and the relief alike - so a turn one way that runs clear is one the
            // other way too. Clockwise, which is the way the wheel's slots are laid to meet the pin.
            Motion = new Mechanism(
                [new MovingPart(0, Joint.Revolute, Vector2.Zero), new MovingPart(1, Joint.Revolute, new Vector2(s.Distance, 0))],
                Driver: 0, Layers: [plate + gap + t / 2f], Turns: -1, Step: 1, Reach: 0.3)
        };

        // The check is the same run the panel plays, so the two cannot disagree.
        var film = Films.Shoot(made, token);
        if (film.Jam is { } jam) throw new Refusal(jam);

        double stepped = Math.Abs(film.Last(1)) * 180 / Math.PI;
        if (Math.Abs(stepped - 360.0 / s.Slots) > 3)
            throw new Refusal($"The wheel turned {stepped:0} degrees in a turn of the driver, not {360.0 / s.Slots:0}.");

        return made;
    }

    /// <summary>
    /// Everything within <paramref name="reach"/> of an arc of radius <paramref name="radius"/>
    /// running from minus <paramref name="half"/> to plus it: a band with rounded ends, anticlockwise.
    /// </summary>
    private static List<Vector2> Band(float radius, float reach, float half)
    {
        var loop = new List<Vector2>();
        const int steps = 90;
        Vector2 At(float r, float a) => r * new Vector2(MathF.Cos(a), MathF.Sin(a));

        for (int i = 0; i <= steps; i++) loop.Add(At(radius + reach, -half + 2f * half * i / steps));
        var end = At(radius, half);
        for (int i = 1; i < steps; i++) loop.Add(end + At(reach, half + MathF.PI * i / steps));
        for (int i = 0; i <= steps; i++) loop.Add(At(radius - reach, half - 2f * half * i / steps));
        var start = At(radius, -half);
        for (int i = 1; i < steps; i++) loop.Add(start + At(reach, -half + MathF.PI + MathF.PI * i / steps));

        return loop;
    }

    /// <summary>
    /// The wheel at its own centre, a slot at pi less beta, so the pin arriving from the far side
    /// turning clockwise meets it square; standing from nought to <paramref name="height"/>.
    /// </summary>
    private static Mesh Wheel(Settings s, Layout p, float outside, float arcs, float slotHalf, float height)
    {
        var cuts = new List<Mesh>();
        for (int k = 0; k < s.Slots; k++)
        {
            float slot = MathF.PI - p.Beta + 2f * p.Beta * k;
            var along = new Vector2(MathF.Cos(slot), MathF.Sin(slot));
            var across = new Vector2(-along.Y, along.X);
            var a = along * p.SlotBottom;
            var b = along * (outside + 2f);
            cuts.Add(Shapes.Prism([a - across * slotHalf, b - across * slotHalf, b + across * slotHalf, a + across * slotHalf], -1, height + 1));
            cuts.Add(Shapes.Cylinder(slotHalf, -1, height + 1, a));

            // The locking arc between this slot and the next, round the driver's disc.
            float arc = slot + p.Beta;
            cuts.Add(Shapes.Cylinder(arcs, -1, height + 1, new Vector2(MathF.Cos(arc), MathF.Sin(arc)) * s.Distance));
        }

        if (s.Bore > 0) cuts.Add(Shapes.Cylinder(s.Bore / 2f, -1, height + 1));
        return Shapes.Subtract(Shapes.Cylinder(outside, 0, height), cuts);
    }

}

public enum CamLaw
{
    Cycloidal,
    Harmonic,
    [ShownAs("3-4-5 polynomial")] Polynomial345
}

public enum FollowerKind
{
    Roller,
    [ShownAs("Flat face")] Flat
}

/// <summary>
/// A disc cam from a motion: rise, dwell, return and dwell, by one of the standard laws, for a
/// roller or a flat-faced follower. The profile is the follower's envelope as the cam turns, so
/// the follower lifts by exactly the motion asked for; the largest pressure angle is said, and a
/// profile that would undercut - the roller too big for a sharp turn - is refused.
/// </summary>
public sealed class Cam : Generator<Cam.Settings>
{
    public override string Id => "mechanism.cam";
    public override int Version => 1;
    public override string Category => "Mechanisms";
    public override string Title => "Disc cam";
    public override string Summary => "A cam that lifts its follower by a rise, dwell and return you choose.";

    public sealed record Settings(
        [Length("Base circle", 5, 60, Hint = "The cam's radius where the follower rests at its lowest")] float Base = 15f,
        [Length("Rise", 1, 40)] float Rise = 10f,
        [Angle("Rising over", 30, 240, Group = "Motion")] float RiseAngle = 120f,
        [Angle("Dwell at the top", 0, 180, Group = "Motion")] float Dwell = 60f,
        [Angle("Returning over", 30, 240, Group = "Motion")] float ReturnAngle = 120f,
        [Choice("Law", Group = "Motion", Hint = "Cycloidal starts and stops most gently; harmonic is smoothest in the middle")] CamLaw Law = CamLaw.Cycloidal,
        [Choice("Follower", Group = "Follower")] FollowerKind Follower = FollowerKind.Roller,
        [Length("Roller radius", 2, 15, Group = "Follower"), ShowWhen(nameof(Follower), FollowerKind.Roller)] float Roller = 5f,
        [Length("Thickness", 3, 20)] float Thickness = 6f,
        [Length("Bore", 0, 12)] float Bore = 5f);

    protected override IEnumerable<string> Check(Settings s, Printer printer)
    {
        if (s.RiseAngle + s.Dwell + s.ReturnAngle > 360) yield return "Rise, dwell and return come to more than a turn.";
        if (s.Bore / 2f + 2f > s.Base) yield return "The bore is too big for the base circle.";
    }

    /// <summary>The follower's lift at the cam's turn <paramref name="theta"/>, and its rate per radian.</summary>
    public static (double Lift, double Rate) Motion(Settings s, double theta)
    {
        double rise = s.RiseAngle * Math.PI / 180, dwell = s.Dwell * Math.PI / 180, back = s.ReturnAngle * Math.PI / 180;
        theta %= 2 * Math.PI;
        if (theta < 0) theta += 2 * Math.PI;

        (double F, double D) Law(double u) => s.Law switch
        {
            CamLaw.Cycloidal => (u - Math.Sin(2 * Math.PI * u) / (2 * Math.PI), 1 - Math.Cos(2 * Math.PI * u)),
            CamLaw.Harmonic => ((1 - Math.Cos(Math.PI * u)) / 2, Math.PI / 2 * Math.Sin(Math.PI * u)),
            _ => (10 * u * u * u - 15 * Math.Pow(u, 4) + 6 * Math.Pow(u, 5), 30 * u * u - 60 * u * u * u + 30 * Math.Pow(u, 4))
        };

        if (theta < rise)
        {
            var (f, d) = Law(theta / rise);
            return (s.Rise * f, s.Rise * d / rise);
        }

        if (theta < rise + dwell) return (s.Rise, 0);

        if (theta < rise + dwell + back)
        {
            var (f, d) = Law((theta - rise - dwell) / back);
            return (s.Rise * (1 - f), -s.Rise * d / back);
        }

        return (0, 0);
    }

    protected override Generated Build(Settings s, Printer printer, CancellationToken token)
    {
        const int samples = 720;
        var profile = new List<Vector2>(samples);
        double worst = 0;

        for (int i = 0; i < samples; i++)
        {
            double theta = 2 * Math.PI * i / samples;
            var (lift, rate) = Motion(s, theta);

            // The follower rides straight up the cam's plus Y; the cam turns under it, so the
            // profile at the cam's own angle theta is what faces the follower after turning by theta.
            Vector2 point;
            if (s.Follower == FollowerKind.Roller)
            {
                double pitch = s.Base + s.Roller + lift;
                worst = Math.Max(worst, Math.Abs(Math.Atan2(rate, pitch)));

                // Inside the pitch curve by the roller's radius, along its normal.
                var c = new Vector2((float)(pitch * Math.Cos(theta)), (float)(pitch * Math.Sin(theta)));
                var d = new Vector2((float)(rate * Math.Cos(theta) - pitch * Math.Sin(theta)), (float)(rate * Math.Sin(theta) + pitch * Math.Cos(theta)));
                var normal = Vector2.Normalize(new Vector2(d.Y, -d.X));
                point = c - normal * s.Roller;
            }
            else
            {
                double r = s.Base + lift;
                point = new Vector2((float)(r * Math.Cos(theta) - rate * Math.Sin(theta)), (float)(r * Math.Sin(theta) + rate * Math.Cos(theta)));
            }

            profile.Add(point);
        }

        // An envelope that folds back on itself is a follower that cannot follow.
        if (Polygon2.SignedArea(profile) <= 0 || Folds(profile))
            throw new Refusal(s.Follower == FollowerKind.Roller
                ? "The roller is too big for how sharply this cam turns: the profile would undercut. A smaller roller, a bigger base circle or a gentler motion."
                : "A flat follower cannot follow a motion this sharp: the profile would fold. A bigger base circle or a gentler motion.");

        token.ThrowIfCancellationRequested();
        var cam = Shapes.Prism(profile, s.Bore > 0 ? [Shapes.Circle(s.Bore / 2f)] : [], 0, s.Thickness);

        // The follower: a rounded tip the size of the roller, or a flat foot, on a stem.
        const float stem = 30f;
        float width = s.Follower == FollowerKind.Roller ? 2 * s.Roller : 2 * (float)MaxRate(s) + 6f;
        var follower = s.Follower == FollowerKind.Roller
            ? Shapes.Union(Shapes.Cylinder(s.Roller, 0, s.Thickness), Shapes.Box(-s.Roller / 2f, 0, 0, s.Roller / 2f, stem, s.Thickness))
            : Shapes.Union(Shapes.Box(-width / 2f, 0, 0, width / 2f, 3f, s.Thickness), Shapes.Box(-2.5f, 2.9f, 0, 2.5f, stem, s.Thickness));

        // Resting on the cam at the start of the rise, with a hair between.
        float rest = s.Follower == FollowerKind.Roller ? s.Base + s.Roller + 0.1f : s.Base + 0.1f;
        var parts = Shapes.InARow(
        [
            ("Cam", "cam", cam, Matrix4x4.CreateRotationZ(MathF.PI / 2f)),
            ("Follower", "follower", follower, Matrix4x4.CreateTranslation(0, rest, 0))
        ]);

        var notes = new List<string> { $"{s.Rise:0.#} mm of lift. The follower slides in a guide of your own, straight up the cam's middle." };
        if (s.Follower == FollowerKind.Roller)
        {
            double degrees = worst * 180 / Math.PI;
            notes.Add($"The largest pressure angle is {degrees:0} degrees{(degrees > 30 ? " - over 30, it will bind: a bigger base circle or a slower rise" : "")}.");
        }

        // The cam turns clockwise, so the profile meets the follower in the order it was laid:
        // rise, dwell, return. The follower rides up when pushed and back down on its spring.
        return new Generated(parts, notes)
        {
            Motion = new Mechanism(
                [new MovingPart(0, Joint.Revolute, Vector2.Zero), new MovingPart(1, Joint.Prismatic, default, Vector2.UnitY, Reach: 3, Returns: true)],
                Driver: 0, Layers: [s.Thickness / 2f], Turns: -1, Step: 1)
        };
    }

    private static double MaxRate(Settings s) => Enumerable.Range(0, 360).Max(i => Math.Abs(Motion(s, i * Math.PI / 180).Rate));

    /// <summary>Whether consecutive steps of the profile ever turn back on themselves.</summary>
    private static bool Folds(List<Vector2> loop)
    {
        for (int i = 0; i < loop.Count; i++)
        {
            var a = loop[(i + loop.Count - 1) % loop.Count];
            var b = loop[i];
            var c = loop[(i + 1) % loop.Count];
            if (Vector2.Dot(b - a, c - b) < 0) return true;
        }

        return false;
    }
}
