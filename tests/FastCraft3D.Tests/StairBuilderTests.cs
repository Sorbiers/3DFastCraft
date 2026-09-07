using FastCraft3D.Geometry;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>
/// A flight of steps as one solid.
///
/// Built by hand it was a dozen boxes, each meeting the next on part of a face - the contact the
/// boolean is worst at, and the one that turned up the plane-epsilon defect. A single closed
/// profile swept sideways has no seams inside it at all.
/// </summary>
public class StairBuilderTests
{
    [Theory]
    [InlineData(26.5f, 34.8f, 14f, 12)]
    [InlineData(30f, 45f, 20f, 15)]
    [InlineData(10f, 10f, 10f, 3)]
    [InlineData(50f, 60f, 12f, 25)]
    public void AFlightComesOutAsOneSoundSolid(float rise, float run, float width, int steps)
    {
        var flight = StairBuilder.Build(rise, run, width, steps);
        var health = flight.CheckHealth();

        Assert.True(health.IsWatertight, health.Describe());
        Assert.True(health.SignedVolume > 0, "the flight came out inside out");
    }

    [Fact]
    public void ItIsTheSizeAskedFor()
    {
        var bounds = StairBuilder.Build(26.5f, 34.8f, 14f, 12).ComputeBounds();

        Assert.Equal(34.8f, bounds.Max.X - bounds.Min.X, 3);
        Assert.Equal(14f, bounds.Max.Y - bounds.Min.Y, 3);
        Assert.Equal(26.5f, bounds.Max.Z - bounds.Min.Z, 3);
    }

    /// <summary>Half the volume of the box round it, give or take one step.</summary>
    [Fact]
    public void ItHoldsAboutHalfItsBox()
    {
        double volume = StairBuilder.Build(26.5f, 34.8f, 14f, 12).ComputeSignedVolume();
        double box = 26.5 * 34.8 * 14;

        Assert.InRange(volume, box * 0.45, box * 0.58);
    }

    /// <summary>
    /// The arithmetic nobody should be doing on paper: 17.7 cm risers on 25 cm treads at 1:87 is
    /// a stair; the same flight in half the steps is a ladder, and it says so.
    /// </summary>
    [Fact]
    public void ItSaysWhetherAnyoneCouldClimbIt()
    {
        // The house's own flight: 26.5 mm over 13 treads of 2.9 mm, at 1:87.
        var good = StairBuilder.Measure(26.5f, 37.7f, 13, 87f);
        Assert.InRange(good.RiserMm, 150f, 190f);
        Assert.InRange(good.GoingMm, 240f, 320f);
        Assert.True(good.IsClimbable);
        Assert.Equal("", good.Advice);

        var ladder = StairBuilder.Measure(26.5f, 37.7f, 6, 87f);
        Assert.False(ladder.IsClimbable);
        Assert.Contains("too tall", ladder.Advice);
    }

    [Fact]
    public void NothingSillyGetsThrough()
    {
        Assert.Equal(0, StairBuilder.Build(0, 10, 10, 5).TriangleCount);
        Assert.Equal(0, StairBuilder.Build(10, 10, 0, 5).TriangleCount);
        Assert.True(StairBuilder.Build(10, 10, 10, 0).CheckHealth().IsWatertight);
        Assert.True(StairBuilder.Build(10, 10, 10, 10_000).CheckHealth().IsWatertight);
    }
}
