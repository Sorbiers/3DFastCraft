using System.Runtime.ExceptionServices;
using FastCraft3D.ViewModels;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>
/// Escape puts down whichever tool has the object.
///
/// Every tool had a Cancel button in its own panel and none of them answered the key everybody
/// reaches for first. Kept in one place rather than wired up per tool, which is how the seventh
/// one would have been missed.
/// </summary>
public class CancelToolTests
{
    private static void WithModel(Action<MainViewModel> body)
    {
        ExceptionDispatchInfo? error = null;

        var thread = new Thread(() =>
        {
            try { body(new MainViewModel()); }
            catch (Exception ex) { error = ExceptionDispatchInfo.Capture(ex); }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        error?.Throw();
    }

    [Fact]
    public void TheSplitPlaneIsPutAway() => WithModel(model =>
    {
        model.IsSplitMode = true;

        Assert.True(model.CancelActiveTool());
        Assert.False(model.IsSplitMode);
    });

    [Fact]
    public void SoIsTheTape() => WithModel(model =>
    {
        model.IsMeasureMode = true;

        Assert.True(model.CancelActiveTool());
        Assert.False(model.IsMeasureMode);
    });

    [Fact]
    public void SoIsAWaitingSubtraction() => WithModel(model =>
    {
        model.IsSubtractMode = true;

        Assert.True(model.CancelActiveTool());
        Assert.False(model.IsSubtractMode);
    });

    [Fact]
    public void SoIsLetteringAndTheFaceItWasWaitingFor() => WithModel(model =>
    {
        model.IsEmbossMode = true;

        Assert.True(model.CancelActiveTool());
        Assert.False(model.IsEmbossMode);
    });

    [Fact]
    public void SoIsAPatternWaitingForAFace() => WithModel(model =>
    {
        model.IsEngraveMode = true;

        Assert.True(model.CancelActiveTool());
        Assert.False(model.IsEngraveMode);
    });

    [Fact]
    public void SoIsPickingAFaceToLayOn() => WithModel(model =>
    {
        model.IsLayMode = true;

        Assert.True(model.CancelActiveTool());
        Assert.False(model.IsLayMode);
    });

    /// <summary>
    /// With no tool in hand it says so rather than doing something instead. The key press then
    /// carries on to whatever else wanted it, which is what stops Escape becoming a key that
    /// quietly swallows itself.
    /// </summary>
    [Fact]
    public void WithNoToolInHandItSaysSo() => WithModel(model =>
        Assert.False(model.CancelActiveTool()));

    /// <summary>One press, one tool. Only one is ever in hand at a time.</summary>
    [Fact]
    public void TheOneInHandIsTheOneThatGoes() => WithModel(model =>
    {
        model.IsSplitMode = true;

        model.CancelActiveTool();

        Assert.False(model.IsSplitMode);
        Assert.False(model.CancelActiveTool());
    });
}
