using System.Numerics;

namespace FastCraft3D.Geometry;

/// <summary>The outside of a reciprocating frame.</summary>
public enum FrameEnds
{
    /// <summary>Half circles round each end, as a mangle rack is drawn.</summary>
    Round,

    /// <summary>A plain rectangle.</summary>
    Square
}

/// <summary>
/// A reciprocating frame laid out flat, with how it moves: what the frame's solid is made from,
/// and what a test drives it by.
///
/// Everything is at the gear's turn of nought. The frame's own coordinates have the gear's centre
/// travelling along the X axis between the two places it parks, x = 0 and x = -Travel; at a turn
/// of psi, anticlockwise, the gear's centre is at <see cref="GearAt"/>. <see cref="Cavity"/>,
/// <see cref="LockCavity"/> and <see cref="Outline"/> have the same number of points and run the
/// same way, one point each on a common set of rays, which is how the faces between them are
/// laid.
/// </summary>
/// <param name="Gear">The cut-away gear's outline in the tooth layer.</param>
/// <param name="Lock">The lock disc on top of the gear, or null with no lock.</param>
/// <param name="Cavity">The inside of the frame's tooth layer.</param>
/// <param name="LockCavity">The inside of the frame's lock layer, or null with no lock.</param>
/// <param name="Outline">The frame's outside.</param>
/// <param name="Travel">How far the frame slides each way.</param>
/// <param name="PushFrom">The turn at which a push begins; the other push begins half a turn later.</param>
/// <param name="PushAngle">How far the gear turns during one push.</param>
/// <param name="Shown">The turn the pair is shown at: parked, halfway through a dwell.</param>
/// <param name="LockPlay">About how far the parked frame can move either way, from the lock's geometry.</param>
public sealed record FramePlan(
    IReadOnlyList<Vector2> Gear,
    IReadOnlyList<Vector2>? Lock,
    IReadOnlyList<Vector2> Cavity,
    IReadOnlyList<Vector2>? LockCavity,
    IReadOnlyList<Vector2> Outline,
    double Travel,
    double PushFrom,
    double PushAngle,
    double Shown,
    double LockPlay,
    Func<double, double> Slide,
    IReadOnlyList<string> Notes)
{
    /// <summary>The gear's centre, in the frame's coordinates, once the gear has turned by <paramref name="psi"/>.</summary>
    public Vector2 GearAt(double psi) => new((float)-Slide(psi), 0f);

    /// <summary>Whether the frame is meant to be standing still at this turn.</summary>
    public bool Parked(double psi)
    {
        double u = psi - PushFrom;
        u -= Math.Floor(u / Math.PI) * Math.PI;
        return u > PushAngle;
    }
}

public static partial class Gears
{
    /// <summary>
    /// The least a frame is left parked at each end, as a turn of the gear. The lock works by
    /// handing the frame from one lobe to the other while it is parked, and much less than this
    /// leaves it nothing to hand over with.
    /// </summary>
    private const double LeastDwell = 12.0 * Math.PI / 180.0;

    /// <summary>How far inside the tooth layer's band the lock disc stops, so the two layers' walls never lie on top of each other.</summary>
    private const double LockStep = 0.4;

    /// <summary>The spacing of the rays the frame's inside is measured along.</summary>
    private const double RayStep = 0.02;

    /// <summary>How closely the gear's outline is sampled when its sweep is taken.</summary>
    private const double SweepSpacing = 0.05;

    /// <summary>Added to every measurement of the sweep, for what falls between two samples of it.</summary>
    private const double SweepMargin = 0.02;

    /// <summary>
    /// The reciprocating frame for a cut-away gear, or null with the reason it cannot have one.
    ///
    /// The frame is generated rather than drawn, the way the gear's own teeth are: the gear is
    /// run through a whole turn of the motion it is meant to give - push, park, push back, park -
    /// and the frame is everything it never reaches, less the clearance. Drawing it was tried
    /// first, as two racks a pitch diameter apart joined by half circles, with the teeth laid by
    /// arc length. That took the travel to be the sector's own teeth times the pitch, when a
    /// sector stays in mesh for its contact ratio past that and its last tip then drags the rack
    /// until it slips off: a five-tooth sector of module 2 drives 42.7 mm, not 31.4. The frame
    /// ran off the end of one run or met the other half a tooth out, and jammed both ways. A
    /// generated frame has no phase to get wrong - the second run's teeth are wherever the gear
    /// finds them - and works with an odd number of teeth as well as an even one.
    ///
    /// A generated frame can still slide back along the path the gear came by, since that path is
    /// clear by definition: without a lock, a parked frame was measured moving 8.7 mm. So there is
    /// a lock as well, in a layer of its own on top: a disc on the gear, and on the frame a rail
    /// either side of the path with a pocket round each place it parks. Here the frame is drawn
    /// and the disc generated from it - the gear is whatever the rails never reach - because the
    /// other way round the rails' corners were cut off as they came in to park and nothing was
    /// left to hold with. A lock in the tooth layer was tried too and held nothing: the sector,
    /// turning past while parked, clears everything a lobe could hold against. See LockRadii.
    /// </summary>
    public static FramePlan? PlanFrame(GearOptions options, out string? refusal)
    {
        var o = options.Sane();
        refusal = null;

        int kept = o.KeptTeeth;
        if (kept <= 0 || kept >= o.Teeth)
        {
            refusal = "A frame needs a gear cut away to fewer teeth than it has.";
            return null;
        }

        if (o.Form != ToothForm.Straight)
        {
            refusal = "No frame: it needs straight teeth, since its runs are cut for them.";
            return null;
        }

        double m = o.Module, tau = 2.0 * Math.PI / o.Teeth;
        double c = o.FrameClearance;

        var motion = FrameMotion.Of(o, kept);
        double dwell = Math.PI - motion.Window;

        if (dwell < LeastDwell)
        {
            // Every tooth more on the sector is a tooth's turn more of pushing; the rest is fixed.
            int most = kept - (int)Math.Ceiling((LeastDwell - dwell) / tau);
            refusal = most >= 1
                ? $"No frame: {kept} teeth push for so much of each half turn that the frame never parks. Keep {most} or fewer."
                : "No frame: even one tooth pushes for too much of each half turn on a gear this small.";
            return null;
        }

        var whole = ExternalProfile(o, o.Teeth, 0.0, null);
        var relieved = ExternalProfile(o, o.Teeth, Relief * m, whole);
        Tooth What(int k) => k >= kept ? Tooth.Gone : k == 0 || k == kept - 1 ? Tooth.Relieved : Tooth.Whole;
        var gear = PartialLoop(whole, relieved, o.Teeth, 0.0, What);

        double travel = motion.Travel;
        var sweep = Sweep(gear, motion, c);
        var notes = new List<string>();

        // --- The lock -------------------------------------------------------------------

        float lockHeight = o.LockHeight;
        double lockR = whole.BandReach - LockStep;
        double railR = lockR, beta = 0.0;
        double[]? lockRadii = null;

        if (lockHeight > 0f)
        {
            // Each lobe holds the frame by a corner of the rails, where a rail meets a pocket, and
            // hands it to the other lobe partway through the park. A corner further round holds
            // more firmly, but the two lobes then cover it for less of the park; past half the
            // park, less a little, there is a gap in the middle where neither does.
            beta = Math.Clamp((dwell - 4.0 * Math.PI / 180.0) / 2.0, 3.0 * Math.PI / 180.0, 40.0 * Math.PI / 180.0);

            // The two pockets' corners must not run into each other on a short frame.
            double roomForCorner = travel / 2.0 - 0.5;
            if ((lockR + c) * Math.Sin(beta) > roomForCorner)
                beta = Math.Asin(Math.Clamp(roomForCorner / (lockR + c), 0.0, 1.0));

            if (beta < 3.0 * Math.PI / 180.0 - 1e-9)
            {
                notes.Add("No lock: the frame is too short for one.");
                lockHeight = 0f;
            }
            else
            {
                railR = (lockR + c) * Math.Cos(beta) - c;
                lockRadii = LockRadii(motion, travel, lockR, railR, c);

                var bore = BoreLoop(o);
                double boreReach = bore?.Max(p => p.Length()) ?? 0.0;
                double hubRadius = o.HubDiameter > 0 && o.HubHeight > 0 ? o.HubDiameter / 2.0 : 0.0;

                if (lockRadii.Min() < Math.Max(boreReach + LeastWall, hubRadius + 0.2))
                {
                    notes.Add("No lock: the bore or hub leaves no room for one inside the roots.");
                    lockRadii = null;
                    lockHeight = 0f;
                }
            }
        }

        // --- The frame's outlines, on one set of rays -------------------------------------

        var rays = FrameRays(sweep, travel, o.Rim, o.FrameEnds, lockRadii is null ? null : (lockR + c, railR + c));
        var keep = RaysWorthKeeping(rays);

        var cavity = keep.Select(i => rays[i].Point(rays[i].Tooth)).ToList();
        var outline = keep.Select(i => rays[i].Point(rays[i].Outer)).ToList();
        var lockCavity = lockRadii is null ? null : keep.Select(i => rays[i].Point(rays[i].Lock)).ToList();

        List<Vector2>? lockDisc = null;
        if (lockRadii is not null)
        {
            var round = Enumerable.Range(0, lockRadii.Length)
                .Select(k => Polar(lockRadii[k], 2.0 * Math.PI * k / lockRadii.Length)).ToList();
            var wanted = new bool[round.Count];
            Simplify(round, wanted);
            lockDisc = round.Where((_, k) => wanted[k]).ToList();
        }

        // --- What to say about it ---------------------------------------------------------

        double shown = motion.Start + motion.Window + dwell / 2.0;
        double play = lockRadii is null ? double.NaN : c / Math.Sin(beta);
        double lean = Radians(o.PressureAngle);

        notes.Insert(0, $"Slides {travel:0.#} mm each way: one turn of the gear takes it there and back.");
        notes.Insert(1, $"Moving for {100.0 * motion.Window / Math.PI:0}% of each turn and parked for the rest, half at each end.");
        notes.Add(lockRadii is not null
            ? $"A {lockHeight:0.#} mm lock on top holds it while parked, to within about {play:0.#} mm either way."
            : "Nothing holds it while it is parked but friction.");
        notes.Add($"{c:0.##} mm clearance all round: about {2.0 * c / Math.Cos(lean):0.##} mm of play along the run while it is driven.");
        notes.Add("Made to turn anticlockwise, seen from above as it prints.");
        if (lockRadii is not null)
            notes.Add("The frame prints with its lock side down: turn it over to fit it round the gear.");

        var box = Bounds2(outline);
        notes.Add($"Frame {box.X:0.#} by {box.Y:0.#} mm, {o.ForMate().Thickness + lockHeight:0.#} mm thick, "
                + (o.FrameEnds == FrameEnds.Round ? "round ends." : "square ends.")
                + " Shown parked at one end.");
        notes.Add("Its arms are a plain bar: add one with Cube and Merge.");

        return new FramePlan(
            gear, lockDisc, cavity, lockCavity, outline,
            travel, motion.Start, motion.Window, shown, play, motion.Slide, notes);

        static Vector2 Bounds2(List<Vector2> loop) =>
            new(loop.Max(p => p.X) - loop.Min(p => p.X), loop.Max(p => p.Y) - loop.Min(p => p.Y));
    }

    /// <summary>
    /// The frame's solid, built where it prints, with where it goes round the gear to be looked at.
    ///
    /// The tooth layer is at the bottom and the lock layer on top, as round the gear. It prints the
    /// other way up: the lock layer's inside is the smaller of the two, so standing on the tooth
    /// layer it would overhang the gear's path by several millimetres, and standing on the lock
    /// layer nothing overhangs at all.
    /// </summary>
    private static (Mesh Print, Matrix4x4 InMesh) FrameSolid(FramePlan plan, GearOptions o, float lockHeight, float gearReach)
    {
        float t = o.ForMate().Thickness, h = o.Thickness;
        bool locked = plan.LockCavity is not null && lockHeight > 0f;
        float top = t + (locked ? lockHeight : 0f);

        var outline = plan.Outline;
        var cavity = plan.Cavity;
        var mesh = new Mesh();

        Band2(mesh, outline, cavity, 0f, up: false);
        Wall(mesh, outline, 0f, outline, top, outward: true);
        Wall(mesh, cavity, 0f, cavity, t, outward: false);

        if (locked)
        {
            var lockCavity = plan.LockCavity!;
            Band2(mesh, cavity, lockCavity, t, up: false);
            Wall(mesh, lockCavity, t, lockCavity, top, outward: false);
            Band2(mesh, outline, lockCavity, top, up: true);
        }
        else
        {
            Band2(mesh, outline, cavity, t, up: true);
        }

        var built = mesh.Welded();

        // Round the gear: slid along to the end it is parked at, and raised or lowered so the two
        // lock layers are level whatever the frame's own thickness.
        var assembled = Matrix4x4.CreateTranslation((float)plan.Slide(plan.Shown), 0f, h - t);

        var turned = locked
            ? Matrix4x4.CreateRotationX(MathF.PI) * Matrix4x4.CreateTranslation(0f, 0f, top)
            : Matrix4x4.Identity;
        var laid = MeshTransform.Transformed(built, turned);
        float aside = gearReach + 3f - laid.ComputeBounds().Min.X;
        var print = turned * Matrix4x4.CreateTranslation(aside, 0f, 0f);

        Matrix4x4.Invert(print, out var back);
        return (MeshTransform.Transformed(built, print), back * assembled);
    }

    /// <summary>The faces between two loops laid on the same rays, point for point.</summary>
    private static void Band2(Mesh mesh, IReadOnlyList<Vector2> outer, IReadOnlyList<Vector2> inner, float z, bool up)
    {
        for (int i = 0; i < outer.Count; i++)
        {
            int j = (i + 1) % outer.Count;
            Face(mesh, outer[i], outer[j], inner[j], z, up);
            Face(mesh, outer[i], inner[j], inner[i], z, up);
        }
    }

    // --- The motion -----------------------------------------------------------------------

    /// <summary>
    /// How a cut-away gear turning anticlockwise moves its frame, as the gear turns by psi.
    ///
    /// The frame stands still until the sector's leading tooth reaches the start of the line of
    /// action - where the rack's tips cross it - and from there moves as a rack in mesh does, by
    /// the pitch radius for each radian, until the trailing tooth leaves the line at its tip. Then
    /// that tooth's tip corner goes on pushing the rack tooth's flank, more and more slowly, until
    /// it slips past the rack tooth's tip. That last part is easy to leave out and is not small:
    /// on a module 2 gear it is 7 mm, more than a pitch.
    ///
    /// Half a turn later the same happens on the other run, back again.
    /// </summary>
    private sealed class FrameMotion
    {
        private double radius, tipRadius, tipHalf, lean, ex, ey, rolled;

        /// <summary>The turn at which the bottom run's push begins.</summary>
        public double Start { get; private set; }

        /// <summary>The turn at which the trailing tooth leaves the line of action.</summary>
        public double Contact { get; private set; }

        /// <summary>The turn at which the trailing tooth's tip slips off the rack tooth, and the push ends.</summary>
        public double Release { get; private set; }

        public double Travel { get; private set; }

        public double PitchRadius => radius;

        /// <summary>How far the gear turns during one push.</summary>
        public double Window => Release - Start;

        public static FrameMotion Of(GearOptions o, int kept)
        {
            double m = o.Module, r = m * o.Teeth / 2.0, pressure = Radians(o.PressureAngle);
            double baseRadius = r * Math.Cos(pressure), tau = 2.0 * Math.PI / o.Teeth;

            // The first and last teeth are the shortened ones, whatever the sector's size.
            double shrink = Relief * m;
            var relieved = ExternalProfile(o, o.Teeth, shrink, null);
            double Half(double rho) => ToothHalfAngle(m, o.Teeth, pressure, o.Backlash, rho) - shrink / rho;
            double tip = relieved.Radii[^1];

            // Where contact starts: the rack's tips, an addendum inside the pitch line, cross the
            // line of action this far before the pitch point.
            double approach = m / Math.Sin(pressure);
            double ax = -approach * Math.Cos(pressure), ay = -r + approach * Math.Sin(pressure);
            double start = Math.Atan2(ay, ax) - Half(Math.Sqrt(ax * ax + ay * ay)) - (kept - 1) * tau;

            // Where it ends: the trailing tooth's tip reaches the line of action.
            double recess = Math.Sqrt(tip * tip - baseRadius * baseRadius) - r * Math.Sin(pressure);
            double ex = recess * Math.Cos(pressure), ey = -r - recess * Math.Sin(pressure);
            double tipHalf = Half(tip);
            double contact = Math.Atan2(ey, ex) - tipHalf;

            // And where the dragged rack tooth lets go: the tip corner rises past the rack's tips.
            double release = -Math.Asin((r - m) / tip) - tipHalf;

            var motion = new FrameMotion
            {
                radius = r, tipRadius = tip, tipHalf = tipHalf, lean = Math.Tan(pressure), ex = ex, ey = ey,
                Start = start, Contact = contact, Release = release
            };
            motion.rolled = r * (contact - start);
            motion.Travel = motion.rolled + motion.Drag(release + tipHalf);
            return motion;
        }

        /// <summary>How far the tip corner, at angle theta, has pushed the rack past where the line of action left it.</summary>
        private double Drag(double theta) =>
            tipRadius * Math.Cos(theta) - ex - lean * (tipRadius * Math.Sin(theta) - ey);

        /// <summary>How far one push has carried the frame, <paramref name="u"/> radians after it began.</summary>
        private double Push(double u)
        {
            double psi = Start + u;
            if (psi <= Start) return 0.0;
            if (psi <= Contact) return radius * u;
            if (psi <= Release) return rolled + Drag(psi + tipHalf);
            return Travel;
        }

        /// <summary>How far the frame has moved, in +X, at a turn of <paramref name="psi"/>: nought parked at one end, the travel at the other.</summary>
        public double Slide(double psi)
        {
            double u = psi - Start;
            u -= Math.Floor(u / (2.0 * Math.PI)) * 2.0 * Math.PI;

            if (u <= Window) return Push(u);
            if (u <= Math.PI) return Travel;
            if (u <= Math.PI + Window) return Travel - Push(u - Math.PI);
            return 0.0;
        }
    }

    // --- The sweep ------------------------------------------------------------------------

    /// <param name="Top">How far the sweep reaches above the path at each step along it, from x = -travel to 0.</param>
    /// <param name="Bottom">The same below it.</param>
    /// <param name="Right">How far it reaches from the end it parks at x = 0, at each angle from -90 to 90 degrees.</param>
    /// <param name="Left">The same round the other end, x = -travel, from 90 to 270 degrees.</param>
    private sealed record FrameSweep(double[] Top, double[] Bottom, double[] Right, double[] Left);

    /// <summary>
    /// How far the gear, grown by the clearance, reaches out from its own path over a whole turn,
    /// measured along rays: straight up and down from each point of the path, and round from the
    /// two places it parks at the ends.
    ///
    /// Each ray keeps the furthest the gear ever gets along it. That is exact wherever the frame's
    /// inside is a single stretch along the ray, which it is everywhere but under an overhang, and
    /// there it only takes out a little more than it need - never too little. The growth is taken
    /// as a disc round every sampled point of the outline, so the gap left is measured square to
    /// the surface rather than along the ray.
    /// </summary>
    private static FrameSweep Sweep(Vector2[] gear, FrameMotion motion, double c)
    {
        var (allX, allY) = Densify(gear, SweepSpacing);

        double root = double.MaxValue, reach = 0.0;
        for (int i = 0; i < allX.Length; i++)
        {
            double d = Math.Sqrt(allX[i] * allX[i] + allY[i] * allY[i]);
            root = Math.Min(root, d);
            reach = Math.Max(reach, d);
        }

        // A point on the root circle never reaches past the root circle itself, which every ray
        // starts from anyway - and on a cut-away gear that is most of its outline.
        var outside = Enumerable.Range(0, allX.Length)
            .Where(i => allX[i] * allX[i] + allY[i] * allY[i] > (root + 1e-4) * (root + 1e-4)).ToArray();
        var px = outside.Select(i => allX[i]).ToArray();
        var py = outside.Select(i => allY[i]).ToArray();

        double travel = motion.Travel;
        int nx = Math.Max(2, (int)Math.Ceiling(travel / RayStep) + 1);
        double dx = travel / (nx - 1);
        int quarters = (int)Math.Ceiling(Math.PI * (reach + c) / RayStep / 4.0);
        int na = 4 * quarters + 1;
        double da = Math.PI / (na - 1);

        // The root circle goes everywhere the gear does, so nothing reaches less far than it.
        double floor = root + c;

        // Fine enough that no point moves more than three quarters of the clearance from one sample
        // to the next - turning, and carried along by the frame at most at the pitch radius - so
        // the discs round its successive places overlap into an unbroken track.
        double step = Math.Min(0.5 * Math.PI / 180.0, 0.75 * c / (reach + motion.PitchRadius));
        int samples = (int)Math.Ceiling(2.0 * Math.PI / step);
        double c2 = c * c;

        var rayCos = new double[na];
        var raySin = new double[na];
        for (int k = 0; k < na; k++)
        {
            rayCos[k] = Math.Cos(-Math.PI / 2.0 + k * da);
            raySin[k] = Math.Sin(-Math.PI / 2.0 + k * da);
        }

        // The turn is shared out in pieces, each measured into its own copy, and the copies taken
        // together at the end: a preview is rebuilt at every keystroke.
        int pieces = Math.Clamp(Environment.ProcessorCount, 1, 16);
        var parts = new (double[] Top, double[] Bottom, double[] Right, double[] Left)[pieces];

        Parallel.For(0, pieces, piece =>
        {
            var top = Filled(nx, floor);
            var bottom = Filled(nx, floor);
            var right = Filled(na, floor);
            var left = Filled(na, floor);

            for (int s = piece; s < samples; s += pieces)
            {
                double psi = motion.Start + 2.0 * Math.PI * s / samples;
                double gx = -motion.Slide(psi);
                double cos = Math.Cos(psi), sin = Math.Sin(psi);

                for (int i = 0; i < px.Length; i++)
                {
                    double wx = gx + cos * px[i] - sin * py[i];
                    double wy = sin * px[i] + cos * py[i];
                    double ay = Math.Abs(wy);

                    // Up and down from the path. Anything nearer it than the root circle cannot matter.
                    if (ay > root && wx >= -travel - c && wx <= c)
                    {
                        var column = wy > 0 ? top : bottom;
                        int j0 = Math.Max(0, (int)Math.Ceiling((wx - c + travel) / dx));
                        int j1 = Math.Min(nx - 1, (int)Math.Floor((wx + c + travel) / dx));
                        for (int j = j0; j <= j1; j++)
                        {
                            double off = -travel + j * dx - wx;
                            double h = c2 - off * off;
                            if (h < 0) continue;
                            double v = ay + Math.Sqrt(h);
                            if (v > column[j]) column[j] = v;
                        }
                    }

                    // Round the ends. The left end's rays are the right end's turned half round, so
                    // the point is turned half round to meet them.
                    if (wx > -c) Round(right, wx, wy);
                    if (wx < -travel + c) Round(left, -(wx + travel), -wy);
                }
            }

            parts[piece] = (top, bottom, right, left);
        });

        var all = parts[0];
        for (int p = 1; p < pieces; p++)
        {
            Widen(all.Top, parts[p].Top);
            Widen(all.Bottom, parts[p].Bottom);
            Widen(all.Right, parts[p].Right);
            Widen(all.Left, parts[p].Left);
        }

        foreach (var list in new[] { all.Top, all.Bottom, all.Right, all.Left })
            for (int k = 0; k < list.Length; k++) list[k] += SweepMargin;

        return new FrameSweep(all.Top, all.Bottom, all.Right, all.Left);

        // One point's disc onto the rays round an end, from -90 to 90 degrees.
        void Round(double[] rays, double rx, double ry)
        {
            double d2 = rx * rx + ry * ry;
            if (d2 <= root * root) return;

            double d = Math.Sqrt(d2);
            double angle = Math.Atan2(ry, rx);
            double spread = Math.Asin(Math.Min(1.0, c / d));

            int k0 = Math.Max(0, (int)Math.Ceiling((angle - spread + Math.PI / 2.0) / da));
            int k1 = Math.Min(na - 1, (int)Math.Floor((angle + spread + Math.PI / 2.0) / da));
            for (int k = k0; k <= k1; k++)
            {
                double along = rx * rayCos[k] + ry * raySin[k];
                double h = c2 - (d2 - along * along);
                if (h < 0) continue;
                double v = along + Math.Sqrt(h);
                if (v > rays[k]) rays[k] = v;
            }
        }

        static void Widen(double[] into, double[] from)
        {
            for (int k = 0; k < into.Length; k++)
                if (from[k] > into[k]) into[k] = from[k];
        }

        static double[] Filled(int count, double value)
        {
            var list = new double[count];
            Array.Fill(list, value);
            return list;
        }
    }

    /// <summary>A loop's points with none more than <paramref name="spacing"/> apart.</summary>
    private static (double[] X, double[] Y) Densify(IReadOnlyList<Vector2> loop, double spacing)
    {
        var xs = new List<double>();
        var ys = new List<double>();
        for (int i = 0; i < loop.Count; i++)
        {
            var a = loop[i];
            var b = loop[(i + 1) % loop.Count];
            int pieces = Math.Max(1, (int)Math.Ceiling(Vector2.Distance(a, b) / spacing));
            for (int k = 0; k < pieces; k++)
            {
                double t = k / (double)pieces;
                xs.Add(a.X + (b.X - a.X) * t);
                ys.Add(a.Y + (b.Y - a.Y) * t);
            }
        }

        return (xs.ToArray(), ys.ToArray());
    }

    // --- The rays --------------------------------------------------------------------------

    /// <param name="Tooth">How far out the tooth layer's inside is along this ray.</param>
    /// <param name="Lock">The same for the lock layer; unused with no lock.</param>
    /// <param name="Outer">How far out the frame's outside is.</param>
    private readonly record struct FrameRay(double X, double Y, double Dx, double Dy, double Tooth, double Lock, double Outer)
    {
        public Vector2 Point(double along) => new((float)(X + Dx * along), (float)(Y + Dy * along));
    }

    /// <summary>
    /// The rays every outline of the frame is laid on, anticlockwise: round the end at x = 0, back
    /// along the top, round the other end and along the bottom.
    /// </summary>
    private static List<FrameRay> FrameRays(FrameSweep sweep, double travel, double rim, FrameEnds ends,
                                            (double Pocket, double Rail)? lockSizes)
    {
        int nx = sweep.Top.Length, na = sweep.Right.Length;
        double dx = travel / (nx - 1), da = Math.PI / (na - 1);
        var rays = new List<FrameRay>(2 * nx + 2 * na);

        void Add(double x, double y, double a, double tooth) =>
            rays.Add(new FrameRay(x, y, Math.Cos(a), Math.Sin(a), tooth, 0.0, 0.0));

        // Where an end's first and last rays point straight up or down they are also the path's
        // own rays at that end, measured both ways; the further of the two stands.
        for (int k = 0; k < na; k++)
        {
            double tooth = sweep.Right[k];
            if (k == 0) tooth = Math.Max(tooth, sweep.Bottom[nx - 1]);
            if (k == na - 1) tooth = Math.Max(tooth, sweep.Top[nx - 1]);
            Add(0.0, 0.0, -Math.PI / 2.0 + k * da, tooth);
        }

        for (int j = nx - 2; j >= 1; j--) Add(-travel + j * dx, 0.0, Math.PI / 2.0, sweep.Top[j]);

        for (int k = 0; k < na; k++)
        {
            double tooth = sweep.Left[k];
            if (k == 0) tooth = Math.Max(tooth, sweep.Top[0]);
            if (k == na - 1) tooth = Math.Max(tooth, sweep.Bottom[0]);
            Add(-travel, 0.0, Math.PI / 2.0 + k * da, tooth);
        }

        for (int j = 1; j <= nx - 2; j++) Add(-travel + j * dx, 0.0, -Math.PI / 2.0, sweep.Bottom[j]);

        double outer = rays.Max(ray => ray.Tooth) + rim;

        for (int i = 0; i < rays.Count; i++)
        {
            var ray = rays[i];
            bool end = Math.Abs(ray.Dx) > 1e-9 || ray.X == 0.0 || ray.X == -travel;

            double lockAlong = 0.0;
            if (lockSizes is { } sizes)
            {
                // Round an end, the pocket; along the path, the rail, or the pocket where it
                // bulges past the rail near either end.
                if (end) lockAlong = sizes.Pocket;
                else
                {
                    lockAlong = sizes.Rail;
                    foreach (double centre in new[] { 0.0, -travel })
                    {
                        double off = ray.X - centre;
                        if (Math.Abs(off) < sizes.Pocket)
                            lockAlong = Math.Max(lockAlong, Math.Sqrt(sizes.Pocket * sizes.Pocket - off * off));
                    }
                }
            }

            double outerAlong = outer;
            if (ends == FrameEnds.Square && end && Math.Abs(ray.Dx) > 1e-9)
            {
                // Out to whichever side of the rectangle the ray meets first.
                double side = outer / Math.Abs(ray.Dx);
                double flank = Math.Abs(ray.Dy) > 1e-9 ? outer / Math.Abs(ray.Dy) : double.MaxValue;
                outerAlong = Math.Min(side, flank);
            }

            rays[i] = ray with { Lock = lockAlong, Outer = outerAlong };
        }

        return rays;
    }

    /// <summary>
    /// Which rays are worth a point: the ones any of the outlines bends at. Every outline keeps the
    /// same ones, so that they still pair off point for point.
    /// </summary>
    private static List<int> RaysWorthKeeping(List<FrameRay> rays)
    {
        var keep = new bool[rays.Count];
        Mark(rays.Select(ray => ray.Point(ray.Tooth)).ToList());
        if (rays[0].Lock > 0) Mark(rays.Select(ray => ray.Point(ray.Lock)).ToList());
        Mark(rays.Select(ray => ray.Point(ray.Outer)).ToList());

        return Enumerable.Range(0, rays.Count).Where(i => keep[i]).ToList();

        void Mark(List<Vector2> loop) => Simplify(loop, keep);
    }

    /// <summary>
    /// Marks the points of a closed loop worth keeping, by Douglas-Peucker: split it at its first
    /// point and the point furthest from that, and keep whatever strays more than three microns
    /// from the chord across each piece.
    /// </summary>
    private static void Simplify(List<Vector2> loop, bool[] keep)
    {
        int far = 0;
        float farthest = 0f;
        for (int i = 1; i < loop.Count; i++)
        {
            float d = Vector2.DistanceSquared(loop[0], loop[i]);
            if (d > farthest) (farthest, far) = (d, i);
        }

        keep[0] = keep[far] = true;
        var pending = new Stack<(int From, int To)>();
        pending.Push((0, far));
        pending.Push((far, loop.Count));

        while (pending.Count > 0)
        {
            var (from, to) = pending.Pop();
            if (to - from < 2) continue;

            var a = loop[from];
            var b = loop[to % loop.Count];
            var along = b - a;
            float length = along.Length();

            int worst = -1;
            float most = 0.003f;
            for (int i = from + 1; i < to; i++)
            {
                var p = loop[i] - a;
                float off = length > 1e-6f ? MathF.Abs(along.X * p.Y - along.Y * p.X) / length : p.Length();
                if (off > most) (most, worst) = (off, i);
            }

            if (worst < 0) continue;
            keep[worst] = true;
            pending.Push((from, worst));
            pending.Push((worst, to));
        }
    }

    // --- The lock disc ---------------------------------------------------------------------

    /// <summary>
    /// The lock disc's radius at each angle round the gear, at its turn of nought: the furthest it
    /// can reach without the frame's lock layer ever coming within the clearance of it, over a
    /// whole turn of the motion.
    ///
    /// The lock layer's inside is a rail either side of the path, <paramref name="railR"/> plus the
    /// clearance from it, and a pocket round each end, <paramref name="lockR"/> plus the clearance.
    /// Taking the clearance off both leaves three convex pieces - a rectangle along the path and
    /// the two circles - and along any ray from the gear's centre the frame is first met where the
    /// ray has left all three.
    ///
    /// Drawing the disc as lobes and notches and generating the rails from it was tried first. The
    /// lobe that should hold the frame as it comes in to park is already facing the rail's corner
    /// for the last few millimetres of the push, and the sweep cut the corner away: the frame came
    /// to rest and slid 3.3 mm back. Generated this way round, the disc gets a ramp there instead,
    /// shaped by the corner's own path, and the corner survives.
    /// </summary>
    private static double[] LockRadii(FrameMotion motion, double travel, double lockR, double railR, double c)
    {
        const int count = 1440;
        var radii = new double[count];
        Array.Fill(radii, lockR);

        var cu = new double[count];
        var su = new double[count];
        for (int k = 0; k < count; k++)
        {
            cu[k] = Math.Cos(2.0 * Math.PI * k / count);
            su[k] = Math.Sin(2.0 * Math.PI * k / count);
        }

        double step = Math.Min(0.25 * Math.PI / 180.0, 0.75 * c / (lockR + motion.PitchRadius));
        int samples = (int)Math.Ceiling(2.0 * Math.PI / step);

        var turns = new (double Slide, double Cos, double Sin)[samples];
        for (int s = 0; s < samples; s++)
        {
            double psi = motion.Start + 2.0 * Math.PI * s / samples;
            turns[s] = (-motion.Slide(psi), Math.Cos(psi), Math.Sin(psi));
        }

        // Each angle round the disc is its own question, so they are shared out as they stand.
        Parallel.For(0, count, k =>
        {
            double least = radii[k];
            foreach (var (g, cos, sin) in turns)
            {
                double exit = Exit(g, cu[k] * cos - su[k] * sin, su[k] * cos + cu[k] * sin);
                if (exit < least) least = exit;
            }

            radii[k] = least;
        });

        for (int k = 0; k < count; k++) radii[k] -= 0.01;
        return radii;

        double Exit(double g, double dx, double dy)
        {
            // Out of the rectangle along the path, which the gear's centre is always inside.
            double along = double.MaxValue;
            if (dx > 1e-12) along = Math.Min(along, -g / dx);
            else if (dx < -1e-12) along = Math.Min(along, (-travel - g) / dx);
            if (Math.Abs(dy) > 1e-12) along = Math.Min(along, railR / Math.Abs(dy));

            // And on through either pocket that carries on from where it leaves off.
            for (int pass = 0; pass < 2; pass++)
            {
                foreach (double centre in new[] { 0.0, -travel })
                {
                    double off = g - centre;
                    double b = off * dx;
                    double disc = b * b - (off * off - lockR * lockR);
                    if (disc < 0) continue;
                    double root = Math.Sqrt(disc);
                    double enter = -b - root, leave = -b + root;
                    if (enter <= along + 1e-9 && leave > along) along = leave;
                }
            }

            return along;
        }
    }
}
