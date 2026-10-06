using System.Numerics;
using System.Runtime.ExceptionServices;
using FastCraft3D.Geometry;
using FastCraft3D.Model;
using FastCraft3D.ViewModels;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>
/// Drop to plate sets a part down on z = 0. An assembly is one thing: with Each off it goes down as
/// one block and its parts keep their places relative to each other.
/// </summary>
public class DropToPlateAssemblyTests
{
    private static void Stage(Action<MainViewModel, SceneObject, SceneObject, SceneObject> body)
    {
        ExceptionDispatchInfo? error = null;

        var thread = new Thread(() =>
        {
            try
            {
                var model = new MainViewModel();

                // A house in two parts, hanging in the air: the walls from 30 to 50, the roof on them
                // from 50 to 60. And a loose part beside it, from 80 to 90.
                var walls = new SceneObject("Walls", Primitives.Box(20, 20, 20)) { Position = new Vector3(0, 0, 40) };
                var roof = new SceneObject("Roof", Primitives.Box(20, 20, 10)) { Position = new Vector3(0, 0, 55) };
                var loose = new SceneObject("Loose", Primitives.Box(5, 5, 10)) { Position = new Vector3(40, 0, 85) };
                model.Scene.Objects.Add(walls);
                model.Scene.Objects.Add(roof);
                model.Scene.Objects.Add(loose);

                model.Undo.Execute(AssemblyTools.Assemble(model.Scene, [walls, roof], out _));

                body(model, walls, roof, loose);
            }
            catch (Exception ex) { error = ExceptionDispatchInfo.Capture(ex); }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        error?.Throw();
    }

    private static void Select(MainViewModel model, params SceneObject[] objects)
    {
        foreach (var o in model.Scene.Objects) o.IsSelected = objects.Contains(o);
        model.RefreshSelection();
    }

    private static float Bottom(SceneObject o) => o.WorldBounds.Min.Z;

    [Fact]
    public void AnAssemblyGoesDownAsOneBlockKeepingItsPartsPlaces() => Stage((model, walls, roof, _) =>
    {
        Select(model, walls, roof);

        model.AlignToPlateCommand.Execute(null);

        Assert.Equal(0f, Bottom(walls), 3);
        Assert.Equal(20f, Bottom(roof), 3); // still on top of the walls, not set down beside them
    });

    [Fact]
    public void WithEachOnEveryPartIsSetDownByItself() => Stage((model, walls, roof, _) =>
    {
        model.EachOnItsOwn = true;
        Select(model, walls, roof);

        model.AlignToPlateCommand.Execute(null);

        Assert.Equal(0f, Bottom(walls), 3);
        Assert.Equal(0f, Bottom(roof), 3);
    });

    [Fact]
    public void ALoosePartBesideAnAssemblyIsStillSetDownOnItsOwn() => Stage((model, walls, roof, loose) =>
    {
        Select(model, walls, roof, loose);

        model.AlignToPlateCommand.Execute(null);

        Assert.Equal(0f, Bottom(walls), 3);
        Assert.Equal(20f, Bottom(roof), 3);
        Assert.Equal(0f, Bottom(loose), 3);
    });

    [Fact]
    public void SeveralLoosePartsAreEachSetDownByThemselves() => Stage((model, walls, roof, loose) =>
    {
        var other = new SceneObject("Other", Primitives.Box(5, 5, 5)) { Position = new Vector3(-40, 0, 20) };
        model.Scene.Objects.Add(other);
        Select(model, loose, other);

        model.AlignToPlateCommand.Execute(null);

        Assert.Equal(0f, Bottom(loose), 3);
        Assert.Equal(0f, Bottom(other), 3);
    });

    [Fact]
    public void DroppingOnePartOfAnAssemblyLeavesTheRestWhereItWas() => Stage((model, walls, roof, _) =>
    {
        Select(model, roof);

        model.AlignToPlateCommand.Execute(null);

        Assert.Equal(0f, Bottom(roof), 3);
        Assert.Equal(30f, Bottom(walls), 3);
    });

    [Fact]
    public void TheBlockDropIsOneUndoStepAndSidewaysPlacesAreKept() => Stage((model, walls, roof, _) =>
    {
        Select(model, walls, roof);

        model.AlignToPlateCommand.Execute(null);
        Assert.Equal(0f, walls.Position.X, 3);

        model.UndoCommand.Execute(null);

        Assert.Equal(30f, Bottom(walls), 3);
        Assert.Equal(50f, Bottom(roof), 3);
    });
}
