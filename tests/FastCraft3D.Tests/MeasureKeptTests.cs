using System.Numerics;
using System.Runtime.ExceptionServices;
using FastCraft3D.ViewModels;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>Measurements kept on screen can still be adjusted, point by point.</summary>
public class MeasureKeptTests
{
    private static void Stage(Action<MainViewModel> body)
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
    public void APointOfAKeptMeasurementMovesWhenDragged() => Stage(model =>
    {
        model.BeginMeasureCommand.Execute(null);
        model.TakeMeasurePoint(new Vector3(0, 0, 0));
        model.TakeMeasurePoint(new Vector3(10, 0, 0));
        model.TakeMeasurePoint(new Vector3(0, 5, 0)); // starts the next; the first is kept

        Assert.Single(model.KeptMeasures);

        model.MoveMeasurePoint(0, 1, new Vector3(20, 0, 0));

        Assert.Equal(new Vector3(20, 0, 0), model.KeptMeasures[0][1]);
        Assert.Equal(new Vector3(0, 5, 0), model.MeasurePoints[0]);
    });
}
