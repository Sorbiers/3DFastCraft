using FastCraft3D.Generators;
using FastCraft3D.Generators.Boxes;
using FastCraft3D.Geometry;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>What only the open box can be asked: that it is the size typed and hollow by as much as its walls say.</summary>
public class OpenBoxTests
{
    private static readonly OpenBox Box = new();

    private static Mesh Made(OpenBox.Settings settings)
    {
        var made = Box.Make(settings, Printer.Default);
        Assert.Null(made.Refusal);
        return Assert.Single(made.Parts).Mesh;
    }

    [Fact]
    public void ASquareCorneredBoxIsItsOutsideLessItsCavity()
    {
        var s = Box.Default with { Width = 50, Depth = 30, Height = 20, Wall = 2, Floor = 1.5f, Radius = 0 };
        var mesh = Made(s);

        var size = mesh.ComputeBounds().Size;
        Assert.Equal((50f, 30f, 20f), (size.X, size.Y, size.Z));

        double expected = 50.0 * 30 * 20 - 46.0 * 26 * 18.5;
        Assert.Equal(expected, mesh.ComputeSignedVolume(), 1);
    }

    [Fact]
    public void RoundedCornersTakeOffWhatARoundedRectangleDoes()
    {
        var s = Box.Default with { Width = 50, Depth = 30, Height = 20, Wall = 2, Floor = 1.5f, Radius = 6 };
        var mesh = Made(s);

        static double Area(double w, double d, double r) => w * d - (4 - Math.PI) * r * r;
        double expected = Area(50, 30, 6) * 20 - Area(46, 26, 4) * 18.5;

        // The corners are faceted, which takes a little off the true curve.
        Assert.InRange(mesh.ComputeSignedVolume(), expected * 0.995, expected * 1.0001);
    }

    [Fact]
    public void ARadiusOfHalfTheSideMakesARoundCupThatIsStillClosed()
    {
        var mesh = Made(Box.Default with { Width = 40, Depth = 40, Radius = 20, Wall = 2 });

        Assert.True(mesh.CheckHealth().IsWatertight, mesh.CheckHealth().Describe());
    }

    [Theory]
    [InlineData(10f, 40f, 30f, 5f, 1f, 3f, "leave no room inside a box 10 mm wide")]
    [InlineData(60f, 10f, 30f, 5f, 1f, 3f, "leave no room inside a box 10 mm deep")]
    [InlineData(60f, 40f, 10f, 1.6f, 10f, 3f, "fills a box 10 mm high")]
    [InlineData(60f, 40f, 30f, 1.6f, 1.2f, 25f, "more than half the box across")]
    public void ABoxThatCannotBeMadeSaysWhy(float width, float depth, float height, float wall, float floor, float radius, string reason)
    {
        var made = Box.Make(Box.Default with { Width = width, Depth = depth, Height = height, Wall = wall, Floor = floor, Radius = radius }, Printer.Default);

        Assert.Empty(made.Parts);
        Assert.Contains(reason, made.Refusal);
    }

    [Fact]
    public void AWallThinnerThanTheNozzleIsMadeWithAWarning()
    {
        var made = Box.Make(Box.Default with { Wall = 0.4f }, Printer.Default with { Nozzle = 0.6f });

        Assert.Single(made.Parts);
        Assert.Contains(made.Notes, n => n.Contains("thinner than one nozzle"));
    }
}
