using System.IO;
using System.Numerics;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FastCraft3D.Geometry;
using FastCraft3D.Io;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>
/// Lithophanes: a picture carried as thickness in a translucent plate.
///
/// The plate is built as a grid rather than cut, so it is closed by construction; what these
/// check is that it really is, that the thickness follows the light rather than the brightness,
/// and that what comes out is the size that was asked for.
/// </summary>
public class LithophaneTests
{
    private static Greyscale Flat(float brightness, int width = 16, int height = 12) =>
        new(width, height, Enumerable.Repeat(brightness, width * height).ToArray());

    /// <summary>A gradient from black on the left to white on the right.</summary>
    private static Greyscale Ramp(int width = 32, int height = 24)
    {
        var samples = new float[width * height];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                samples[y * width + x] = (float)x / (width - 1);

        return new Greyscale(width, height, samples);
    }

    private static void Closed(Mesh mesh)
    {
        var health = mesh.CheckHealth();
        Assert.True(health.IsWatertight, health.Describe());
        Assert.True(mesh.ComputeSignedVolume() > 0, "inside out");
    }

    [Fact]
    public void WhiteComesOutThinAndBlackComesOutThick()
    {
        var o = new LithophaneOptions { MinThickness = 0.8f, MaxThickness = 3.2f };

        Assert.Equal(0.8f, Lithophane.Thickness(1f, o), 3);
        Assert.Equal(3.2f, Lithophane.Thickness(0f, o), 3);

        // And never the wrong way round in between.
        float last = float.MaxValue;
        for (float b = 0f; b <= 1f; b += 0.05f)
        {
            float t = Lithophane.Thickness(b, o);
            Assert.True(t <= last + 1e-4f, $"{b:0.##} came out thicker than the brightness below it");
            last = t;
        }
    }

    /// <summary>
    /// The point of the curve. Light dies away through plastic by a fraction of what is left per
    /// millimetre, so half the light needs far less than half the thickness range - a thickness
    /// set straight from the brightness spends its whole range on the darkest tones and leaves
    /// the middle of the picture flat.
    /// </summary>
    [Fact]
    public void TheMiddleToneFollowsTheLightNotTheBrightness()
    {
        var o = new LithophaneOptions { MinThickness = 0.8f, MaxThickness = 3.2f };

        float middle = Lithophane.Thickness(0.5f, o);
        float straight = (0.8f + 3.2f) / 2f;

        Assert.True(middle < straight - 0.3f, $"mid grey came out {middle:0.##} mm, near the flat {straight:0.##} mm");
        Assert.InRange(middle, 0.8f, 3.2f);
    }

    [Fact]
    public void ThicknessAndTransmissionAreEachOthersInverse()
    {
        var o = new LithophaneOptions();

        for (float b = 0f; b <= 1f; b += 0.1f)
            Assert.Equal(b, Lithophane.Transmission(Lithophane.Thickness(b, o), o), 3);
    }

    [Fact]
    public void GammaAndNegativeChangeWhatIsRead()
    {
        var plain = new LithophaneOptions();

        Assert.Equal(Lithophane.Thickness(1f, plain), Lithophane.Thickness(0f, plain with { Negative = true }), 3);

        // Gamma over one reads the middle darker, which is thicker.
        Assert.True(Lithophane.Thickness(0.5f, plain with { Gamma = 2f }) > Lithophane.Thickness(0.5f, plain));
    }

    [Fact]
    public void BrightnessMovesEveryToneAndContrastSpreadsThemAboutTheMiddle()
    {
        var plain = new LithophaneOptions();

        // Brighter reads lighter, which is thinner; darker reads thicker.
        Assert.True(Lithophane.Thickness(0.5f, plain with { Brightness = 0.3f }) < Lithophane.Thickness(0.5f, plain));
        Assert.True(Lithophane.Thickness(0.5f, plain with { Brightness = -0.3f }) > Lithophane.Thickness(0.5f, plain));

        // Contrast pushes the ends apart and leaves mid grey where it was.
        var harder = plain with { Contrast = 2f };
        Assert.Equal(Lithophane.Thickness(0.5f, plain), Lithophane.Thickness(0.5f, harder), 3);
        Assert.True(Lithophane.Thickness(0.3f, harder) > Lithophane.Thickness(0.3f, plain));
        Assert.True(Lithophane.Thickness(0.7f, harder) < Lithophane.Thickness(0.7f, plain));

        // None of it can push a thickness outside what was asked for.
        var extreme = plain with { Brightness = 1f, Contrast = 4f, Gamma = 5f };
        foreach (float b in new[] { 0f, 0.25f, 0.5f, 0.75f, 1f })
            Assert.InRange(Lithophane.Thickness(b, extreme), extreme.MinThickness, extreme.MaxThickness);
    }

    /// <summary>What the layer height leaves of the range is what the picture actually gets.</summary>
    [Theory]
    [InlineData(0.1f, 25)]
    [InlineData(0.2f, 13)]
    public void TheGreysThatSurviveAreTheLayersAcrossTheRange(float layer, int expected)
    {
        var o = new LithophaneOptions { MinThickness = 0.8f, MaxThickness = 3.2f, LayerHeight = layer };

        Assert.Equal(expected, Lithophane.GreyLevels(o));
    }

    [Fact]
    public void APlateComesOutClosedAndStandingOnItsBottomEdge()
    {
        var o = new LithophaneOptions { Width = 60f, Pitch = 1f, Frame = 0f };
        var mesh = Lithophane.Build(Ramp(), o);

        Closed(mesh);

        var box = mesh.ComputeBounds();
        Assert.Equal(60f, box.Size.X, 1);
        Assert.Equal(0f, box.Min.Z, 3);
        Assert.Equal(45f, box.Size.Z, 1);          // a 32 by 24 picture, 60 mm across
        Assert.Equal(3.2f, box.Max.Y, 2);          // the black end of the ramp
        Assert.Equal(0f, box.Min.Y, 3);            // the back is flat
    }

    /// <summary>
    /// One even grey makes a plate of one even thickness, which is a volume that can be checked
    /// against arithmetic rather than against itself.
    /// </summary>
    [Fact]
    public void AnEvenGreyMakesAPlateOfOneThickness()
    {
        var o = new LithophaneOptions { Width = 40f, Pitch = 1f, Frame = 0f };
        var mesh = Lithophane.Build(Flat(0.5f), o);

        Closed(mesh);

        var box = mesh.ComputeBounds();
        float thickness = Lithophane.Thickness(0.5f, o);
        Assert.Equal(thickness, box.Size.Y, 2);
        Assert.Equal((double)box.Size.X * box.Size.Z * thickness, mesh.ComputeSignedVolume(), 1.0);
    }

    [Fact]
    public void AFrameGoesRoundThePictureAtTheFullThickness()
    {
        var o = new LithophaneOptions { Width = 40f, Pitch = 1f, Frame = 4f, MaxThickness = 3f };
        var mesh = Lithophane.Build(Flat(1f, 8, 8), o);        // all white: the picture is as thin as it goes

        Closed(mesh);

        var box = mesh.ComputeBounds();
        Assert.Equal(48f, box.Size.X, 1);                      // 40 mm of picture, 4 mm of frame each side
        Assert.Equal(3f, box.Max.Y, 2);                        // the frame, at the full thickness
    }

    [Fact]
    public void ACurvedPlateWrapsRoundWithoutFoldingThroughItself()
    {
        var o = new LithophaneOptions
        {
            Width = 80f, Pitch = 1f, Frame = 0f, Shape = LithophaneShape.Curved, Angle = 180f
        };

        var mesh = Lithophane.Build(Ramp(), o);

        Closed(mesh);

        // Half a turn of an 80 mm arc sits on a radius of 80/pi. At the ends of a half turn the
        // face is edge on, so what reaches furthest across is the outside of it: the inner arc
        // plus the thickness at each end - black one side of the ramp, white the other.
        float radius = 80f / MathF.PI;
        var box = mesh.ComputeBounds();
        Assert.Equal(2f * radius + 3.2f + 0.8f, box.Size.X, 0.5f);
        Assert.True(box.Size.Y > radius * 0.5f, $"it came out only {box.Size.Y:0.#} mm deep - it has not curved");
    }

    [Fact]
    public void TheGridAndItsTriangleCountAreKnownBeforeItIsBuilt()
    {
        var o = new LithophaneOptions { Width = 50f, Pitch = 0.5f, Frame = 0f };
        var picture = Ramp(40, 20);

        var grid = Lithophane.Grid(picture.Width, picture.Height, o);
        var mesh = Lithophane.Build(picture, o);

        Assert.Equal(grid.Triangles, mesh.TriangleCount);
        Assert.Equal(grid.Width, mesh.ComputeBounds().Size.X, 2);
        Assert.Equal(grid.Height, mesh.ComputeBounds().Size.Z, 2);
    }

    [Fact]
    public void APictureIsAveragedDownRatherThanPickedFrom()
    {
        // A checkerboard of black and white pixels: averaged, it is a flat mid grey; picked
        // from, it would come back as one or the other.
        int size = 32;
        var pixels = new byte[size * size * 4];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                byte value = (byte)((x + y) % 2 == 0 ? 0 : 255);
                int p = (y * size + x) * 4;
                pixels[p] = pixels[p + 1] = pixels[p + 2] = value;
                pixels[p + 3] = 255;
            }

        var picture = PictureReader.Reduce(pixels, size, size, 8);

        Assert.Equal(8, picture.Width);
        Assert.All(picture.Samples, s => Assert.Equal(0.5f, s, 1));
    }

    /// <summary>The whole way in: a real file off the disk, through the decoder, to brightness.</summary>
    [Fact]
    public void APictureIsReadFromAFileTheWayRoundItWasWritten()
    {
        string file = Path.Combine(Path.GetTempPath(), $"litho-{Guid.NewGuid():N}.png");
        int width = 64, height = 16;

        var pixels = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                byte value = (byte)(x * 255 / (width - 1));      // black on the left, white on the right
                int p = (y * width + x) * 4;
                pixels[p] = pixels[p + 1] = pixels[p + 2] = value;
                pixels[p + 3] = 255;
            }

        try
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(
                BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4)));
            using (var stream = File.Create(file)) encoder.Save(stream);

            var picture = PictureReader.Read(file);

            Assert.Equal(width, picture.Width);
            Assert.Equal(height, picture.Height);
            Assert.True(picture.At(0, 0) < 0.1f, "the dark end did not come back dark");
            Assert.True(picture.At(width - 1, 0) > 0.9f, "the light end did not come back light");

            // And it builds from there, which is the path the tool actually takes.
            Closed(Lithophane.Build(picture, new LithophaneOptions { Width = 40f, Pitch = 1f }));
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void WhatIsSeeThroughReadsAsWhite()
    {
        var pixels = new byte[4];       // one pixel, black but fully transparent
        pixels[3] = 0;

        var picture = PictureReader.Reduce(pixels, 1, 1, 1);

        Assert.Equal(1f, picture.At(0, 0), 3);
    }

    [Fact]
    public void WhatItWillLookLitIsSteppedToTheLayersItWillPrintAt()
    {
        var o = new LithophaneOptions { LayerHeight = 0.4f, MinThickness = 0.8f, MaxThickness = 3.2f };
        var lit = Lithophane.Backlit(Ramp(64, 8), o, 64, 8);

        Assert.Equal(64 * 8, lit.Length);
        Assert.All(lit, v => Assert.InRange(v, 0f, 1f));

        // Coarse layers leave few steps, so a smooth ramp comes back as a handful of levels.
        int levels = lit.Select(v => MathF.Round(v, 3)).Distinct().Count();
        Assert.True(levels <= Lithophane.GreyLevels(o), $"{levels} greys from {Lithophane.GreyLevels(o)} layers");
        Assert.True(levels > 1, "the ramp came back flat");
    }
}
