using System.Numerics;
using FastCraft3D.Geometry;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>
/// Typing the split plane's angle rather than dragging it round.
///
/// The tilt rings snap to fifteen degrees, so a cut at 22.5 could not be set at all. Two angles
/// describe the facing completely; the third a rotation usually carries would be a turn about
/// the plane's own normal, which leaves the plane exactly where it was.
/// </summary>
public class PlaneTiltTests
{
    private const double Radian = Math.PI / 180.0;

    private static void Same(Vector3 expected, Vector3 actual)
    {
        float apart = Vector3.Distance(Vector3.Normalize(expected), Vector3.Normalize(actual));
        Assert.True(apart < 1e-5f, $"expected {expected}, got {actual}");
    }

    private static Vector3 Turned(Vector3 v, Vector3 axis, double degrees) =>
        Vector3.Transform(v, Matrix4x4.CreateFromAxisAngle(axis, (float)(degrees * Radian)));

    [Theory]
    [InlineData(Axis.X)]
    [InlineData(Axis.Y)]
    [InlineData(Axis.Z)]
    public void NoTiltLeavesThePlaneOnTheAxisItWasPutOn(Axis axis) =>
        Same(PlaneSplit.NormalFor(axis), PlaneTilt.Normal(axis, 0, 0));

    [Theory]
    [InlineData(Axis.X)]
    [InlineData(Axis.Y)]
    [InlineData(Axis.Z)]
    public void APlaneOnItsAxisReadsAsNoTiltAtAll(Axis axis)
    {
        var (first, second) = PlaneTilt.Angles(axis, PlaneSplit.NormalFor(axis));

        Assert.Equal(0, first, 6);
        Assert.Equal(0, second, 6);
    }

    /// <summary>
    /// The same turn the ring of that colour makes, so a typed 30 and a dragged 30 are the same
    /// cut. Both angles are checked: it is the pair that has to agree, not just one of them.
    /// </summary>
    [Fact]
    public void TheAnglesTurnThePlaneTheWayTheRingsDo()
    {
        Same(Turned(Vector3.UnitZ, Vector3.UnitX, 30), PlaneTilt.Normal(Axis.Z, 30, 0));
        Same(Turned(Vector3.UnitZ, Vector3.UnitY, 30), PlaneTilt.Normal(Axis.Z, 0, 30));
    }

    /// <summary>The first angle is applied first, and the second turns what it left.</summary>
    [Fact]
    public void TheSecondAngleTurnsWhatTheFirstOneLeft()
    {
        Vector3 byHand = Turned(Turned(Vector3.UnitZ, Vector3.UnitX, 30), Vector3.UnitY, 40);

        Same(byHand, PlaneTilt.Normal(Axis.Z, 30, 40));
    }

    [Fact]
    public void AQuarterTurnLaysThePlaneOnTheNextAxisRound()
    {
        Same(Vector3.UnitX, PlaneTilt.Normal(Axis.Z, 0, 90));
        Same(-Vector3.UnitY, PlaneTilt.Normal(Axis.Z, 90, 0));
    }

    /// <summary>
    /// Whatever the rings have done to the plane, the boxes can say it. This is the property the
    /// panel leans on: the angles are read back off the facing rather than kept beside it, so
    /// there is only ever one answer to which way the plane faces.
    /// </summary>
    [Fact]
    public void EveryFacingReadsBackAsTheAnglesItWasMadeFrom()
    {
        foreach (var axis in new[] { Axis.X, Axis.Y, Axis.Z })
        {
            for (int a = -80; a <= 80; a += 20)
            {
                for (int b = -170; b <= 180; b += 25)
                {
                    var (first, second) = PlaneTilt.Angles(axis, PlaneTilt.Normal(axis, a, b));

                    Assert.Equal(a, first, 3);
                    Assert.Equal(b, second, 3);
                }
            }
        }
    }

    /// <summary>
    /// A plane tilted 120 degrees is the same plane as one tilted 60 the other way about, so
    /// that is what the boxes show: where the plane is now, not how it was got there.
    /// </summary>
    [Fact]
    public void ATiltPastAQuarterTurnReadsAsTheShorterWayRound()
    {
        var (first, second) = PlaneTilt.Angles(Axis.Z, PlaneTilt.Normal(Axis.Z, 120, 0));

        Assert.Equal(60, first, 3);
        Assert.Equal(180, Math.Abs(second), 3);
    }

    [Fact]
    public void TheAxesAreNamedInTheOrderTheAnglesApplyToThem()
    {
        Assert.Equal(("X", "Y"), PlaneTilt.NamesFor(Axis.Z));
        Assert.Equal(("Y", "Z"), PlaneTilt.NamesFor(Axis.X));
        Assert.Equal(("Z", "X"), PlaneTilt.NamesFor(Axis.Y));
    }

    [Fact]
    public void EachAxisIsNamedForTheOneItTurnsThePlaneAbout()
    {
        foreach (var axis in new[] { Axis.X, Axis.Y, Axis.Z })
        {
            var (first, second) = PlaneTilt.AxesFor(axis);
            var (firstName, secondName) = PlaneTilt.NamesFor(axis);

            Assert.Equal(first, PlaneSplit.NormalFor(Enum.Parse<Axis>(firstName)));
            Assert.Equal(second, PlaneSplit.NormalFor(Enum.Parse<Axis>(secondName)));
        }
    }

    /// <summary>A box with nothing turned in it should not be showing a minus sign.</summary>
    [Fact]
    public void NeitherAngleComesBackAsNegativeZero()
    {
        var (first, second) = PlaneTilt.Angles(Axis.Z, Vector3.UnitZ);

        Assert.False(double.IsNegative(first));
        Assert.False(double.IsNegative(second));
    }

    /// <summary>A facing that arrived from somewhere else - a picked face - still reads.</summary>
    [Fact]
    public void AFacingFromAnywhereElseStillReadsAsAPairOfAngles()
    {
        var picked = Vector3.Normalize(new Vector3(0.3f, -0.5f, 0.81f));

        var (first, second) = PlaneTilt.Angles(Axis.Z, picked);

        Same(picked, PlaneTilt.Normal(Axis.Z, first, second));
    }
}
