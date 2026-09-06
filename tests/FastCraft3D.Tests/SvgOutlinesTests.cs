using System.Numerics;
using FastCraft3D.Geometry;
using FastCraft3D.Geometry.Engraving;
using FastCraft3D.Io;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>
/// Reading a drawing in as outlines. What comes back is what the font reader gives, so the
/// lettering tool can stamp a logo without knowing the difference.
/// </summary>
public class SvgOutlinesTests
{
    private static (Vector2 Min, Vector2 Max) Extent(IEnumerable<TextShape> shapes)
    {
        var min = new Vector2(float.MaxValue);
        var max = new Vector2(float.MinValue);

        foreach (var shape in shapes)
            foreach (var point in shape.Outline)
            {
                min = Vector2.Min(min, point);
                max = Vector2.Max(max, point);
            }

        return (min, max);
    }

    [Fact]
    public void ARectangleComesBackTheRightShapeAndSize()
    {
        var shapes = SvgOutlines.Parse(
            """<svg xmlns="http://www.w3.org/2000/svg"><rect x="4" y="7" width="20" height="10"/></svg>""",
            10f);

        var one = Assert.Single(shapes);
        Assert.Equal(4, one.Outline.Count);
        Assert.Empty(one.Holes);

        var (min, max) = Extent(shapes);
        Assert.Equal(20f, max.X - min.X, 3);
        Assert.Equal(10f, max.Y - min.Y, 3);

        // Sized to the height asked for and centred, wherever it was drawn.
        Assert.Equal(0f, (min + max).X * 0.5f, 3);
        Assert.Equal(0f, (min + max).Y * 0.5f, 3);
    }

    /// <summary>A subpath inside another is its hole, whichever way round either was drawn.</summary>
    [Fact]
    public void ALoopInsideAnotherBecomesAHole()
    {
        var shapes = SvgOutlines.Parse(
            """<svg><path d="M0,0 H20 V20 H0 Z M5,5 H15 V15 H5 Z"/></svg>""", 20f);

        var one = Assert.Single(shapes);
        Assert.Single(one.Holes);
        Assert.Equal(4, one.Holes[0].Count);
    }

    [Fact]
    public void TransformsAreFollowedDownTheTree()
    {
        const string plain = """<svg><rect x="0" y="0" width="20" height="10"/></svg>""";
        const string stretched =
            """<svg><g transform="translate(30,4) scale(2,1)"><rect x="0" y="0" width="20" height="10"/></g></svg>""";

        var (aMin, aMax) = Extent(SvgOutlines.Parse(plain, 10f));
        var (bMin, bMax) = Extent(SvgOutlines.Parse(stretched, 10f));

        Assert.Equal(20f, aMax.X - aMin.X, 3);
        Assert.Equal(40f, bMax.X - bMin.X, 3); // twice as wide, same height
        Assert.Equal(10f, bMax.Y - bMin.Y, 3);
    }

    /// <summary>
    /// The drawing arrives upside down - SVG measures down the page - so a triangle drawn
    /// pointing down comes back pointing down in millimetres, which means its apex is at the
    /// bottom and its base along the top.
    /// </summary>
    [Fact]
    public void TheDrawingIsTurnedTheRightWayUp()
    {
        var shapes = SvgOutlines.Parse("""<svg><polygon points="0,0 10,0 5,10"/></svg>""", 10f);
        var one = Assert.Single(shapes);

        var (min, max) = Extent(shapes);

        Assert.Equal(2, one.Outline.Count(p => MathF.Abs(p.Y - max.Y) < 1e-3f));
        Assert.Equal(1, one.Outline.Count(p => MathF.Abs(p.Y - min.Y) < 1e-3f));
    }

    /// <summary>
    /// Minified path data writes an arc's flags with nothing between them - "0 111 0" is four
    /// values, not one - which is the case a naive split on separators gets wrong.
    /// </summary>
    [Fact]
    public void MinifiedPathDataReadsTheSameAsSpacedOut()
    {
        var spaced = SvgOutlines.Parse(
            """<svg><path d="M 10 0 A 10 10 0 1 1 10 20 Z"/></svg>""", 20f);
        var tight = SvgOutlines.Parse("""<svg><path d="M10 0A10 10 0 1110 20Z"/></svg>""", 20f);

        var (aMin, aMax) = Extent(spaced);
        var (bMin, bMax) = Extent(tight);

        Assert.Equal(aMax.X - aMin.X, bMax.X - bMin.X, 2);
        Assert.Equal(aMax.Y - aMin.Y, bMax.Y - bMin.Y, 2);
        Assert.True(aMax.X - aMin.X > 9f, "the half circle lost its bulge");
    }

    /// <summary>Relative commands, and a curve, both measured against what they should trace.</summary>
    [Fact]
    public void RelativeCommandsAndCurvesAreFollowed()
    {
        var shapes = SvgOutlines.Parse(
            """<svg><path d="m 0,0 c 0,-10 20,-10 20,0 l 0,10 l -20,0 z"/></svg>""", 15f);

        var one = Assert.Single(shapes);
        var (min, max) = Extent(shapes);

        Assert.True(one.Outline.Count > 10, $"the curve was not flattened ({one.Outline.Count} points)");

        // The arch rises 7.5, not 10: a cubic with both handles at the same height reaches
        // three quarters of it. So the shape is 20 across and 17.5 tall.
        Assert.Equal(20f / 17.5f, (max.X - min.X) / (max.Y - min.Y), 2);
    }

    [Fact]
    public void ACircleIsRound()
    {
        var shapes = SvgOutlines.Parse("""<svg><circle cx="5" cy="5" r="5"/></svg>""", 10f);
        var one = Assert.Single(shapes);

        float area = MathF.Abs(Polygon2.SignedArea(one.Outline)) * 0.5f;
        Assert.Equal(MathF.PI * 25f, area, 25f * 0.01f);
    }

    /// <summary>Held for reuse rather than drawn, so nothing in it is part of the picture.</summary>
    [Fact]
    public void DefinitionsAndTextAreLeftOut()
    {
        var shapes = SvgOutlines.Parse(
            """
            <svg>
              <defs><rect x="0" y="0" width="99" height="99"/></defs>
              <text x="0" y="0">not read</text>
              <g style="display:none"><rect x="0" y="0" width="50" height="50"/></g>
              <rect x="0" y="0" width="20" height="10"/>
            </svg>
            """, 10f);

        var (min, max) = Extent(shapes);
        Assert.Equal(20f, max.X - min.X, 3);
    }

    [Fact]
    public void NothingUsableComesBackEmptyRatherThanThrowing()
    {
        Assert.Empty(SvgOutlines.Parse("<svg></svg>", 10f));
        Assert.Empty(SvgOutlines.Parse("""<svg><path d="M0,0 L10,0"/></svg>""", 10f));
        Assert.Empty(SvgOutlines.Parse("", 10f));
        Assert.Empty(SvgOutlines.Parse("""<svg><rect width="10" height="10"/></svg>""", 0f));
    }
}
