using System.Windows.Media.Imaging;
using FastCraft3D.Geometry.Engraving;
using FastCraft3D.View;
using Xunit;

namespace FastCraft3D.Tests;

public class TexturePreviewTests
{
    private static byte[] Pixels(BitmapSource picture)
    {
        var pixels = new byte[picture.PixelWidth * picture.PixelHeight * 3];
        picture.CopyPixels(pixels, picture.PixelWidth * 3, 0);
        return pixels;
    }

    private static TextureOptions Of(TextureKind kind) =>
        TextureOptions.Default with { Kind = kind, PitchMm = kind == TextureKind.Grain ? 2.5f : 8f, LineMm = 0.8f };

    public static TheoryData<TextureKind> EveryPattern
    {
        get
        {
            var data = new TheoryData<TextureKind>();
            foreach (var kind in Enum.GetValues<TextureKind>().Where(k => k is not (TextureKind.None or TextureKind.Logs)))
                data.Add(kind);
            return data;
        }
    }

    /// <summary>
    /// Every pattern has both pictures, and they are of something: the flat one in two tones, the
    /// relief in shades - not one flat grey, which a field read off the wrong way up, or tiles
    /// built on a wall that was not found, would come back as.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryPattern))]
    public void EveryPatternHasAFlatPictureAndAShadedOne(TextureKind kind)
    {
        var pictures = TexturePreview.Render(Of(kind), 1.2f);

        Assert.NotNull(pictures);

        var flat = Pixels(pictures!.Flat);
        var relief = Pixels(pictures.Relief);

        int tones = Enumerable.Range(0, flat.Length / 3).Select(i => (flat[i * 3] << 16) | (flat[i * 3 + 1] << 8) | flat[i * 3 + 2]).Distinct().Count();
        Assert.Equal(2, tones);
        Assert.True(relief.Distinct().Count() > 8, $"{kind} relief came out {relief.Distinct().Count()} shades");
    }

    /// <summary>
    /// The same size for every pattern, flat and relief alike, so the panel does not grow and shrink
    /// with the choice.
    /// </summary>
    [Fact]
    public void EveryPictureIsTheSameSize()
    {
        var sizes = new HashSet<(int, int)>();

        foreach (var kind in Enum.GetValues<TextureKind>().Where(k => k is not (TextureKind.None or TextureKind.Logs)))
        {
            var pictures = TexturePreview.Render(Of(kind), 1.2f);
            Assert.NotNull(pictures);

            sizes.Add((pictures!.Flat.PixelWidth, pictures.Flat.PixelHeight));
            sizes.Add((pictures.Relief.PixelWidth, pictures.Relief.PixelHeight));
        }

        Assert.Single(sizes);
    }

    [Fact]
    public void NothingChosenAndLogWallsHaveNoPictures()
    {
        Assert.Null(TexturePreview.Render(TextureOptions.Default with { Kind = TextureKind.None }, 1f));
        Assert.Null(TexturePreview.Render(TextureOptions.Default with { Kind = TextureKind.Logs }, 1f));
    }

    /// <summary>
    /// Two views of one patch: where the flat picture says there is material, the relief is higher
    /// than where it says there is none - so the pair cannot be of two different things.
    /// </summary>
    [Theory]
    [InlineData(TextureKind.Hex)]
    [InlineData(TextureKind.Rubble)]
    [InlineData(TextureKind.Siding)]
    public void TheFlatPictureAndTheReliefAreOfTheSamePatch(TextureKind kind)
    {
        var pictures = TexturePreview.Render(Of(kind), 1.2f)!;
        var flat = Pixels(pictures.Flat);
        var relief = Pixels(pictures.Relief);

        double dark = 0, light = 0;
        int darkCount = 0, lightCount = 0;
        for (int i = 0; i < flat.Length / 3; i++)
        {
            if (flat[i * 3] < 128) { dark += relief[i * 3]; darkCount++; }
            else { light += relief[i * 3]; lightCount++; }
        }

        Assert.True(darkCount > 0 && lightCount > 0, $"{kind}: the flat picture is all one tone");

        // Shaded from the upper left, with the tops a shade lighter: material averages brighter than a joint.
        Assert.True(dark / darkCount > light / lightCount - 6, $"{kind}: material {dark / darkCount:0} against joints {light / lightCount:0}");
    }
}
