using FastCraft3D.Geometry.Engraving;
using FastCraft3D.View;
using Xunit;

namespace FastCraft3D.Tests;

public class TexturePreviewTests
{
    /// <summary>
    /// Every texture without an outline to draw has a picture for the panel, and the picture is of
    /// something: shaded, not one flat grey - which a field read off the wrong way up, or tiles
    /// built on a wall that was not found, would come back as.
    /// </summary>
    [Theory]
    [InlineData(TextureKind.Rubble)]
    [InlineData(TextureKind.CoursedStone)]
    [InlineData(TextureKind.Bark)]
    [InlineData(TextureKind.Grain)]
    [InlineData(TextureKind.Planks)]
    [InlineData(TextureKind.RoofTiles)]
    [InlineData(TextureKind.Siding)]
    public void EveryShapedTextureHasAShadedPictureForThePanel(TextureKind kind)
    {
        var o = TextureOptions.Default with { Kind = kind, PitchMm = kind == TextureKind.Grain ? 2.5f : 8f, LineMm = 0.8f };
        var picture = TexturePreview.Render(o, 1.2f);

        Assert.NotNull(picture);
        var pixels = new byte[picture!.PixelWidth * picture.PixelHeight * 3];
        picture.CopyPixels(pixels, picture.PixelWidth * 3, 0);

        Assert.True(pixels.Distinct().Count() > 8, $"{kind} came out {pixels.Distinct().Count()} shades");
    }

    [Fact]
    public void AFlatTextureIsDrawnAsOutlinesInstead()
    {
        Assert.Null(TexturePreview.Render(TextureOptions.Default with { Kind = TextureKind.Brick }, 1f));
    }
}
