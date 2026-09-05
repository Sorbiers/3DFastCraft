using System.Windows;
using FastCraft3D.Render;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>
/// The drag rules behind the on-screen handles. These decide what a mouse movement does to a
/// model, so they are worth pinning down independently of anything that needs a GPU.
/// </summary>
public class GizmoMathTests
{
    private static readonly Vector Right = new(1, 0);
    private static readonly Vector Up = new(0, -1); // screen Y grows downward

    [Fact]
    public void DraggingAlongTheAxisMovesByTheScreenDistanceDividedByScale()
    {
        // 40 px of travel where 4 px is one millimetre.
        double mm = GizmoMath.MillimetresAlongAxis(new Vector(40, 0), Right, pixelsPerMm: 4);

        Assert.Equal(10, mm, 6);
    }

    [Fact]
    public void DraggingAcrossTheAxisDoesNothing()
    {
        // Perpendicular travel has no component along the axis, so the object must not drift.
        double mm = GizmoMath.MillimetresAlongAxis(new Vector(0, 40), Right, pixelsPerMm: 4);

        Assert.Equal(0, mm, 6);
    }

    [Fact]
    public void DraggingBackwardsAlongTheAxisMovesNegative()
    {
        double mm = GizmoMath.MillimetresAlongAxis(new Vector(-20, 0), Right, pixelsPerMm: 4);

        Assert.Equal(-5, mm, 6);
    }

    [Fact]
    public void DiagonalDragCountsOnlyTheAxisComponent()
    {
        // A 45 degree drag up and to the right, projected onto the vertical screen axis.
        double mm = GizmoMath.MillimetresAlongAxis(new Vector(30, -30), Up, pixelsPerMm: 3);

        Assert.Equal(10, mm, 6);
    }

    [Fact]
    public void AZeroScaleFactorCannotDivideByZero()
    {
        Assert.Equal(0, GizmoMath.MillimetresAlongAxis(new Vector(50, 50), Right, pixelsPerMm: 0), 6);
    }

    /// <summary>Resizing works about the centre, so both faces move.</summary>
    [Fact]
    public void PullingAHandleOutFiveMillimetresGrowsTheDimensionByTen()
    {
        float ratio = GizmoMath.ScaleRatio(startExtent: 20f, millimetresOutward: 5);

        Assert.Equal(30f / 20f, ratio, 5);
    }

    [Fact]
    public void PushingAHandleInwardShrinks()
    {
        float ratio = GizmoMath.ScaleRatio(startExtent: 20f, millimetresOutward: -5);

        Assert.Equal(10f / 20f, ratio, 5);
    }

    /// <summary>
    /// Dragging a handle far enough past the centre would otherwise produce a negative scale,
    /// which mirrors the object without asking - and a zero scale would collapse the mesh.
    /// </summary>
    [Fact]
    public void ScaleNeverGoesToZeroOrNegative()
    {
        float ratio = GizmoMath.ScaleRatio(startExtent: 20f, millimetresOutward: -500);

        Assert.True(ratio > 0f, "scale collapsed or flipped the object");
        Assert.Equal(0.01f, ratio, 5);
    }

    [Fact]
    public void ScaleOfADegenerateDimensionIsANoOp()
    {
        Assert.Equal(1f, GizmoMath.ScaleRatio(startExtent: 0f, millimetresOutward: 10), 5);
    }

    /// <summary>
    /// Screen Y points down, so sweeping the pointer anticlockwise on screen must read as a
    /// positive rotation for a ring facing the viewer.
    /// </summary>
    [Fact]
    public void AnticlockwiseSweepTowardsTheViewerIsPositive()
    {
        var centre = new Point(100, 100);
        var start = new Point(150, 100);   // due right
        var quarter = new Point(100, 50);  // straight up on screen

        double degrees = GizmoMath.RotationDegrees(centre, start, quarter, facingSign: 1, snap: false);

        Assert.Equal(90, degrees, 4);
    }

    [Fact]
    public void ARingFacingAwayTurnsTheOtherWay()
    {
        var centre = new Point(100, 100);
        var start = new Point(150, 100);
        var quarter = new Point(100, 50);

        double towards = GizmoMath.RotationDegrees(centre, start, quarter, facingSign: 1, snap: false);
        double away = GizmoMath.RotationDegrees(centre, start, quarter, facingSign: -1, snap: false);

        Assert.Equal(-towards, away, 4);
    }

    [Fact]
    public void SnappingRoundsToTheNearestStep()
    {
        var centre = new Point(0, 0);
        var start = new Point(100, 0);
        // About 20 degrees anticlockwise on screen.
        var current = new Point(100 * Math.Cos(-0.35), 100 * Math.Sin(-0.35));

        double free = GizmoMath.RotationDegrees(centre, start, current, 1, snap: false);
        double snapped = GizmoMath.RotationDegrees(centre, start, current, 1, snap: true, snapStep: 15);

        Assert.InRange(free, 19, 21);
        Assert.Equal(15, snapped, 4);
    }

    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(90f, 90f)]
    [InlineData(180f, 180f)]
    [InlineData(190f, -170f)]
    [InlineData(360f, 0f)]
    [InlineData(450f, 90f)]
    [InlineData(-190f, 170f)]
    [InlineData(-360f, 0f)]
    public void AnglesFoldIntoAReadableRange(float input, float expected)
    {
        Assert.Equal(expected, GizmoMath.NormaliseDegrees(input), 3);
    }

    [Fact]
    public void RepeatedRotationsDoNotDriftOutOfRange()
    {
        float angle = 0;
        for (int i = 0; i < 50; i++)
            angle = GizmoMath.NormaliseDegrees(angle + 15f);

        Assert.InRange(angle, -180f, 180f);
    }
}
