using System.Numerics;

namespace FastCraft3D.Geometry.Engraving;

/// <summary>
/// How far an object's own surface is from an upright axis, in every direction and at every
/// height - so lettering wrapped round it can follow the object rather than a circle.
///
/// Wrapping used a circle through the point clicked, centred on the middle of the object. That is
/// right for a round barrel and wrong for anything else: on a barrel stretched to an oval the real
/// surface bulged past the circle at one end of a heart and fell inside it at the other, so the
/// cutter's top grazed the surface at one end and its floor at the other - the shallow crossings
/// the boolean tears on. It came and went with where the click landed, since that decided how far
/// the circle was out. More clearance did not help, because the floor was the half at fault.
///
/// Sampled once, as sections up the height and directions round each one, and read by
/// interpolation. The outermost surface in each direction is kept, so a cup's inside wall does not
/// pull lettering in.
/// </summary>
public sealed class SurfaceProfile
{
    /// <summary>A quarter of a degree - a tenth of a millimetre apart on a 25 mm barrel.</summary>
    public const int Directions = 1440;

    private const float SectionStepMm = 0.25f;
    private const int MaximumSections = 480;
    private static readonly float Spacing = MathF.Tau / Directions;

    private readonly float[] reach;
    private readonly int sections;
    private readonly float bottom;
    private readonly float step;

    private SurfaceProfile(float[] reach, int sections, float bottom, float step)
    {
        this.reach = reach;
        this.sections = sections;
        this.bottom = bottom;
        this.step = step;
    }

    /// <param name="world">The object, in world space.</param>
    /// <param name="axis">Where the upright axis crosses the plate, in world X and Y.</param>
    /// <returns>Null for something with no height to wrap round.</returns>
    public static SurfaceProfile? Build(Mesh world, Vector2 axis)
    {
        int triangles = world.Indices.Count / 3;
        if (triangles == 0) return null;

        var bounds = world.ComputeBounds();
        float height = bounds.Max.Z - bounds.Min.Z;
        if (height <= 1e-4f) return null;

        int sections = Math.Clamp((int)MathF.Ceiling(height / SectionStepMm), 1, MaximumSections);
        float step = height / sections;

        var reach = new float[sections * Directions];
        Array.Fill(reach, float.NaN);

        var low = new float[triangles];
        var high = new float[triangles];
        for (int t = 0; t < triangles; t++)
        {
            float a = world.Positions[world.Indices[3 * t]].Z;
            float b = world.Positions[world.Indices[3 * t + 1]].Z;
            float c = world.Positions[world.Indices[3 * t + 2]].Z;
            low[t] = MathF.Min(a, MathF.Min(b, c));
            high[t] = MathF.Max(a, MathF.Max(b, c));
        }

        // Swept upwards, so each section looks only at the triangles that reach it rather than at
        // the whole mesh every time.
        var order = Enumerable.Range(0, triangles).OrderBy(t => low[t]).ToArray();
        var active = new List<int>();
        int next = 0;
        Span<Vector2> crossing = stackalloc Vector2[3];

        for (int s = 0; s < sections; s++)
        {
            // Halfway up each band, so no section lies exactly on a flat top or a row of corners.
            float z = bounds.Min.Z + (s + 0.5f) * step;

            while (next < triangles && low[order[next]] <= z) active.Add(order[next++]);
            active.RemoveAll(t => high[t] < z);

            foreach (int t in active)
            {
                if (low[t] >= z || high[t] <= z) continue;

                int found = 0;
                for (int e = 0; e < 3; e++)
                {
                    var p = world.Positions[world.Indices[3 * t + e]];
                    var q = world.Positions[world.Indices[3 * t + (e + 1) % 3]];
                    if (p.Z >= z == q.Z >= z) continue;

                    float f = (z - p.Z) / (q.Z - p.Z);
                    crossing[found++] = new Vector2(p.X + f * (q.X - p.X), p.Y + f * (q.Y - p.Y)) - axis;
                    if (found == 2) break;
                }

                if (found == 2) Mark(reach, s, crossing[0], crossing[1]);
            }
        }

        // Smoothed round each section before it is used. Followed exactly, the profile carried the
        // flats of the barrel into the cutter: its walls kinked at every corner of the object, right
        // where the object's own corner edges ran, and every heart tried tore - worse than the
        // circle. Blurred over about a flat's width it is smooth, as the circle was, and off the real
        // surface by no more than a flat's sag, which the cutter's clearance already covers.
        var smoothed = new float[Directions];
        for (int s = 0; s < sections; s++)
        {
            var row = reach.AsSpan(s * Directions, Directions);
            for (int pass = 0; pass < 2; pass++)
            {
                Blur(row, smoothed, BlurHalfWidth);
                smoothed.CopyTo(row);
            }
        }

        return new SurfaceProfile(reach, sections, bounds.Min.Z, step);
    }

    /// <summary>
    /// Six degrees either side, twice over: wider than a flat of a 32-sided barrel, which is where
    /// the default cylinder sits, and narrow enough to keep the bulge of an oval.
    /// </summary>
    private const int BlurHalfWidth = 24;

    /// <summary>A running average round the circle that skips directions nothing was seen in.</summary>
    private static void Blur(ReadOnlySpan<float> row, Span<float> into, int half)
    {
        for (int j = 0; j < Directions; j++)
        {
            if (float.IsNaN(row[j]))
            {
                into[j] = float.NaN;
                continue;
            }

            float sum = 0;
            int count = 0;
            for (int k = -half; k <= half; k++)
            {
                float value = row[((j + k) % Directions + Directions) % Directions];
                if (float.IsNaN(value)) continue;

                sum += value;
                count++;
            }

            into[j] = sum / count;
        }
    }

    /// <summary>Records one piece of a section against every direction that looks through it.</summary>
    private static void Mark(float[] reach, int section, Vector2 p, Vector2 q)
    {
        float from = MathF.Atan2(p.Y, p.X);
        float span = MathF.Atan2(q.Y, q.X) - from;
        if (span > MathF.PI) span -= MathF.Tau;
        else if (span < -MathF.PI) span += MathF.Tau;

        // A piece the axis itself lies on has no one direction to be seen from.
        if (MathF.Abs(span) > MathF.PI - 1e-3f) return;

        int first = (int)MathF.Ceiling(MathF.Min(from, from + span) / Spacing);
        int last = (int)MathF.Floor(MathF.Max(from, from + span) / Spacing);
        var along = q - p;

        for (int j = first; j <= last; j++)
        {
            float angle = j * Spacing;
            var look = new Vector2(MathF.Cos(angle), MathF.Sin(angle));

            float facing = Cross(look, along);
            if (MathF.Abs(facing) < 1e-9f) continue;

            float distance = Cross(p, along) / facing;
            if (distance <= 0) continue;

            int index = section * Directions + (j % Directions + Directions) % Directions;
            if (float.IsNaN(reach[index]) || distance > reach[index]) reach[index] = distance;
        }
    }

    private static float Cross(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;

    /// <summary>
    /// How far out the surface is in that direction at that height, or null where there is none -
    /// above or below the object, or through a gap in it.
    /// </summary>
    public float? RadiusAt(float angle, float z)
    {
        float s = Math.Clamp((z - bottom) / step - 0.5f, 0f, sections - 1);
        int s0 = (int)s;
        int s1 = Math.Min(s0 + 1, sections - 1);
        float up = s - s0;

        float d = angle / Spacing;
        float floor = MathF.Floor(d);
        float round = d - floor;
        int d0 = ((int)(floor % Directions) + Directions) % Directions;
        int d1 = (d0 + 1) % Directions;

        float sum = 0, weight = 0;
        Take(s0, d0, (1 - up) * (1 - round));
        Take(s0, d1, (1 - up) * round);
        Take(s1, d0, up * (1 - round));
        Take(s1, d1, up * round);

        return weight > 1e-6f ? sum / weight : null;

        void Take(int section, int direction, float share)
        {
            float value = reach[section * Directions + direction];
            if (float.IsNaN(value) || share <= 0) return;

            sum += value * share;
            weight += share;
        }
    }
}
