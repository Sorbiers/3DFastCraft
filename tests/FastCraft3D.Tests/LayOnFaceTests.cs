using System.Numerics;
using System.Runtime.ExceptionServices;
using FastCraft3D.Geometry;
using FastCraft3D.Model;
using FastCraft3D.ViewModels;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>Bringing one direction onto another by the shortest way round.</summary>
public class TurnFromToTests
{
    private static void Lands(Vector3 from, Vector3 to)
    {
        var turned = Vector3.TransformNormal(
            Vector3.Normalize(from), MeshTransform.TurnFromTo(from, to));

        Assert.True((turned - Vector3.Normalize(to)).Length() < 1e-4f,
            $"{from} ended up at {turned} rather than {to}");
    }

    [Theory]
    [InlineData(0f, 0f, 1f)]
    [InlineData(1f, 0f, 0f)]
    [InlineData(0f, 1f, 0f)]
    [InlineData(0.6f, 0.3f, 0.74f)]
    [InlineData(-2f, 5f, -1f)]
    public void ADirectionEndsUpPointingDown(float x, float y, float z)
    {
        Lands(new Vector3(x, y, z), -Vector3.UnitZ);
    }

    /// <summary>Already there: nothing to do, and nothing done.</summary>
    [Fact]
    public void SomethingAlreadyFacingTheRightWayIsLeftAlone()
    {
        Assert.Equal(Matrix4x4.Identity, MeshTransform.TurnFromTo(-Vector3.UnitZ, -Vector3.UnitZ));
    }

    /// <summary>
    /// Exactly the wrong way round has no unique answer - every axis across it turns it over -
    /// so any of them will do, as long as it arrives.
    /// </summary>
    [Fact]
    public void SomethingFacingExactlyTheWrongWayStillTurnsOver()
    {
        Lands(Vector3.UnitZ, -Vector3.UnitZ);
        Lands(Vector3.UnitX, -Vector3.UnitX);
    }

    /// <summary>The shortest way round: nothing turns further than it has to.</summary>
    [Fact]
    public void TheTurnIsTheShortOneNotTheLongOne()
    {
        // Ten degrees off vertical should be a ten degree turn, not three hundred and fifty.
        var nearlyDown = Vector3.Normalize(new Vector3(0.17f, 0, -1f));
        var turn = MeshTransform.TurnFromTo(nearlyDown, -Vector3.UnitZ);

        var across = Vector3.TransformNormal(Vector3.UnitY, turn);
        Assert.True(Vector3.Dot(across, Vector3.UnitY) > 0.98f, "it took the long way round");
    }
}

/// <summary>
/// Tipping an object over so a picked face stands on the plate - the printing question of which
/// way up to lay a part, answered by pointing at the face that should be down.
/// </summary>
public class LayOnFaceTests
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

    private static (MainViewModel Model, SceneObject Part) With(string kind)
    {
        var model = new MainViewModel();
        model.InsertCommand.Execute(kind);
        model.BeginLayCommand.Execute(null);

        return (model, model.Scene.Objects[0]);
    }

    /// <summary>Whether that direction now points straight down.</summary>
    private static void FacesDown(SceneObject part, Vector3 wasFacing)
    {
        var now = Vector3.TransformNormal(wasFacing, MeshTransform.Rotation(part.Rotation));

        Assert.True(now.Z < -0.999f, $"{wasFacing} ended up facing {now}");
    }

    [Fact]
    public void ThePickedFaceEndsUpAgainstThePlate()
    {
        RunSta(() =>
        {
            var (model, cube) = With("Cube");

            // The +X face, picked from outside.
            var on = new Vector3(cube.WorldBounds.Max.X, 0, 10);
            Assert.True(model.LayOnFace(cube, on, Vector3.UnitX));

            FacesDown(cube, Vector3.UnitX);
        });
    }

    [Fact]
    public void ItComesToRestOnTheBed()
    {
        RunSta(() =>
        {
            var (model, wedge) = With("Wedge");

            var on = new Vector3(0, wedge.WorldBounds.Max.Y, 12);
            model.LayOnFace(wedge, on, Vector3.UnitY);

            Assert.Equal(0f, wedge.WorldBounds.Min.Z, 3);
        });
    }

    /// <summary>
    /// The hit reports a triangle's own direction, which may be the inward one. It is the outward
    /// face that has to end up against the bed, so an inward normal is turned round first.
    /// </summary>
    [Fact]
    public void AnInwardFacingNormalIsTakenTheRightWayRound()
    {
        RunSta(() =>
        {
            var (model, cube) = With("Cube");

            var on = new Vector3(cube.WorldBounds.Max.X, 0, 10);
            model.LayOnFace(cube, on, -Vector3.UnitX);   // pointing back into the cube

            FacesDown(cube, Vector3.UnitX);
        });
    }

    [Fact]
    public void OneClickIsTheWholeJob()
    {
        RunSta(() =>
        {
            var (model, cube) = With("Cube");
            Assert.True(model.IsLayMode);

            model.LayOnFace(cube, new Vector3(cube.WorldBounds.Max.X, 0, 10), Vector3.UnitX);

            Assert.False(model.IsLayMode);
        });
    }

    [Fact]
    public void ItUndoesInOneStep()
    {
        RunSta(() =>
        {
            var (model, cube) = With("Cube");
            var was = cube.Rotation;

            model.LayOnFace(cube, new Vector3(cube.WorldBounds.Max.X, 0, 10), Vector3.UnitX);
            model.Undo.Undo();

            Assert.Equal(was, model.Scene.Objects[0].Rotation);
        });
    }

    /// <summary>Laying a face that is already down should not send the object anywhere.</summary>
    [Fact]
    public void AFaceAlreadyOnThePlateLeavesItWhereItIs()
    {
        RunSta(() =>
        {
            var (model, cube) = With("Cube");
            var was = cube.Rotation;

            model.LayOnFace(cube, new Vector3(0, 0, cube.WorldBounds.Min.Z), -Vector3.UnitZ);

            Assert.Equal(was, cube.Rotation);
            Assert.Equal(0f, cube.WorldBounds.Min.Z, 3);
        });
    }

    [Fact]
    public void NothingHappensOutsideTheMode()
    {
        RunSta(() =>
        {
            var model = new MainViewModel();
            model.InsertCommand.Execute("Cube");

            Assert.False(model.LayOnFace(model.Scene.Objects[0], Vector3.Zero, Vector3.UnitX));
        });
    }

    /// <summary>It takes the object over, like the other tools that want a click on a face.</summary>
    [Fact]
    public void TheManipulatorStandsDownWhileItRuns()
    {
        RunSta(() =>
        {
            var (model, cube) = With("Cube");

            Assert.True(model.IsToolRunning);
            Assert.False(model.ShowManipulatorBar);

            model.LayOnFace(cube, new Vector3(cube.WorldBounds.Max.X, 0, 10), Vector3.UnitX);

            Assert.False(model.IsToolRunning);
            Assert.True(model.ShowManipulatorBar);
        });
    }
}
