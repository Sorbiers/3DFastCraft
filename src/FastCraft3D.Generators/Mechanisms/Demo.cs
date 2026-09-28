using System.Numerics;
using FastCraft3D.Geometry;
using FastCraft3D.Geometry.Csg;

namespace FastCraft3D.Generators.Mechanisms;

/// <summary>One part of a demonstration model: where it goes as the model goes together, and how to turn it to print.</summary>
internal sealed class DemoPiece(string name, string role, Mesh world, Matrix4x4 toPrint)
{
    public string Name { get; } = name;
    public string Role { get; } = role;
    public Mesh World { get; set; } = world;

    /// <summary>From where it goes to how it prints - standing on the plate, the right way up.</summary>
    public Matrix4x4 ToPrint { get; } = toPrint;

    /// <summary>Takes part in the base's footprint and in the handles' clearances.</summary>
    public bool Counts { get; init; } = true;
}

/// <summary>
/// A mechanism made into a model to print and turn by hand: a base, a shaft for everything that
/// turns, a handle to turn it by, each part held where it goes.
///
/// A shaft is round where it turns and flatted - a D - only where it drives. A gear, a crank or a
/// handwheel has a D-shaped hole a hair over the shaft and grips it: a round shaft pressed into a
/// round hole creeps loose in plastic and slips, where the flat still drives when it has. Where the
/// shaft turns, in a round hole through a boss on the base or a bearing, it is round, with a
/// running clearance all round. Hexagons were tried first, and were wrong for it: a round hole has
/// to clear a hexagon's corners, which left more than half a millimetre of play across its flats -
/// several times the gears' backlash - and six edges grinding on the hole's layers. A D the whole
/// way was tried next, and turned on its flat's edges in the base as the hexagon had.
///
/// The flat runs from the first keyed part to the end the shaft goes in by, since everything
/// that has to pass through a keyed hole on the way in has to be a D; the rest stays round.
///
/// Upright, a shaft stands on a head sunk in a pocket under the base, and what is keyed on it rests
/// on the boss: held from below by the head, from above by the grip, so nothing lifts off and
/// nothing drops out. Lying down, it runs through two bearings and is held between what is keyed
/// on it either side.
///
/// Every shaft prints standing on its end - an upright one on its round head - since a shaft round
/// at its ends has no flat to lie on. The flat is deep enough to drive and shallow enough to leave
/// most of the round. Every handle
/// prints flat with its hub and knob upward, the base with its bosses and bearings up and its
/// pockets on the plate, and nothing anywhere overhangs: a bearing's hole is a teardrop, and a
/// guide is closed over the top, a bridge, rather than lipped.
/// </summary>
internal sealed class Demo
{
    public const float Plate = 5f;
    public const float Pocket = 2.6f;
    public const float Gap = 0.4f;
    public const float Rise = 3f;

    private const float HeadThick = 2f, HubHeight = 8f, KnobRadius = 4f, KnobHeight = 14f, ArmWidth = 8f, ArmThick = 4f;
    private const float Bearing = 6f, BearingSpan = 10f, WheelThick = 5f;

    public Demo(Printer printer, float shaft)
    {
        Size = MathF.Max(shaft, 4f);
        Run = MathF.Max(0.25f, printer.XyClearance + 0.05f);
        Grip = Math.Clamp(printer.XyClearance * 0.25f, 0.02f, 0.1f);
    }

    /// <summary>Every shaft's diameter.</summary>
    public float Size { get; }

    /// <summary>
    /// How far the flat is from the middle. Deep enough that, printed lying on it, the round rises
    /// from the flat's edges at 45 degrees and no flatter.
    /// </summary>
    public float Flat => 0.35f * Size;

    /// <summary>Round anything that turns in a hole, each side.</summary>
    public float Run { get; }

    /// <summary>Round a shaft in the part keyed to it, each side: a push fit, not a slide.</summary>
    public float Grip { get; }

    /// <summary>Where the boss under a shaft's lowest part stands, at the least.</summary>
    public static float Deck => Plate + Rise;

    /// <summary>Where the lowest part of the mechanism stands, over its boss.</summary>
    public static float Lift => Deck + Gap;

    public float HoleRadius => Size / 2f + Run;
    private float HeadRadius => HoleRadius + 2f;

    public readonly List<DemoPiece> Pieces = [];

    /// <summary>The shafts and handles, each with the part it is keyed to: they turn as that part does.</summary>
    public readonly List<(int Part, int With)> Riders = [];
    private readonly List<Spindle> spindles = [];
    private readonly List<Mesh> baseAdd = [], baseCut = [];
    private readonly List<(float X0, float Y0, float X1, float Y1)> reach = [];

    public enum Supports { Ends, Outside }

    private sealed record Spindle(
        bool Upright, Vector3 Point, Vector3 Along, int[] Carried, bool Handle, bool Head, Supports Supports);

    /// <summary>A part already where it goes, printed as it lies.</summary>
    public int Lying(string name, string role, Mesh world)
    {
        var b = world.ComputeBounds();
        Pieces.Add(new DemoPiece(name, role, world, Matrix4x4.CreateTranslation(0, 0, -b.Min.Z)));
        return Pieces.Count - 1;
    }

    /// <summary>A part already where it goes, printed turned as given.</summary>
    public int Placed(string name, string role, Mesh world, Matrix4x4 toPrint)
    {
        Pieces.Add(new DemoPiece(name, role, world, toPrint));
        return Pieces.Count - 1;
    }

    /// <summary>An upright shaft through the base at <paramref name="at"/>, the parts given keyed on it.</summary>
    public void Upright(Vector2 at, int[] carried, bool handle = false, bool head = true) =>
        spindles.Add(new Spindle(true, new Vector3(at, 0), Vector3.UnitZ, carried, handle, head, Supports.Ends));

    /// <summary>A shaft lying along <paramref name="along"/> through <paramref name="point"/>, in two bearings.</summary>
    public void Lying(Vector3 point, Vector3 along, int[] carried, Supports supports, bool handle = false)
    {
        along = Vector3.Normalize(along with { Z = 0 });
        spindles.Add(new Spindle(false, point, along, carried, handle, false, supports));
    }

    public void AddToBase(Mesh piece) => baseAdd.Add(piece);
    public void CutFromBase(Mesh piece) => baseCut.Add(piece);

    /// <summary>Makes the base reach at least this far, for a part that moves over more of it than it stands on.</summary>
    public void Cover(float x0, float y0, float x1, float y1) => reach.Add((x0, y0, x1, y1));

    /// <summary>The D a part is keyed by, round an axis: the shaft's, grown by the grip.</summary>
    public Mesh KeyHole(Vector2 at, float z0, float z1) => Shapes.Prism(D(Size / 2f + Grip, Flat + Grip, at), z0, z1);

    /// <summary>A circle less the part beyond a flat square to plus X, anticlockwise.</summary>
    private static List<Vector2> D(float radius, float flat, Vector2 at)
    {
        float from = MathF.Acos(Math.Clamp(flat / radius, -1f, 1f));
        int n = Shapes.Sides(radius);
        var loop = new List<Vector2>(n + 1);
        for (int i = 0; i <= n; i++)
        {
            float a = from + (2f * MathF.PI - 2f * from) * i / n;
            loop.Add(at + radius * new Vector2(MathF.Cos(a), MathF.Sin(a)));
        }

        return loop;
    }

    /// <summary>
    /// A shaft from one height to another round Z, round but for a flat to plus X over each stretch
    /// in <paramref name="flatted"/>, with a round head under it if asked.
    /// </summary>
    private Mesh Shaft(Vector2 at, float from, float to, bool head, IEnumerable<(float From, float To)> flatted)
    {
        float r = Size / 2f;
        var body = Shapes.Cylinder(r, head ? from + HeadThick - 0.01f : from, to, at, Shapes.Sides(r));
        var flats = flatted.Where(f => f.To > f.From)
            .Select(f => Shapes.Box(at.X + Flat, at.Y - r - 1, MathF.Max(f.From, from - 1), at.X + r + 1, at.Y + r + 1, MathF.Min(f.To, to + 1)))
            .ToList();
        if (flats.Count > 0) body = Shapes.Subtract(body, flats);
        return head ? Shapes.Union(Shapes.Cylinder(HeadRadius, from, from + HeadThick, at), body) : body;
    }

    /// <summary>
    /// The shafts, the handles and the base, and every part as it prints with where it goes. The
    /// pieces the mechanism already made come first, in the order given, so its motion still names
    /// them by number.
    /// </summary>
    public List<GeneratedPart> Finish(CancellationToken token = default)
    {
        // Lying shafts first: their bearings are on the base before any crank is raised clear of it.
        foreach (var s in spindles.OrderBy(s => s.Upright))
        {
            token.ThrowIfCancellationRequested();
            if (s.Upright) BuildUpright(s);
            else BuildLying(s);
        }

        token.ThrowIfCancellationRequested();
        Pieces.Add(new DemoPiece("Base", "base", Base(), Matrix4x4.Identity));

        var row = Pieces.Select(p =>
        {
            Matrix4x4.Invert(p.ToPrint, out var back);
            return (p.Name, p.Role, MeshTransform.Transformed(p.World, p.ToPrint), (Matrix4x4?)back);
        }).ToList();
        return Shapes.InARow(row);
    }

    // --- Upright shafts ---------------------------------------------------------------------

    private void BuildUpright(Spindle s)
    {
        var at = new Vector2(s.Point.X, s.Point.Y);
        float lo = float.MaxValue, hi = float.MinValue, radius = 0;
        foreach (int i in s.Carried)
        {
            var b = Pieces[i].World.ComputeBounds();
            lo = MathF.Min(lo, b.Min.Z);
            hi = MathF.Max(hi, b.Max.Z);
            radius = MathF.Max(radius, Farthest(Pieces[i].World, at));
            Pieces[i].World = Shapes.Subtract(Pieces[i].World, KeyHole(at, lo - 1, hi + 1));
        }

        // The boss up to under the lowest part, the hole through it, the pocket for the head.
        float boss = MathF.Max(Plate + 0.5f, lo - Gap);
        AddToBase(Shapes.Cylinder(HoleRadius + 2.5f, Plate - 0.01f, boss, at));
        CutFromBase(Shapes.Cylinder(HoleRadius, -1, boss + 1, at, Shapes.Sides(HoleRadius)));
        if (s.Head) CutFromBase(Shapes.Cylinder(HeadRadius + Run, -1, Pocket, at));

        float top = hi - 0.5f;
        if (s.Handle)
        {
            float arm = Math.Clamp(radius - 4f, 14f, 40f);
            float sweep = arm + KnobRadius + 1f;

            // Over the parts on its own shaft, and over anything else within its sweep - rails
            // and posts on the base as much as parts.
            float under = hi;
            var near = Pieces.Where((p, i) => p.Counts && !s.Carried.Contains(i)).Select(p => p.World.ComputeBounds())
                .Concat(baseAdd.Select(m => m.ComputeBounds()));
            foreach (var b in near)
            {
                float dx = MathF.Max(0, MathF.Max(b.Min.X - at.X, at.X - b.Max.X));
                float dy = MathF.Max(0, MathF.Max(b.Min.Y - at.Y, at.Y - b.Max.Y));
                if (dx * dx + dy * dy < sweep * sweep) under = MathF.Max(under, b.Max.Z);
            }

            // Pointing away from the rest, so it stands clear as it is put down.
            var rest = Pieces.Where((p, i) => p.Counts && !s.Carried.Contains(i)).Select(p => p.World.ComputeBounds().Center).ToList();
            var away = rest.Count == 0 ? Vector2.UnitX : at - new Vector2(rest.Average(c => c.X), rest.Average(c => c.Y));
            float angle = away.LengthSquared() < 1e-6f ? 0f : MathF.Atan2(away.Y, away.X);

            float bottom = under + Gap;

            // Turned before it is keyed, so its flat lies as the shaft's does.
            var crank = Shapes.Subtract(Shapes.Turned(Crank(arm), angle), KeyHole(Vector2.Zero, -1, HubHeight + 1));
            Pieces.Add(new DemoPiece("Crank", "crank", Shapes.Moved(crank, at.X, at.Y, bottom), Matrix4x4.CreateTranslation(-at.X, -at.Y, -bottom)) { Counts = false });
            Riders.Add((Pieces.Count - 1, s.Carried[0]));
            top = bottom + HubHeight - 0.5f;
        }

        // Round through the base, flatted from the lowest part up: it goes in from underneath,
        // top first, and only what passes through the parts has to be a D.
        var shaft = Shaft(at, 0.3f, top, s.Head, [(lo - 0.2f, top + 1f)]);
        Pieces.Add(new DemoPiece("Shaft", "shaft", shaft, Matrix4x4.CreateTranslation(-at.X, -at.Y, -0.3f)) { Counts = false });
        Riders.Add((Pieces.Count - 1, s.Carried[0]));
    }

    /// <summary>A crank, not yet keyed: a hub, an arm, a knob standing up at its end. Along plus X.</summary>
    private Mesh Crank(float arm)
    {
        float hub = Size / 2f + 3f;
        return Shapes.Union(
            Shapes.Cylinder(hub, 0, HubHeight),
            Shapes.Prism(Shapes.RoundedRect(arm + ArmWidth, ArmWidth, ArmWidth / 2f - 0.01f, new Vector2(arm / 2f, 0)), 0, ArmThick),
            Shapes.Cylinder(KnobRadius, ArmThick - 0.01f, ArmThick + KnobHeight, new Vector2(arm, 0)));
    }

    // --- Shafts lying down ------------------------------------------------------------------

    /// <summary>From a frame with Z along the shaft, X across it level and Y up, to where it goes.</summary>
    private static Matrix4x4 Frame(Vector3 point, Vector3 along)
    {
        var across = Vector3.Normalize(Vector3.Cross(Vector3.UnitZ, along));
        return new Matrix4x4(
            across.X, across.Y, across.Z, 0,
            0, 0, 1, 0,
            along.X, along.Y, along.Z, 0,
            point.X, point.Y, point.Z, 1);
    }

    private void BuildLying(Spindle s)
    {
        var frame = Frame(s.Point, s.Along);
        Matrix4x4.Invert(frame, out var into);

        float s0 = float.MaxValue, s1 = float.MinValue;
        foreach (int i in s.Carried)
        {
            var local = MeshTransform.Transformed(Pieces[i].World, into).ComputeBounds();
            s0 = MathF.Min(s0, local.Min.Z);
            s1 = MathF.Max(s1, local.Max.Z);
        }

        foreach (int i in s.Carried)
            Pieces[i].World = Shapes.Subtract(Pieces[i].World, MeshTransform.Transformed(KeyHole(Vector2.Zero, s0 - 1, s1 + 1), frame));

        // Each bearing a block up from the plate, the hole a teardrop so its top prints without
        // support. Moved out along the shaft, away from what it carries, until it is clear of
        // everything else: a worm's bearings, hard against its ends, stood in its wheel.
        float height = s.Point.Z, half = HoleRadius + 4f;
        var others = Pieces.Where((p, i) => p.Counts && !s.Carried.Contains(i)).Select(p => p.World).ToList();
        Mesh Block(float a, float b) => MeshTransform.Transformed(Shapes.Box(-half, Plate - 0.01f - height, a, half, HoleRadius + 4f, b), frame);
        float Clear(float from, int way)
        {
            for (float off = 0; off < 80f; off += 2f)
            {
                float a = way > 0 ? from + off : from - off - Bearing;
                if (!others.Any(o => Clash(Block(a, a + Bearing), o))) return a;
            }

            throw new Refusal("There is no room for the shaft's bearings clear of the rest.");
        }

        (float, float)[] bearings;
        if (s.Supports == Supports.Ends)
        {
            float low = Clear(s0 - Gap, -1), high = Clear(s1 + Gap, +1);
            bearings = [(low, low + Bearing), (high, high + Bearing)];
        }
        else
        {
            float first = Clear(s1 + Gap, +1), second = Clear(first + Bearing + BearingSpan, +1);
            bearings = [(first, first + Bearing), (second, second + Bearing)];
        }

        foreach (var (a, b) in bearings)
        {
            AddToBase(Block(a, b));
            CutFromBase(MeshTransform.Transformed(Shapes.Prism(Teardrop(HoleRadius), a - 1, b + 1), frame));
        }

        float start = s.Supports == Supports.Ends ? bearings[0].Item1 - 0.6f : s0 + 0.5f;
        float end = bearings[^1].Item2 + 1.5f;
        float hub = end;

        if (s.Handle)
        {
            float wheel = MathF.Min(15f, height - Plate - 1.5f);
            if (wheel < 8f) throw new Refusal("The shaft lies too low for a handwheel.");

            // Out along the shaft until it is clear of everything, as the bearings are.
            var round = Handwheel(wheel);
            float at = bearings[^1].Item2 + Gap;
            while (others.Any(o => Clash(MeshTransform.Transformed(round, Matrix4x4.CreateTranslation(0, 0, at) * frame), o)))
            {
                at += 2f;
                if (at > bearings[^1].Item2 + 80f) throw new Refusal("There is no room for the handwheel clear of the rest.");
            }

            var hand = MeshTransform.Transformed(round, Matrix4x4.CreateTranslation(0, 0, at) * frame);
            Matrix4x4.Invert(Matrix4x4.CreateTranslation(0, 0, at) * frame, out var flat);
            Pieces.Add(new DemoPiece("Handwheel", "handwheel", hand, flat) { Counts = false });
            Riders.Add((Pieces.Count - 1, s.Carried[0]));
            end = at + HubHeight - 0.5f;
            hub = at - 0.2f;
        }

        // Flatted only where it has to pass through keyed holes going in. Between two bearings,
        // it goes in by the handle's end, so from the parts it carries onwards; beyond them both,
        // by its inner end through the bearings, so under the parts and again under the handle.
        (float, float)[] flats = s.Supports == Supports.Ends
            ? [(s0 - 0.2f, end + 1f)]
            : [(start - 1f, s1 + 0.2f), (hub, end + 1f)];
        var shaft = MeshTransform.Transformed(Shaft(Vector2.Zero, start, end, head: false, flats), frame);
        Matrix4x4.Invert(frame, out var back);
        Pieces.Add(new DemoPiece("Shaft", "shaft", shaft, back * Matrix4x4.CreateTranslation(0, 0, -start)) { Counts = false });
        Riders.Add((Pieces.Count - 1, s.Carried[0]));
    }

    /// <summary>Whether two solids share any room - more than a rounding's worth.</summary>
    public static bool Clash(Mesh a, Mesh b)
    {
        var ba = a.ComputeBounds();
        var bb = b.ComputeBounds();
        if (ba.Max.X < bb.Min.X || bb.Max.X < ba.Min.X || ba.Max.Y < bb.Min.Y || bb.Max.Y < ba.Min.Y || ba.Max.Z < bb.Min.Z || bb.Max.Z < ba.Min.Z)
            return false;

        return ManifoldCsg.Intersect(a, b) is { TriangleCount: > 0 } common && Math.Abs(common.ComputeSignedVolume()) > 0.01;
    }

    /// <summary>A wheel to turn a lying shaft by: a disc, a hub keyed to the shaft, a knob near the rim.</summary>
    private Mesh Handwheel(float radius)
    {
        float hub = Size / 2f + 3f;
        var solid = Shapes.Union(
            Shapes.Cylinder(radius, 0, WheelThick),
            Shapes.Cylinder(hub, WheelThick - 0.01f, HubHeight),
            Shapes.Cylinder(3f, WheelThick - 0.01f, WheelThick + 10f, new Vector2(radius - 4f, 0)));
        return Shapes.Subtract(solid, KeyHole(Vector2.Zero, -1, HubHeight + 1));
    }

    /// <summary>A round hole with a point on top, so a hole lying on its side prints without support.</summary>
    private static List<Vector2> Teardrop(float r)
    {
        var loop = new List<Vector2>();
        int n = Shapes.Sides(r);
        for (int i = 0; i <= n; i++)
        {
            float a = MathF.PI / 4f + (MathF.PI * 1.5f) * i / n + MathF.PI / 2f;
            loop.Add(new Vector2(r * MathF.Cos(a), r * MathF.Sin(a)));
        }

        loop.Add(new Vector2(0, r * MathF.Sqrt(2f)));
        return loop;
    }

    // --- The base ---------------------------------------------------------------------------

    private Mesh Base()
    {
        float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
        void Take(float a, float b, float c, float d) { x0 = MathF.Min(x0, a); y0 = MathF.Min(y0, b); x1 = MathF.Max(x1, c); y1 = MathF.Max(y1, d); }

        foreach (var p in Pieces.Where(p => p.Counts))
        {
            var b = p.World.ComputeBounds();
            Take(b.Min.X, b.Min.Y, b.Max.X, b.Max.Y);
        }

        foreach (var m in baseAdd)
        {
            var b = m.ComputeBounds();
            Take(b.Min.X, b.Min.Y, b.Max.X, b.Max.Y);
        }

        foreach (var (a, b, c, d) in reach) Take(a, b, c, d);

        const float margin = 4f;
        float w = x1 - x0 + 2 * margin, h = y1 - y0 + 2 * margin;
        var plate = Shapes.Prism(Shapes.RoundedRect(w, h, 5f, new Vector2((x0 + x1) / 2f, (y0 + y1) / 2f)), 0, Plate);

        var solid = Shapes.Union([plate, .. baseAdd]);
        return baseCut.Count == 0 ? solid : Shapes.Subtract(solid, baseCut);
    }

    private static float Farthest(Mesh mesh, Vector2 from) =>
        mesh.Positions.Count == 0 ? 0 : mesh.Positions.Max(p => Vector2.Distance(new Vector2(p.X, p.Y), from));
}
