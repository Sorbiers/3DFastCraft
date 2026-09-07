using FastCraft3D.Geometry.Engraving;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>
/// The bonds: what separates brick from roof tiles, wall tiles and planks.
///
/// All four are courses of rectangles with joints between them, and the differences are small
/// enough to be worth pinning down - a stagger, the weight of the line under each course, and
/// the proportions each one starts at. Laying a roof as brick was what prompted them: it gave
/// 39 x 13 cm tiles where a pantile should be about 30 x 20.
/// </summary>
public class PatternBondTests
{
    private static readonly Rect2 Wall = new(0, 0, 60, 40);

    private static EngraveOptions Of(PatternKind kind) => EngraveOptions.Default with
    {
        Kind = kind, Size = 6f, GrooveWidth = 0.4f, Depth = 0.5f, Raised = true
    };

    /// <summary>Each bond has proportions of its own, and none of them is brick's three to one.</summary>
    [Theory]
    [InlineData(PatternKind.Brick, 3f)]
    [InlineData(PatternKind.RoofTiles, 1.5f)]
    [InlineData(PatternKind.Tiles, 1f)]
    [InlineData(PatternKind.Planks, 8f)]
    public void EachBondStartsAtItsOwnShape(PatternKind kind, float aspect)
    {
        var piece = GroovePattern.Raised(Of(kind), Wall).Rectangles[4];

        Assert.Equal(6f, piece.MaxU - piece.MinU, 3);
        Assert.Equal(6f / aspect, piece.MaxV - piece.MinV, 3);
    }

    /// <summary>
    /// Tiles line up in both directions; brick does not. Two courses of tiles have their joints
    /// at the same places, which is the whole difference between a tiled wall and a brick one.
    /// </summary>
    [Fact]
    public void TilesLineUpWhereBrickStaggers()
    {
        Assert.True(SameColumns(PatternKind.Tiles));
        Assert.False(SameColumns(PatternKind.Brick));
        Assert.False(SameColumns(PatternKind.RoofTiles));
    }

    /// <summary>A tile laps the course below it, and that lap is a heavier line than the joints.</summary>
    [Fact]
    public void ARoofHasAHeavierLineUnderEachCourse()
    {
        var options = Of(PatternKind.RoofTiles) with { Raised = false };

        var bed = Widest(GroovePattern.Build(options, Wall));
        var joint = Widest(GroovePattern.Build(Of(PatternKind.Brick) with { Raised = false }, Wall));

        Assert.True(bed > joint,
            $"the roof's course line is {bed:0.###} mm, no heavier than brick's {joint:0.###}");
    }

    /// <summary>Courses are level by definition; boarding and grain can run either way.</summary>
    [Theory]
    [InlineData(PatternKind.Brick, false)]
    [InlineData(PatternKind.RoofTiles, false)]
    [InlineData(PatternKind.Tiles, false)]
    [InlineData(PatternKind.Planks, true)]
    [InlineData(PatternKind.Wood, true)]
    [InlineData(PatternKind.Stripes, true)]
    public void OnlyTheOnesThatCanTurnDo(PatternKind kind, bool turns) =>
        Assert.Equal(turns, GroovePattern.Turns(kind));

    /// <summary>Every bond still counts what it is about to cut before cutting it.</summary>
    [Theory]
    [InlineData(PatternKind.Brick)]
    [InlineData(PatternKind.RoofTiles)]
    [InlineData(PatternKind.Tiles)]
    [InlineData(PatternKind.Planks)]
    public void TheEstimateIsInTheRightOrder(PatternKind kind)
    {
        var options = Of(kind) with { Raised = false };
        int guessed = GroovePattern.Count(options, Wall);
        int cut = GroovePattern.Build(options, Wall).Rectangles.Count;

        Assert.True(guessed >= cut, $"guessed {guessed} but cut {cut}");
        Assert.True(guessed <= cut * 3, $"guessed {guessed} for {cut} - too far out to be a guide");
    }

    /// <summary>Whether alternate courses put their joints in the same columns.</summary>
    private static bool SameColumns(PatternKind kind)
    {
        var pieces = GroovePattern.Raised(Of(kind), Wall).Rectangles;
        float height = pieces[0].MaxV - pieces[0].MinV;

        var lower = Columns(pieces, pieces[0].MinV);
        var upper = Columns(pieces, pieces[0].MinV + height + 0.4f);

        return upper.Any() && lower.Any()
            && upper.All(u => lower.Any(l => MathF.Abs(l - u) < 0.05f));
    }

    private static List<float> Columns(IReadOnlyList<Rect2> pieces, float v) =>
        pieces.Where(p => MathF.Abs(p.MinV - v) < 0.05f).Select(p => p.MinU).ToList();

    private static float Widest(GrooveSet set) =>
        set.Rectangles.Where(r => r.MaxU - r.MinU > 50).Min(r => r.MaxV - r.MinV);
}
