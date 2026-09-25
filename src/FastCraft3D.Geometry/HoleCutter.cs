using System.Globalization;
using System.Numerics;
using FastCraft3D.Geometry.Csg;

namespace FastCraft3D.Geometry;

public enum HoleKind
{
    /// <summary>A hole a screw passes through.</summary>
    Screw,

    /// <summary>A pocket a heat-set threaded insert is melted into.</summary>
    Insert
}

public enum HoleHead
{
    /// <summary>A plain hole: the head sits on the surface.</summary>
    Plain,

    /// <summary>A 90 degree cone, so a countersunk head sits flush.</summary>
    Countersunk,

    /// <summary>A wider, flat-bottomed step, so a cap head sits below the surface.</summary>
    Counterbored
}

/// <summary>How several holes are set out round the handles.</summary>
public enum HolePattern
{
    /// <summary>In a line along the cutter's own X, centred on the handles.</summary>
    Row,

    /// <summary>Evenly round a circle centred on the handles: a bolt circle.</summary>
    Circle
}

/// <param name="Name">M3 and so on.</param>
/// <param name="Clearance">The hole a screw passes through, ISO 273 medium.</param>
/// <param name="CountersunkHead">Across the head of a countersunk screw, ISO 10642.</param>
/// <param name="CapHead">Across the head of a socket cap screw, ISO 4762.</param>
/// <param name="CapHeight">How tall that head is.</param>
/// <param name="NutAcrossFlats">A hexagon nut, ISO 4032.</param>
/// <param name="NutThickness">How thick it is.</param>
/// <param name="InsertHole">The pocket for a common heat-set insert of this thread.</param>
/// <param name="InsertLength">How long that insert is.</param>
public readonly record struct ScrewSize(
    string Name, float Clearance, float CountersunkHead, float CapHead, float CapHeight,
    float NutAcrossFlats, float NutThickness, float InsertHole, float InsertLength);

/// <summary>What hole to make. A record class, so a new one has these defaults.</summary>
public sealed record HoleOptions
{
    public HoleKind Kind { get; init; } = HoleKind.Screw;
    public string Size { get; init; } = "M3";
    public HoleHead Head { get; init; } = HoleHead.Countersunk;

    /// <summary>A hexagon pocket at the far end for the nut to sit in.</summary>
    public bool NutPocket { get; init; }

    /// <summary>How deep the hole goes below the surface.</summary>
    public float Depth { get; init; } = 10f;

    /// <summary>
    /// Added to every diameter, for a printer: holes come out small, as the plastic spreads into
    /// them. Not added to an insert's pocket, which is meant to be tight.
    /// </summary>
    public float ExtraClearance { get; init; } = 0.2f;

    /// <summary>How many holes, set out by <see cref="Pattern"/>.</summary>
    public int Count { get; init; } = 1;

    public HolePattern Pattern { get; init; } = HolePattern.Row;

    /// <summary>Between the middles of neighbouring holes in a row.</summary>
    public float Spacing { get; init; } = 20f;

    /// <summary>Across the circle the holes' middles lie on.</summary>
    public float CircleDiameter { get; init; } = 30f;
}

/// <summary>
/// The solid a screw hole or an insert pocket is cut with: a shaft, a countersink or counterbore at
/// the top, and a nut pocket at the bottom if asked for, in the standard sizes for each thread.
///
/// Built by turning its outline round the axis, so it is closed by construction and a single
/// Subtract takes it out of a part. It stands a little proud of the surface it opens from, or the
/// cut would leave a skin of the part's own face across the top of the hole.
/// </summary>
public static class HoleCutter
{
    /// <summary>How far the cutter stands out above the surface.</summary>
    public const float Overshoot = 0.5f;

    private const int Sides = 48;

    public static IReadOnlyList<ScrewSize> Sizes { get; } =
    [
        new("M2", 2.4f, 4.4f, 3.8f, 2.0f, 4.0f, 1.6f, 3.2f, 4.0f),
        new("M2.5", 2.9f, 5.5f, 4.5f, 2.5f, 5.0f, 2.0f, 3.6f, 5.0f),
        new("M3", 3.4f, 6.72f, 5.5f, 3.0f, 5.5f, 2.4f, 4.0f, 5.7f),
        new("M4", 4.5f, 8.96f, 7.0f, 4.0f, 7.0f, 3.2f, 5.6f, 8.1f),
        new("M5", 5.5f, 11.2f, 8.5f, 5.0f, 8.0f, 4.7f, 6.4f, 9.5f),
        new("M6", 6.6f, 13.44f, 10.0f, 6.0f, 10.0f, 5.2f, 8.0f, 12.7f),
        new("M8", 9.0f, 17.92f, 13.0f, 8.0f, 13.0f, 6.8f, 9.6f, 12.7f)
    ];

    public static ScrewSize SizeOf(string name) => Sizes.FirstOrDefault(s => s.Name == name, Sizes[2]);

    /// <summary>
    /// How deep the hole actually goes: as asked for a screw, held deep enough for its head and
    /// nut; an insert's own length and a little more, for the plastic it pushes ahead of it.
    /// </summary>
    public static float DepthOf(HoleOptions o, float asked)
    {
        var size = SizeOf(o.Size);
        if (o.Kind == HoleKind.Insert) return size.InsertLength + 1f;

        float least = o.Head switch
        {
            HoleHead.Countersunk => (size.CountersunkHead - size.Clearance) / 2f + 1f,
            HoleHead.Counterbored => size.CapHeight + 1.5f,
            _ => 1f
        };
        if (o.NutPocket) least += size.NutThickness + 1f;

        return MathF.Max(float.IsFinite(asked) ? asked : o.Depth, least);
    }

    /// <summary>
    /// Where each hole's middle goes, in the cutter's own frame: across its X and Y, the handles at
    /// the origin. A row along X centred on the handles, or a circle round them starting on +X.
    /// </summary>
    public static List<Vector2> Stations(HoleOptions o)
    {
        int count = Math.Clamp(o.Count, 1, 100);
        if (count == 1) return [Vector2.Zero];

        if (o.Pattern == HolePattern.Circle)
        {
            float radius = MathF.Max(o.CircleDiameter, 0f) / 2f;
            return Enumerable.Range(0, count)
                .Select(i => radius * new Vector2(MathF.Cos(MathF.Tau * i / count), MathF.Sin(MathF.Tau * i / count)))
                .ToList();
        }

        float spacing = MathF.Max(o.Spacing, 0f);
        return Enumerable.Range(0, count).Select(i => new Vector2((i - (count - 1) / 2f) * spacing, 0f)).ToList();
    }

    /// <summary>How wide a hole is at its widest: its head, its nut, or its insert's lead-in.</summary>
    public static float Widest(HoleOptions o)
    {
        var size = SizeOf(o.Size);
        float extra = MathF.Max(0f, o.ExtraClearance);
        if (o.Kind == HoleKind.Insert) return size.InsertHole + 1f;

        float widest = size.Clearance + extra;
        if (o.Head == HoleHead.Countersunk) widest = size.CountersunkHead + extra;
        if (o.Head == HoleHead.Counterbored) widest = size.CapHead + 1f + extra;
        if (o.NutPocket) widest = MathF.Max(widest, 2f * (size.NutAcrossFlats + extra) / MathF.Sqrt(3f));
        return widest;
    }

    /// <summary>
    /// How far along a ray the last of a solid's surface is: where a hole drilled that way comes out
    /// the far side. Null when the ray misses it.
    /// </summary>
    public static float? FarSide(Mesh world, Vector3 origin, Vector3 direction)
    {
        direction = Vector3.Normalize(direction);
        float? far = null;

        for (int t = 0; t + 2 < world.Indices.Count; t += 3)
        {
            var a = world.Positions[world.Indices[t]];
            var b = world.Positions[world.Indices[t + 1]];
            var c = world.Positions[world.Indices[t + 2]];

            // Moller-Trumbore, both sides of the face counted.
            var ab = b - a;
            var ac = c - a;
            var p = Vector3.Cross(direction, ac);
            float det = Vector3.Dot(ab, p);
            if (MathF.Abs(det) < 1e-9f) continue;

            var s = origin - a;
            float u = Vector3.Dot(s, p) / det;
            if (u < -1e-5f || u > 1f + 1e-5f) continue;

            var q = Vector3.Cross(s, ab);
            float v = Vector3.Dot(direction, q) / det;
            if (v < -1e-5f || u + v > 1f + 1e-5f) continue;

            float along = Vector3.Dot(ac, q) / det;
            if (along > 0f && (far is null || along > far)) far = along;
        }

        return far;
    }

    /// <summary>
    /// The cutter, its axis on Z, the surface it opens from at Z = 0 and the hole going down to
    /// <paramref name="depth"/> below it; or null when the nut pocket could not be joined on cleanly.
    /// <paramref name="through"/>: the depth is where the far side of the part is, so the shaft goes
    /// a little past it rather than ending in its surface, and a nut pocket sinks into that side.
    /// </summary>
    public static Mesh? Build(HoleOptions o, float depth, bool through = false)
    {
        var size = SizeOf(o.Size);
        float extra = MathF.Max(0f, float.IsFinite(o.ExtraClearance) ? o.ExtraClearance : 0f);
        depth = DepthOf(o, depth);

        List<(float Radius, float Z)> outline;
        if (o.Kind == HoleKind.Insert)
        {
            // A small lead-in, so the insert starts square before it is pressed in.
            float r = size.InsertHole / 2f;
            outline = [(r + 0.5f, Overshoot), (r + 0.5f, 0f), (r, -0.5f), (r, -depth)];
        }
        else
        {
            float shaft = (size.Clearance + extra) / 2f;
            switch (o.Head)
            {
                case HoleHead.Countersunk:
                {
                    float head = (size.CountersunkHead + extra) / 2f;
                    outline = [(head, Overshoot), (head, 0f), (shaft, -(head - shaft)), (shaft, -depth)];
                    break;
                }
                case HoleHead.Counterbored:
                {
                    // A millimetre wider than the head, which a printed wall needs to clear it.
                    float bore = (size.CapHead + 1f + extra) / 2f;
                    float down = size.CapHeight + 0.5f;
                    outline = [(bore, Overshoot), (bore, -down), (shaft, -down), (shaft, -depth)];
                    break;
                }
                default:
                    outline = [(shaft, Overshoot), (shaft, -depth)];
                    break;
            }

            // Out through the far side, as it stands proud of the near one.
            if (through) outline[^1] = (shaft, -depth - Overshoot);
        }

        var cutter = Lathe(outline, Sides);
        if (o.Kind == HoleKind.Insert || !o.NutPocket) return cutter;

        // Open at the bottom, as deep as the nut and a little more, turned to lie flat on a flat of
        // the hexagon.
        float across = size.NutAcrossFlats + extra;
        float from = -depth - Overshoot, to = -depth + size.NutThickness + 0.3f;
        var nut = MeshTransform.Transformed(
            Primitives.Prism(across / MathF.Sqrt(3f), to - from, 6),
            Matrix4x4.CreateRotationZ(MathF.PI / 6f) * Matrix4x4.CreateTranslation(0, 0, (from + to) / 2f));

        var joined = ManifoldCsg.Union(cutter, nut) ?? LocalCsg.Union(cutter, nut);
        return joined.CheckHealth().IsWatertight ? joined : null;
    }

    /// <summary>
    /// A solid turned from an outline of radius and height pairs, from the top down, closed with a
    /// flat face at each end.
    /// </summary>
    public static Mesh Lathe(IReadOnlyList<(float Radius, float Z)> outline, int sides)
    {
        var mesh = new Mesh();
        Vector3 At(int k, int i)
        {
            float angle = MathF.Tau * i / sides;
            return new Vector3(outline[k].Radius * MathF.Cos(angle), outline[k].Radius * MathF.Sin(angle), outline[k].Z);
        }

        for (int k = 0; k + 1 < outline.Count; k++)
            for (int i = 0; i < sides; i++)
            {
                int j = (i + 1) % sides;
                mesh.AddTriangle(At(k, i), At(k + 1, i), At(k + 1, j));
                mesh.AddTriangle(At(k, i), At(k + 1, j), At(k, j));
            }

        var top = new Vector3(0, 0, outline[0].Z);
        var bottom = new Vector3(0, 0, outline[^1].Z);
        for (int i = 0; i < sides; i++)
        {
            int j = (i + 1) % sides;
            mesh.AddTriangle(top, At(0, i), At(0, j));
            mesh.AddTriangle(bottom, At(outline.Count - 1, j), At(outline.Count - 1, i));
        }

        return mesh.Welded();
    }

    /// <summary>How the holes are set out, and whether they run into each other, as lines for the panel.</summary>
    public static List<string> DescribePattern(HoleOptions o)
    {
        var lines = new List<string>();
        int count = Math.Clamp(o.Count, 1, 100);
        if (count == 1) return lines;

        var stations = Stations(o);
        float nearest = float.MaxValue;
        for (int i = 0; i < stations.Count; i++)
            for (int j = i + 1; j < stations.Count; j++)
                nearest = MathF.Min(nearest, Vector2.Distance(stations[i], stations[j]));

        lines.Add(o.Pattern == HolePattern.Circle
            ? $"{count} round a {F(o.CircleDiameter)} mm circle, {F(nearest)} mm apart."
            : $"{count} in a row, {F(o.Spacing)} mm apart.");
        if (nearest < Widest(o))
            lines.Add($"They run into each other - at least {F(Widest(o))} mm apart keeps them separate.");

        return lines;

        static string F(float value) => value.ToString("0.##", CultureInfo.CurrentCulture);
    }

    /// <summary>The sizes that go into a hole, as lines for the panel.</summary>
    public static List<string> Describe(HoleOptions o, float depth)
    {
        var size = SizeOf(o.Size);
        float extra = MathF.Max(0f, o.ExtraClearance);
        depth = DepthOf(o, depth);
        var lines = new List<string>();

        if (o.Kind == HoleKind.Insert)
        {
            lines.Add($"{F(size.InsertHole)} mm pocket, {F(depth)} mm deep, for a {size.Name} x {F(size.InsertLength)} heat-set insert.");
            return lines;
        }

        lines.Add(o.Count > 1
            ? $"{F(size.Clearance + extra)} mm holes, {F(depth)} mm deep."
            : $"{F(size.Clearance + extra)} mm hole, {F(depth)} mm deep.");
        if (o.Head == HoleHead.Countersunk)
            lines.Add($"Countersunk to {F(size.CountersunkHead + extra)} mm at 90 degrees, for an ISO 10642 head.");
        if (o.Head == HoleHead.Counterbored)
            lines.Add($"Counterbored {F(size.CapHead + 1f + extra)} mm across and {F(size.CapHeight + 0.5f)} mm deep, for a cap head.");
        if (o.NutPocket)
            lines.Add($"Nut pocket {F(size.NutAcrossFlats + extra)} mm across the flats, {F(size.NutThickness + 0.3f)} mm deep, at the far end.");

        return lines;

        static string F(float value) => value.ToString("0.##", CultureInfo.CurrentCulture);
    }
}
