using System.Numerics;
using FastCraft3D.Geometry;
using FastCraft3D.Geometry.Csg;
using Xunit;

namespace FastCraft3D.Tests;

public class CavityFillTests
{
    private static Mesh Box(float x0, float y0, float z0, float x1, float y1, float z1) =>
        MeshTransform.Transformed(Primitives.Box(x1 - x0, y1 - y0, z1 - z0),
            Matrix4x4.CreateTranslation((x0 + x1) / 2, (y0 + y1) / 2, (z0 + z1) / 2));

    /// <summary>A 40 x 40 x 30 box with a 30 x 30 hole down to 5 mm off its bottom, open at the top.</summary>
    private static Mesh Cup() => ManifoldCsg.Subtract(Box(-20, -20, 0, 20, 20, 30), Box(-15, -15, 5, 15, 15, 40))!;

    private const int Detail = 120;

    [Fact]
    public void ACupFillsToItsRimAndBecomesTheSolidItWasCutFrom()
    {
        var cup = Cup();
        var fill = CavityFill.Analyse(cup, Detail);
        Assert.True(fill.HoldsAnything);

        var (part, apart) = fill.Apply(cup, 30f, apart: false)!.Value;

        Assert.Null(apart);
        Assert.True(part.CheckHealth().IsWatertight);
        Assert.Equal(40 * 40 * 30, part.ComputeSignedVolume(), 40 * 40 * 30 * 0.01);
        Assert.Equal(30f, part.ComputeBounds().Max.Z, 2);
    }

    [Fact]
    public void KeptApartTheFillIsExactlyTheSpaceInside()
    {
        var cup = Cup();
        var (part, apart) = CavityFill.Analyse(cup, Detail).Apply(cup, 30f, apart: true)!.Value;

        Assert.Same(cup, part);
        Assert.NotNull(apart);
        Assert.True(apart!.CheckHealth().IsWatertight);
        Assert.Equal(30 * 30 * 25, apart.ComputeSignedVolume(), 30 * 30 * 25 * 0.01);

        // Its sides are the cup's walls, not the grid's steps.
        var b = apart.ComputeBounds();
        Assert.Equal(-15f, b.Min.X, 2);
        Assert.Equal(15f, b.Max.X, 2);
        Assert.Equal(5f, b.Min.Z, 2);
    }

    [Fact]
    public void AFillLevelBelowTheRimStopsThere()
    {
        var cup = Cup();
        var fill = CavityFill.Analyse(cup, Detail);
        var (_, apart) = fill.Apply(cup, 20f, apart: true)!.Value;

        Assert.Equal(30 * 30 * 15, apart!.ComputeSignedVolume(), 30 * 30 * 15 * 0.02);
        Assert.Equal(20f, apart.ComputeBounds().Max.Z, CavityFill.Flush * 2);
        Assert.Equal(30 * 30 * 15, fill.VolumeMm3(20f), 30 * 30 * 15 * 0.1);
    }

    [Theory]
    [InlineData(80)]
    [InlineData(120)]
    [InlineData(200)]
    public void PouredPastTheRimTheFillStandsFlushWithIt(int detail)
    {
        // A post on one wall taller than the rim, as a chimney on a tray: the part's top is the post.
        var cup = ManifoldCsg.Union(Cup(), Box(-20, -20, 0, -16, -16, 40))!;
        var fill = CavityFill.Analyse(cup, detail);

        var (_, apart) = fill.Apply(cup, 40f, apart: true)!.Value;
        Assert.Equal(30f, apart!.ComputeBounds().Max.Z, CavityFill.Flush * 2);

        var (part, _) = fill.Apply(cup, 40f, apart: false)!.Value;
        Assert.True(part.CheckHealth().IsWatertight);
        Assert.Equal(40 * 40 * 30 + 4 * 4 * 10, part.ComputeSignedVolume(), 40 * 40 * 30 * 0.01);
    }

    [Fact]
    public void LiquidRunsOutOfAHoleInTheSideSoTheFillStopsBelowIt()
    {
        // A window in one wall from 15 mm up.
        var cup = ManifoldCsg.Subtract(Cup(), Box(10, -5, 15, 30, 5, 25))!;
        var (_, apart) = CavityFill.Analyse(cup, Detail).Apply(cup, 30f, apart: true)!.Value;

        Assert.InRange(apart!.ComputeBounds().Max.Z, 14f, 15.5f);
    }

    [Fact]
    public void ASealedHollowIsFilledWhole()
    {
        var hollow = ManifoldCsg.Subtract(Box(-20, -20, 0, 20, 20, 30), Box(-10, -10, 8, 10, 10, 22))!;
        var (part, _) = CavityFill.Analyse(hollow, Detail).Apply(hollow, 30f, apart: false)!.Value;

        Assert.Equal(40 * 40 * 30, part.ComputeSignedVolume(), 40 * 40 * 30 * 0.01);
    }

    [Fact]
    public void ATubeStandingOnThePlateHoldsLiquidOnlyWhenThePlateIsAFloor()
    {
        var tube = ManifoldCsg.Subtract(Box(-20, -20, 0, 20, 20, 30), Box(-15, -15, -5, 15, 15, 40))!;

        Assert.False(CavityFill.Analyse(tube, Detail).HoldsAnything);

        var fill = CavityFill.Analyse(tube, Detail, plateFloor: true);
        var (_, apart) = fill.Apply(tube, 30f, apart: true)!.Value;
        Assert.Equal(30 * 30 * 30, apart!.ComputeSignedVolume(), 30 * 30 * 30 * 0.01);
        Assert.Equal(0f, apart.ComputeBounds().Min.Z, 2);
    }

    [Fact]
    public void ACupUpsideDownOrASolidBlockHoldsNothing()
    {
        var upsideDown = MeshTransform.Transformed(Cup(), Matrix4x4.CreateRotationX(MathF.PI) * Matrix4x4.CreateTranslation(0, 0, 30));
        Assert.False(CavityFill.Analyse(upsideDown, Detail).HoldsAnything);

        var block = Box(-10, -10, 0, 10, 10, 10);
        var fill = CavityFill.Analyse(block, Detail);
        Assert.False(fill.HoldsAnything);
        Assert.Null(fill.Apply(block, 10f, apart: false));
    }
}
