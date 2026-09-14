using System.ComponentModel;
using System.Runtime.ExceptionServices;
using FastCraft3D.ViewModels;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>
/// Every tool greys out the rest of the window while it has the object, and gives it back after.
///
/// The ribbon follows <see cref="MainViewModel.NothingInHand"/>, and only hears about it when a
/// mode says so. Engrave, Emboss and Lay never did: the ribbon stayed live behind them, and
/// switching from Engrave straight to Emboss left it live for good.
/// </summary>
public class ToolInHandTests
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

    [Theory]
    [InlineData(nameof(MainViewModel.IsSplitMode))]
    [InlineData(nameof(MainViewModel.IsSubtractMode))]
    [InlineData(nameof(MainViewModel.IsMeasureMode))]
    [InlineData(nameof(MainViewModel.IsEngraveMode))]
    [InlineData(nameof(MainViewModel.IsEmbossMode))]
    [InlineData(nameof(MainViewModel.IsLayMode))]
    [InlineData(nameof(MainViewModel.IsExtrudeMode))]
    [InlineData(nameof(MainViewModel.IsConnectMode))]
    public void EveryToolTellsTheWindowToStandDownAndToComeBack(string mode) => WithModel(model =>
    {
        var property = typeof(MainViewModel).GetProperty(mode)!;
        var heard = new List<string?>();
        ((INotifyPropertyChanged)model).PropertyChanged += (_, e) => heard.Add(e.PropertyName);

        property.SetValue(model, true);
        Assert.Contains(nameof(MainViewModel.NothingInHand), heard);
        Assert.False(model.NothingInHand);

        heard.Clear();
        property.SetValue(model, false);
        Assert.Contains(nameof(MainViewModel.NothingInHand), heard);
        Assert.True(model.NothingInHand);
    });

    [Fact]
    public void GoingFromEngraveStraightToEmbossKeepsTheWindowStoodDown() => WithModel(model =>
    {
        model.IsEngraveMode = true;
        model.IsEngraveMode = false;
        model.IsEmbossMode = true;

        Assert.False(model.NothingInHand);

        model.IsEmbossMode = false;
        Assert.True(model.NothingInHand);
    });
}
