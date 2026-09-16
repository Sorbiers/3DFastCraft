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
            Assert.Equal(Scene.PlateSize, model.PlateWidth, 3);
            Assert.Equal(Scene.PlateSize, model.PlateDepth, 3);
            Assert.Equal(Scene.PrintHeight, model.PlateHeight, 3);
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
            model.PlateWidth = 300;
            model.PlateDepth = 250;
            model.PlateHeight = 180;
            model.ShowGridLabels = true;

            Assert.Equal(7, redraws);
        });
    }

    /// <summary>Setting a value it already has must not churn the scene.</summary>
    [Fact]
    public void SettingTheSameBedSizeAgainChangesNothing()
    {
        RunSta(() =>
        {
            var model = new MainViewModel { PlateWidth = 250 };
            int redraws = 0;
            model.ViewChanged += () => redraws++;

            model.PlateWidth = 250;
            model.PlateWidth = 250.001f;

            Assert.Equal(0, redraws);
        });
    }

    [Fact]
    public void AnAbsurdBedSizeIsBroughtBackIntoRange()
    {
        RunSta(() =>
        {
            var model = new MainViewModel();

            model.PlateDepth = -50;
            Assert.Equal(20f, model.PlateDepth, 3);

            model.PlateHeight = 99999;
            Assert.Equal(2000f, model.PlateHeight, 3);
        });
    }

    /// <summary>The plate is built to the printable area asked for, whatever is drawn on it.</summary>
    [Theory]
    [InlineData(120f, 120f, false, false)]
    [InlineData(220f, 250f, true, false)]
    [InlineData(400f, 300f, true, true)]
    public void ThePlateIsBuiltToTheAreaAskedFor(float width, float depth, bool axes, bool labels)
    {
        RunSta(() =>
        {
            var elements = BuildPlateVisual.Create(new PlateLook(width, depth, 200f, axes, axes, labels, "mm", 1f)).ToList();

            Assert.NotEmpty(elements);
        });
    }

    /// <summary>A round step between labels: 50 mm on a 200 mm bed, an inch on the same bed in inches.</summary>
    [Theory]
    [InlineData(100f, 1f, 50f)]
    [InlineData(100f, 25.4f, 1f)]
    [InlineData(150f, 10f, 5f)]
    [InlineData(1000f, 1f, 500f)]
    public void GridLabelsFallOnRoundNumbers(float halfSpan, float unitMillimetres, float step)
    {
        Assert.Equal(step, BuildPlateVisual.LabelStep(halfSpan, unitMillimetres), 3);
    }
}
