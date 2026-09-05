using System.Runtime.ExceptionServices;
using FastCraft3D.Model;
using FastCraft3D.Render;
using FastCraft3D.ViewModels;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>
/// What the viewport shows. The rendering itself needs a graphics device and is not testable
/// here; what is testable is that the settings hold their values, stay in range, and tell the
/// viewport when they change - which is the part that silently stops working.
/// </summary>
public class ViewSettingsTests
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
    public void ItOpensShowingThePlateAndNothingElseTurnedOn()
    {
        RunSta(() =>
        {
            var model = new MainViewModel();

            Assert.False(model.ShowWireframe);
            Assert.False(model.ShowXray);
            Assert.True(model.ShowPlate);
            Assert.Equal(Scene.PlateSize, model.PlateSize, 3);
        });
    }

    [Fact]
    public void EachSettingTellsTheViewportToRedraw()
    {
        RunSta(() =>
        {
            var model = new MainViewModel();
            int redraws = 0;
            model.ViewChanged += () => redraws++;

            model.ShowWireframe = true;
            model.ShowXray = true;
            model.ShowPlate = false;
            model.PlateSize = 300;

            Assert.Equal(4, redraws);
        });
    }

    /// <summary>Setting a value it already has must not churn the scene.</summary>
    [Fact]
    public void SettingTheSameBedSizeAgainChangesNothing()
    {
        RunSta(() =>
        {
            var model = new MainViewModel { PlateSize = 250 };
            int redraws = 0;
            model.ViewChanged += () => redraws++;

            model.PlateSize = 250;
            model.PlateSize = 250.001f;

            Assert.Equal(0, redraws);
        });
    }

    [Fact]
    public void AnAbsurdBedSizeIsBroughtBackIntoRange()
    {
        RunSta(() =>
        {
            var model = new MainViewModel();

            model.PlateSize = -50;
            Assert.Equal(20f, model.PlateSize, 3);

            model.PlateSize = 99999;
            Assert.Equal(2000f, model.PlateSize, 3);
        });
    }

    [Fact]
    public void TheOfferedBedSizesCoverTheUsualPrinters()
    {
        RunSta(() =>
        {
            var model = new MainViewModel();

            Assert.Contains(200f, model.PlateSizes); // the default, and an Ender's bed
            Assert.Contains(250f, model.PlateSizes); // a Bambu's
            Assert.All(model.PlateSizes, size => Assert.True(size is >= 20f and <= 2000f));
        });
    }

    /// <summary>The plate is rebuilt at whatever size it is asked for, squares and all.</summary>
    [Theory]
    [InlineData(120f)]
    [InlineData(200f)]
    [InlineData(400f)]
    public void ThePlateIsBuiltToTheSizeAskedFor(float size)
    {
        RunSta(() =>
        {
            var elements = BuildPlateVisual.Create(size).ToList();

            Assert.NotEmpty(elements);
        });
    }
}
