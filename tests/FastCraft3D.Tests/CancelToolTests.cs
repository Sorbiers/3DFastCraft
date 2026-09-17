using System.Runtime.ExceptionServices;
using System.Windows.Threading;
using FastCraft3D.View;
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
[Collection("Tool panels")]
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

    [Fact]
    public void SoIsACutWaitingToBeExtrudedDown() => WithModel(model =>
    {
        model.IsExtrudeMode = true;

        Assert.True(model.CancelActiveTool());
        Assert.False(model.IsExtrudeMode);
    });

    [Fact]
    public void SoAreConnectorsBetweenTwoParts() => WithModel(model =>
    {
        model.IsConnectMode = true;

        Assert.True(model.CancelActiveTool());
        Assert.False(model.IsConnectMode);
    });

    /// <summary>
    /// The tools that ask for numbers in the side panel answer it too, and the panel closes with
    /// nothing applied - the window sends Escape here wherever the caret happens to be.
    /// </summary>
    [Fact]
    public void SoIsAToolAskingForNumbersInThePanel() => WithModel(model =>
    {
        ToolPanel.Host = model.ShowPanel;
        var panel = new ToolPanel { Title = "Asking" };
        bool cancelled = false;

        // Sent while the panel is up, as the key handler does.
        Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() => cancelled = model.CancelActiveTool()));

        Assert.NotEqual(true, panel.ShowDialog());
        Assert.True(cancelled);
        Assert.False(model.IsToolInHand);
        Assert.Null(model.OpenPanel);
    });

    /// <summary>Nothing is left holding the object: every mode the window greys out for is down.</summary>
    [Fact]
    public void AfterEscapeNothingHasTheObject() => WithModel(model =>
    {
        foreach (var open in new Action[]
                 {
                     () => model.IsSplitMode = true, () => model.IsMeasureMode = true,
                     () => model.IsSubtractMode = true, () => model.IsEmbossMode = true,
                     () => model.IsEngraveMode = true, () => model.IsLayMode = true,
                     () => model.IsExtrudeMode = true, () => model.IsConnectMode = true
                 })
        {
            open();
            Assert.True(model.IsToolInHand, "the tool did not take the object");
            Assert.True(model.CancelActiveTool());
            Assert.False(model.IsToolInHand, "something still had the object after Escape");
        }
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
