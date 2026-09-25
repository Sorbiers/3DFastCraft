using System.Numerics;

namespace FastCraft3D.Geometry;

/// <summary>A picture as brightness from 0 (black) to 1 (white), the first row its top.</summary>
public sealed record Greyscale(int Width, int Height, float[] Samples)
{
    public float At(int x, int y) => Samples[y * Width + x];
}

/// <summary>What the picture is laid on.</summary>
public enum LithophaneShape
{
    /// <summary>A flat plate, standing on its bottom edge.</summary>
    Flat,

    /// <summary>Bent round part of a cylinder, the picture on the outside of the curve.</summary>
    Curved
}

/// <param name="Width">Across the picture itself, in millimetres; the height follows its proportions.</param>
/// <param name="MinThickness">Where the picture is white: thin enough to let the light through.</param>
/// <param name="MaxThickness">Where it is black: thick enough to stop it.</param>
/// <param name="Pitch">Millimetres between samples across and up - the detail the nozzle can actually lay down.</param>
/// <param name="Brightness">Added to every tone, from -1 to 1: the whole picture lighter or darker.</param>
/// <param name="Contrast">Spread about mid grey. One leaves it alone, over one pushes the ends apart.</param>
/// <param name="Gamma">Over one darkens the middle tones, under one lifts them.</param>
/// <param name="Negative">Light for dark, for a picture meant to be read the other way round.</param>
/// <param name="Angle">How far round the cylinder a curved plate goes, in degrees.</param>
/// <param name="Frame">A border at the full thickness round the picture, in millimetres; zero for none.</param>
/// <param name="LayerHeight">What it will be printed at, which is what decides how many greys survive.</param>
public sealed record LithophaneOptions
{
    public float Width { get; init; } = 100f;
    public float MinThickness { get; init; } = 0.8f;
    public float MaxThickness { get; init; } = 3.2f;
    public float Pitch { get; init; } = 0.3f;
    public float Brightness { get; init; }
    public float Contrast { get; init; } = 1f;
    public float Gamma { get; init; } = 1f;
    public bool Negative { get; init; }
    public LithophaneShape Shape { get; init; } = LithophaneShape.Flat;
    public float Angle { get; init; } = 120f;
    public float Frame { get; init; } = 3f;
    public float LayerHeight { get; init; } = 0.1f;

    /// <summary>The same settings with every number brought inside what can be built from it.</summary>
    public LithophaneOptions Sane()
    {
        float min = Math.Clamp(Finite(MinThickness, 0.8f), 0.2f, 10f);

        return this with
        {
            Width = Math.Clamp(Finite(Width, 100f), 5f, 1000f),
            MinThickness = min,

            // Always thicker than the thin end by something, or black and white come out the
            // same and the picture is a blank plate.
            MaxThickness = Math.Clamp(Finite(MaxThickness, 3.2f), min + 0.2f, 20f),
            Pitch = Math.Clamp(Finite(Pitch, 0.3f), 0.05f, 2f),
            Brightness = Math.Clamp(Finite(Brightness, 0f), -1f, 1f),
            Contrast = Math.Clamp(Finite(Contrast, 1f), 0f, 4f),
            Gamma = Math.Clamp(Finite(Gamma, 1f), 0.2f, 5f),
            Angle = Math.Clamp(Finite(Angle, 120f), 5f, 355f),
            Frame = Math.Clamp(Finite(Frame, 3f), 0f, 50f),
            LayerHeight = Math.Clamp(Finite(LayerHeight, 0.1f), 0.02f, 1f)
        };

        static float Finite(float value, float otherwise) => float.IsFinite(value) ? value : otherwise;
    }
}

/// <param name="Columns">Samples across the whole plate, frame and all.</param>
/// <param name="Rows">Samples up it.</param>
public readonly record struct LithophaneGrid(int Columns, int Rows, float Width, float Height, int Triangles);

/// <summary>
/// A lithophane: a picture carried as thickness in a thin translucent plate, invisible until it
/// is lit from behind. White goes thin and lets the light through, black goes thick and stops it.
///
/// Thickness does not follow brightness. Light dies away through plastic by something close to
/// Beer's law - each further millimetre takes the same fraction of what is left, not the same
/// amount - so a thickness set straight from the brightness comes out flat and washed, all of its
/// range spent in the first fraction of a millimetre. What is inverted here is the transmission:
/// the thickness that lets through as much light as the picture's own brightness asks for. That
/// one curve is most of the difference between a lithophane worth printing and a grey slab.
///
/// The plate is built as a grid of samples: the picture's face displaced by thickness, a flat
/// back, and walls joining the two round the edge. Every vertex is shared with its neighbours by
/// index, so it is closed by construction - no boolean is involved and there is nothing here for
/// one to tear.
///
/// It is built standing up, its bottom edge on the plate, because that is how it has to print.
/// Laid flat, thickness becomes layer count and every grey in the picture becomes a step.
/// </summary>
public static class Lithophane
{
    /// <summary>
    /// How fast light dies away through the plastic, per millimetre. About right for white PLA
    /// over the millimetre or three a lithophane is; it decides how much of the range the middle
    /// tones get, and is not worth asking anyone for - gamma is the control that means something.
    /// </summary>
    public const float Attenuation = 1.3f;

    /// <summary>
    /// The brightness the picture is read at: inverted if asked, then contrast about mid grey,
    /// brightness on top of that, and gamma last.
    ///
    /// Contrast before brightness, so turning one up does not quietly undo the other: spread
    /// about the middle first, then move the whole lot.
    /// </summary>
    public static float Adjusted(float brightness, LithophaneOptions options)
    {
        float b = Math.Clamp(brightness, 0f, 1f);
        if (options.Negative) b = 1f - b;

        b = Math.Clamp((b - 0.5f) * options.Contrast + 0.5f + options.Brightness, 0f, 1f);
        return MathF.Pow(b, options.Gamma);
    }

    /// <summary>
    /// How thick the plate has to be to pass as much light as this brightness asks for: the
    /// inverse of <see cref="Transmission"/>, and the reason the middle tones read at all.
    /// </summary>
    public static float Thickness(float brightness, LithophaneOptions options)
    {
        var o = options.Sane();
        float b = Adjusted(brightness, o);

        double clear = Math.Exp(-Attenuation * o.MinThickness);   // through the thin end
        double dark = Math.Exp(-Attenuation * o.MaxThickness);    // through the thick end
        double through = dark + b * (clear - dark);

        return Math.Clamp((float)(-Math.Log(through) / Attenuation), o.MinThickness, o.MaxThickness);
    }

    /// <summary>What a thickness lets through, from 0 at the thick end to 1 at the thin end.</summary>
    public static float Transmission(float thickness, LithophaneOptions options)
    {
        var o = options.Sane();
        double clear = Math.Exp(-Attenuation * o.MinThickness);
        double dark = Math.Exp(-Attenuation * o.MaxThickness);
        double through = Math.Exp(-Attenuation * Math.Clamp(thickness, o.MinThickness, o.MaxThickness));

        return (float)Math.Clamp((through - dark) / (clear - dark), 0.0, 1.0);
    }

    /// <summary>
    /// How many greys the print can actually tell apart: the thickness range in layers. A picture
    /// asked for more than this loses the difference, which is worth knowing before it is printed
    /// rather than after.
    /// </summary>
    public static int GreyLevels(LithophaneOptions options)
    {
        var o = options.Sane();
        return Math.Max(2, (int)MathF.Floor((o.MaxThickness - o.MinThickness) / o.LayerHeight) + 1);
    }

    /// <summary>The samples a picture of these proportions comes to, and what it costs in triangles.</summary>
    public static LithophaneGrid Grid(int pictureWidth, int pictureHeight, LithophaneOptions options)
    {
        var o = options.Sane();

        float across = o.Width;
        float up = across * Math.Max(pictureHeight, 1) / Math.Max(pictureWidth, 1);

        int columns = Math.Max(2, (int)MathF.Round(across / o.Pitch) + 1);
        int rows = Math.Max(2, (int)MathF.Round(up / o.Pitch) + 1);
        int border = (int)MathF.Round(o.Frame / o.Pitch);

        int w = columns + 2 * border, h = rows + 2 * border;
        int triangles = 4 * (w - 1) * (h - 1) + 4 * ((w - 1) + (h - 1));

        return new LithophaneGrid(w, h, (w - 1) * o.Pitch, (h - 1) * o.Pitch, triangles);
    }

    /// <summary>
    /// The thickness at every sample of the plate, row 0 its bottom edge. The frame, where there
    /// is one, is the full thickness: a border that stops the light is what gives the picture an
    /// edge to end on and the plate something to hold it straight.
    /// </summary>
    public static float[] Thicknesses(Greyscale picture, LithophaneOptions options)
    {
        var o = options.Sane();
        var grid = Grid(picture.Width, picture.Height, o);
        int border = (int)MathF.Round(o.Frame / o.Pitch);
        int columns = grid.Columns - 2 * border, rows = grid.Rows - 2 * border;

        var thickness = new float[grid.Columns * grid.Rows];

        for (int j = 0; j < grid.Rows; j++)
            for (int i = 0; i < grid.Columns; i++)
            {
                int x = i - border, y = j - border;
                bool inside = x >= 0 && x < columns && y >= 0 && y < rows;

                thickness[j * grid.Columns + i] = inside
                    ? Thickness(Sample(picture, (float)x / (columns - 1), (float)y / (rows - 1)), o)
                    : o.MaxThickness;
            }

        return thickness;
    }

    /// <summary>
    /// What it will look like lit, as brightness from 0 to 1, at whatever size is asked for.
    ///
    /// Worked out from the thickness the printer will actually lay down - stepped to whole layers
    /// - rather than from the thickness asked for, so the banding a picture is about to lose its
    /// detail to shows up here rather than on the bed.
    /// </summary>
    public static float[] Backlit(Greyscale picture, LithophaneOptions options, int width, int height)
    {
        var o = options.Sane();
        var lit = new float[Math.Max(1, width) * Math.Max(1, height)];

        for (int j = 0; j < height; j++)
            for (int i = 0; i < width; i++)
            {
                float u = width > 1 ? (float)i / (width - 1) : 0f;
                float v = height > 1 ? 1f - (float)j / (height - 1) : 0f;

                float asked = Thickness(Sample(picture, u, v), o);
                float printed = o.MinThickness + MathF.Round((asked - o.MinThickness) / o.LayerHeight) * o.LayerHeight;
                lit[j * width + i] = Transmission(printed, o);
            }

        return lit;
    }

    /// <summary>
    /// The plate itself, standing on the plate with its bottom edge at z = 0 and the picture
    /// facing +Y. Closed by construction: the face, the back and the four walls all share the
    /// same edge vertices by index.
    /// </summary>
    public static Mesh Build(Greyscale picture, LithophaneOptions options)
    {
        var o = options.Sane();
        var grid = Grid(picture.Width, picture.Height, o);
        var thickness = Thicknesses(picture, o);

        int w = grid.Columns, h = grid.Rows;
        bool curved = o.Shape == LithophaneShape.Curved;

        // A curve of this width has to sit on a cylinder this far out for its arc to come to the
        // width asked for; flat, the radius is nothing and the sample's own thickness is its Y.
        float sweep = o.Angle * MathF.PI / 180f;
        float inner = curved ? grid.Width / sweep : 0f;

        var positions = new List<Vector3>(2 * w * h);
        var indices = new List<int>(3 * grid.Triangles);

        // The face first, then the back, so the two are a fixed distance apart in the list.
        for (int pass = 0; pass < 2; pass++)
            for (int j = 0; j < h; j++)
                for (int i = 0; i < w; i++)
                {
                    float out_ = pass == 0 ? inner + thickness[j * w + i] : inner;
                    float u = (float)i / (w - 1);
                    float z = (float)j / (h - 1) * grid.Height;

                    positions.Add(curved
                        ? new Vector3(out_ * MathF.Sin((u - 0.5f) * sweep), out_ * MathF.Cos((u - 0.5f) * sweep) - inner, z)
                        : new Vector3((u - 0.5f) * grid.Width, out_, z));
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
                // The face wound the other way round from the back, so the two look outwards.
                Quad(Face(i, j), Face(i, j + 1), Face(i + 1, j + 1), Face(i + 1, j));
                Quad(Back(i, j), Back(i + 1, j), Back(i + 1, j + 1), Back(i, j + 1));
            }

        for (int i = 0; i + 1 < w; i++)
        {
            Quad(Face(i, 0), Face(i + 1, 0), Back(i + 1, 0), Back(i, 0));
            Quad(Face(i + 1, h - 1), Face(i, h - 1), Back(i, h - 1), Back(i + 1, h - 1));
        }

        for (int j = 0; j + 1 < h; j++)
        {
            Quad(Face(0, j + 1), Face(0, j), Back(0, j), Back(0, j + 1));
            Quad(Face(w - 1, j), Face(w - 1, j + 1), Back(w - 1, j + 1), Back(w - 1, j));
        }

        return new Mesh(positions, indices);
    }

    /// <summary>The picture read between its samples, with u across and v up from its bottom edge.</summary>
    private static float Sample(Greyscale picture, float u, float v)
    {
        float x = Math.Clamp(u, 0f, 1f) * (picture.Width - 1);
        float y = (1f - Math.Clamp(v, 0f, 1f)) * (picture.Height - 1);

        int x0 = Math.Clamp((int)x, 0, picture.Width - 1), x1 = Math.Min(x0 + 1, picture.Width - 1);
        int y0 = Math.Clamp((int)y, 0, picture.Height - 1), y1 = Math.Min(y0 + 1, picture.Height - 1);
        float fx = x - x0, fy = y - y0;

        float top = picture.At(x0, y0) + (picture.At(x1, y0) - picture.At(x0, y0)) * fx;
        float bottom = picture.At(x0, y1) + (picture.At(x1, y1) - picture.At(x0, y1)) * fx;
        return top + (bottom - top) * fy;
    }
}
