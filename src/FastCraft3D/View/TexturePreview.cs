using System.Numerics;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FastCraft3D.Geometry;
using FastCraft3D.Geometry.Engraving;

namespace FastCraft3D.View;

/// <summary>
/// A picture of a texture, for the Texture and Cladding panels: the height of the relief over a
/// patch of face, shaded as if lit from the upper left, the way a relief is drawn.
///
/// The flat textures draw their outlines in the panel and need nothing of this. These are the
/// ones with no outline to draw - stone, bark, grain, boarding, roof tiles, siding - where the
/// panel showed an empty box and the texture could only be judged by laying it on a part.
///
/// The picture is the very field the part would get, read off pixel by pixel rather than built as
/// a mesh: the shaped ones are a height, and the laid ones - tiles, siding - are built as the slabs
/// they are and their heights read off the slabs.
/// </summary>
public static class TexturePreview
{
    private const int WidthPx = 240;
    private const int LeastHeightPx = 60, MostHeightPx = 150;

    /// <summary>The picture, or null for a texture that is drawn as outlines instead.</summary>
    public static BitmapSource? Render(TextureOptions options, float depthMm)
    {
        var o = options.Sane();
        if (!o.IsProfiled) return null;

        // How much of the face the picture shows: a handful of pieces across, in the proportions
        // the texture's own pieces have.
        float course = MathF.Max(o.PitchMm / MathF.Max(o.Courses, 0.2f), TextureOptions.LeastPadMm);
        var (wide, tall) = o.Kind switch
        {
            TextureKind.Grain => (o.PitchMm * 34f, o.PitchMm * 14f),
            TextureKind.Bark => (o.PitchMm * 7f, o.PitchMm * o.Courses * 1.2f),
            TextureKind.Siding => (o.PitchMm * 2f, o.PitchMm * 3.2f),
            _ => (o.PitchMm * 5f, MathF.Max(course * 3.5f, o.PitchMm * 2f))
        };

        int w = WidthPx, h = Math.Clamp((int)(WidthPx * tall / wide), LeastHeightPx, MostHeightPx);
        float depth = MathF.Max(depthMm, 0.1f);

        var height = o.IsLaid ? Laid(o, depth, wide, tall, w, h) : Shaped(o, depth, wide, tall, w, h);
        return height is null ? null : Shade(height, w, h, wide / w, depth);
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
