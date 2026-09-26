using System.Numerics;
using FastCraft3D.Geometry;

namespace FastCraft3D.Generators.Buildings;

public enum StairKind { Straight, Spiral }

public enum Handrail
{
    None,
    [ShownAs("Solid balustrade")] Solid,
    [ShownAs("Posts and rail")] Posts
}

public enum RailSide { Left, Right, Both }

/// <summary>
/// A flight of steps, arriving level with the floor above: straight, solid underneath or open
/// with a sloping soffit, with a balustrade or posts and a rail if wanted; or a spiral round a
/// column.
///
/// The numbers are the whole of the job: a riser somewhere between 150 and 190 mm on a going of
/// 240 or more is a stair, and anything else is a ladder or a ramp. At the model's scale that is
/// arithmetic nobody should be doing on paper, so the panel says in real millimetres what the
/// flight would be to climb. A straight flight is one side outline run across its width, so it
/// has no seams inside.
/// </summary>
public sealed class Stair : Generator<Stair.Settings>
{
    public override string Id => "building.stair";
    public override int Version => 1;
    public override string Category => "Buildings";
    public override string Title => "Stair";
    public override string Summary => "A straight flight or a spiral, solid or open underneath, with handrails if wanted.";
    public override bool IsBeta => false;

    public sealed record Settings(
        [Length("Rise", 0.5, 1000, Hint = "Floor to floor")] float Rise = 26.5f,
        [Length("Run", 0.5, 1000, Hint = "How far it travels along the floor")] float Run = 37.7f,
        [Length("Width", 0.5, 1000, Hint = "Across the flight; a spiral's step from the column out")] float Width = 14f,
        [Count("Steps", 1, StairBuilder.MaximumSteps, UnitText = "risers", Hint = "Risers, the last of them onto the floor above")] int Steps = 13,
        [Choice("Kind")] StairKind Kind = StairKind.Straight,
        [Toggle("Solid underneath", Hint = "Off for a flight open underneath, with a sloping soffit")] bool Solid = true,
        [Length("Soffit thickness", 0.3, 50, Hint = "Square to the slope, under the inner corners of the steps")] float Thickness = 2f,
        [Choice("Handrail", Group = "Handrail")] Handrail Handrail = Handrail.None,
        [Choice("Side", Group = "Handrail")] RailSide Side = RailSide.Both,
        [Length("Rail height", 1, 100, Group = "Handrail", Hint = "Above the nosings: about 900 mm full size")] float RailHeight = 10f,
        [Angle("Turn", 90, 720, Hint = "How far a spiral goes round from bottom to top")] float Turn = 360f,
        [Length("Column", 0.5, 100, Hint = "The spiral's central column, across")] float Column = 3f,
        [Length("Rail width", 0, 20, Group = "Handrail", Hint = "The posts, the rail and a balustrade's thickness. Nought for one to suit the stair.")] float RailWidth = 0f);

    protected override IEnumerable<(string Name, Settings Settings)> Shipped =>
    [
        ("Straight, solid", Default),
        ("Open with handrails", Default with { Solid = false, Handrail = Handrail.Posts }),
        ("Between walls, balustrade one side", Default with { Handrail = Handrail.Solid, Side = RailSide.Right }),
        ("Spiral", Default with { Kind = StairKind.Spiral, Width = 10f, Steps = 14 }),
        ("Spiral with handrail", Default with { Kind = StairKind.Spiral, Width = 10f, Steps = 14, Handrail = Handrail.Posts })
    ];

    protected override bool Shows(Settings s, string parameter) => parameter switch
    {
        nameof(Settings.Run) or nameof(Settings.Solid) => s.Kind == StairKind.Straight,
        nameof(Settings.Thickness) => s.Kind == StairKind.Straight && !s.Solid,
        nameof(Settings.Side) => s.Kind == StairKind.Straight && s.Handrail != Handrail.None,
        nameof(Settings.RailHeight) or nameof(Settings.RailWidth) => s.Handrail != Handrail.None,
        nameof(Settings.Turn) or nameof(Settings.Column) => s.Kind == StairKind.Spiral,
        _ => true
    };

    /// <summary>How far down the slope the soffit sits under the steps' inner corners.</summary>
    private static float Drop(Settings s) =>
        s.Thickness * MathF.Sqrt(1 + MathF.Pow(s.Rise / s.Run, 2));

    protected override IEnumerable<string> Check(Settings s, Printer printer)
    {
        if (s.Kind == StairKind.Straight && !s.Solid && Drop(s) >= s.Rise - s.Rise / s.Steps)
            yield return "The soffit is as thick as the flight is high. A thinner soffit, or leave it solid.";
        if (s.Handrail != Handrail.None && s.RailHeight <= s.Rise / s.Steps)
            yield return "The rail would be no higher than a step.";
        if (s.Kind == StairKind.Spiral && s.Turn / s.Steps < 5)
            yield return "Under five degrees a step: more turn, or fewer steps.";
        if (s.Handrail != Handrail.None && s.RailWidth > (s.Kind == StairKind.Spiral ? s.Width : s.Width / 2f) - 0.5f)
            yield return "The rail is as wide as the steps.";
        if (s.Handrail != Handrail.None && s.RailWidth > s.RailHeight / 2f)
            yield return "The rail is thicker than half its height.";
        if (s.Kind == StairKind.Spiral && s.Handrail != Handrail.None && s.Turn > 360 && s.Rise * 360 / s.Turn < s.RailHeight + 2 * s.Rise / s.Steps)
            yield return "The turn above comes down onto the handrail: more rise, or less turn.";
    }

    protected override Generated Build(Settings s, Printer printer, CancellationToken token) =>
        new([new GeneratedPart("Stair", s.Kind == StairKind.Spiral ? Spiral(s) : Straight(s, token), Role: "stair")],
            s.Kind == StairKind.Spiral
                ? ["The steps stand out from the column with nothing under them: print it with supports."]
                : []);

    /// <summary>From a side outline in (along the run, up) to a solid across the width, the run along X.</summary>
    private static Mesh Across(List<Vector2> side, float y0, float y1) =>
        MeshTransform.Transformed(Shapes.Prism(side, 0, y1 - y0),
            new Matrix4x4(1, 0, 0, 0, 0, 0, 1, 0, 0, -1, 0, 0, 0, y1, 0, 1));

    private static Mesh Straight(Settings s, CancellationToken token)
    {
        float riser = s.Rise / s.Steps, going = s.Run / s.Steps, x0 = -s.Run / 2f, half = s.Width / 2f;

        // Up the nosings and along the treads, then down the back and home along the floor - or,
        // open underneath, back down a soffit parallel to the slope.
        var side = new List<Vector2> { new(x0, 0) };
        for (int n = 1; n <= s.Steps; n++)
        {
            side.Add(new(x0 + (n - 1) * going, n * riser));
            side.Add(new(x0 + n * going, n * riser));
        }

        if (s.Solid)
        {
            side.Add(new(x0 + s.Run, 0));
        }
        else
        {
            float drop = Drop(s);
            side.Add(new(x0 + s.Run, s.Rise - drop));
            side.Add(new(x0 + drop * going / riser, 0));
        }

        var pieces = new List<Mesh> { Across(side, -half, half) };
        if (s.Handrail == Handrail.None) return pieces[0];

        token.ThrowIfCancellationRequested();
        foreach (float sign in s.Side switch { RailSide.Left => new[] { 1f }, RailSide.Right => [-1f], _ => [1f, -1f] })
            pieces.AddRange(Rail(s, sign, riser, going, x0, half));

        return Shapes.Union(pieces);
    }

    /// <summary>A balustrade, or posts and a rail, along one side of a straight flight.</summary>
    private static IEnumerable<Mesh> Rail(Settings s, float sign, float riser, float going, float x0, float half)
    {
        float h = s.RailHeight, x1 = x0 + s.Run;

        // Kept off the stair's own side faces, so nothing meets in one plane: a solid balustrade
        // stands just outside the flight and runs a hair into it, posts stand a hair inside it.
        if (s.Handrail == Handrail.Solid)
        {
            float t = s.RailWidth > 0 ? s.RailWidth : MathF.Max(0.6f, s.Width * 0.06f);
            float inner = half - 0.05f, outer = half + t;
            yield return Across(
            [
                // Its foot slopes a little less than the flight, clear of the steps' corners.
                new(x0, 0), new(x1, s.Rise - 0.5f * riser), new(x1, s.Rise + h), new(x0, h)
            ], sign > 0 ? inner : -outer, sign > 0 ? outer : -inner);
            yield break;
        }

        float post = s.RailWidth > 0 ? s.RailWidth : MathF.Max(0.5f, MathF.Min(going * 0.4f, s.Width * 0.08f));
        float a = half - 0.1f - post, b = half - 0.1f;
        (float y0, float y1) = sign > 0 ? (a, b) : (-b, -a);

        yield return Across([new(x0, h - post), new(x1, s.Rise + h - post), new(x1, s.Rise + h), new(x0, h)], y0, y1);

        for (int n = 1; n <= s.Steps; n++)
        {
            float c = x0 + (n - 0.5f) * going;
            float top = h + (c - x0) * s.Rise / s.Run - post / 2f;
            yield return Shapes.Box(c - post / 2f, y0, n * riser - 0.05f, c + post / 2f, y1, top);
        }
    }

    /// <summary>Wedge-shaped steps round a column, each a little over its share of the turn so neighbours overlap.</summary>
    private static Mesh Spiral(Settings s)
    {
        float riser = s.Rise / s.Steps, step = s.Turn * MathF.PI / 180f / s.Steps;
        float inner = s.Column / 2f - 0.1f, outer = s.Column / 2f + s.Width, thick = riser * 1.5f;

        var pieces = new List<Mesh> { Shapes.Cylinder(s.Column / 2f, 0, s.Rise) };
        for (int i = 0; i < s.Steps; i++)
        {
            float a0 = i * step, a1 = (i + 1) * step + step * 0.08f;
            int arc = Math.Max(2, (int)MathF.Ceiling((a1 - a0) * outer / 0.8f));
            var wedge = new List<Vector2>();
            for (int k = 0; k <= arc; k++) wedge.Add(outer * Dir(a0 + (a1 - a0) * k / arc));
            wedge.Add(inner * Dir(a1));
            wedge.Add(inner * Dir(a0));

            float top = (i + 1) * riser;
            pieces.Add(Shapes.Prism(wedge, MathF.Max(0, top - thick), top));
        }

        if (s.Handrail != Handrail.None) pieces.AddRange(SpiralRail(s, riser, step, outer));
        return Shapes.Union(pieces);

        static Vector2 Dir(float a) => new(MathF.Cos(a), MathF.Sin(a));
    }

    /// <summary>
    /// Round the outside of a spiral: a post on every step and a rail from post to post rising
    /// with the steps - the posts close enough that the rail bridges between them rather than
    /// wanting support - or a solid balustrade stepping up with them.
    /// </summary>
    private static IEnumerable<Mesh> SpiralRail(Settings s, float riser, float step, float outer)
    {
        float w = s.RailWidth > 0 ? s.RailWidth : MathF.Max(0.5f, MathF.Min(MathF.Min(s.Width * 0.08f, step * outer * 0.4f), s.RailHeight * 0.4f));
        float h = s.RailHeight;

        if (s.Handrail == Handrail.Solid)
        {
            // One band round the outside, its foot and its top both rising with the steps, swept as
            // one closed solid. A piece of wall per step was tried first, and stepped along its top.
            yield return Helix(outer - w, outer, 0, s.Turn * MathF.PI / 180f,
                a => MathF.Max(0, (a / step - 0.5f) * riser), a => h + (a / step + 0.5f) * riser);
            yield break;
        }

        float r = outer - w / 2f - 0.1f;
        Vector3 At(float a) => new(r * Dir(a), h + (a / step + 0.5f) * riser);

        for (int i = 0; i < s.Steps; i++)
        {
            float a = (i + 0.5f) * step;
            yield return Shapes.Cylinder(w / 2f, (i + 1) * riser - 0.05f, At(a).Z, r * Dir(a), 10);
        }

        // Straight lengths from post to post, each run on past its ends so they close up where they meet.
        for (int i = 0; i + 1 < s.Steps; i++)
        {
            Vector3 from = At((i + 0.5f) * step), to = At((i + 1.5f) * step);
            var along = Vector3.Normalize(to - from);
            float length = Vector3.Distance(from, to) + w;
            yield return MeshTransform.Transformed(Shapes.Cylinder(w / 2f, 0, length, default, 10),
                MeshTransform.RotationBetween(Vector3.UnitZ, along) * Matrix4x4.CreateTranslation(from - along * (w / 2f)));
        }

        static Vector2 Dir(float a) => new(MathF.Cos(a), MathF.Sin(a));
    }

    /// <summary>
    /// A band between two radii from one angle to another, its foot and top at heights given by
    /// the angle: a rectangle swept round and up, closed at both ends.
    /// </summary>
    private static Mesh Helix(float inner, float outer, float from, float to, Func<float, float> foot, Func<float, float> top)
    {
        int n = Math.Max(8, (int)MathF.Ceiling((to - from) * outer / 0.8f));
        var mesh = new Mesh();
        Vector3[] Ring(int k)
        {
            float a = from + (to - from) * k / n, c = MathF.Cos(a), sn = MathF.Sin(a), lo = foot(a), hi = top(a);
            return [new(inner * c, inner * sn, lo), new(outer * c, outer * sn, lo), new(outer * c, outer * sn, hi), new(inner * c, inner * sn, hi)];
        }

        var first = Ring(0);
        mesh.AddTriangle(first[0], first[2], first[1]);
        mesh.AddTriangle(first[0], first[3], first[2]);
        var prev = first;
        for (int k = 1; k <= n; k++)
        {
            var next = Ring(k);
            for (int e = 0; e < 4; e++)
            {
                int f = (e + 1) % 4;
                mesh.AddTriangle(prev[e], prev[f], next[f]);
                mesh.AddTriangle(prev[e], next[f], next[e]);
            }

            prev = next;
        }

        mesh.AddTriangle(prev[0], prev[1], prev[2]);
        mesh.AddTriangle(prev[0], prev[2], prev[3]);

        var built = mesh.Welded();
        if (built.ComputeSignedVolume() < 0) built.FlipWinding();
        return built;
    }

    protected override IEnumerable<string> Describe(Settings s, float modelScale)
    {
        float scale = modelScale <= 0 ? 1f : modelScale;

        // A spiral is walked on the line halfway along its steps.
        float run = s.Kind == StairKind.Spiral ? s.Turn * MathF.PI / 180f * (s.Column / 2f + s.Width / 2f) : s.Run;
        var check = StairBuilder.Measure(s.Rise, run, s.Steps, scale);

        string real = scale > 1.5f
            ? $"At 1:{scale:0}, that is {check.RiserMm:0} mm risers on {check.GoingMm:0} mm treads."
            : $"Risers of {check.RiserMm:0.#} mm on treads of {check.GoingMm:0.#} mm.";

        yield return $"{s.Steps} risers of {s.Rise / s.Steps:0.##} mm. {real}";

        if (check.Advice.Length > 0) yield return check.Advice;
        else if (check.IsClimbable) yield return "A comfortable flight.";
    }
}
