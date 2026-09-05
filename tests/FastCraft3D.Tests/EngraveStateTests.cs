using System.Numerics;
using FastCraft3D.Geometry;
using FastCraft3D.Geometry.Engraving;
using FastCraft3D.ViewModels;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>
/// The advice shown beside the settings. This is the part that decides whether what someone
/// exports actually appears on the print, so it is worth pinning down: 0.1 mm looks like a
/// reasonable number and is half a layer.
/// </summary>
public class EngraveStateTests
{
    /// <summary>A wall 10 mm thick, engraved on the top.</summary>
    private static EngraveState OnAWall(EngraveOptions options)
    {
        var mesh = Primitives.Box(100, 100, 10);
        var face = FacePatch.Find(mesh, new Vector3(0, 0, 5), Vector3.UnitZ)!;

        var state = new EngraveState { Options = options };
        state.Pick(mesh, face);
        return state;
    }

    private static EngraveOptions Sensible =>
        new(PatternKind.Brick, Size: 20, GrooveWidth: 1.2f, Depth: 0.6f);

    [Fact]
    public void WithNoFacePickedItAsksForOne()
    {
        var state = new EngraveState();

        Assert.False(state.HasFace);
        Assert.Contains("Click the face", state.Describe());
        Assert.Equal("", state.Advice());
    }

    [Fact]
    public void ItReportsTheFaceSizeAndHowManyGrooves()
    {
        var summary = OnAWall(Sensible).Describe();

        Assert.Contains("100", summary);
        Assert.Contains("grooves", summary);
        Assert.Contains("0.6 mm deep", summary);
    }

    [Fact]
    public void SensibleSettingsGetNoWarningAtAll()
    {
        Assert.Equal("", OnAWall(Sensible).Advice());
    }

    /// <summary>The number in the original request, and the reason the advice exists.</summary>
    [Fact]
    public void ATenthOfAMillimetreIsCalledOutAsTooShallowForFdm()
    {
        var advice = OnAWall(Sensible with { Depth = 0.1f }).Advice();

        Assert.Contains("0.2 mm layers", advice);
        Assert.Contains("resin", advice);
    }

    [Fact]
    public void TwoLayersDeepIsAccepted()
    {
        Assert.Equal("", OnAWall(Sensible with { Depth = EngraveState.FdmMinimumDepth }).Advice());
    }

    [Fact]
    public void ALineThinnerThanTwoNozzlePassesIsCalledOut()
    {
        var advice = OnAWall(Sensible with { GrooveWidth = 0.4f }).Advice();

        Assert.Contains("0.4 mm nozzle", advice);
    }

    [Fact]
    public void CuttingDeeperThanTheWallIsCalledOutFirst()
    {
        // Also under the FDM minimum would be, on a thin enough wall - going through is worse.
        var advice = OnAWall(Sensible with { Depth = 12f }).Advice();

        Assert.Contains("right through", advice);
        Assert.Contains("10", advice); // the thickness it has to work with
    }

    [Fact]
    public void TakingOverHalfTheThicknessIsWorthMentioning()
    {
        var advice = OnAWall(Sensible with { Depth = 6f }).Advice();

        Assert.Contains("half", advice);
    }

    /// <summary>On resin nobody minds 0.1 mm, but nobody wants to lose the wall either.</summary>
    [Fact]
    public void TheThroughCutWarningBeatsTheShallowOne()
    {
        var mesh = Primitives.Box(60, 60, 0.2f);
        var face = FacePatch.Find(mesh, new Vector3(0, 0, 0.1f), Vector3.UnitZ)!;
        var state = new EngraveState { Options = Sensible with { Depth = 0.3f } };
        state.Pick(mesh, face);

        Assert.Contains("right through", state.Advice());
    }

    /// <summary>
    /// A fine pattern on a big face. The lines are kept printably wide on purpose - a thin line
    /// would draw the nozzle warning instead, which is the more useful of the two.
    /// </summary>
    [Fact]
    public void AVeryFinePatternOnALargeFaceIsFlaggedAsSlow()
    {
        var mesh = Primitives.Box(300, 300, 10);
        var face = FacePatch.Find(mesh, new Vector3(0, 0, 5), Vector3.UnitZ)!;
        var state = new EngraveState
        {
            Options = Sensible with { Size = 1f, GrooveWidth = 0.85f, Depth = 0.5f }
        };
        state.Pick(mesh, face);

        Assert.Contains("take a while", state.Advice());
    }

    [Fact]
    public void ClearingForgetsTheFace()
    {
        var state = OnAWall(Sensible);
        state.Clear();

        Assert.False(state.HasFace);
        Assert.Null(state.WorldMesh);
    }
}
