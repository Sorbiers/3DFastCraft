using FastCraft3D.Render;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>
/// Resizing one way only. About the centre both faces move, which keeps a part where it sits but
/// pushes it into its neighbours; holding the far face still is what you want when the part has
/// to keep meeting the one next to it.
/// </summary>
public class OneSideResizeTests
{
    [Fact]
    public void AboutTheCentreTheDimensionGrowsByTwiceTheDrag()
    {
        // A 20 mm object, handle dragged 5 mm outward: both faces move, so it becomes 30.
        float ratio = GizmoMath.ScaleRatio(20f, 5.0);

        Assert.Equal(30f / 20f, ratio, 5);
    }

    [Fact]
    public void HeldOnOneSideItGrowsByExactlyTheDrag()
    {
        float ratio = GizmoMath.ScaleRatio(20f, 5.0, aboutCentre: false);

        Assert.Equal(25f / 20f, ratio, 5);
    }

    /// <summary>Dragging a handle inward shrinks the object, whichever way it is anchored.</summary>
    [Fact]
    public void DraggingInwardShrinksIt()
    {
        Assert.True(GizmoMath.ScaleRatio(20f, -4.0) < 1f);
        Assert.True(GizmoMath.ScaleRatio(20f, -4.0, aboutCentre: false) < 1f);
    }

    [Fact]
    public void ItCanNeverBeCollapsedOrTurnedInsideOut()
    {
        Assert.True(GizmoMath.ScaleRatio(20f, -1000.0) > 0f);
        Assert.True(GizmoMath.ScaleRatio(20f, -1000.0, aboutCentre: false) > 0f);
    }

    /// <summary>
    /// The point of the whole thing: after the resize, the anchored face is exactly where it was.
    /// </summary>
    [Fact]
    public void TheAnchoredFaceDoesNotMove()
    {
        // A 20 mm cube spanning 0..20, dragged 5 mm out on the far side, held at 0.
        const float anchor = 0f, centre = 10f, halfSize = 10f;
        float ratio = GizmoMath.ScaleRatio(20f, 5.0, aboutCentre: false);

        float movedCentre = GizmoMath.ScaledAbout(anchor, centre, ratio);
        float near = movedCentre - halfSize * ratio;
        float far = movedCentre + halfSize * ratio;

        Assert.Equal(0f, near, 4);   // the held face has not budged
        Assert.Equal(25f, far, 4);   // and the dragged face moved the full 5 mm
    }

    /// <summary>Held at the other end, it grows the other way and the far face stays put.</summary>
    [Fact]
    public void HoldingTheOtherEndGrowsTheOtherWay()
    {
        const float anchor = 20f, centre = 10f, halfSize = 10f;
        float ratio = GizmoMath.ScaleRatio(20f, 5.0, aboutCentre: false);

        float movedCentre = GizmoMath.ScaledAbout(anchor, centre, ratio);

        Assert.Equal(20f, movedCentre + halfSize * ratio, 4);
        Assert.Equal(-5f, movedCentre - halfSize * ratio, 4);
    }

    [Fact]
    public void ScalingAboutAPlaneLeavesThatPlaneAlone()
    {
        Assert.Equal(7f, GizmoMath.ScaledAbout(7f, 7f, 3.5f), 5);
        Assert.Equal(7f, GizmoMath.ScaledAbout(7f, 7f, 0.1f), 5);
    }

    /// <summary>A ratio of one moves nothing, whatever it is measured from.</summary>
    [Fact]
    public void NotResizingMovesNothing()
    {
        Assert.Equal(42f, GizmoMath.ScaledAbout(-3f, 42f, 1f), 5);
        Assert.Equal(1f, GizmoMath.ScaleRatio(20f, 0), 5);
        Assert.Equal(1f, GizmoMath.ScaleRatio(20f, 0, aboutCentre: false), 5);
    }

    /// <summary>Several parts resized together keep their spacing in proportion.</summary>
    [Fact]
    public void PartsKeepTheirRelativeSpacing()
    {
        float ratio = GizmoMath.ScaleRatio(100f, 50.0, aboutCentre: false); // 1.5x, held at 0

        float first = GizmoMath.ScaledAbout(0f, 20f, ratio);
        float second = GizmoMath.ScaledAbout(0f, 60f, ratio);

        Assert.Equal(30f, first, 4);
        Assert.Equal(90f, second, 4);
        Assert.Equal(1.5f, (second - first) / 40f, 4);
    }
}
