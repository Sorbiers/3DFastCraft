using System.Numerics;
using FastCraft3D.Geometry;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>
/// Splitting with a plane that is not axis-aligned, which is what the tilt rings produce.
/// </summary>
public class PlaneSplitTests
{
    [Fact]
    public void AnAxisAlignedSplitHalvesTheSolid()
    {
        var cube = Primitives.Box(20, 20, 20);

        var (front, back) = PlaneSplit.Split(cube, Vector3.UnitZ, 0f, SplitKeep.Both);

        Assert.NotNull(front);
        Assert.NotNull(back);
        Assert.Equal(4000.0, front!.ComputeSignedVolume(), 1);
        Assert.Equal(4000.0, back!.ComputeSignedVolume(), 1);
    }

    /// <summary>A diagonal plane through the centre still gives two capped, closed halves.</summary>
    [Fact]
    public void ATiltedSplitStillProducesTwoClosedHalves()
    {
        var cube = Primitives.Box(20, 20, 20);
        var normal = Vector3.Normalize(new Vector3(1, 0, 1));

        var (front, back) = PlaneSplit.Split(cube, normal, 0f, SplitKeep.Both);

        Assert.NotNull(front);
        Assert.NotNull(back);
        Assert.True(front!.CheckHealth().IsWatertight, front.CheckHealth().Describe());
        Assert.True(back!.CheckHealth().IsWatertight, back.CheckHealth().Describe());
        Assert.Equal(8000.0, front.ComputeSignedVolume() + back.ComputeSignedVolume(), 1);
    }

    [Fact]
    public void TheNormalDecidesWhichHalfIsTheFront()
    {
        var cube = Primitives.Box(20, 20, 20);

        // Splitting above centre leaves a thin slice on the side the normal points to.
        var (front, back) = PlaneSplit.Split(cube, Vector3.UnitZ, 5f, SplitKeep.Both);

        Assert.Equal(20 * 20 * 5.0, front!.ComputeSignedVolume(), 1);
        Assert.Equal(20 * 20 * 15.0, back!.ComputeSignedVolume(), 1);
    }

    /// <summary>
    /// The half-space box is centred on the solid, not on the world origin, so a part far out
    /// on the plate is still cut rather than missed sideways.
    /// </summary>
    [Fact]
    public void ASolidFarFromTheOriginIsStillSplit()
    {
        var cube = MeshTransform.Transformed(
            Primitives.Box(20, 20, 20), Matrix4x4.CreateTranslation(400, 300, 10));

        var (front, back) = PlaneSplit.Split(cube, Vector3.UnitZ, 10f, SplitKeep.Both);

        Assert.NotNull(front);
        Assert.NotNull(back);
        Assert.Equal(4000.0, front!.ComputeSignedVolume(), 1);
        Assert.Equal(4000.0, back!.ComputeSignedVolume(), 1);
    }

    [Fact]
    public void APlaneThatMissesLeavesOneSideEmpty()
    {
        var cube = Primitives.Box(20, 20, 20);

        var (front, back) = PlaneSplit.Split(cube, Vector3.UnitZ, 500f, SplitKeep.Both);

        Assert.Null(front); // nothing lies above the plane
        Assert.NotNull(back);
        Assert.Equal(8000.0, back!.ComputeSignedVolume(), 1);
    }

    [Fact]
    public void KeepingOneSideProducesOnlyThatSide()
    {
        var cube = Primitives.Box(20, 20, 20);

        var (frontOnly, noBack) = PlaneSplit.Split(cube, Vector3.UnitZ, 0f, SplitKeep.Front);
        var (noFront, backOnly) = PlaneSplit.Split(cube, Vector3.UnitZ, 0f, SplitKeep.Back);

        Assert.NotNull(frontOnly);
        Assert.Null(noBack);
        Assert.Null(noFront);
        Assert.NotNull(backOnly);
    }

    [Fact]
    public void TheOffsetRangeSpansTheSolidAlongTheNormal()
    {
        var bounds = Primitives.Box(20, 20, 40).ComputeBounds();

        var (min, max) = PlaneSplit.OffsetRange(bounds, Vector3.UnitZ);

        Assert.Equal(-20f, min, 3);
        Assert.Equal(20f, max, 3);
    }

    [Fact]
    public void TheOffsetRangeFollowsATiltedNormal()
    {
        var bounds = Primitives.Box(20, 20, 20).ComputeBounds();
        var normal = Vector3.Normalize(new Vector3(1, 1, 1));

        var (min, max) = PlaneSplit.OffsetRange(bounds, normal);

        // The cube's diagonal extent along (1,1,1)/sqrt(3) is 10*3/sqrt(3) each way.
        float expected = 30f / MathF.Sqrt(3f);
        Assert.Equal(-expected, min, 3);
        Assert.Equal(expected, max, 3);
    }

    [Fact]
    public void RotationBetweenCarriesOneDirectionOntoAnother()
    {
        var m = MeshTransform.RotationBetween(Vector3.UnitZ, Vector3.UnitX);

        var turned = Vector3.Transform(Vector3.UnitZ, m);

        Assert.Equal(1f, turned.X, 4);
        Assert.Equal(0f, turned.Y, 4);
        Assert.Equal(0f, turned.Z, 4);
    }

    /// <summary>Opposed directions have a degenerate cross product and need the fallback axis.</summary>
    [Fact]
    public void RotationBetweenHandlesOpposedDirections()
    {
        var m = MeshTransform.RotationBetween(Vector3.UnitZ, -Vector3.UnitZ);

        var turned = Vector3.Transform(Vector3.UnitZ, m);

        Assert.Equal(-1f, turned.Z, 4);
    }

    [Fact]
    public void RotationBetweenIdenticalDirectionsIsIdentity()
    {
        Assert.Equal(Matrix4x4.Identity, MeshTransform.RotationBetween(Vector3.UnitY, Vector3.UnitY));
    }
}
