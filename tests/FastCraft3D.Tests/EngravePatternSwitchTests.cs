using System.Runtime.ExceptionServices;
using FastCraft3D.Geometry.Engraving;
using FastCraft3D.ViewModels;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>
/// The brick underside sets a hollow's depth; leaving it gives the groove patterns theirs back.
/// A brick pattern chosen after it was set to cut 8.6 mm deep.
/// </summary>
public class EngravePatternSwitchTests
{
    private static void RunSta(Action body)
    {
        ExceptionDispatchInfo? error = null;
        var thread = new Thread(() =>
        {
            try { body(); }
            catch (Exception ex) { error = ExceptionDispatchInfo.Capture(ex); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        error?.Throw();
    }

    [Fact]
    public void LeavingTheBrickUndersideGivesTheGrooveItsDepthBack() => RunSta(() =>
    {
        var model = new MainViewModel();
        model.EngraveDepth = 0.8f;

        model.EngravePattern = PatternKind.StudUnderside;
        Assert.Equal(BrickStuds.BrickHollow, model.EngraveDepth, 0.01f);

        model.EngravePattern = PatternKind.Brick;
        Assert.Equal(0.8f, model.EngraveDepth, 0.01f);
    });

    [Fact]
    public void SwitchingBetweenGroovePatternsKeepsTheDepth() => RunSta(() =>
    {
        var model = new MainViewModel();
        model.EngraveDepth = 1.2f;

        model.EngravePattern = PatternKind.Planks;
        model.EngravePattern = PatternKind.Studs;
        model.EngravePattern = PatternKind.Tiles;

        Assert.Equal(1.2f, model.EngraveDepth, 0.01f);
    });
}
