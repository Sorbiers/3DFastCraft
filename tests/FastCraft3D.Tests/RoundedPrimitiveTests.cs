using System.Numerics;
using FastCraft3D.Geometry;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>
/// Rounded shapes are generated from their parameters, so they can be checked against the exact
/// analytic volume rather than merely "looking right".
/// </summary>
public class RoundedPrimitiveTests
{
    /// <summary>Inner box, six slabs, twelve quarter-cylinders and eight octants of a sphere.</summary>
    private static double ExactRoundedBoxVolume(double s, double r)
    {
        double inner = s - 2 * r;
        return inner * inner * inner
             + 2 * r * (3 * inner * inner)
             + Math.PI * r * r * (3 * inner)
             + 4.0 / 3.0 * Math.PI * r * r * r;
    }

    [Fact]
    public void ARoundedBoxIsWatertightAndFacesOutwards()
    {
        var health = RoundedPrimitives.RoundedBox(20, 20, 20, 4).CheckHealth();

        Assert.True(health.IsWatertight, health.Describe());
        Assert.False(health.IsInsideOut);
    }

    [Fact]
    public void ARoundedBoxKeepsItsNominalSize()
    {
        var bounds = RoundedPrimitives.RoundedBox(20, 30, 40, 5).ComputeBounds();

        Assert.Equal(20f, bounds.Size.X, 3);
        Assert.Equal(30f, bounds.Size.Y, 3);
        Assert.Equal(40f, bounds.Size.Z, 3);
        Assert.True(bounds.Center.Length() < 1e-3f, "the rounded box is not centred");
    }

    [Fact]
    public void ARoundedBoxApproachesItsAnalyticVolume()
    {
        double exact = ExactRoundedBoxVolume(20, 4);

        double coarse = RoundedPrimitives.RoundedBox(20, 20, 20, 4, arcSteps: 2).ComputeSignedVolume();
        double fine = RoundedPrimitives.RoundedBox(20, 20, 20, 4, arcSteps: 16).ComputeSignedVolume();

        // Faceting always cuts inside the true surface, so both under-read - and more segments
        // must get closer.
        Assert.True(fine < exact, "a faceted solid cannot exceed the smooth one");
        Assert.True(exact - fine < exact - coarse, "more segments did not improve the result");
        Assert.InRange(fine, exact * 0.995, exact); // within half a percent at 16 steps
    }

    [Fact]
    public void AZeroRadiusGivesAnOrdinaryBox()
    {
        var rounded = RoundedPrimitives.RoundedBox(20, 20, 20, 0);

        Assert.Equal(8000.0, rounded.ComputeSignedVolume(), 3);
        Assert.Equal(12, rounded.TriangleCount);
    }

    /// <summary>At the maximum radius a cube becomes a sphere.</summary>
    [Fact]
    public void RoundingACubeAllTheWayGivesASphere()
    {
        var mesh = RoundedPrimitives.RoundedBox(20, 20, 20, 10, arcSteps: 16);
        var health = mesh.CheckHealth();

        double sphere = 4.0 / 3.0 * Math.PI * 1000;
        Assert.True(health.IsWatertight, health.Describe());
        Assert.InRange(health.SignedVolume, sphere * 0.99, sphere);
    }

    [Fact]
    public void TheRadiusIsClampedToWhatTheShapeAllows()
    {
        var huge = RoundedPrimitives.RoundedBox(20, 20, 20, 500, arcSteps: 16);

        // Clamped to half the smallest side, which is the sphere case.
        double sphere = 4.0 / 3.0 * Math.PI * 1000;
        Assert.InRange(huge.ComputeSignedVolume(), sphere * 0.99, sphere);
        Assert.True(huge.CheckHealth().IsWatertight);
    }

    [Fact]
    public void ARoundedCylinderIsWatertightAndKeepsItsSize()
    {
        var mesh = RoundedPrimitives.RoundedCylinder(10, 20, 3);
        var health = mesh.CheckHealth();
        var bounds = mesh.ComputeBounds();

        Assert.True(health.IsWatertight, health.Describe());
        Assert.False(health.IsInsideOut);
        Assert.Equal(20f, bounds.Size.Z, 3);
        Assert.Equal(20f, bounds.Size.X, 2);
    }

    [Fact]
    public void ARoundedCylinderHoldsLessThanASharpOne()
    {
        double sharp = Primitives.Prism(10, 20, 32).ComputeSignedVolume();
        double rounded = RoundedPrimitives.RoundedCylinder(10, 20, 3).ComputeSignedVolume();

        Assert.True(rounded < sharp, "rounding the rims must remove material");
        Assert.True(rounded > sharp * 0.85, "rounding removed far more than the rims");
    }

    [Fact]
    public void OnlyShapesThatCanBeRoundedSaySo()
    {
        Assert.True(RoundedPrimitives.Supports(PrimitiveKind.Cube));
        Assert.True(RoundedPrimitives.Supports(PrimitiveKind.Cylinder));
        Assert.False(RoundedPrimitives.Supports(PrimitiveKind.Sphere));
        Assert.False(RoundedPrimitives.Supports(PrimitiveKind.Torus));
        Assert.False(RoundedPrimitives.Supports(PrimitiveKind.Wedge));
    }

    [Fact]
    public void TheMaximumRadiusIsHalfTheSmallestDimensionInvolved()
    {
        var box = new Vector3(20, 30, 10);

        // All edges: limited by the shortest side of all.
        Assert.Equal(5f, RoundedPrimitives.MaximumRadius(PrimitiveKind.Cube, box, RoundEdges.All), 3);

        // Upright edges only: limited by the footprint, not the height.
        Assert.Equal(10f, RoundedPrimitives.MaximumRadius(PrimitiveKind.Cube, box, RoundEdges.Sides), 3);

        // Top only: limited by the height alone.
        Assert.Equal(5f, RoundedPrimitives.MaximumRadius(PrimitiveKind.Cube, box, RoundEdges.Top), 3);

        Assert.Equal(4f, RoundedPrimitives.MaximumRadius(
            PrimitiveKind.Cylinder, new Vector3(20, 20, 8), RoundEdges.All), 3);

        // Nothing selected rounds nothing.
        Assert.Equal(0f, RoundedPrimitives.MaximumRadius(PrimitiveKind.Cube, box, RoundEdges.None), 3);
    }

    /// <summary>Every combination has to close, including the awkward ones where a fillet
    /// runs into a sharp edge.</summary>
    [Theory]
    [InlineData(RoundEdges.Top)]
    [InlineData(RoundEdges.Bottom)]
    [InlineData(RoundEdges.Sides)]
    [InlineData(RoundEdges.Top | RoundEdges.Bottom)]
    [InlineData(RoundEdges.Top | RoundEdges.Sides)]
    [InlineData(RoundEdges.Bottom | RoundEdges.Sides)]
    [InlineData(RoundEdges.All)]
    public void EveryEdgeCombinationIsWatertight(RoundEdges edges)
    {
        var health = RoundedPrimitives.RoundedBox(20, 30, 40, 4, edges).CheckHealth();

        Assert.True(health.IsWatertight, $"{edges}: {health.Describe()}");
        Assert.False(health.IsInsideOut, $"{edges} came out inside-out");
    }

    [Theory]
    [InlineData(RoundEdges.Top)]
    [InlineData(RoundEdges.Bottom)]
    [InlineData(RoundEdges.Top | RoundEdges.Bottom)]
    public void EveryCylinderRimCombinationIsWatertight(RoundEdges edges)
    {
        var health = RoundedPrimitives.RoundedCylinder(10, 20, 3, edges).CheckHealth();

        Assert.True(health.IsWatertight, $"{edges}: {health.Describe()}");
        Assert.False(health.IsInsideOut);
    }

    /// <summary>Rounding fewer edges must leave more material behind.</summary>
    [Fact]
    public void RoundingFewerEdgesRemovesLessMaterial()
    {
        double sharp = Primitives.Box(20, 20, 20).ComputeSignedVolume();
        double topOnly = RoundedPrimitives.RoundedBox(20, 20, 20, 4, RoundEdges.Top).ComputeSignedVolume();
        double topAndBottom = RoundedPrimitives.RoundedBox(20, 20, 20, 4, RoundEdges.Top | RoundEdges.Bottom)
            .ComputeSignedVolume();
        double all = RoundedPrimitives.RoundedBox(20, 20, 20, 4, RoundEdges.All).ComputeSignedVolume();

        Assert.True(topOnly < sharp);
        Assert.True(topAndBottom < topOnly);
        Assert.True(all < topAndBottom);
    }

    /// <summary>Rounding only the top must leave the bottom face perfectly flat and full size.</summary>
    [Fact]
    public void RoundingTheTopLeavesTheBottomSquare()
    {
        var mesh = RoundedPrimitives.RoundedBox(20, 20, 20, 5, RoundEdges.Top);
        var bounds = mesh.ComputeBounds();

        Assert.Equal(20f, bounds.Size.X, 2);
        Assert.Equal(20f, bounds.Size.Z, 2);

        // Every corner of the bottom face is still present at full extent.
        Assert.Contains(mesh.Positions, p =>
            MathF.Abs(p.X - 10f) < 1e-3f && MathF.Abs(p.Y - 10f) < 1e-3f && MathF.Abs(p.Z + 10f) < 1e-3f);
    }

    /// <summary>And rounding only the sides must leave both flat faces square.</summary>
    [Fact]
    public void RoundingTheSidesLeavesTopAndBottomSquare()
    {
        var mesh = RoundedPrimitives.RoundedBox(20, 20, 20, 5, RoundEdges.Sides);

        // The upright edges are gone, so no corner reaches (10, 10) in plan...
        Assert.DoesNotContain(mesh.Positions, p =>
            MathF.Abs(p.X - 10f) < 1e-3f && MathF.Abs(p.Y - 10f) < 1e-3f);

        // ...while the straight run of the side still reaches full width, at full height. There
        // is no vertex at the side's midpoint: a straight run is one quad between arc ends.
        Assert.Contains(mesh.Positions, p =>
            MathF.Abs(p.X - 10f) < 1e-3f && MathF.Abs(p.Z - 10f) < 1e-3f);
        Assert.Contains(mesh.Positions, p =>
            MathF.Abs(p.X - 10f) < 1e-3f && MathF.Abs(p.Z + 10f) < 1e-3f);
    }

    [Fact]
    public void ARoundedShapeStillWorksAsBooleanInput()
    {
        var block = RoundedPrimitives.RoundedBox(20, 20, 20, 4);
        var drill = Primitives.Prism(5, 40, 32);

        var health = FastCraft3D.Geometry.Csg.CsgSolid.Subtract(block, drill).CheckHealth();

        Assert.True(health.IsWatertight, health.Describe());
    }
}
