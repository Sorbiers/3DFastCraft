using System.Numerics;
using FastCraft3D.Geometry;

namespace FastCraft3D.Generators.Mechanisms;

/// <summary>The bars and pins the linkages here are made of: flat links with a hole at each end.</summary>
internal static class Links
{
    /// <summary>A link with holes <paramref name="length"/> apart, lying flat, centred on the origin along X.</summary>
    public static Mesh Bar(float length, float width, float thickness, float hole) =>
        Shapes.Prism(Shapes.RoundedRect(length + width, width, width / 2f),
            [Shapes.Circle(hole / 2f, new Vector2(-length / 2f, 0)), Shapes.Circle(hole / 2f, new Vector2(length / 2f, 0))],
            0, thickness);

    /// <summary>A pin standing up, long enough to go through two links with a little to spare.</summary>
    public static Mesh Pin(float diameter, float thickness) => Shapes.Cylinder(diameter / 2f, 0, 2 * thickness + 1.5f);

    /// <summary>Where a link made along X about the origin goes to run from one point to another, at a height.</summary>
    public static Matrix4x4 Along(Vector2 a, Vector2 b, float z)
    {
        var mid = (a + b) / 2f;
        return Matrix4x4.CreateRotationZ(MathF.Atan2(b.Y - a.Y, b.X - a.X)) * Matrix4x4.CreateTranslation(mid.X, mid.Y, z);
    }

    /// <summary>A link from one point to another, lying at a height, with no holes yet.</summary>
    public static Mesh Between(Vector2 a, Vector2 b, float width, float thickness, float z)
    {
        float length = Vector2.Distance(a, b);
        var bar = Shapes.Prism(Shapes.RoundedRect(length + width, width, width / 2f - 0.01f), z, z + thickness);
        var mid = (a + b) / 2f;
        return Shapes.Moved(Shapes.Turned(bar, MathF.Atan2(b.Y - a.Y, b.X - a.X)), mid.X, mid.Y, 0);
    }

    /// <summary>
    /// A rivet for a joint of two links, one on the other: its shank gripped by the lower link and
    /// turning in the upper, its head over the upper holding it down - or, for the joint the model
    /// is turned by, a knob to take hold of. Printed head down.
    /// </summary>
    public static (Mesh Rivet, Matrix4x4 ToPrint) Rivet(Vector2 at, float pin, float bottom, float top, bool knob)
    {
        float head = knob ? 12f : 1.6f, round = knob ? 4f : pin / 2f + 2f;
        var rivet = Shapes.Union(Shapes.Cylinder(pin / 2f, bottom + 0.3f, top + 0.01f, at, 32), Shapes.Cylinder(round, top, top + head, at));
        return (rivet, Matrix4x4.CreateTranslation(-at.X, -at.Y, -(top + head)) * Matrix4x4.CreateRotationX(MathF.PI));
    }
}

/// <summary>
/// A four-bar linkage: ground, crank, coupler and rocker, with pins. The Grashof condition says
/// which of them can turn all the way round, and the rocker's swing and the coupler's path are
/// worked out in closed form - a four-bar has one, so there is nothing to solve by steps.
/// </summary>
public sealed class FourBar : Generator<FourBar.Settings>
{
    public override string Id => "mechanism.four-bar";
    public override int Version => 1;
    public override string Category => "Mechanisms";
    public override string Title => "Four-bar linkage";
    public override string Summary => "Four links and their pins, with what the linkage does worked out beside them.";

    public sealed record Settings(
        [Length("Ground", 10, 200, Group = "Lengths", Hint = "Between the two fixed pivots")] float Ground = 80f,
        [Length("Crank", 5, 150, Group = "Lengths", Hint = "The link that is turned")] float Crank = 25f,
        [Length("Coupler", 10, 200, Group = "Lengths")] float Coupler = 70f,
        [Length("Rocker", 10, 200, Group = "Lengths")] float Rocker = 60f,
        [Length("Link width", 4, 20, Group = "Links")] float Width = 8f,
        [Length("Link thickness", 2, 10, Group = "Links")] float Thickness = 4f,
        [Length("Pin", 2, 10, Group = "Links")] float Pin = 4f,
        [Clearance("Fit", 0.05, 1, Group = "Links", Hint = "Round each pin in its holes")] float Fit = 0.2f,
        [Toggle("Demo", Group = "Demo", Hint = "A model to turn by hand: the crank and the rocker keyed on D-shafts through a base, the coupler over them on rivets, a knob on the crank's rivet to turn it by")] bool Demo = false,
        [Toggle("Organize", Hint = "Laid out on the bed to print. Off: put on the plate as it goes together, as the preview shows it.")] bool Organise = true);

    protected override IEnumerable<string> Check(Settings s, Printer printer)
    {
        if (s.Pin + 2 * s.Fit > s.Width - 2 * 1.2f) yield return "The pins leave less than 1.2 mm of link round them.";
        if (MathF.Min(s.Crank, MathF.Min(s.Coupler, s.Rocker)) < s.Pin + 2 * s.Fit + 2.4f)
            yield return "A link is too short for its two holes: they would run into each other.";

        var lengths = new[] { s.Ground, s.Crank, s.Coupler, s.Rocker };
        if (lengths.Max() >= lengths.Sum() - lengths.Max()) yield return "No linkage closes: the longest link is as long as the other three together.";
        else if (s.Demo && Clash(s) is { } why) yield return why;
    }

    /// <summary>Where the rocker's end is with the crank at <paramref name="theta"/>, or null where the linkage does not close.</summary>
    private static Vector2? Rocker(Settings s, double theta)
    {
        var b = new Vector2((float)(s.Crank * Math.Cos(theta)), (float)(s.Crank * Math.Sin(theta)));
        var d = new Vector2(s.Ground, 0);
        float f = Vector2.Distance(b, d);
        if (f > s.Coupler + s.Rocker || f < MathF.Abs(s.Coupler - s.Rocker) || f < 1e-3f) return null;

        float along = (f * f + s.Rocker * s.Rocker - s.Coupler * s.Coupler) / (2 * f);
        float up = MathF.Sqrt(MathF.Max(s.Rocker * s.Rocker - along * along, 0));
        var toward = Vector2.Normalize(b - d);
        return d + toward * along + new Vector2(-toward.Y, toward.X) * up;
    }

    /// <summary>
    /// Why the demo cannot be made, or null: the crank and the rocker share the lower layer, so
    /// they must never come within a link's width of each other as the crank goes round - and the
    /// crank's rivet, turning, must not pass over the rocker's shaft.
    /// </summary>
    private static string? Clash(Settings s)
    {
        for (int i = 0; i < 360; i += 2)
        {
            double theta = i * Math.PI / 180;
            if (Rocker(s, theta) is not { } c) continue;
            var b = new Vector2((float)(s.Crank * Math.Cos(theta)), (float)(s.Crank * Math.Sin(theta)));
            if (Apart(Vector2.Zero, b, new Vector2(s.Ground, 0), c) < s.Width + 1f)
                return "The crank and the rocker would run into each other on the base: a longer ground, or a shorter crank.";
        }

        return null;

        static float Apart(Vector2 a, Vector2 b, Vector2 c, Vector2 d) =>
            MathF.Min(MathF.Min(ToSegment(a, c, d), ToSegment(b, c, d)), MathF.Min(ToSegment(c, a, b), ToSegment(d, a, b)));

        static float ToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            float t = Math.Clamp(Vector2.Dot(p - a, ab) / MathF.Max(ab.LengthSquared(), 1e-6f), 0, 1);
            return Vector2.Distance(p, a + ab * t);
        }
    }

    /// <summary>What kind of four-bar it is, by Grashof: whether the shortest link can turn right round.</summary>
    public static string Kind(Settings s)
    {
        var sorted = new[] { s.Ground, s.Crank, s.Coupler, s.Rocker }.OrderBy(x => x).ToArray();
        bool grashof = sorted[0] + sorted[3] <= sorted[1] + sorted[2];
        float shortest = sorted[0];

        if (!grashof) return "No link turns all the way round: a triple rocker.";
        if (shortest == s.Crank) return "A crank-rocker: the crank turns right round and the rocker swings.";
        if (shortest == s.Ground) return "A double crank: both the crank and the rocker turn right round.";
        if (shortest == s.Coupler) return "A double rocker whose coupler turns right round.";
        return "A double rocker: neither the crank nor the rocker turns right round.";
    }

    protected override Generated Build(Settings s, Printer printer, CancellationToken token)
    {
        float hole = s.Pin + 2 * s.Fit, th = s.Thickness;

        // Put together, each link on a layer of its own - ground, crank, coupler, rocker, upwards -
        // each pin as long as the two links it joins, the rocker's reaching down through the
        // layers between to the ground. On a layer together the crank and the rocker ran into each
        // other, so the crank starts where nothing crosses the rocker's pin. With no such place,
        // the set is only laid out to print.
        double? start = Clear(s);
        var a = Vector2.Zero;
        var d = new Vector2(s.Ground, 0);
        var b = Crank(s, start ?? 0);
        var c = start is { } at ? Rocker(s, at)!.Value : d;
        Matrix4x4? Put(Matrix4x4 m) => start is null ? null : m;
        var parts = new List<(string, string, Mesh, Matrix4x4?)>
        {
            ("Ground link", "ground", Links.Bar(s.Ground, s.Width, th, hole), Put(Links.Along(a, d, 0))),
            ("Crank", "crank", Links.Bar(s.Crank, s.Width, th, hole), Put(Links.Along(a, b, th))),
            ("Coupler", "coupler", Links.Bar(s.Coupler, s.Width, th, hole), Put(Links.Along(b, c, 2 * th))),
            ("Rocker", "rocker", Links.Bar(s.Rocker, s.Width, th, hole), Put(Links.Along(d, c, 3 * th)))
        };

        var joints = new[] { (a, 0f, 2f), (b, 1f, 3f), (c, 2f, 4f), (d, 0f, 4f) };
        for (int i = 0; i < 4; i++)
        {
            var (spot, from, to) = joints[i];
            parts.Add(($"Pin {i + 1}", $"pin {i + 1}", Shapes.Cylinder(s.Pin / 2f, 0, (to - from) * th),
                Put(Matrix4x4.CreateTranslation(spot.X, spot.Y, from * th))));
        }

        // The rocker's swing and the coupler's middle, over a turn of the crank.
        double least = double.MaxValue, most = double.MinValue;
        int reached = 0;
        var path = new List<Vector2>();
        for (int i = 0; i < 360; i++)
        {
            double theta = i * Math.PI / 180;
            if (Rocker(s, theta) is not { } end) continue;
            var pin = Crank(s, theta);

            reached++;
            double phi = Math.Atan2(end.Y - d.Y, end.X - d.X) * 180 / Math.PI;
            least = Math.Min(least, phi);
            most = Math.Max(most, phi);
            path.Add((pin + end) / 2f);
        }

        var notes = new List<string> { Kind(s) };
        if (reached == 360) notes.Add($"Turned right round, the rocker swings {most - least:0} degrees.");
        else if (reached > 0) notes.Add($"The crank reaches only {reached} degrees of its turn before the linkage locks.");

        if (path.Count > 0)
        {
            var box = (Min: path.Aggregate(Vector2.Min), Max: path.Aggregate(Vector2.Max));
            notes.Add($"The coupler's middle traces a path {box.Max.X - box.Min.X:0} by {box.Max.Y - box.Min.Y:0} mm.");
        }

        if (s.Demo) return Demonstrate(s, printer, notes, token);

        notes.Add("Plain pins: a drop of glue, or a washer melted over each end, keeps them in.");
        return new Generated(Shapes.InARow(parts), notes)
        {
            Motion = start is { } first ? Moves(s, first, crank: 1, rocker: 3, coupler: 2, parts.Count, [(5, 1), (6, 3)]) : null,
            LaidOut = s.Organise
        };
    }

    /// <summary>
    /// Where the crank can start with the rocker's pin, standing up through the crank's layer and
    /// the coupler's, clear of both; null where there is nowhere.
    /// </summary>
    private static double? Clear(Settings s)
    {
        var d = new Vector2(s.Ground, 0);
        float room = s.Width / 2f + s.Pin / 2f + 0.3f;
        foreach (int k in Enumerable.Range(0, 36))
        {
            double t = (60 + k * 10) * Math.PI / 180;
            if (Rocker(s, t) is not { } c) continue;
            var b = Crank(s, t);
            if (ToSegment(d, Vector2.Zero, b) >= room && ToSegment(d, b, c) >= room) return t;
        }

        return null;
    }

    private static float ToSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        float t = Math.Clamp(Vector2.Dot(p - a, ab) / MathF.Max(ab.LengthSquared(), 1e-6f), 0, 1);
        return Vector2.Distance(p, a + ab * t);
    }

    private static Vector2 Crank(Settings s, double theta) =>
        new((float)(s.Crank * Math.Cos(theta)), (float)(s.Crank * Math.Sin(theta)));

    /// <summary>Where the crank starts: a third of the way round, or the nearest after it where the linkage closes.</summary>
    private static double Start(Settings s) =>
        Enumerable.Range(0, 36).Select(k => (60 + k * 10) * Math.PI / 180).FirstOrDefault(t => Rocker(s, t) is not null, Math.PI / 3);

    /// <summary>
    /// The linkage turned by its crank: the crank about its pivot, the rocker about its own, the
    /// coupler swinging as it is carried by its end on the crank - worked out in closed form at
    /// each step, where the linkage goes, and stopped where it cannot.
    /// </summary>
    private static Mechanism Moves(Settings s, double start, int crank, int rocker, int coupler, int count, IReadOnlyList<(int, int)> riders)
    {
        var d = new Vector2(s.Ground, 0);
        var b0 = Crank(s, start);
        var c0 = Rocker(s, start) ?? d;
        double Angle(Vector2 from, Vector2 to) => Math.Atan2(to.Y - from.Y, to.X - from.X);
        double swing0 = Angle(b0, c0), rock0 = Angle(d, c0);

        return new Mechanism(
            [new MovingPart(crank, Geometry.Motion.Joint.Revolute, Vector2.Zero),
             new MovingPart(rocker, Geometry.Motion.Joint.Revolute, d),
             new MovingPart(coupler, Geometry.Motion.Joint.Revolute, b0)],
            0, [], Riders: riders,
            Script: t =>
            {
                if (Rocker(s, start + t) is not { } c) return null;
                var b = Crank(s, start + t);
                var places = new (double, Vector2)[count];
                places[crank] = (t, Vector2.Zero);
                places[rocker] = (Angle(d, c) - rock0, Vector2.Zero);
                places[coupler] = (Angle(b, c) - swing0, b - b0);
                return places;
            });
    }

    /// <summary>
    /// The linkage as a model to turn by hand, standing where the crank is a third of the way
    /// round: the crank and the rocker keyed on D-shafts through a base, their tops flush so the
    /// coupler passes over them, and the coupler on them by rivets - each gripped by the link under
    /// it and turning in the coupler, its head over the coupler. The crank's rivet stands up as a
    /// knob, to turn it by.
    /// </summary>
    private static Generated Demonstrate(Settings s, Printer printer, List<string> notes, CancellationToken token)
    {
        var demo = new Demo(printer, s.Pin);
        double theta = Start(s);
        var a = Vector2.Zero;
        var b = new Vector2((float)(s.Crank * Math.Cos(theta)), (float)(s.Crank * Math.Sin(theta)));
        var c = Rocker(s, theta)!.Value;
        var d = new Vector2(s.Ground, 0);

        float th = s.Thickness, low = Demo.Lift, high = low + th + Demo.Gap, top = high + th + Demo.Gap;
        float grip = s.Pin / 2f + demo.Grip, run = s.Pin / 2f + s.Fit;
        Mesh Hole(Vector2 at, float r, float z) => Shapes.Cylinder(r, z - 1, z + th + 1, at, 32);

        demo.Lying("Crank", "crank", Shapes.Subtract(Links.Between(a, b, s.Width, th, low), Hole(b, grip, low)));
        demo.Lying("Rocker", "rocker", Shapes.Subtract(Links.Between(d, c, s.Width, th, low), Hole(c, grip, low)));
        demo.Lying("Coupler", "coupler", Shapes.Subtract(Links.Between(b, c, s.Width, th, high), Hole(b, run, high), Hole(c, run, high)));

        var (knob, knobPrint) = Links.Rivet(b, s.Pin, low, top, knob: true);
        var (rivet, rivetPrint) = Links.Rivet(c, s.Pin, low, top, knob: false);
        demo.Placed("Crank knob", "knob", knob, knobPrint);
        demo.Placed("Rivet", "rivet", rivet, rivetPrint);

        demo.Upright(a, [0]);
        demo.Upright(d, [1]);
        demo.Riders.Add((3, 0));
        demo.Riders.Add((4, 1));
        demo.Cover(-s.Crank - s.Width, -s.Crank - s.Width, s.Ground + s.Width, s.Crank + s.Width);

        token.ThrowIfCancellationRequested();
        var parts = demo.Finish(token);
        notes.Add($"The crank and the rocker go on {demo.Size:0.#} mm D-shafts, in from under the base. Lay the coupler over them and press a rivet down through each end: the link underneath grips it, the coupler turns on it.");
        notes.Add("Turn it by the knob on the crank's rivet.");
        return new Generated(parts, notes) { Motion = Moves(s, theta, crank: 0, rocker: 1, coupler: 2, parts.Count, demo.Riders), LaidOut = s.Organise };
    }
}

/// <summary>A crank, a connecting rod and a slider, turning round into back and forth, with the stroke worked out.</summary>
public sealed class CrankSlider : Generator<CrankSlider.Settings>
{
    public override string Id => "mechanism.crank-slider";
    public override int Version => 1;
    public override string Category => "Mechanisms";
    public override string Title => "Crank and slider";
    public override string Summary => "A crank, a rod and a slider block, with the stroke worked out.";

    public sealed record Settings(
        [Length("Crank", 5, 80)] float Crank = 20f,
        [Length("Rod", 15, 250)] float Rod = 70f,
        [Length("Offset", 0, 40, Hint = "How far the slider's line runs beside the crank's axis")] float Offset = 0f,
        [Length("Link width", 4, 20)] float Width = 8f,
        [Length("Link thickness", 2, 10)] float Thickness = 4f,
        [Length("Pin", 2, 10)] float Pin = 4f,
        [Clearance("Fit", 0.05, 1)] float Fit = 0.2f,
        [Toggle("Demo", Group = "Demo", Hint = "A model to turn by hand: the crank keyed on a D-shaft through a base, the slider in a channel, the rod over them on rivets, a knob on the crank's rivet")] bool Demo = false,
        [Toggle("Organize", Hint = "Laid out on the bed to print. Off: put on the plate as it goes together, as the preview shows it.")] bool Organise = true);

    protected override IEnumerable<string> Check(Settings s, Printer printer)
    {
        if (s.Rod <= s.Crank + s.Offset) yield return "The rod has to be longer than the crank and the offset together, or the crank cannot turn right round.";
        if (s.Pin + 2 * s.Fit > s.Width - 2 * 1.2f) yield return "The pins leave less than 1.2 mm of link round them.";
        else if (s.Crank < s.Pin + 2 * s.Fit + 2.4f) yield return "The crank is too short for its two holes: they would run into each other.";
        else if (s.Demo && Travel(s).Least - s.Width - 1f < s.Crank + s.Width / 2f + 1f)
            yield return "The slider's channel would run into the crank: a longer rod, or a shorter crank.";
    }

    /// <summary>How far along the slider's line the slider's middle goes, least and most.</summary>
    private static (float Least, float Most) Travel(Settings s)
    {
        float least = float.MaxValue, most = float.MinValue;
        for (int i = 0; i < 720; i++)
        {
            double theta = i * Math.PI / 360;
            double y = s.Crank * Math.Sin(theta) - s.Offset;
            float x = (float)(s.Crank * Math.Cos(theta) + Math.Sqrt(s.Rod * s.Rod - y * y));
            least = MathF.Min(least, x);
            most = MathF.Max(most, x);
        }

        return (least, most);
    }

    public static double Stroke(Settings s)
    {
        double least = double.MaxValue, most = double.MinValue;
        for (int i = 0; i < 720; i++)
        {
            double theta = i * Math.PI / 360;
            double y = s.Crank * Math.Sin(theta) - s.Offset;
            double x = s.Crank * Math.Cos(theta) + Math.Sqrt(s.Rod * s.Rod - y * y);
            least = Math.Min(least, x);
            most = Math.Max(most, x);
        }

        return most - least;
    }

    protected override Generated Build(Settings s, Printer printer, CancellationToken token)
    {
        float hole = s.Pin + 2 * s.Fit, th = s.Thickness;
        float block = s.Width * 2f;
        var slider = Shapes.Prism(Shapes.Rect(-block / 2f, -s.Width, block / 2f, s.Width), [Shapes.Circle(hole / 2f)], 0, th * 2f);

        // Put together with the crank straight up: the crank over nothing, the rod over it and
        // over the slider, a pin in each joint.
        var b = Crank(s, Start);
        var p = SliderAt(s, Start);
        var parts = new List<(string, string, Mesh, Matrix4x4?)>
        {
            ("Crank", "crank", Links.Bar(s.Crank, s.Width, th, hole), Links.Along(Vector2.Zero, b, th)),
            ("Rod", "rod", Links.Bar(s.Rod, s.Width, th, hole), Links.Along(b, p, 2 * th)),
            ("Slider", "slider", slider, Matrix4x4.CreateTranslation(p.X, p.Y, 0)),
            ("Pin 1", "pin 1", Links.Pin(s.Pin, th), Matrix4x4.CreateTranslation(b.X, b.Y, th)),
            ("Pin 2", "pin 2", Links.Pin(s.Pin, th), Matrix4x4.CreateTranslation(p.X, p.Y, th))
        };

        if (s.Demo) return Demonstrate(s, printer, token);

        return new Generated(Shapes.InARow(parts),
            [$"A stroke of {Stroke(s):0.#} mm.", "The slider runs in a guide of your own; plain pins, glued or capped."])
        {
            Motion = Moves(s, crank: 0, slider: 2, rod: 1, parts.Count, [(3, 0), (4, 2)]),
            LaidOut = s.Organise
        };
    }

    /// <summary>The crank's start: straight up, where the rod leans least.</summary>
    private const double Start = Math.PI / 2;

    private static Vector2 Crank(Settings s, double theta) =>
        new((float)(s.Crank * Math.Cos(theta)), (float)(s.Crank * Math.Sin(theta)));

    private static Vector2 SliderAt(Settings s, double theta)
    {
        double y = s.Crank * Math.Sin(theta) - s.Offset;
        return new Vector2((float)(s.Crank * Math.Cos(theta) + Math.Sqrt(s.Rod * s.Rod - y * y)), s.Offset);
    }

    /// <summary>
    /// Turned by its crank, worked out at each step: the crank about its pivot, the slider along
    /// its line, the rod swinging as its ends are carried.
    /// </summary>
    private static Mechanism Moves(Settings s, int crank, int slider, int rod, int count, IReadOnlyList<(int, int)> riders)
    {
        var b0 = Crank(s, Start);
        var p0 = SliderAt(s, Start);
        double Angle(Vector2 from, Vector2 to) => Math.Atan2(to.Y - from.Y, to.X - from.X);
        double lean0 = Angle(b0, p0);

        return new Mechanism(
            [new MovingPart(crank, Geometry.Motion.Joint.Revolute, Vector2.Zero),
             new MovingPart(slider, Geometry.Motion.Joint.Prismatic, default, Vector2.UnitX),
             new MovingPart(rod, Geometry.Motion.Joint.Revolute, b0)],
            0, [], Riders: riders,
            Script: t =>
            {
                var b = Crank(s, Start + t);
                var p = SliderAt(s, Start + t);
                var places = new (double, Vector2)[count];
                places[crank] = (t, Vector2.Zero);
                places[slider] = (p.X - p0.X, Vector2.Zero);
                places[rod] = (Angle(b, p) - lean0, b - b0);
                return places;
            });
    }

    /// <summary>
    /// The crank and slider as a model to turn by hand: the crank keyed on a D-shaft through a
    /// base, the slider in a channel along its line, the rod over the two on rivets gripped by the
    /// link under each end, and a knob on the crank's rivet to turn it by. The channel stops short
    /// of the crank's reach, and its rails stand no higher than the slider, so the rod passes over.
    /// </summary>
    private static Generated Demonstrate(Settings s, Printer printer, CancellationToken token)
    {
        var demo = new Demo(printer, s.Pin);
        float th = s.Thickness, low = Demo.Lift, high = low + th + Demo.Gap, top = high + th + Demo.Gap, c = demo.Run;
        float grip = s.Pin / 2f + demo.Grip, run = s.Pin / 2f + s.Fit;
        Mesh Hole(Vector2 at, float r, float z) => Shapes.Cylinder(r, z - 1, z + th + 1, at, 32);

        // The crank straight up, where the rod leans least.
        var b = new Vector2(0, s.Crank);
        var p = new Vector2(MathF.Sqrt(s.Rod * s.Rod - (s.Crank - s.Offset) * (s.Crank - s.Offset)), s.Offset);
        float block = s.Width * 2f;

        demo.Lying("Crank", "crank", Shapes.Subtract(Links.Between(Vector2.Zero, b, s.Width, th, low), Hole(b, grip, low)));
        demo.Lying("Slider", "slider", Shapes.Subtract(Shapes.Prism(Shapes.Rect(p.X - block / 2f, p.Y - s.Width, p.X + block / 2f, p.Y + s.Width), low, low + th), Hole(p, grip, low)));
        demo.Lying("Rod", "rod", Shapes.Subtract(Links.Between(b, p, s.Width, th, high), Hole(b, run, high), Hole(p, run, high)));

        var (knob, knobPrint) = Links.Rivet(b, s.Pin, low, top, knob: true);
        var (rivet, rivetPrint) = Links.Rivet(p, s.Pin, low, top, knob: false);
        demo.Placed("Crank knob", "knob", knob, knobPrint);
        demo.Placed("Rivet", "rivet", rivet, rivetPrint);
        demo.Upright(Vector2.Zero, [0]);
        demo.Riders.Add((3, 0));
        demo.Riders.Add((4, 1));

        // The channel: a bed the slider runs on and a rail each side, the length of its travel.
        var (least, most) = Travel(s);
        float x0 = least - block / 2f - 1f, x1 = most + block / 2f + 1f;
        demo.AddToBase(Shapes.Box(x0, s.Offset - s.Width, Demo.Plate - 0.01f, x1, s.Offset + s.Width, low - Demo.Gap));
        demo.AddToBase(Shapes.Box(x0, s.Offset - s.Width - c - 2.5f, Demo.Plate - 0.01f, x1, s.Offset - s.Width - c, low + th - 0.5f));
        demo.AddToBase(Shapes.Box(x0, s.Offset + s.Width + c, Demo.Plate - 0.01f, x1, s.Offset + s.Width + c + 2.5f, low + th - 0.5f));
        demo.Cover(-s.Crank - s.Width, -s.Crank - s.Width, x1, s.Crank + s.Width);

        token.ThrowIfCancellationRequested();
        var parts = demo.Finish(token);
        return new Generated(parts,
        [
            $"A stroke of {Stroke(s):0.#} mm.",
            $"The crank goes on a {demo.Size:0.#} mm D-shaft, in from under the base; the slider drops into its channel; the rod lies over the two, a rivet pressed down through each end.",
            "Turn it by the knob on the crank's rivet."
        ]) { Motion = Moves(s, crank: 0, slider: 1, rod: 2, parts.Count, demo.Riders), LaidOut = s.Organise };
    }
}

/// <summary>
/// A ball bearing printed in place: two races and the balls between them, each its own part,
/// the gaps the printer's clearance. The balls stand on the plate, so nothing prints in the air
/// but the top of each ball.
/// </summary>
public sealed class Bearing : Generator<Bearing.Settings>
{
    public override string Id => "mechanism.bearing";
    public override int Version => 1;
    public override string Category => "Mechanisms";
    public override string Title => "Print-in-place bearing";
    public override string Summary => "Two races and the balls between them, printed together in place.";

    public sealed record Settings(
        [Length("Bore", 3, 60)] float Bore = 8f,
        [Length("Outside", 15, 120)] float Outside = 30f,
        [Length("Width", 5, 30)] float Width = 9f,
        [Clearance("Clearance", 0.1, 1, Hint = "Round each ball. Print-in-place wants a little more than a sliding fit: raise it if the balls fuse.")] float Clearance = 0.35f,
        [Toggle("Demo", Group = "Demo", Hint = "A stand the inner race presses onto, and a spinner that grips the outer race, with a knob to turn it by")] bool Demo = false,
        [Toggle("Organize", Hint = "Laid out on the bed to print. Off: put on the plate as it goes together, as the preview shows it.")] bool Organise = true);

    private static float Ball(Settings s) => MathF.Min(s.Width / 2f, ((s.Outside - s.Bore) / 2f - 2 * 1.6f) / 2f);

    protected override IEnumerable<string> Check(Settings s, Printer printer)
    {
        if (Ball(s) < 1.5f) yield return "No room for balls between the races: a bigger outside or a smaller bore.";
    }

    protected override Generated Build(Settings s, Printer printer, CancellationToken token)
    {
        float rb = Ball(s), c = s.Clearance;
        float pitch = (s.Bore / 2f + s.Outside / 2f) / 2f;
        float height = MathF.Max(s.Width, 2 * rb);
        int count = Math.Max(3, (int)MathF.Floor(2 * MathF.PI * pitch / (2 * rb + 1.2f)));

        var groove = Shapes.Moved(Primitives.Torus(pitch, rb + c, Shapes.Sides(pitch), 32), 0, 0, rb);

        var inner = Shapes.Subtract(Shapes.Turned([new(s.Bore / 2f, 0), new(pitch - c, 0), new(pitch - c, height), new(s.Bore / 2f, height)]), groove);
        var outer = Shapes.Subtract(Shapes.Turned([new(pitch + c, 0), new(s.Outside / 2f, 0), new(s.Outside / 2f, height), new(pitch + c, height)]), groove);

        token.ThrowIfCancellationRequested();
        var parts = new List<GeneratedPart>
        {
            new("Inner race", inner, Role: "inner"),
            new("Outer race", outer, Role: "outer")
        };

        var ball = Shapes.Moved(Primitives.Sphere(rb, 24, 12), 0, 0, rb);
        for (int i = 0; i < count; i++)
        {
            float a = 2f * MathF.PI * i / count;
            var at = new Vector3(pitch * MathF.Cos(a), pitch * MathF.Sin(a), 0);
            parts.Add(new($"Ball {i + 1}", Shapes.Moved(ball, at.X, at.Y, 0), Role: $"ball {i + 1}", Pivot: at));
        }

        var notes = new List<string> { $"{count} balls of {2 * rb:0.#} mm. Print it as it lies, all together; a turn by hand frees it." };
        if (!s.Demo) return new Generated(parts, notes) { LaidOut = s.Organise };

        // The stand: a plate, a shoulder under the inner race only, and a post the race presses
        // onto. The spinner grips the outer race and swings a knob round on an arm. The bearing
        // prints together where it is; the two beside it.
        var demo = new Demo(printer, 4f);
        float plate = Demo.Plate, raised = plate + 2f, bore = s.Bore / 2f, outside = s.Outside / 2f;
        float shoulder = MathF.Max(bore + 1f, pitch - rb - 0.8f);
        var stand = Shapes.Union(
            Shapes.Prism(Shapes.RoundedRect(s.Outside + 12f, s.Outside + 12f, 5f), 0, plate),
            Shapes.Tube(shoulder, bore - 0.5f, plate - 0.01f, raised),
            Shapes.Cylinder(bore - demo.Grip, plate - 0.01f, raised + height - 0.5f, sides: Shapes.Sides(bore)));

        float arm = 22f;
        var spinner = Shapes.Subtract(
            Shapes.Union(
                Shapes.Cylinder(outside + 3f, 0, height - 1f),
                Shapes.Prism(Shapes.RoundedRect(arm + 8f, 8f, 3.99f, new Vector2(outside + arm / 2f, 0)), 0, 4f),
                Shapes.Cylinder(4f, 3.99f, 18f, new Vector2(outside + arm, 0))),
            Shapes.Cylinder(outside + demo.Grip, -1, height + 1, sides: Shapes.Sides(outside)));

        var lift = Matrix4x4.CreateTranslation(0, 0, raised);
        float beside = outside + arm + 12f;
        var placed = parts.Select(p => p with { Assembled = lift }).ToList();
        placed.Add(new GeneratedPart("Stand", Shapes.Moved(stand, beside + s.Outside / 2f + 6f, 0, 0), Matrix4x4.CreateTranslation(-(beside + s.Outside / 2f + 6f), 0, 0), Role: "base"));
        placed.Add(new GeneratedPart("Spinner", Shapes.Moved(spinner, 0, -beside - 4f, 0), Matrix4x4.CreateTranslation(0, beside + 4f, raised + 0.5f), Role: "spinner"));

        notes.Add("Press the inner race down onto the stand's post, and the spinner down over the outer race; turn it by the knob.");
        return new Generated(placed, notes) { LaidOut = s.Organise };
    }
}
