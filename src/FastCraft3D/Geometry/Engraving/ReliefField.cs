using System.Numerics;

namespace FastCraft3D.Geometry.Engraving;

/// <summary>
/// A texture with a shape to it, rather than one cut from flat outlines.
///
/// Everything in <see cref="SurfaceTexture"/> comes out at one height: a pad is proud or it is
/// sunk, and the bevel slopes its walls, never its face. That is the whole of what a lap, a slope
/// or a grain is, so siding, roof tiles and sawn boards cannot be said in outlines at all.
///
/// Said as a height over the face they are all one line of arithmetic. What this asks of a profile
/// is that height, and where the profile changes direction - because the samples are put on those
/// lines rather than on a grid. A course of siding is a straight ramp, so it needs two rows and no
/// more however tall the wall is; a grain needs a sample every quarter millimetre because that is
/// how fine it is. Laid on a blind grid instead, siding cost as many triangles as grain did and
/// came out with its laps in steps.
/// </summary>
public interface IRelief
{
    /// <summary>Where the profile changes across the face, the two edges included, in millimetres.</summary>
    float[] Across(float acrossMm);

    /// <summary>The same up the face.</summary>
    float[] Up(float upMm);

    /// <summary>How far the surface stands proud at this point. Never negative.</summary>
    float Height(Vector2 at);
}

/// <summary>
/// Builds the solid a profile describes: a sheet at its own height over the face, a flat sheet
/// sunk into it, and walls joining the two round the edge.
///
/// Closed by construction, the way <see cref="Lithophane"/> closes its plate - the face, the back
/// and the four walls all share the same edge vertices by index, so there is nothing to weld and
/// nothing to check. It is laid on an <see cref="IPlacementSurface"/> rather than on a plane, so a
/// roof and a round tower cost the same.
/// </summary>
public static class ReliefField
{
    /// <summary>Past this the profile is finer than the face is worth and the boolean crawls.</summary>
    public const int MostSamples = 1_200;

    /// <summary>
    /// How far the back sheet sits inside the object. Enough that the two solids genuinely overlap
    /// rather than meeting face to face, which is the one thing the boolean cannot be asked to do.
    /// </summary>
    public const float SinkMm = 0.3f;

    /// <summary>Past this the result is more triangles than the rest of the model put together.</summary>
    public const long HeavyTriangles = 400_000;

    /// <summary>
    /// What a profile comes to over this face: the samples, the triangles, and whether it can be
    /// built at all.
    ///
    /// <paramref name="Refusal"/> is the part that matters. The builder used to hand back an empty
    /// mesh when a profile asked for more samples than it would give, and the panel went on quoting
    /// a triangle count for a thing that was never going to appear - which from the outside is a
    /// tool that does nothing and says nothing.
    /// </summary>
    public readonly record struct ReliefCost(int Across, int Up, long Triangles, string? Refusal)
    {
        public bool CanBuild => Refusal is null;

        /// <summary>Whether it will build, but at a price worth mentioning first.</summary>
        public bool IsHeavy => CanBuild && Triangles > HeavyTriangles;
    }

    /// <summary>The samples a profile comes to over this face, and what it costs in triangles.</summary>
    public static ReliefCost Cost(IRelief relief, float acrossMm, float upMm)
    {
        var us = Trimmed(relief.Across(acrossMm), acrossMm);
        var vs = Trimmed(relief.Up(upMm), upMm);

        long quads = (long)(us.Length - 1) * (vs.Length - 1);
        long triangles = 2 * (2 * quads + 2 * (us.Length - 1) + 2 * (vs.Length - 1));

        string? refusal =
            us.Length < 2 || vs.Length < 2
                ? "The face is smaller than one course of this - try a finer pitch."
            : us.Length > MostSamples || vs.Length > MostSamples
                ? $"This is too fine for a face of that size: it wants "
                  + $"{Math.Max(us.Length, vs.Length):N0} sample lines where {MostSamples:N0} is "
                  + "the most it will take. A coarser pitch, or a smaller face."
                : null;

        return new ReliefCost(us.Length, vs.Length, triangles, refusal);
    }

    /// <summary>
    /// The solid, ready to be unioned onto the object. Empty when the profile asks for more samples
    /// than the field is worth.
    /// </summary>
    public static Mesh Build(IPlacementSurface surface, IRelief relief, float acrossMm, float upMm)
    {
        var us = Trimmed(relief.Across(acrossMm), acrossMm);
        var vs = Trimmed(relief.Up(upMm), upMm);

        // Refused rather than quietly empty. The caller asks Cost first and shows the reason; this
        // is only the backstop for anything that did not.
        if (us.Length < 2 || vs.Length < 2) return new Mesh();
        if (us.Length > MostSamples || vs.Length > MostSamples) return new Mesh();

        int w = us.Length, h = vs.Length;
        var positions = new List<Vector3>(2 * w * h);
        var indices = new List<int>();

        // The face first and the back second, so the two are a fixed distance apart in the list and
        // an index into one is an index into the other.
        for (int pass = 0; pass < 2; pass++)
            for (int j = 0; j < h; j++)
                for (int i = 0; i < w; i++)
                {
                    var at = new Vector2(us[i], vs[j]);
                    float out_ = pass == 0 ? MathF.Max(relief.Height(at), 0f) : -SinkMm;

                    positions.Add(surface.At(at, out_));
                }

        int Face(int i, int j) => j * w + i;
        int Back(int i, int j) => w * h + j * w + i;

        void Triangle(int a, int b, int c)
        {
            indices.Add(a);
            indices.Add(b);
            indices.Add(c);
        }

        void Quad(int a, int b, int c, int d)
        {
            Triangle(a, b, c);
            Triangle(a, c, d);
        }

        for (int j = 0; j + 1 < h; j++)
            for (int i = 0; i + 1 < w; i++)
            {
                // The back wound the other way round from the face, so the two look outwards.
                Quad(Face(i, j), Face(i + 1, j), Face(i + 1, j + 1), Face(i, j + 1));
                Quad(Back(i, j), Back(i, j + 1), Back(i + 1, j + 1), Back(i + 1, j));
            }

        for (int i = 0; i + 1 < w; i++)
        {
            Quad(Face(i, 0), Back(i, 0), Back(i + 1, 0), Face(i + 1, 0));
            Quad(Face(i, h - 1), Face(i + 1, h - 1), Back(i + 1, h - 1), Back(i, h - 1));
        }

        for (int j = 0; j + 1 < h; j++)
        {
            Quad(Face(0, j), Face(0, j + 1), Back(0, j + 1), Back(0, j));
            Quad(Face(w - 1, j), Back(w - 1, j), Back(w - 1, j + 1), Face(w - 1, j + 1));
        }

        return new Mesh(positions, indices);
    }

    /// <summary>
    /// The profile's own sample lines, sorted, inside the face, and with the two edges on the ends.
    ///
    /// Samples a hair apart are what a sharp step is made of - two rows at the same height with
    /// different profiles either side - so they are kept, and only exact repeats are dropped.
    /// </summary>
    private static float[] Trimmed(float[] wanted, float extentMm)
    {
        float half = extentMm / 2f;
        var kept = new List<float> { -half };

        foreach (float at in wanted.OrderBy(v => v))
        {
            if (at <= -half + 1e-5f || at >= half - 1e-5f) continue;
            if (at - kept[^1] < 1e-5f) continue;

            kept.Add(at);
        }

        if (half - kept[^1] < 1e-5f) kept[^1] = half;
        else kept.Add(half);

        return [.. kept];
    }

    /// <summary>
    /// Sample lines every <paramref name="stepMm"/> across an extent, for a profile with no lines
    /// of its own - a grain, which changes everywhere and nowhere in particular.
    /// </summary>
    public static float[] Evenly(float extentMm, float stepMm)
    {
        int count = Math.Max(1, (int)MathF.Ceiling(extentMm / MathF.Max(stepMm, 1e-3f)));
        var at = new float[count + 1];

        for (int i = 0; i <= count; i++) at[i] = -extentMm / 2f + extentMm * i / count;

        return at;
    }

    /// <summary>
    /// Value noise, in one pass, deterministic and with no state.
    ///
    /// Grain wants something that looks unplanned and comes out the same every time the preview is
    /// drawn; a random number generator gives the first and not the second, and the preview crawled
    /// with a different grain every keystroke.
    /// </summary>
    public static float Noise(float x, float y)
    {
        int ix = (int)MathF.Floor(x), iy = (int)MathF.Floor(y);
        float fx = x - ix, fy = y - iy;

        // Smoothstep, so the value has no corners where one cell meets the next.
        float sx = fx * fx * (3f - 2f * fx);
        float sy = fy * fy * (3f - 2f * fy);

        float a = At(ix, iy), b = At(ix + 1, iy), c = At(ix, iy + 1), d = At(ix + 1, iy + 1);

        return (a + (b - a) * sx) + ((c + (d - c) * sx) - (a + (b - a) * sx)) * sy;

        static float At(int x, int y)
        {
            uint n = (uint)(x * 374761393 + y * 668265263);
            n = (n ^ (n >> 13)) * 1274126177u;
            return ((n ^ (n >> 16)) & 0xFFFF) / 65535f;
        }
    }
}
