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
        [Length("Center distance", 20, 120, Hint = "Between the driver's axis and the wheel's")] float Distance = 40f,
        [Length("Pin", 2, 10, Hint = "The driving pin's diameter")] float Pin = 4f,
        [Length("Thickness", 3, 15)] float Thickness = 5f,
        [Length("Bore", 0, 10, Hint = "For both shafts. Nought for none.")] float Bore = 4f,
        [Clearance("Clearance", 0.15, 1, Hint = "Round the pin in the slot, and round the locking disk. Under 0.15 a printed Geneva binds.")] float Clearance = 0.3f,
        [Toggle("Working model", Group = "Working model", Hint = "A model to turn by hand: the driver and the wheel keyed on D-shafts the bore's size through a base, a crank on the driver")] bool Demo = false,
        [Toggle("Lay out for printing", Hint = "On: the parts side by side, ready to print. Off: put down assembled, as the preview shows it.")] bool Organise = true);

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
        if (s.Bore / 2f + 2f > p.Lock - p.Crank * 0.2f) yield return "The driver's bore is too big for its locking disk.";
        if (s.Demo)
        {
            float hole = new Demo(printer, s.Bore).HoleRadius;
            if (hole + 2f > p.SlotBottom || hole + 2f > p.Lock - p.Crank * 0.2f)
                yield return "There is no room for D-shafts: a bigger center distance, or a smaller bore.";

            // The wheel's boss stands up past the driver's crank plate, which reaches under the wheel.
            else if (p.Crank + s.Pin / 2f + 2f + hole + 2.5f + Demo.Gap > s.Distance)
                yield return "The driver's plate would run into the wheel's shaft: a bigger center distance, or a smaller pin or bore.";
        }
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
        if (s.Bore > 0 && !s.Demo) driver = Shapes.Subtract(driver, Shapes.Cylinder(s.Bore / 2f, -1, plate + t + 1));

        // Put together: the driver turned to start with its pin on the far side, the wheel beside it.
        float lift = s.Demo ? Demo.Lift : 0f;
        var driverTogether = Matrix4x4.CreateRotationZ(MathF.PI) * Matrix4x4.CreateTranslation(0, 0, lift);
        var wheelTogether = Matrix4x4.CreateTranslation(s.Distance, 0, lift + plate + gap);

        var set = new List<(string, string, Mesh, Matrix4x4?)> { ("Geneva driver", "driver", driver, driverTogether), ("Geneva wheel", "wheel", wheel, wheelTogether) };
        List<GeneratedPart> laid;
        List<(int, int)>? riders = null;
        string? keyed = null;
        if (s.Demo)
        {
            // Each on a D-shaft of its own through a base, a crank on the driver.
            var demo = new Demo(printer, s.Bore);
            foreach (var (name, role, mesh, together) in set) demo.Lying(name, role, MeshTransform.Transformed(mesh, together!.Value));
            demo.Upright(Vector2.Zero, [0], handle: true);
            demo.Upright(new Vector2(s.Distance, 0), [1]);
            token.ThrowIfCancellationRequested();
            laid = demo.Finish(token);
            riders = demo.Riders;
            keyed = $"Both go on {demo.Size:0.#} mm D-shafts, in from under the base; the crank goes on the driver's last.";
        }
        else
        {
            laid = Shapes.InARow(set);
        }

        var made = new Generated(
            laid,
            [$"Turned by the motion check: the wheel steps {360f / s.Slots:0.#} degrees for each turn of the driver, "
             + $"moving through {180f - 360f / s.Slots:0} degrees of it and held still for the rest."])
        {
            // One way only: the drive is its own mirror image across the line of centres - the
            // slots, the arcs and the relief alike - so a turn one way that runs clear is one the
            // other way too. Clockwise, which is the way the wheel's slots are laid to meet the pin.
            Motion = new Mechanism(
                [new MovingPart(0, Joint.Revolute, Vector2.Zero), new MovingPart(1, Joint.Revolute, new Vector2(s.Distance, 0))],
                Driver: 0, Layers: [lift + plate + gap + t / 2f], Turns: -1, Step: 1, Reach: 0.3, Riders: riders),
            LaidOut = s.Organise
        };

        // The check is the same run the panel plays, so the two cannot disagree.
        var film = Films.Shoot(made, token);
        if (film.Jam is { } jam) throw new Refusal(jam);

        double stepped = Math.Abs(film.Last(1)) * 180 / Math.PI;
        if (Math.Abs(stepped - 360.0 / s.Slots) > 3)
            throw new Refusal($"The wheel turned {stepped:0} degrees in a turn of the driver, not {360.0 / s.Slots:0}.");

        return keyed is null ? made : made with { Notes = [.. made.Notes, keyed] };
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

        if (s.Bore > 0 && !s.Demo) cuts.Add(Shapes.Cylinder(s.Bore / 2f, -1, height + 1));
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
    public override string Title => "Disk cam";
    public override string Summary => "A cam that lifts its follower by a rise, dwell and return you choose.";

    public sealed record Settings(
        [Length("Base circle", 5, 60, Hint = "The cam's radius where the follower rests at its lowest")] float Base = 15f,
        [Length("Rise", 1, 40)] float Rise = 10f,
        [Angle("Rise angle", 30, 240, Group = "Motion")] float RiseAngle = 120f,
        [Angle("Dwell at the top", 0, 180, Group = "Motion")] float Dwell = 60f,
        [Angle("Return angle", 30, 240, Group = "Motion")] float ReturnAngle = 120f,
        [Choice("Motion profile", Group = "Motion", Hint = "Cycloidal starts and stops most gently; harmonic is smoothest in the middle")] CamLaw Law = CamLaw.Cycloidal,
        [Choice("Follower", Group = "Follower")] FollowerKind Follower = FollowerKind.Roller,
        [Length("Roller radius", 2, 15, Group = "Follower"), ShowWhen(nameof(Follower), FollowerKind.Roller)] float Roller = 5f,
        [Length("Thickness", 3, 20)] float Thickness = 6f,
        [Length("Bore", 0, 12)] float Bore = 5f,
        [Toggle("Working model", Group = "Working model", Hint = "A model to turn by hand: the cam keyed on a D-shaft through a base, a crank on it, the follower in a channel with pegs for a rubber band to bring it back")] bool Demo = false,
        [Toggle("Lay out for printing", Hint = "On: the parts side by side, ready to print. Off: put down assembled, as the preview shows it.")] bool Organise = true);

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
        bool based = s.Demo;
        var cam = Shapes.Prism(profile, s.Bore > 0 && !based ? [Shapes.Circle(s.Bore / 2f)] : [], 0, s.Thickness);

        // The follower: a rounded tip the size of the roller, or a flat foot, on a stem - long
        // enough, in a demonstration, to stay between its rails the whole way up and down.
        float tip = s.Follower == FollowerKind.Roller ? s.Roller : 3f;
        float stem = based ? MathF.Max(40f, s.Rise + tip + 30f) : 30f;
        float width = s.Follower == FollowerKind.Roller ? 2 * s.Roller : 2 * (float)MaxRate(s) + 6f;
        var follower = s.Follower == FollowerKind.Roller
            ? Shapes.Union(Shapes.Cylinder(s.Roller, 0, s.Thickness), Shapes.Box(-s.Roller / 2f, 0, 0, s.Roller / 2f, stem, s.Thickness))
            : Shapes.Union(Shapes.Box(-width / 2f, 0, 0, width / 2f, 3f, s.Thickness), Shapes.Box(-2.5f, 2.9f, 0, 2.5f, stem, s.Thickness));

        // Resting on the cam at the start of the rise, with a hair between.
        float rest = s.Follower == FollowerKind.Roller ? s.Base + s.Roller + 0.1f : s.Base + 0.1f;
        if (based) return Demonstrate(s, printer, cam, follower, rest, stem, tip, worst, token);

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
            Motion = Moves(0f, s),
            LaidOut = s.Organise
        };
    }

    private static Mechanism Moves(float lift, Settings s, IReadOnlyList<(int, int)>? riders = null) => new(
        [new MovingPart(0, Joint.Revolute, Vector2.Zero), new MovingPart(1, Joint.Prismatic, default, Vector2.UnitY, Reach: 3, Returns: true)],
        Driver: 0, Layers: [lift + s.Thickness / 2f], Turns: -1, Step: 1, Riders: riders);

    /// <summary>
    /// The cam as a model to turn by hand: keyed on a D-shaft through a base with a crank on
    /// top; a peg on the follower's stem and two on the base for a rubber band, which is the spring
    /// that brings the follower back down the cam. The follower is dropped between open rails and
    /// held down by a cap pressed onto posts over them: a lip along each rail would print hanging
    /// in the air, and a roof printed with the base left no way to get the follower in.
    /// </summary>
    private static Generated Demonstrate(Settings s, Printer printer, Mesh cam, Mesh follower, float rest, float stem, float tip, double worst, CancellationToken token)
    {
        var demo = new Demo(printer, s.Bore);
        float lift = Demo.Lift, top = lift + s.Thickness, c = demo.Run, rail = 2f;
        float half = s.Follower == FollowerKind.Roller ? s.Roller / 2f : 2.5f;

        // Three pegs for the rubber band - one on the stem, two on the base - each with a hole in
        // its top for a band holder: a pin with a flat head wider than the peg, pressed in once the
        // band is on, so the band cannot ride up off the peg. Separate, and printed head down, so
        // the head is a flat disc on the plate rather than a brim hanging off a peg.
        float pegTop = top + 6f, pin = 1f;
        Mesh Socket(Vector2 at) => Shapes.Cylinder(pin + demo.Grip, pegTop - 4f, pegTop + 1f, at, 24);
        var stemPeg = new Vector2(0, stem - 4f);
        var peg = Shapes.Subtract(Shapes.Cylinder(2.5f, s.Thickness - 0.01f, s.Thickness + 6f, stemPeg),
            Shapes.Cylinder(pin + demo.Grip, s.Thickness + 2f, s.Thickness + 7f, stemPeg, 24));
        demo.Lying("Cam", "cam", Shapes.Moved(Shapes.Turned(cam, MathF.PI / 2f), 0, 0, lift));
        demo.Lying("Follower", "follower", Shapes.Moved(Shapes.Union(follower, peg), 0, rest, lift));
        demo.Upright(Vector2.Zero, [0], handle: true);

        // The channel above where the roller or the foot reaches at the top of the rise, so only
        // the stem passes between its rails.
        // It ends short of the peg on the stem's end, so the peg never runs under a lip.
        float from = rest + s.Rise + tip + 1f, to = rest + stem - 8f;
        demo.AddToBase(Shapes.Box(-half - c, from, Demo.Plate - 0.01f, half + c, to, lift - Demo.Gap));
        // Open rails the follower is dropped between, and a cap pressed onto four posts over
        // them to hold it down. A lip on each rail would print hanging in the air, and a roof
        // printed with the base would leave no way to get the follower in.
        float wide = half + c + rail, cap = top + Demo.Gap, post = 1.5f;
        demo.AddToBase(Shapes.Box(-wide, from, Demo.Plate - 0.01f, -half - c, to, cap));
        demo.AddToBase(Shapes.Box(half + c, from, Demo.Plate - 0.01f, wide, to, cap));

        var holes = new List<Mesh>();
        foreach (float side in new[] { -1f, 1f })
        {
            foreach (float y in new[] { from + 2.5f, to - 2.5f })
            {
                var at = new Vector2(side * (wide + post + 0.5f), y);
                demo.AddToBase(Shapes.Cylinder(post, Demo.Plate - 0.01f, cap + 1.7f, at, 24));
                holes.Add(Shapes.Cylinder(post + demo.Grip, cap - 1, cap + 3f, at, 24));
            }

            var spot = new Vector2(side * (wide + 9f), from + 3f);
            demo.AddToBase(Shapes.Cylinder(2.5f, Demo.Plate - 0.01f, pegTop, spot));
            demo.CutFromBase(Socket(spot));
            Holder(spot, rides: false);
        }

        demo.Lying("Guide cap", "guide cap", Shapes.Subtract(Shapes.Box(-wide - 2 * post - 2f, from, cap, wide + 2 * post + 2f, to, cap + 2f), holes));
        Holder(stemPeg + new Vector2(0, rest), rides: true);

        void Holder(Vector2 at, bool rides)
        {
            // A neck between the peg's top and the head for the band to sit round, held between
            // the two: on the head alone, it slid down the peg.
            float neck = 4f;
            var holder = Shapes.Union(
                Shapes.Cylinder(pin, pegTop - 3.7f, pegTop + 0.06f, at, 24),
                Shapes.Cylinder(1.6f, pegTop + 0.05f, pegTop + neck + 0.06f, at),
                Shapes.Cylinder(4.5f, pegTop + neck + 0.05f, pegTop + neck + 1.25f, at));
            int i = demo.Placed("Band holder", "band holder", holder,
                Matrix4x4.CreateTranslation(-at.X, -at.Y, -(pegTop + neck + 1.25f)) * Matrix4x4.CreateRotationX(MathF.PI));

            // The stem's holder moves with the follower.
            if (rides) demo.Riders.Add((i, 1));
        }

        demo.Cover(-half - 12f, from, half + 12f, rest + stem + s.Rise);
        token.ThrowIfCancellationRequested();
        var parts = demo.Finish(token);

        var notes = new List<string>
        {
            $"{s.Rise:0.#} mm of lift, on a {demo.Size:0.#} mm D-shaft the cam grips.",
            "Drop the follower's stem between the rails and press the guide cap down onto the four posts over it; then the cam onto its shaft, and the crank. A rubber band round the peg on the stem and the two on the base brings the follower back down; press a band holder into the top of each peg, and hook the band round the holders' necks."
        };
        if (s.Follower == FollowerKind.Roller)
            notes.Add($"The largest pressure angle is {worst * 180 / Math.PI:0} degrees.");

        return new Generated(parts, notes) { Motion = Moves(lift, s, demo.Riders), LaidOut = s.Organise };
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
