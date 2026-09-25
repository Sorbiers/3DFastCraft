using System.IO;
using System.Numerics;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FastCraft3D.Geometry;

namespace FastCraft3D.Io;

/// <summary>
/// A small picture of what is being written, for Windows to show as the file's icon.
///
/// Drawn here rather than captured from the viewport: an export runs off the UI thread and may
/// have no window to photograph. It is a depth buffer and a flat shade, which is all a 256 pixel
/// icon needs; WPF is asked only to pack the result into a PNG.
///
/// A 3MF without one gets the blank page icon in Explorer, which is why every file from a slicer
/// or from 3D Builder shows a picture of the model and ours did not.
///
/// Kept in the generator library, under the namespace it had in the app, because the library's
/// catalogue draws its pictures with it and the library cannot reach into the app; the app's 3MF
/// writer reaches it here instead.
/// </summary>
public static class MeshThumbnail
{
    /// <summary>Which way the camera lies from the model: over the front right corner, as the app opens.</summary>
    private static readonly Vector3 From = Vector3.Normalize(new Vector3(0.62f, -0.78f, 0.52f));

    /// <summary>Where the light comes from - a little above the camera, so the top faces are brightest.</summary>
    private static readonly Vector3 Light = Vector3.Normalize(new Vector3(0.40f, -0.55f, 0.73f));

    /// <summary>How much of the picture the model fills, leaving a margin so nothing touches the edge.</summary>
    private const float Fills = 0.86f;

    /// <summary>
    /// The parts as a PNG, or null when there is nothing to draw or the machine will not encode one
    /// - the file is then written without a picture rather than not written at all.
    /// </summary>
    public static byte[]? Png(IReadOnlyList<(Mesh Mesh, Vector3 Colour)> parts, int size = 256)
    {
        if (parts.Count == 0 || size < 8) return null;

        try
        {
            var bounds = Bounds.Empty;
            foreach (var (mesh, _) in parts) bounds = bounds.Union(mesh.ComputeBounds());
            if (bounds.IsEmpty) return null;

            var forward = -From;
            var right = Vector3.Cross(forward, Vector3.UnitZ);
            right = right.LengthSquared() < 1e-6f ? Vector3.UnitX : Vector3.Normalize(right);
            var up = Vector3.Normalize(Vector3.Cross(right, forward));
            var centre = bounds.Center;

            // Projected first, so the scale is read off what is actually in the picture rather than
            // guessed from the box's diagonal - a long part seen end-on is not its own length wide.
            var facing = new List<(Vector3 A, Vector3 B, Vector3 C, Vector3 Colour)>();
            float reach = 1e-4f;

            foreach (var (mesh, colour) in parts)
                for (int i = 0; i + 2 < mesh.Indices.Count; i += 3)
                {
                    var a = mesh.Positions[mesh.Indices[i]];
                    var b = mesh.Positions[mesh.Indices[i + 1]];
                    var c = mesh.Positions[mesh.Indices[i + 2]];

                    var normal = Vector3.Cross(b - a, c - a);
                    if (normal.LengthSquared() < 1e-12f) continue;
                    normal = Vector3.Normalize(normal);

                    // The far side of a closed model is never seen, and drawing it only gives the
                    // depth buffer more to reject.
                    if (Vector3.Dot(normal, From) <= 0f) continue;

                    float lit = 0.32f + 0.68f * MathF.Max(0f, Vector3.Dot(normal, Light));
                    facing.Add((Eye(a), Eye(b), Eye(c), colour * lit));
                }

            if (facing.Count == 0) return null;

            foreach (var (a, b, c, _) in facing)
                foreach (var p in new[] { a, b, c })
                    reach = MathF.Max(reach, MathF.Max(MathF.Abs(p.X), MathF.Abs(p.Y)));

            float scale = size * Fills / (2f * reach);
            float middle = size / 2f;

            var pixels = new byte[size * size * 4];
            var depth = new float[size * size];
            Array.Fill(depth, float.MaxValue);

            foreach (var (a, b, c, colour) in facing)
                Fill(pixels, depth, size, Screen(a), Screen(b), Screen(c), colour);

            return Encode(pixels, size);

            Vector3 Eye(Vector3 point)
            {
                var offset = point - centre;
                return new Vector3(Vector3.Dot(offset, right), Vector3.Dot(offset, up), Vector3.Dot(offset, forward));
            }

            Vector3 Screen(Vector3 eye) => new(middle + eye.X * scale, middle - eye.Y * scale, eye.Z);
        }
        catch (Exception)
        {
            // A picture is a nicety; the file itself matters.
            return null;
        }
    }

    /// <summary>One triangle into the buffer, nearest to the camera wins.</summary>
    private static void Fill(byte[] pixels, float[] depth, int size,
                             Vector3 a, Vector3 b, Vector3 c, Vector3 colour)
    {
        int left = Math.Max(0, (int)MathF.Floor(MathF.Min(a.X, MathF.Min(b.X, c.X))));
        int right = Math.Min(size - 1, (int)MathF.Ceiling(MathF.Max(a.X, MathF.Max(b.X, c.X))));
        int top = Math.Max(0, (int)MathF.Floor(MathF.Min(a.Y, MathF.Min(b.Y, c.Y))));
        int bottom = Math.Min(size - 1, (int)MathF.Ceiling(MathF.Max(a.Y, MathF.Max(b.Y, c.Y))));

        float area = (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
        if (MathF.Abs(area) < 1e-6f) return;

        for (int y = top; y <= bottom; y++)
            for (int x = left; x <= right; x++)
            {
                float px = x + 0.5f, py = y + 0.5f;

                // The three barycentric weights, each the share of the corner it faces.
                float wa = ((b.X - px) * (c.Y - py) - (b.Y - py) * (c.X - px)) / area;
                float wb = ((c.X - px) * (a.Y - py) - (c.Y - py) * (a.X - px)) / area;
                float wc = ((a.X - px) * (b.Y - py) - (a.Y - py) * (b.X - px)) / area;
                if (wa < 0 || wb < 0 || wc < 0) continue;

                float z = wa * a.Z + wb * b.Z + wc * c.Z;
                int at = y * size + x;
                if (z >= depth[at]) continue;

                depth[at] = z;
                pixels[at * 4 + 0] = Byte(colour.Z);
                pixels[at * 4 + 1] = Byte(colour.Y);
                pixels[at * 4 + 2] = Byte(colour.X);
                pixels[at * 4 + 3] = 255;
            }

        static byte Byte(float value) => (byte)Math.Clamp((int)(value * 255f + 0.5f), 0, 255);
    }

    private static byte[] Encode(byte[] pixels, int size)
    {
        var picture = BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32, null, pixels, size * 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(picture));

        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }
}
