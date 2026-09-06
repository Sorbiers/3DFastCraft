using System.Numerics;
using System.Runtime.ExceptionServices;
using FastCraft3D.Geometry;
using FastCraft3D.Model;
using FastCraft3D.ViewModels;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>
/// Where an object says it is, against where it actually is.
///
/// Anything built from geometry that is already in build-plate coordinates - a group, a boolean,
/// a split, an import - used to arrive reading 0, 0, 0 whatever corner of the bed it was really
/// on. The shape was drawn in the right place; the position boxes simply described somewhere
/// else, so typing a coordinate measured from the wrong origin.
/// </summary>
public class PlateCoordinateTests
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

    /// <summary>Two cubes well away from the middle of the bed, both selected.</summary>
    private static MainViewModel TwoCubesOffCentre()
    {
        var model = new MainViewModel();

        model.InsertCommand.Execute("Cube");
        model.Scene.Objects[0].Position = new Vector3(40, 30, 10);

        model.InsertCommand.Execute("Cube");
        model.Scene.Objects[1].Position = new Vector3(60, 30, 10);

        foreach (var o in model.Scene.Objects) o.IsSelected = true;
        model.RefreshSelection();

        return model;
    }

    /// <summary>What the position boxes say has to be where the shape actually is.</summary>
    private static void PositionMatchesTheGeometry(SceneObject o)
    {
        var middle = o.WorldBounds.Center;

        Assert.True((o.Position - middle).Length() < 1e-3f,
            $"{o.Name} says it is at {o.Position} but sits at {middle}");
    }

    [Fact]
    public void GroupingKeepsTheCoordinatesOfWhereItIs()
    {
        RunSta(() =>
        {
            var model = TwoCubesOffCentre();

            model.GroupCommand.Execute(null);

            var group = Assert.Single(model.Scene.Objects);
            PositionMatchesTheGeometry(group);

            // Both cubes spanned 30 to 70 in X and sat at 30 in Y.
            Assert.Equal(50f, group.Position.X, 2);
            Assert.Equal(30f, group.Position.Y, 2);
        });
    }

    [Fact]
    public void GroupingDoesNotMoveAnything()
    {
        RunSta(() =>
        {
            var model = TwoCubesOffCentre();
            var before = model.Scene.ComputeBounds();

            model.GroupCommand.Execute(null);
            var after = model.Scene.ComputeBounds();

            Assert.Equal(before.Min, after.Min);
            Assert.Equal(before.Max, after.Max);
        });
    }

    /// <summary>
    /// The point of it: after grouping, typing a coordinate takes the object there rather than
    /// nudging it from wherever it happened to be.
    /// </summary>
    [Fact]
    public void TypingACoordinateTakesTheGroupThere()
    {
        RunSta(() =>
        {
            var model = TwoCubesOffCentre();
            model.GroupCommand.Execute(null);

            var group = model.Scene.Objects[0];
            group.PositionX = 0f;

            Assert.Equal(0f, group.WorldBounds.Center.X, 2);
        });
    }

    [Fact]
    public void UngroupingGivesEachPieceItsOwnCoordinates()
    {
        RunSta(() =>
        {
            var model = TwoCubesOffCentre();
            model.GroupCommand.Execute(null);
            model.UngroupCommand.Execute(null);

            Assert.Equal(2, model.Scene.Objects.Count);
            Assert.All(model.Scene.Objects, PositionMatchesTheGeometry);
        });
    }

    [Fact]
    public void ABooleanResultKnowsWhereItIs()
    {
        RunSta(() =>
        {
            var model = new MainViewModel();

            model.InsertCommand.Execute("Cube");
            model.Scene.Objects[0].Position = new Vector3(40, 30, 10);

            model.InsertCommand.Execute("Sphere");
            model.Scene.Objects[1].Position = new Vector3(48, 30, 10);

            foreach (var o in model.Scene.Objects) o.IsSelected = true;
            model.RefreshSelection();

            model.BooleanCommand.Execute("Subtract");

            // The boolean runs on a background thread; wait for it to land.
            for (int i = 0; i < 200 && model.Scene.Objects.Count > 1; i++) Thread.Sleep(25);

            var result = Assert.Single(model.Scene.Objects);
            PositionMatchesTheGeometry(result);
        });
    }

    /// <summary>Nothing that was already in step is disturbed by putting it back in step.</summary>
    [Fact]
    public void AFreshPrimitiveIsAlreadyInStep()
    {
        RunSta(() =>
        {
            var model = new MainViewModel();
            model.InsertCommand.Execute("Cylinder");

            var o = model.Scene.Objects[0];
            var was = o.Position;

            o.Centred();

            Assert.Equal(was, o.Position);
            PositionMatchesTheGeometry(o);
        });
    }

    /// <summary>Centring must leave a turned, stretched object exactly where it was drawn.</summary>
    [Fact]
    public void CentringDoesNotMoveATurnedObject()
    {
        var mesh = MeshTransform.Transformed(
            Primitives.Box(20, 10, 6), Matrix4x4.CreateTranslation(35, -12, 4));

        var o = new SceneObject("Off centre", mesh)
        {
            Position = new Vector3(5, 5, 5),
            Rotation = new Vector3(0, 0, 30),
            Scale = new Vector3(2, 1, 1)
        };

        var before = o.ToWorldMesh().Positions.ToList();
        o.Centred();
        var after = o.ToWorldMesh().Positions.ToList();

        Assert.Equal(before.Count, after.Count);
        for (int i = 0; i < before.Count; i++)
            Assert.True((before[i] - after[i]).Length() < 1e-3f,
                $"a corner moved from {before[i]} to {after[i]}");

        PositionMatchesTheGeometry(o);
    }
}
