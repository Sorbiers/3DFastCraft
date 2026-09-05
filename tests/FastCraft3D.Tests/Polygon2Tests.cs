using System.Numerics;
using FastCraft3D.Geometry;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>
/// Turning outlines into triangles. The case that matters is the hole: the letter O is two loops
/// and no amount of splitting the outer one produces its middle.
/// </summary>
public class Polygon2Tests
{
    private static List<Vector2> Square(float x, float y, float size) =>
    [
        new(x, y), new(x + size, y), new(x + size, y + size), new(x, y + size)
    ];

    /// <summary>Total area of the triangles produced, which must match the shape's own.</summary>
    private static float AreaOf((List<Vector2> Points, List<int> Triangles) result)
    {
        float total = 0;
        for (int i = 0; i + 2 < result.Triangles.Count; i += 3)
        {
            var a = result.Points[result.Triangles[i]];
            var b = result.Points[result.Triangles[i + 1]];
            var c = result.Points[result.Triangles[i + 2]];
            total += ((b.X - a.X) * (c.Y - a.Y) - (c.X - a.X) * (b.Y - a.Y)) / 2;
        }

        return total;
    }

    [Fact]
    public void ASquareBecomesTwoTriangles()
    {
        var result = Polygon2.Triangulate(Square(0, 0, 10), []);

        Assert.Equal(2, result.Triangles.Count / 3);
        Assert.Equal(100f, AreaOf(result), 2);
    }

    [Fact]
    public void EveryTriangleComesOutAnticlockwise()
    {
        var result = Polygon2.Triangulate(Square(0, 0, 10), []);

        for (int i = 0; i + 2 < result.Triangles.Count; i += 3)
        {
            var a = result.Points[result.Triangles[i]];
            var b = result.Points[result.Triangles[i + 1]];
            var c = result.Points[result.Triangles[i + 2]];

            Assert.True((b.X - a.X) * (c.Y - a.Y) - (c.X - a.X) * (b.Y - a.Y) > 0);
        }
    }

    /// <summary>An outline given the wrong way round is corrected rather than inverted.</summary>
    [Fact]
    public void AClockwiseOutlineIsTurnedTheRightWay()
    {
        var backwards = Square(0, 0, 10);
        backwards.Reverse();

        Assert.Equal(100f, AreaOf(Polygon2.Triangulate(backwards, [])), 2);
    }

    /// <summary>The whole point: the middle of an O has to be a hole, not filled in.</summary>
    [Fact]
    public void AHoleIsLeftEmpty()
    {
        var result = Polygon2.Triangulate(Square(0, 0, 10), [Square(3, 3, 4)]);

        Assert.Equal(100f - 16f, AreaOf(result), 1);
    }

    [Fact]
    public void AHoleGivenTheWrongWayRoundIsStillAHole()
    {
        var hole = Square(3, 3, 4);
        hole.Reverse();

        Assert.Equal(84f, AreaOf(Polygon2.Triangulate(Square(0, 0, 10), [hole])), 1);
    }

    /// <summary>A B has two holes, so one is not enough.</summary>
    [Fact]
    public void SeveralHolesInOneOutlineAllSurvive()
    {
        var result = Polygon2.Triangulate(
            Square(0, 0, 20), [Square(2, 2, 4), Square(12, 12, 5)]);

        Assert.Equal(400f - 16f - 25f, AreaOf(result), 1);
    }

    [Fact]
    public void AConcaveOutlineIsHandled()
    {
        // An L: the corner at (5,5) is reflex, which plain fan triangulation gets wrong.
        List<Vector2> shape =
        [
            new(0, 0), new(10, 0), new(10, 5), new(5, 5), new(5, 10), new(0, 10)
        ];

        Assert.Equal(75f, AreaOf(Polygon2.Triangulate(shape, [])), 2);
        Assert.Equal(4, Polygon2.Triangulate(shape, []).Triangles.Count / 3);
    }

    [Fact]
    public void AManySidedOutlineIsHandled()
    {
        var circle = new List<Vector2>();
        for (int i = 0; i < 64; i++)
        {
            float angle = i / 64f * MathF.Tau;
            circle.Add(new Vector2(MathF.Cos(angle) * 10, MathF.Sin(angle) * 10));
        }

        var result = Polygon2.Triangulate(circle, []);

        Assert.Equal(62, result.Triangles.Count / 3);
        Assert.Equal(MathF.PI * 100, AreaOf(result), 0);
    }

    [Fact]
    public void SignedAreaTellsWhichWayALoopRuns()
    {
        var anticlockwise = Square(0, 0, 10);
        var clockwise = Square(0, 0, 10);
        clockwise.Reverse();

        Assert.True(Polygon2.SignedArea(anticlockwise) > 0);
        Assert.True(Polygon2.SignedArea(clockwise) < 0);
    }

    [Fact]
    public void NotEnoughPointsProducesNothingRatherThanThrowing()
    {
        Assert.Empty(Polygon2.Triangulate([], []).Triangles);
        Assert.Empty(Polygon2.Triangulate([new Vector2(0, 0), new Vector2(1, 1)], []).Triangles);
    }

    /// <summary>A hole too small to have an area is ignored rather than corrupting the outline.</summary>
    [Fact]
    public void ADegenerateHoleIsSkipped()
    {
        var result = Polygon2.Triangulate(Square(0, 0, 10), [[new Vector2(5, 5), new Vector2(5, 6)]]);

        Assert.Equal(100f, AreaOf(result), 2);
    }
}
