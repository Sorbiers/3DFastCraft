using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FastCraft3D.Geometry;

namespace FastCraft3D.Io;

/// <summary>
/// Reads a photograph as brightness, for a lithophane to carry as thickness.
///
/// Brought down to the size it is wanted at by averaging whole blocks of pixels rather than
/// picking one out of each: a photograph has noise in it, and a picture sampled by picking turns
/// that noise into bumps on a printed surface that has no business showing it.
///
/// Anything see-through is read as white, which is the thin end: a logo on a transparent
/// background comes out as the logo, not as the logo on a black field.
/// </summary>
public static class PictureReader
{
    /// <summary>What Windows Imaging reads without anything else installed.</summary>
    public static readonly string[] Extensions = [".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff"];

    /// <summary>The filter for an open dialog, in the order the box lists them.</summary>
    public const string Filter =
        "Pictures (*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff)|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff|All files (*.*)|*.*";

    public static bool Reads(string file) =>
        Extensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The picture as brightness, no wider than <paramref name="atMostWide"/>. A photograph is
    /// far finer than any nozzle can lay down, so there is nothing to gain by carrying all of it.
    /// </summary>
    public static Greyscale Read(string file, int atMostWide = 1600)
    {
        using var stream = File.OpenRead(file);
        var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
        var colours = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);

        int width = colours.PixelWidth, height = colours.PixelHeight;
        var pixels = new byte[width * height * 4];
        colours.CopyPixels(pixels, width * 4, 0);

        return Reduce(pixels, width, height, Math.Max(1, Math.Min(width, atMostWide)));
    }

    /// <summary>The same from pixels already in hand, which is what the tests use.</summary>
    public static Greyscale Reduce(byte[] bgra, int width, int height, int atMostWide)
    {
        int wanted = Math.Max(1, Math.Min(width, atMostWide));
        int tall = Math.Max(1, (int)MathF.Round(height * (float)wanted / width));
        var samples = new float[wanted * tall];

        for (int y = 0; y < tall; y++)
        {
            int from = y * height / tall, to = Math.Max(from + 1, (y + 1) * height / tall);

            for (int x = 0; x < wanted; x++)
            {
                int left = x * width / wanted, right = Math.Max(left + 1, (x + 1) * width / wanted);
                double total = 0;
                int counted = 0;

                for (int sy = from; sy < to && sy < height; sy++)
                    for (int sx = left; sx < right && sx < width; sx++)
                    {
                        int p = (sy * width + sx) * 4;
                        float alpha = bgra[p + 3] / 255f;

                        // Over white, so what is see-through reads as the thin end rather than
                        // as whatever colour happened to be left behind it.
                        float blue = bgra[p] * alpha + 255f * (1f - alpha);
                        float green = bgra[p + 1] * alpha + 255f * (1f - alpha);
                        float red = bgra[p + 2] * alpha + 255f * (1f - alpha);

                        total += (0.299 * red + 0.587 * green + 0.114 * blue) / 255.0;
                        counted++;
                    }

                samples[y * wanted + x] = counted == 0 ? 1f : (float)(total / counted);
            }
        }

        return new Greyscale(wanted, tall, samples);
    }
}
