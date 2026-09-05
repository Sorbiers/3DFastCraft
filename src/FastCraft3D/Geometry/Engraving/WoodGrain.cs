using System.Numerics;

namespace FastCraft3D.Geometry.Engraving;

/// <summary>
/// Wood grain: long flowing lines that swell around knots, with a ring or two marking each knot.
///
/// Straight lines were tried first and read as nothing at all - at any spacing fine enough to
/// look like timber they are indistinguishable from the board seams beside them. Grain is the
/// flow, so the lines here are genuine curves, cut as ribbons following their own path.
///
/// The lines never cross, which the cutter depends on absolutely: two ribbons overlapping would
/// leave a wall inside the material being removed and tear the boolean open. Rather than trusting
/// the wave and knot maths to stay well behaved, the lines are generated freely and then walked
/// in order with a minimum gap enforced between neighbours. Where that bites, the grain flattens
/// slightly; it can never fold.
/// </summary>
public static class WoodGrain
{
    /// <summary>Clear space kept between neighbouring lines, as a multiple of the cut width.</summary>
    private const float Separation = 1.7f;

    /// <summary>Beyond this a knot would flatten the grain rather than bend it.</summary>
    private const float MaximumKnots = 8;

    public static List<Polyline2> Build(EngraveOptions options, Rect2 area, Vector2 anchor)
    {
        float pitch = Math.Max(options.Size, EngraveOptions.MinimumSize);

        // Grain lines are hairlines beside their spacing; a line as wide as the gap is a stripe.
        float width = Math.Min(options.GrooveWidth, pitch * 0.4f);
        float gap = width * Separation;
        if (area.IsEmpty || pitch <= gap) return [];

        float step = pitch * 0.5f;
        var samples = SampleColumns(area, step);
        var knots = Knots(area, pitch);

        var lines = Lines(area, anchor, pitch, samples, knots);
        Separate(lines, gap);

        var ribbons = lines
            .Select(line => new Polyline2(
                samples.Zip(line, (u, v) => new Vector2(u, v)).ToList(), width))
            .Where(r => r.IsUsable)
            .ToList();

        ribbons.AddRange(Rings(knots, pitch, width, gap, samples, lines));
        return ribbons;
    }

    /// <summary>How many separate cuts the pattern makes, for the estimate in the panel.</summary>
    public static int Count(EngraveOptions options, Rect2 area)
    {
        float pitch = Math.Max(options.Size, EngraveOptions.MinimumSize);
        if (area.IsEmpty || pitch <= 0) return 0;

        return (int)(area.Height / pitch) + 2 + Knots(area, pitch).Count * 2;
    }

    private static float[] SampleColumns(Rect2 area, float step)
    {
        // Fine enough to carry the shortest wave smoothly, coarse enough that a large face does
        // not turn into a hundred thousand triangles.
        step = Math.Clamp(step, area.Width / 240f, area.Width / 8f);

        int count = Math.Max((int)(area.Width / step) + 1, 2);
        return Enumerable.Range(0, count)
            .Select(i => area.MinU + Math.Min(i * step, area.Width))
            .ToArray();
    }

    /// <summary>
    /// The lines before separation is enforced: a shared wave so they run parallel, plus a swell
    /// away from each knot that is stronger the nearer the line passes to it.
    /// </summary>
    private static List<float[]> Lines(
        Rect2 area, Vector2 anchor, float pitch, float[] samples, List<Vector2> knots)
    {
        var lines = new List<float[]>();

        // Start below the area so the first visible line is not a special case.
        float first = anchor.Y + MathF.Floor((area.MinV - anchor.Y) / pitch) * pitch;

        for (float baseline = first; baseline <= area.MaxV + pitch; baseline += pitch)
        {
            var line = new float[samples.Length];

            for (int i = 0; i < samples.Length; i++)
            {
                float u = samples[i];
                float v = baseline + Wave(u, pitch);

                foreach (var knot in knots) v += Swell(u, baseline, knot, pitch);

                line[i] = v;
            }

            lines.Add(line);
        }

        return lines;
    }

    /// <summary>
    /// Two slow waves, shared by every line so the grain runs together rather than tangling.
    /// Their wavelengths are multiples of the spacing, so the grain keeps its character whether
    /// the pattern is set fine or coarse.
    /// </summary>
    private static float Wave(float u, float pitch) =>
        pitch * (0.45f * MathF.Sin(u / (18f * pitch) * MathF.Tau)
               + 0.22f * MathF.Sin(u / (7f * pitch) * MathF.Tau + 1.7f));

    /// <summary>
    /// How far a line is pushed aside by one knot: away from its centre on both sides, fading
    /// out with distance, so the grain opens around it the way it does around a real one.
    /// </summary>
    private static float Swell(float u, float baseline, Vector2 knot, float pitch)
    {
        float spread = 3f * pitch;
        float du = (u - knot.X) / (spread * 1.6f);
        float dv = (baseline - knot.Y) / spread;

        // Which side the line goes, not how far: a line running straight over the middle of the
        // knot has to be pushed off it like any other, and a push that faded to nothing at the
        // centre would leave that one line lying across the knot with no room for its rings.
        float side = dv >= 0 ? 1f : -1f;
        float falloff = MathF.Exp(-(du * du + dv * dv) * 0.5f);

        return 1.5f * pitch * side * falloff;
    }

    /// <summary>
    /// Walks the lines in order and holds each one clear of the one below it.
    ///
    /// This is what makes the pattern safe to cut whatever the wave and knots do. Everything
    /// above is shape; this is the guarantee.
    /// </summary>
    private static void Separate(List<float[]> lines, float gap)
    {
        for (int k = 1; k < lines.Count; k++)
            for (int i = 0; i < lines[k].Length; i++)
                lines[k][i] = Math.Max(lines[k][i], lines[k - 1][i] + gap);
    }

    private static List<Vector2> Knots(Rect2 area, float pitch)
    {
        // Roughly one knot per two dozen line-spacings square - enough to be a feature, not so
        // many that the grain never gets to flow.
        int wanted = (int)(area.Width * area.Height / (150f * pitch * pitch));
        int count = Math.Clamp(wanted, 0, (int)MaximumKnots);

        var knots = new List<Vector2>(count);
        for (int i = 0; i < count; i++)
        {
            // Kept off the edges: half a knot at the boundary reads as a dent, not a knot.
            knots.Add(new Vector2(
                area.MinU + area.Width * (0.12f + 0.76f * Fraction(i, 0)),
                area.MinV + area.Height * (0.12f + 0.76f * Fraction(i, 1))));
        }

        return knots;
    }

    /// <summary>
    /// The rings at the heart of each knot, fitted into the space the grain has opened up.
    ///
    /// Each ring point is held clear of the lines above and below it, exactly as the lines are
    /// held clear of each other, so a knot can never cut into its own grain.
    /// </summary>
    private static List<Polyline2> Rings(
        List<Vector2> knots, float pitch, float width, float gap, float[] samples, List<float[]> lines)
    {
        var rings = new List<Polyline2>();

        foreach (var knot in knots)
        {
            float room = RoomAt(knot, samples, lines);
            if (room < gap * 2.5f) continue; // the grain never opened far enough here

            for (int ring = 0; ring < 2; ring++)
            {
                // A ring is only drawn where it fits. Squeezing one into a gap that is not
                // there used to bend it back on itself, and a ribbon that doubles back has
                // crossed its own edge - which the boolean cannot make sense of. Shrink until
                // it fits or give the knot up; a missing ring costs nothing.
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    float ry = room * (ring == 0 ? 0.52f : 0.26f) * MathF.Pow(0.65f, attempt);
                    if (ry < width * 1.5f) break;

                    if (Ellipse(knot, ry, ry * 2.4f, gap, samples, lines) is { } points)
                    {
                        rings.Add(new Polyline2(points, width, Closed: true));
                        break;
                    }
                }
            }
        }

        return rings;
    }

    /// <summary>
    /// A ring of the given size, or null when the grain has not left room for it anywhere along
    /// its length. Nothing is clamped into place: a ring either fits or is not drawn.
    /// </summary>
    private static List<Vector2>? Ellipse(
        Vector2 knot, float ry, float rx, float gap, float[] samples, List<float[]> lines)
    {
        const int steps = 28;
        var points = new List<Vector2>(steps);

        for (int i = 0; i < steps; i++)
        {
            float angle = i / (float)steps * MathF.Tau;
            float u = knot.X + rx * MathF.Cos(angle);
            float v = knot.Y + ry * MathF.Sin(angle);

            if (Clamped(u, v, gap, samples, lines) != v) return null;

            points.Add(new Vector2(u, v));
        }

        return points;
    }

    /// <summary>How far the grain has parted at a knot, measured on the lines either side of it.</summary>
    private static float RoomAt(Vector2 knot, float[] samples, List<float[]> lines)
    {
        int column = Column(knot.X, samples);
        float above = float.MaxValue, below = float.MaxValue;

        foreach (var line in lines)
        {
            float distance = line[column] - knot.Y;
            if (distance >= 0) above = Math.Min(above, distance);
            else below = Math.Min(below, -distance);
        }

        // A knot near the top or bottom edge has grain on only one side of it.
        if (above == float.MaxValue) above = below;
        if (below == float.MaxValue) below = above;

        return above == float.MaxValue ? 0 : Math.Min(above, below);
    }

    /// <summary>Holds a ring point inside the gap the grain has left for it.</summary>
    private static float Clamped(float u, float v, float gap, float[] samples, List<float[]> lines)
    {
        int column = Column(u, samples);
        float ceiling = float.MaxValue, floor = float.MinValue;

        foreach (var line in lines)
        {
            if (line[column] > v) ceiling = Math.Min(ceiling, line[column] - gap);
            else floor = Math.Max(floor, line[column] + gap);
        }

        return floor >= ceiling ? (floor + ceiling) * 0.5f : Math.Clamp(v, floor, ceiling);
    }

    private static int Column(float u, float[] samples)
    {
        int index = Array.BinarySearch(samples, u);
        if (index < 0) index = ~index;
        return Math.Clamp(index, 0, samples.Length - 1);
    }

    /// <summary>Repeatable 0..1, so the same face always grows the same knots.</summary>
    private static float Fraction(int knot, int axis)
    {
        uint hash = (uint)(knot * 374761393 ^ (axis + 1) * 668265263);
        hash ^= hash >> 15;
        hash *= 2246822519;
        hash ^= hash >> 13;

        return (hash % 1000) / 1000f;
    }
}
