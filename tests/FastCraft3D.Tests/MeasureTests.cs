using System.Numerics;
using System.Runtime.ExceptionServices;
using FastCraft3D.Geometry;
using FastCraft3D.ViewModels;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>
/// Snapping a picked point onto a real feature. A measurement taken wherever the pointer happened
/// to land is worth very little; what people want is corner to corner.
/// </summary>
public class VertexSnapTests
{
    private static Mesh Box() => Primitives.Box(20, 20, 20);

    [Fact]
    public void APointNearACornerLandsOnIt()
    {
        var near = new Vector3(9.4f, 9.6f, 9.5f);

        var snapped = VertexSnap.Nearest(Box(), near, radiusMm: 2f);

        Assert.Equal(new Vector3(10, 10, 10), snapped);
    }

    [Fact]
    public void APointOutInTheMiddleOfAFaceIsLeftWhereItIs()
    {
        var middle = new Vector3(0, 0, 10);

        var snapped = VertexSnap.Nearest(Box(), middle, radiusMm: 2f);

        Assert.Equal(middle, snapped);
    }

    /// <summary>Half way along an edge is a real feature too, and often the one wanted.</summary>
    [Fact]
    public void APointNearTheMiddleOfAnEdgeLandsOnIt()
    {
        var near = new Vector3(0.3f, 10f, 10f);

        var snapped = VertexSnap.Nearest(Box(), near, radiusMm: 1.5f);

        Assert.Equal(0f, snapped.X, 3);
        Assert.Equal(10f, snapped.Y, 3);
        Assert.Equal(10f, snapped.Z, 3);
    }

    [Fact]
    public void ATightRadiusSnapsToNothing()
    {
        var near = new Vector3(8f, 8f, 10f);

        Assert.Equal(near, VertexSnap.Nearest(Box(), near, radiusMm: 0.1f));
    }

    [Fact]
    public void SnappingCanBeTurnedOffEntirely()
    {
        var anywhere = new Vector3(9.99f, 9.99f, 10f);

        Assert.Equal(anywhere, VertexSnap.Nearest(Box(), anywhere, radiusMm: 0f));
        Assert.Equal(anywhere, VertexSnap.Nearest(Box(), anywhere, radiusMm: -1f));
    }

    [Fact]
    public void AnEmptyMeshOffersNothingToSnapTo()
    {
        var point = new Vector3(1, 2, 3);

        Assert.Equal(point, VertexSnap.Nearest(new Mesh(), point, 5f));
    }

    /// <summary>Snapping a corner to itself must not move it.</summary>
    [Fact]
    public void ACornerStaysExactlyWhereItIs()
    {
        var corner = new Vector3(10, 10, 10);

        Assert.Equal(corner, VertexSnap.Nearest(Box(), corner, 3f));
    }
}

/// <summary>The measuring mode itself: what a click does, and what it reads back.</summary>
public class MeasureModeTests
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

    private static MainViewModel WithACube()
    {
        var model = new MainViewModel();
        model.InsertCommand.Execute("Cube");
        return model;
    }

    [Fact]
    public void MeasuringNeedsSomethingToMeasure()
    {
        RunSta(() =>
        {
            var model = new MainViewModel();
            Assert.False(model.BeginMeasureCommand.CanExecute(null));

            model.InsertCommand.Execute("Cube");
            Assert.True(model.BeginMeasureCommand.CanExecute(null));
        });
    }

    [Fact]
    public void TwoClicksGiveADistance()
    {
        RunSta(() =>
        {
            var model = WithACube();
            model.BeginMeasureCommand.Execute(null);

            Assert.Contains("first point", model.MeasureSummary);

            model.TakeMeasurePoint(new Vector3(0, 0, 0));
            Assert.Contains("second point", model.MeasureSummary);
            Assert.False(model.HasMeasurement);

            model.TakeMeasurePoint(new Vector3(30, 40, 0));

            Assert.True(model.HasMeasurement);
            Assert.Contains("50", model.MeasureSummary); // 3-4-5
        });
    }

    /// <summary>A single number hides which way the gap runs, so the axes are given too.</summary>
    [Fact]
    public void TheReadingBreaksDownByAxis()
    {
        RunSta(() =>
        {
            var model = WithACube();
            model.BeginMeasureCommand.Execute(null);

            model.TakeMeasurePoint(Vector3.Zero);
            model.TakeMeasurePoint(new Vector3(3, 4, 12));

            Assert.Contains("X 3", model.MeasureSummary);
            Assert.Contains("Y 4", model.MeasureSummary);
            Assert.Contains("Z 12", model.MeasureSummary);
        });
    }

    /// <summary>A third click starts a fresh measurement rather than doing nothing.</summary>
    [Fact]
    public void AThirdClickStartsAgain()
    {
        RunSta(() =>
        {
            var model = WithACube();
            model.BeginMeasureCommand.Execute(null);

            model.TakeMeasurePoint(Vector3.Zero);
            model.TakeMeasurePoint(new Vector3(10, 0, 0));
            model.TakeMeasurePoint(new Vector3(5, 5, 5));

            Assert.False(model.HasMeasurement);
            Assert.Equal(new Vector3(5, 5, 5), model.MeasureFrom);
            Assert.Null(model.MeasureTo);
        });
    }

    [Fact]
    public void LeavingTheModeClearsTheTape()
    {
        RunSta(() =>
        {
            var model = WithACube();
            model.BeginMeasureCommand.Execute(null);
            model.TakeMeasurePoint(Vector3.Zero);
            model.TakeMeasurePoint(new Vector3(10, 0, 0));

            model.CancelMeasureCommand.Execute(null);

            Assert.False(model.IsMeasureMode);
            Assert.Null(model.MeasureFrom);
            Assert.Null(model.MeasureTo);
        });
    }

    [Fact]
    public void AClickOutsideTheModeIsIgnored()
    {
        RunSta(() =>
        {
            var model = WithACube();

            model.TakeMeasurePoint(Vector3.Zero);

            Assert.Null(model.MeasureFrom);
        });
    }

    /// <summary>Every mode claims the click, so starting one has to end the others.</summary>
    [Fact]
    public void MeasuringEndsSplittingAndEngraving()
    {
        RunSta(() =>
        {
            var model = WithACube();

            model.BeginEngraveCommand.Execute(null);
            model.BeginMeasureCommand.Execute(null);
            Assert.False(model.IsEngraveMode);

            model.BeginSplitCommand.Execute(null);
            model.BeginMeasureCommand.Execute(null);
            Assert.False(model.IsSplitMode);
            Assert.True(model.IsMeasureMode);
        });
    }

    /// <summary>
    /// Either end can be moved after it is down. Before this the only way to correct a point
    /// was a third click, which threw the measurement away and started another.
    /// </summary>
    [Fact]
    public void EitherEndCanBeMovedWithoutStartingAgain()
    {
        RunSta(() =>
        {
            var model = WithACube();
            model.BeginMeasureCommand.Execute(null);
            model.TakeMeasurePoint(Vector3.Zero);
            model.TakeMeasurePoint(new Vector3(10, 0, 0));

            model.MoveMeasurePoint(second: true, new Vector3(30, 40, 0));

            Assert.True(model.HasMeasurement);
            Assert.Equal(Vector3.Zero, model.MeasureFrom);
            Assert.Contains("50", model.MeasureSummary);

            model.MoveMeasurePoint(second: false, new Vector3(30, 0, 0));

            Assert.Equal(new Vector3(30, 0, 0), model.MeasureFrom);
            Assert.Contains("40", model.MeasureSummary);
        });
    }

    [Fact]
    public void DraggingOutsideTheModeChangesNothing()
    {
        RunSta(() =>
        {
            var model = WithACube();

            model.MoveMeasurePoint(second: false, new Vector3(5, 5, 5));

            Assert.Null(model.MeasureFrom);
        });
    }

    [Fact]
    public void TheViewportIsToldWheneverTheTapeMoves()
    {
        RunSta(() =>
        {
            var model = WithACube();
            int redraws = 0;
            model.MeasureChanged += () => redraws++;

            model.BeginMeasureCommand.Execute(null);
            model.TakeMeasurePoint(Vector3.Zero);
            model.TakeMeasurePoint(Vector3.One);

            Assert.Equal(3, redraws); // entering the mode, then each point
        });
    }
}
