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

    /// <summary>Flat panels round a lamp - three to twelve sides - a picture on each, on a base with a hole for the light.</summary>
    Lamp,

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

    /// <summary>How many panels round a lamp.</summary>
    public int LampSides { get; init; } = 4;

    /// <summary>A lamp stands on a hollow base with a hole in its top for a bulb socket, rather than on a frame round a hole.</summary>
    public bool Socket { get; init; }

    /// <summary>Across the socket's hole: 40 mm takes an E26 or E27 socket held by its shade ring, 10.5 mm one on a threaded nipple.</summary>
    public float SocketHole { get; init; } = 40f;

    /// <summary>How tall the hollow base under a socket is - room below the floor for the socket's body and the cable.</summary>
    public float SocketBase { get; init; } = 45f;

    /// <summary>The same settings with every number brought inside what can be built from it.</summary>
    public LithophaneOptions Sane()
    {
        float min = Math.Clamp(Finite(MinThickness, 0.8f), 0.2f, 10f);

        return this with
        {
            LampSides = Math.Clamp(LampSides, 3, 12),
            SocketHole = Math.Clamp(Finite(SocketHole, 40f), 5f, 120f),
            SocketBase = Math.Clamp(Finite(SocketBase, 45f), 5f, 200f),
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

    /// <summary>
    /// A lamp: flat panels round a regular polygon, a picture on each, joined at the corners by
    /// posts, with a rim round the top and a base under it. Pictures fewer than the sides go round
    /// again; each is stretched to the first one's proportions so every panel is the same size.
    ///
    /// Each panel's flat back is the outside and its relief the inside, as a lithophane lamp is
    /// made. The panels' inner corners meet, so no picture runs into the next one's; the wedge
    /// left outside at each corner is the post.
    ///
    /// The base is a frame round a hole for a light standing under it, or - for a bulb - a hollow
    /// pedestal with a floor on top holding the socket and a notch in its foot for the cable.
    /// </summary>
    public static Mesh BuildLamp(IReadOnlyList<Greyscale> pictures, LithophaneOptions options)
    {
        var o = options.Sane() with { Shape = LithophaneShape.Flat };
        int n = o.LampSides;
        var first = pictures[0];

        var panels = Enumerable.Range(0, n).Select(k =>
        {
            var picture = pictures[k % pictures.Count];
            return Build(picture.Width == first.Width && picture.Height == first.Height ? picture : Stretched(picture, first.Width, first.Height), o);
        }).ToList();

        var size = panels[0].ComputeBounds();
        float width = size.Size.X, height = size.Size.Z, thick = o.MaxThickness;
        float half = MathF.PI / n;
        // To the outside of a panel. A little past where the panels' inner corners would just
        // meet, so they stand apart and the post alone joins them. Touching along a line, two
        // panels came out of the union as a seam shared by four faces; overlapping, a panel's
        // edge lay along the next one's relief as often as not.
        float apothem = width / (2f * MathF.Tan(half)) + thick + 0.3f;
        float inside = apothem - thick;
        const float floor = 2f, rim = 2f, over = 0.5f, wall = 3f;
        float lift = o.Socket ? o.SocketBase : 0f;
        // Every piece overlaps the next by a real amount, never flush: a panel's top lying exactly
        // on the rim's underside left the union with edges shared by four faces.
        float bottom = lift + floor, sunk = bottom - 0.3f, top = sunk + height;

        Vector2 Out(int k) => new(MathF.Cos(-MathF.PI / 2f + k * 2f * half), MathF.Sin(-MathF.PI / 2f + k * 2f * half));
        Vector2 Along(int k) { var r = Out(k); return new(-r.Y, r.X); }

        var pieces = new List<Mesh>();
        for (int k = 0; k < n; k++)
        {
            var turn = Matrix4x4.CreateRotationZ(k * 2f * half);
            var at = Vector3.Transform(new Vector3(0, -apothem, 0), turn);
            pieces.Add(MeshTransform.Transformed(panels[k], turn * Matrix4x4.CreateTranslation(at.X, at.Y, sunk)));

            // The post between this panel and the next: from each panel's inner corner, a little
            // along it so the two overlap, out past the outer face to the polygon's corner. None
            // of its corners lies in a side of the rim or the base: where one did - the inner edge
            // in the side of the rim's opening, the outer face in the rim's own - the union came
            // out with edges shared by four faces.
            int j = (k + 1) % n;
            float end = width / 2f - 0.7f, deep = inside - 0.8f, out_ = apothem + 0.3f;
            var corner = new[]
            {
                Out(k) * deep + Along(k) * end,
                Out(k) * out_ + Along(k) * end,
                Corner(k, out_),
                Out(j) * out_ - Along(j) * end,
                Out(j) * deep - Along(j) * end
            };
            pieces.Add(Prism(corner, bottom - 0.6f, top + 0.3f));
        }

        // The rim round the top, open to the lamp.
        pieces.Add(Hollowed(Prism(Polygon(apothem + over), top - 0.5f, top + rim), Prism(Polygon(inside - over), top - 1f, top + rim + 1f)));

        if (!o.Socket)
        {
            // A frame round a round hole, for a light standing under it.
            pieces.Add(Hollowed(Prism(Polygon(apothem + over), 0, floor), Round(inside - over - 3f, -1f, floor + 1f)));
        }
        else
        {
            // A floor with the socket's hole, on a hollow pedestal with a notch in its foot for the cable.
            float hole = MathF.Min(o.SocketHole / 2f, inside - over - 2f);
            pieces.Add(Hollowed(Prism(Polygon(apothem + over), lift - 0.01f, bottom), Round(hole, lift - 1f, bottom + 1f)));

            var pedestal = Hollowed(Prism(Polygon(apothem + over), 0, lift), Prism(Polygon(apothem + over - wall), -1f, lift - 0.02f));
            float notchTop = MathF.Min(12f, lift * 0.6f);
            var notch = MeshTransform.Transformed(Primitives.Box(10f, 4f * wall, notchTop + 3f), Matrix4x4.CreateTranslation(0, -(apothem + over), (notchTop - 3f) / 2f));
            pieces.Add(Hollowed(pedestal, notch));
        }

        return Csg.ManifoldCsg.UnionAll(pieces) ?? Mesh.Combine(pieces);

        Vector2 Corner(int k, float at)
        {
            var c = (Out(k) + Out((k + 1) % n)) / 2f;
            return Vector2.Normalize(c) * (at / MathF.Cos(half));
        }

        // The outline round the lamp, at a distance from its middle to each side's middle.
        List<Vector2> Polygon(float at) => Enumerable.Range(0, n).Select(k => Corner(k, at)).ToList();

        static Mesh Round(float radius, float z0, float z1) =>
            MeshTransform.Transformed(Primitives.Prism(radius, z1 - z0, 64), Matrix4x4.CreateTranslation(0, 0, (z0 + z1) / 2f));

        static Mesh Hollowed(Mesh solid, Mesh cut) => Csg.ManifoldCsg.Subtract(solid, cut) ?? solid;
    }

    /// <summary>A convex outline, anticlockwise, stood up from one height to another.</summary>
    private static Mesh Prism(IReadOnlyList<Vector2> loop, float z0, float z1)
    {
        var mesh = new Mesh();
        int count = loop.Count;
        Vector3 Low(int i) => new(loop[i].X, loop[i].Y, z0);
        Vector3 High(int i) => new(loop[i].X, loop[i].Y, z1);

        for (int i = 1; i + 1 < count; i++)
        {
            mesh.AddTriangle(High(0), High(i), High(i + 1));
            mesh.AddTriangle(Low(0), Low(i + 1), Low(i));
        }

        for (int i = 0; i < count; i++)
        {
            int j = (i + 1) % count;
            mesh.AddTriangle(Low(i), Low(j), High(j));
            mesh.AddTriangle(Low(i), High(j), High(i));
        }

        return mesh.Welded();
    }

    /// <summary>A picture sampled to another size, so four pictures make four panels the same size.</summary>
    private static Greyscale Stretched(Greyscale picture, int width, int height)
    {
        var samples = new float[width * height];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                samples[y * width + x] = Sample(picture, x / (float)Math.Max(1, width - 1), 1f - y / (float)Math.Max(1, height - 1));
        return new Greyscale(width, height, samples);
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
