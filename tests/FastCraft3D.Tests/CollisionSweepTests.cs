using System.Numerics;
using FastCraft3D.Geometry;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>
/// Stopping a drag where one part meets another. Sliding a piece up against its neighbour until
/// it stops is how things get assembled, and it beats typing coordinates for it.
/// </summary>
public class CollisionSweepTests
{
    /// <summary>A 10 mm cube with its near corner at the given point.</summary>
    private static Bounds Cube(float x, float y = 0, float z = 0, float size = 10) =>
        new(new Vector3(x, y, z), new Vector3(x + size, y + size, z + size));

    [Fact]
    public void WithNothingInTheWayTheFullTravelIsAllowed()
    {
        float allowed = CollisionSweep.Limit([Cube(0)], [], Axis.X, 25f);

        Assert.Equal(25f, allowed, 4);
    }

    [Fact]
    public void ItStopsWhereTheTwoMeet()
    {
        // A cube at 0..10 sliding towards one at 30..40: 20 mm of clear air between them.
        float allowed = CollisionSweep.Limit([Cube(0)], [Cube(30)], Axis.X, 50f);

        Assert.Equal(20f, allowed, 2);
        Assert.True(allowed < 20f, "a hair of clearance should be left, not a shared face");
    }

    [Fact]
    public void AShortMoveIsNotStretchedToTheObstacle()
    {
        float allowed = CollisionSweep.Limit([Cube(0)], [Cube(30)], Axis.X, 5f);

        Assert.Equal(5f, allowed, 4);
    }

    [Fact]
    public void MovingAwayIsNeverBlocked()
    {
        float allowed = CollisionSweep.Limit([Cube(0)], [Cube(30)], Axis.X, -50f);

        Assert.Equal(-50f, allowed, 4);
    }

    /// <summary>Something off to one side can never be run into, however far the part slides.</summary>
    [Fact]
    public void SomethingThatMissesSidewaysDoesNotBlock()
    {
        var pastIt = Cube(30, y: 40);

        Assert.Equal(50f, CollisionSweep.Limit([Cube(0)], [pastIt], Axis.X, 50f), 4);
    }

    /// <summary>Touching exactly counts as touching: a box flush against another cannot advance.</summary>
    [Fact]
    public void APartAlreadyTouchingCannotAdvance()
    {
        Assert.Equal(0f, CollisionSweep.Limit([Cube(0)], [Cube(10)], Axis.X, 20f), 4);
    }

    /// <summary>
    /// Two parts already inside one another impose no limit, either way.
    ///
    /// That state cannot be reached with this turned on: parts are overlapped deliberately on the
    /// way to a boolean and the toggle goes on afterwards. There is no contact to stop at, and
    /// holding the object fast until it is switched off again would be pure obstruction.
    /// </summary>
    [Fact]
    public void AnExistingOverlapDoesNotTrapThePart()
    {
        Assert.Equal(20f, CollisionSweep.Limit([Cube(0)], [Cube(5)], Axis.X, 20f), 4);
        Assert.Equal(-20f, CollisionSweep.Limit([Cube(0)], [Cube(5)], Axis.X, -20f), 4);
    }

    [Fact]
    public void TheNearestObstacleIsTheOneThatCounts()
    {
        float allowed = CollisionSweep.Limit(
            [Cube(0)], [Cube(60), Cube(25), Cube(90)], Axis.X, 100f);

        Assert.Equal(15f, allowed, 2);
    }

    /// <summary>A group moves together, so whichever part meets something first stops them all.</summary>
    [Fact]
    public void AGroupIsStoppedByWhicheverPartArrivesFirst()
    {
        var group = new[] { Cube(0), Cube(0, y: 20) };
        var wall = Cube(25, y: 20);

        Assert.Equal(15f, CollisionSweep.Limit(group, [wall], Axis.X, 100f), 2);
    }

    [Fact]
    public void ItWorksOnEveryAxis()
    {
        Assert.Equal(20f, CollisionSweep.Limit([Cube(0)], [Cube(0, 30, 0)], Axis.Y, 50f), 2);
        Assert.Equal(20f, CollisionSweep.Limit([Cube(0)], [Cube(0, 0, 30)], Axis.Z, 50f), 2);
    }

    [Fact]
    public void ItNeverReturnsMoreThanWasAskedForOrTheOtherWay()
    {
        foreach (float travel in new[] { 3f, -3f, 80f, -80f })
        {
            float allowed = CollisionSweep.Limit([Cube(0)], [Cube(30), Cube(-30)], Axis.X, travel);

            Assert.True(Math.Abs(allowed) <= Math.Abs(travel) + 1e-4f);
            Assert.True(allowed == 0 || Math.Sign(allowed) == Math.Sign(travel),
                "a blocked drag must stop, never reverse");
        }
    }

    [Fact]
    public void EmptyBoundsAreIgnoredRatherThanBlockingEverything()
    {
        Assert.Equal(50f, CollisionSweep.Limit([Cube(0)], [Bounds.Empty], Axis.X, 50f), 4);
        Assert.Equal(50f, CollisionSweep.Limit([Bounds.Empty], [Cube(5)], Axis.X, 50f), 4);
    }
}
