using System.Numerics;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FastCraft3D.Geometry;
using FastCraft3D.Geometry.Engraving;

namespace FastCraft3D.View;

/// <summary>
/// Two pictures of a texture for the Texture and Cladding panels, of the same square of face: the
/// pattern flat, as it would be seen from straight above with the pieces dark and the joints light,
/// and the relief, shaded as if lit from the upper left, the way a relief is drawn.
///
/// Both, for every texture, because each says what the other cannot. The flat one shows the layout
/// - which pieces, how big, where the joints run - and the shaded one shows the depth. The textures
/// made of outlines used to have only the first and the shaped ones only the second, which made the
/// panel look like two different tools.
///
/// The shaped textures are a height, read off the profile pixel by pixel; the laid ones - tiles,
/// siding - are built as the slabs they are and their heights read off the slabs; the outlined ones
/// are filled in and softened a pixel or two, which is what a bevel would be. The flat picture of a
/// shaped texture is the same heights seen from above: dark where there is material to speak of.
/// </summary>
public static class TexturePreview
{
    /// <summary>The same size for every texture, so the panel does not grow and shrink with the choice.</summary>
    private const int SizePx = 180;

    private static readonly byte[] Ink = [0x2C, 0x30, 0x37];
    private static readonly byte[] Paper = [0xF4, 0xF5, 0xF7];

    /// <summary>
    /// The two pictures, the same size. Of the same patch of face, but for log walls: the second is
    /// a corner of the wall, the one thing a view of the face cannot show.
    /// </summary>
    public sealed record Pictures(BitmapSource Flat, BitmapSource Relief, string ReliefCaption = "Relief");

    /// <summary>The pictures, or null for what has no pattern to show; <paramref name="sizePx"/> a side, for a use larger than the panel.</summary>
    public static Pictures? Render(TextureOptions options, float depthMm, int sizePx = SizePx)
    {
        var o = options.Sane();
        if (!o.IsOn) return null;

        float side = Side(o);
        float depth = MathF.Max(depthMm, 0.1f);
        int n = Math.Clamp(sizePx, 32, 2048);

        bool logs = o.Kind == TextureKind.Logs;
        bool outlined = !o.IsProfiled && !logs;
        var height = logs ? Rounds(o, depth, side, n)
            : outlined ? Pads(o, side, n, depth)
            : o.IsLaid ? Laid(o, depth, side, side, n, n)
            : Shaped(o, depth, side, side, n, n);
        if (height is null) return null;

        // Where there is material: the footprint of a pad, or of what stands high enough to be seen.
        float level = outlined ? depth / 2f : height.Max() * 0.3f;
        var on = height.Select(z => z > level).ToArray();

        // A log wall is built round its corners, with each log's end standing out past the next
        // wall's: a corner is what shows that, and it is shown in place of the shaded face.
        if (logs && Corner(o, depth, n) is { } corner)
            return new Pictures(Flat(on, n), corner, "Corner");

        var relief = outlined ? Soften(height, n) : height;
        return new Pictures(Flat(on, n), Shade(relief, n, n, side / n, depth));
    }

    /// <summary>
    /// How much of the face the pictures show: enough pieces to read the pattern, and the same
    /// square for flat and shaped alike.
    /// </summary>
    private static float Side(TextureOptions o) => o.Kind switch
    {
        TextureKind.Grain => o.PitchMm * 16f,
        TextureKind.Bark => o.PitchMm * 6f,
        TextureKind.Siding or TextureKind.Logs => o.PitchMm * 5f,
        TextureKind.Planks => o.PitchMm * 3f,
        TextureKind.Rubble or TextureKind.CoursedStone => o.PitchMm * 5f,
        TextureKind.Brick or TextureKind.RoofTiles or TextureKind.Tiles => o.PitchMm * 4f,
        _ => o.PitchMm * 8f
    };

    /// <summary>
    /// Round logs laid one on another along the wall, as <see cref="LogWalls"/> lays them: a circle
    /// of the course less the gap, its middle <paramref name="proudMm"/> short of its own radius
    /// from the wall, so what shows is the cap that stands out of it.
    /// </summary>
    private static float[] Rounds(TextureOptions o, float proudMm, float side, int n)
    {
        float course = MathF.Max(o.PitchMm, 0.5f);
        float gap = Math.Clamp(o.LineMm, 0.05f, course * 0.4f);
        float radius = (course - gap) / 2f;
        float axis = Math.Clamp(proudMm, 0.1f, radius) - radius;

        var height = new float[n * n];
        for (int y = 0; y < n; y++)
        {
            // Up the face, from the middle of a log.
            float up = side / 2f - (y + 0.5f) * side / n;
            float across = up - MathF.Round(up / course) * course;
            float z = MathF.Max(axis + MathF.Sqrt(MathF.Max(radius * radius - across * across, 0f)), 0f);

            for (int x = 0; x < n; x++) height[y * n + x] = z;
        }

        return height;
    }

    /// <summary>
    /// A corner of log wall as <see cref="LogWalls"/> lays it: a small square tower of four walls
    /// with logs laid on them, seen from a corner, a little above - the logs crossing, half a course
    /// apart, and their ends standing out. Null if the walls could not be found or laid.
    /// </summary>
    private static BitmapSource? Corner(TextureOptions o, float proudMm, int n)
    {
        float course = MathF.Max(o.PitchMm, 0.5f);
        float wide = course * 6f, tall = course * 4f;

        var tower = MeshTransform.Transformed(Primitives.Box(wide, wide, tall), Matrix4x4.CreateTranslation(0, 0, tall / 2f));
        if (FacePatch.Find(tower, new Vector3(0, -wide / 2f, tall / 2f), -Vector3.UnitY) is not { } face) return null;

        var pick = new Vector3(0, -wide / 2f, tall / 2f);
        if (WallLoop.Around(tower, face, pick, out _) is not { } loop) return null;
        if (WallRun.Round(loop, tower, out _) is not { } run) return null;

        var logs = LogWalls.Build(run, course, o.LineMm, proudMm, out _);
        if (logs.TriangleCount == 0) return null;

        var pieces = new List<MeshShot.Piece>
        {
            new(tower, new Vector3(0.62f, 0.64f, 0.67f)),
            new(logs, new Vector3(0.93f, 0.92f, 0.88f))
        };

        var middle = new Vector3(0, 0, tall / 2f);
        var eye = middle + Vector3.Normalize(new Vector3(1f, -1f, 0.5f)) * wide * 3.1f;
        var pixels = MeshShot.Render(pieces, eye, middle, 30f, n, new Vector3(0.957f, 0.961f, 0.969f));

        var image = BitmapSource.Create(n, n, 96, 96, PixelFormats.Rgb24, null, pixels, n * 3);
        image.Freeze();
        return image;
    }

    /// <summary>A height field read off the profile at every pixel.</summary>
    private static float[]? Shaped(TextureOptions o, float depth, float wide, float tall, int w, int h)
    {
        if (SurfaceTexture.ProfileOf(o, depth, 0.4f) is not { } relief) return null;

        var height = new float[w * h];
        float px = wide / w;

        // The patch is centred on the origin, as a field is; the picture's top is up the face.
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                height[y * w + x] = relief.Height(new Vector2(
                    -wide / 2f + (x + 0.5f) * px, tall / 2f - (y + 0.5f) * (tall / h)));

        return height;
    }

    /// <summary>
    /// The outlined textures filled in: <paramref name="depth"/> where a pad is, nothing where it is
    /// not. Filled by scan line with every loop - outline and holes alike - toggling, so a hole in a
    /// pad is a hole whichever way round it was wound.
    /// </summary>
    private static float[]? Pads(TextureOptions o, float side, int n, float depth)
    {
        var shapes = SurfaceTexture.Over(side, side, o, seamless: false);
        if (shapes.Count == 0) return null;

        var inside = new bool[n * n];
        float px = side / n;

        void Toggle(IReadOnlyList<Vector2> loop)
        {
            if (loop.Count < 3) return;

            var crossings = new List<float>();
            for (int y = 0; y < n; y++)
            {
                // Row 0 is the top of the face.
                float at = side / 2f - (y + 0.5f) * px;

                crossings.Clear();
                for (int i = 0; i < loop.Count; i++)
                {
                    Vector2 a = loop[i], b = loop[(i + 1) % loop.Count];
                    if ((a.Y <= at) == (b.Y <= at)) continue;

                    crossings.Add(a.X + (at - a.Y) * (b.X - a.X) / (b.Y - a.Y));
                }

                crossings.Sort();
                for (int k = 0; k + 1 < crossings.Count; k += 2)
                {
                    int from = Math.Max((int)MathF.Ceiling((crossings[k] + side / 2f) / px - 0.5f), 0);
                    int to = Math.Min((int)MathF.Floor((crossings[k + 1] + side / 2f) / px - 0.5f), n - 1);
                    for (int x = from; x <= to; x++) inside[y * n + x] ^= true;
                }
            }
        }

        foreach (var shape in shapes)
        {
            Toggle(shape.Outline);
            foreach (var hole in shape.Holes) Toggle(hole);
        }

        return inside.Select(i => i ? depth : 0f).ToArray();
    }

    /// <summary>A pixel's blur either way, so a pad's wall has a slope for the light to catch.</summary>
    private static float[] Soften(float[] height, int n)
    {
        var soft = new float[height.Length];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float sum = 0f;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                        sum += height[Math.Clamp(y + dy, 0, n - 1) * n + Math.Clamp(x + dx, 0, n - 1)];

                soft[y * n + x] = sum / 9f;
            }

        return soft;
    }

    /// <summary>
    /// The heights of tiles or siding laid on a patch: the slabs built on a wall of that size and
    /// their triangles painted into a height buffer, highest where they overlap.
    /// </summary>
    private static float[]? Laid(TextureOptions o, float depth, float wide, float tall, int w, int h)
    {
        if (SurfaceTexture.CoursesOf(o, depth) is not { } courses) return null;

        var wall = MeshTransform.Transformed(
            Primitives.Box(wide + 8f, 8f, tall + 8f), Matrix4x4.CreateTranslation(0, 0, (tall + 8f) / 2f));
        if (FacePatch.Find(wall, new Vector3(0, -4f, (tall + 8f) / 2f), -Vector3.UnitY) is not { } face) return null;

        var surface = new PlanarSurface(face);
        var mesh = TileSolid.Build(surface, courses, wide, tall);
        if (mesh.TriangleCount == 0) return null;

        var height = new float[w * h];
        Array.Fill(height, 0f);

        // In the patch's own terms: across, up, and how far out of the face.
        Vector3 At(int vertex)
        {
            var p = mesh.Positions[vertex];
            var uv = face.ToUv(p) - surface.Middle;
            return new Vector3(uv.X, uv.Y, Vector3.Dot(p - face.Origin, face.Normal));
        }

        for (int t = 0; t + 2 < mesh.Indices.Count; t += 3)
        {
            Vector3 a = At(mesh.Indices[t]), b = At(mesh.Indices[t + 1]), c = At(mesh.Indices[t + 2]);
            Paint(height, w, h, wide, tall, a, b, c);
        }

        return height;
    }

    /// <summary>One triangle's height painted into the buffer, by its barycentric coordinates, keeping the higher.</summary>
    private static void Paint(float[] height, int w, int h, float wide, float tall, Vector3 a, Vector3 b, Vector3 c)
    {
        Vector2 P(Vector3 v) => new((v.X + wide / 2f) / wide * w, (tall / 2f - v.Y) / tall * h);
        Vector2 pa = P(a), pb = P(b), pc = P(c);

        int x0 = Math.Max((int)MathF.Floor(MathF.Min(pa.X, MathF.Min(pb.X, pc.X))), 0);
        int x1 = Math.Min((int)MathF.Ceiling(MathF.Max(pa.X, MathF.Max(pb.X, pc.X))), w - 1);
        int y0 = Math.Max((int)MathF.Floor(MathF.Min(pa.Y, MathF.Min(pb.Y, pc.Y))), 0);
        int y1 = Math.Min((int)MathF.Ceiling(MathF.Max(pa.Y, MathF.Max(pb.Y, pc.Y))), h - 1);

        float area = (pb.X - pa.X) * (pc.Y - pa.Y) - (pc.X - pa.X) * (pb.Y - pa.Y);
        if (MathF.Abs(area) < 1e-6f) return;

        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                float px = x + 0.5f, py = y + 0.5f;
                float l0 = ((pb.X - px) * (pc.Y - py) - (pc.X - px) * (pb.Y - py)) / area;
                float l1 = ((pc.X - px) * (pa.Y - py) - (pa.X - px) * (pc.Y - py)) / area;
                float l2 = 1f - l0 - l1;
                if (l0 < -1e-4f || l1 < -1e-4f || l2 < -1e-4f) continue;

                float z = l0 * a.Z + l1 * b.Z + l2 * c.Z;
                if (z > height[y * w + x]) height[y * w + x] = z;
            }
    }

    /// <summary>The pattern from above: dark where there is material, light where there is not.</summary>
    private static BitmapSource Flat(bool[] on, int n)
    {
        var pixels = new byte[n * n * 3];
        for (int i = 0; i < on.Length; i++)
        {
            var colour = on[i] ? Ink : Paper;
            pixels[i * 3] = colour[0];
            pixels[i * 3 + 1] = colour[1];
            pixels[i * 3 + 2] = colour[2];
        }

        var image = BitmapSource.Create(n, n, 96, 96, PixelFormats.Rgb24, null, pixels, n * 3);
        image.Freeze();
        return image;
    }

    /// <summary>Lambert shading from the upper left over the heights, with the tops a shade lighter.</summary>
    private static BitmapSource Shade(float[] height, int w, int h, float mmPerPixel, float depth)
    {
        var light = Vector3.Normalize(new Vector3(-1f, 1f, 1.1f));
        var pixels = new byte[w * h * 3];

        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float left = height[y * w + Math.Max(x - 1, 0)], right = height[y * w + Math.Min(x + 1, w - 1)];
                float up = height[Math.Max(y - 1, 0) * w + x], down = height[Math.Min(y + 1, h - 1) * w + x];

                // The picture's y runs down the face, so the slope up it is up minus down.
                var normal = Vector3.Normalize(new Vector3(
                    -(right - left) / (2f * mmPerPixel), -(up - down) / (2f * mmPerPixel), 1f));

                float lit = MathF.Max(Vector3.Dot(normal, light), 0f);
                float tone = 0.8f + 0.2f * Math.Clamp(height[y * w + x] / depth, 0f, 1f);
                float g = Math.Clamp((0.25f + 0.75f * lit) * tone * 1.12f, 0f, 1f);

                int k = (y * w + x) * 3;
                pixels[k] = (byte)(255 * g * 0.97f);
                pixels[k + 1] = (byte)(255 * g * 0.97f);
                pixels[k + 2] = (byte)(255 * g * 0.93f);
            }

        var image = BitmapSource.Create(w, h, 96, 96, PixelFormats.Rgb24, null, pixels, w * 3);
        image.Freeze();
        return image;
    }
}
