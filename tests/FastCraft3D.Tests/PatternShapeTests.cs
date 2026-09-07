using FastCraft3D.Geometry.Engraving;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>
/// The proportions of a course.
///
/// Brick was hard-wired at three times as long as it is tall, which is about right for brick and
/// wrong for everything else. A roof tile is nearer one and a half, and the first house came out
/// with 39 x 13 cm tiles where a pantile should be about 30 x 20.
/// </summary>
public class PatternShapeTests
{
    private static readonly Rect2 Wall = new(0, 0, 60, 40);

    private static EngraveOptions Brick(float aspect) => EngraveOptions.Default with
    {
        Kind = PatternKind.Brick, Size = 6f, GrooveWidth = 0.4f, Depth = 0.5f,
        Raised = true, Aspect = aspect
    };

    [Fact]
    public void LeftAloneItIsStillBrick()
    {
        var piece = GroovePattern.Raised(Brick(0), Wall).Rectangles[4];

        Assert.Equal(6f, piece.MaxU - piece.MinU, 3);
        Assert.Equal(6f / GroovePattern.DefaultAspect, piece.MaxV - piece.MinV, 3);
    }

    /// <summary>A roof tile: half again as long as it is tall, not three times.</summary>
    [Theory]
    [InlineData(1.5f)]
    [InlineData(2f)]
    [InlineData(4f)]
    public void TheCoursesTakeTheShapeAskedFor(float aspect)
    {
        var piece = GroovePattern.Raised(Brick(aspect), Wall).Rectangles[4];

        Assert.Equal(6f, piece.MaxU - piece.MinU, 3);
        Assert.Equal(6f / aspect, piece.MaxV - piece.MinV, 3);
    }

    /// <summary>
    /// Nothing silly gets through. Zero and anything below it read as "unset", so an options
    /// record that has never been touched still lays brick.
    /// </summary>
    [Fact]
    public void TheShapeIsHeldToSomethingSensible()
    {
        Assert.Equal(GroovePattern.DefaultAspect, (EngraveOptions.Default with { Aspect = 0f }).Courses, 3);
        Assert.Equal(GroovePattern.DefaultAspect, (EngraveOptions.Default with { Aspect = -5f }).Courses, 3);
        Assert.Equal(GroovePattern.MinimumAspect, (EngraveOptions.Default with { Aspect = 0.01f }).Courses, 3);
        Assert.Equal(GroovePattern.MaximumAspect, (EngraveOptions.Default with { Aspect = 500f }).Courses, 3);
    }
}
