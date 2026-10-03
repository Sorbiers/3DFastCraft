using System.Runtime.ExceptionServices;
using FastCraft3D.Geometry.Engraving;
using FastCraft3D.ViewModels;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>
/// The brick underside sets a hollow's depth; leaving it gives the depth set before back.
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
    public void LeavingTheBrickUndersideGivesTheDepthBack() => RunSta(() =>
    {
        var model = new MainViewModel();
        model.EngravePattern = PatternKind.Studs;
        model.EngraveDepth = 0.8f;

        model.EngravePattern = PatternKind.StudUnderside;
        Assert.Equal(BrickStuds.BrickHollow, model.EngraveDepth, 0.01f);

        model.EngravePattern = PatternKind.Studs;
        Assert.Equal(0.8f, model.EngraveDepth, 0.01f);
    });

    /// <summary>
    /// The tool was Engrave, with brick, tiles, planks, wood grain and stripes as well; those are
    /// the Texture tool's now, and the studs are all it offers.
    /// </summary>
    [Fact]
    public void TheStudsToolOffersStudsAndTheBrickUndersideAlone() => RunSta(() =>
    {
        var model = new MainViewModel();
        Assert.Equal([PatternKind.Studs, PatternKind.StudUnderside], model.EngravePatterns.Select(p => p.Kind));
    });
}
