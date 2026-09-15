using System.Numerics;
using System.Runtime.ExceptionServices;
using FastCraft3D.Geometry;
using FastCraft3D.Model;
using FastCraft3D.ViewModels;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>
/// The boxes in the bar with several objects selected: as one about the middle of the lot, or
/// each on its own. The switch decides what a number refers to.
/// </summary>
public class PivotTypingTests
{
    private static void WithTwo(Action<MainViewModel, SceneObject, SceneObject> body)
    {
        ExceptionDispatchInfo? error = null;

        var thread = new Thread(() =>
        {
            try
            {
                var model = new MainViewModel();
                var a = new SceneObject("A", Primitives.Box(10, 10, 10)) { Position = new Vector3(-20, 0, 0) };
                var b = new SceneObject("B", Primitives.Box(10, 10, 10)) { Position = new Vector3(20, 0, 0) };
                model.Scene.Objects.Add(a);
                model.Scene.Objects.Add(b);
                a.IsSelected = true;
                b.IsSelected = true;
                model.RefreshSelection();
                body(model, a, b);
            }
            catch (Exception ex) { error = ExceptionDispatchInfo.Capture(ex); }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        error?.Throw();
    }

    [Fact]
    public void AsOneAPositionMovesTheMiddleAndKeepsTheArrangement()
    {
        WithTwo((model, a, b) =>
        {
            model.GroupX = 30f;

            Assert.Equal(10f, a.PositionX, 3);
            Assert.Equal(50f, b.PositionX, 3);
        });
    }

    [Fact]
    public void EachOnItsOwnAPositionLinesThemUp()
    {
        WithTwo((model, a, b) =>
        {
            model.AroundSelectionCentre = false;
            model.GroupX = 30f;

            Assert.Equal(30f, a.PositionX, 3);
            Assert.Equal(30f, b.PositionX, 3);
        });
    }

    /// <summary>Not a 0, which is a value; nothing, which the box shows as mixed.</summary>
    [Fact]
    public void EachOnItsOwnValuesThatDifferReadAsMixed()
    {
        WithTwo((model, a, b) =>
        {
            model.AroundSelectionCentre = false;

            Assert.True(float.IsNaN(model.GroupX));
            Assert.Equal(0f, model.GroupY, 3); // both at Y = 0: shared, so shown
        });
    }

    [Fact]
    public void AsOneATurnSwingsThePositionsAndTheBoxReadsZeroAgain()
    {
        WithTwo((model, a, b) =>
        {
            model.GroupYaw = 90f;

            Assert.Equal(0f, model.GroupYaw, 3);
            Assert.Equal(0f, b.PositionX, 2);
            Assert.Equal(20f, MathF.Abs(b.PositionY), 2);
            Assert.Equal(-a.PositionY, b.PositionY, 2);
        });
    }

    [Fact]
    public void AsOneASizeScalesTheGapsToo()
    {
        WithTwo((model, a, b) =>
        {
            model.UniformScale = false;
            model.GroupSizeX = 60f; // the lot spans 50

            Assert.Equal(24f, b.PositionX, 2);
            Assert.Equal(12f, b.SizeX, 2);
        });
    }

    /// <summary>
    /// The lock scales a part on every axis, so as one it has to spread them on every axis too.
    /// Typing a width moved them along X alone: the parts grew into each other and the lot came
    /// out the wrong depth and height, which looked as though each had been resized where it stood.
    /// </summary>
    [Fact]
    public void AsOneALockedSizeSpreadsThePartsOnEveryAxis()
    {
        WithTwo((model, a, b) =>
        {
            b.Position = new Vector3(20, 0, 30);
            model.UniformScale = true;

            float depth = model.GroupSizeY, height = model.GroupSizeZ;
            model.GroupSizeX *= 2f;

            Assert.Equal(depth * 2f, model.GroupSizeY, 2);
            Assert.Equal(height * 2f, model.GroupSizeZ, 2);
        });
    }

    [Fact]
    public void EachOnItsOwnASizeIsSetOnEveryObjectWhereItStands()
    {
        WithTwo((model, a, b) =>
        {
            model.UniformScale = false;
            model.AroundSelectionCentre = false;
            model.GroupSizeX = 40f;

            Assert.Equal(40f, a.SizeX, 2);
            Assert.Equal(40f, b.SizeX, 2);
            Assert.Equal(20f, b.PositionX, 3);
        });
    }

    [Fact]
    public void EachOnItsOwnAChangeByIsAppliedToEveryObject()
    {
        WithTwo((model, a, b) =>
        {
            model.UniformScale = false;
            a.SizeX = 10f;
            b.SizeX = 30f;

            Assert.False(model.ChangeEachBy(nameof(MainViewModel.GroupSizeX), 5f)); // as one: not this path

            model.AroundSelectionCentre = false;
            Assert.True(model.ChangeEachBy(nameof(MainViewModel.GroupSizeX), 5f));

            Assert.Equal(15f, a.SizeX, 2);
            Assert.Equal(35f, b.SizeX, 2);
        });
    }
}
