using System.Numerics;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using FastCraft3D.Geometry;
using FastCraft3D.Model;
using FastCraft3D.Model.Commands;
using FastCraft3D.Render;
using FastCraft3D.ViewModels;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>Fit, Distribute and Keep on bed: parts put on the bed as a whole.</summary>
public class BedPlacementTests
{
    private static SceneObject Box(float x, float y, float z, Vector3 at) =>
        new("Box", Primitives.Box(x, y, z)) { Position = at };

    [Fact]
    public void FitShrinksWhatIsTooBigToTheBedLessItsMargin()
    {
        var box = Box(300, 100, 50, new Vector3(80, -40, 30));
        var parts = new[] { box };

        float ratio = BedPlacement.FitRatio(BedPlacement.Reach(parts).Size, 200, 200, 200);
        BedPlacement.Fit(parts, ratio);

        var reach = box.WorldBounds;
        Assert.Equal(160f, reach.Size.X, 2);
        Assert.Equal(0f, reach.Center.X, 2);
        Assert.Equal(0f, reach.Center.Y, 2);
        Assert.Equal(0f, reach.Min.Z, 2);
    }

    [Fact]
    public void FitNeverMakesASmallPartBiggerButStillCentresIt()
    {
        var box = Box(20, 20, 20, new Vector3(60, 70, 40));

        float ratio = BedPlacement.FitRatio(box.WorldBounds.Size, 200, 200, 200);
        BedPlacement.Fit([box], ratio);

        Assert.Equal(1f, ratio);
        Assert.Equal(20f, box.WorldBounds.Size.X, 3);
        Assert.Equal(new Vector3(0, 0, 10), box.Position);
    }

    [Fact]
    public void FitHoldsATallPartToThePrintableHeight()
    {
        float ratio = BedPlacement.FitRatio(new Vector3(50, 50, 400), 200, 200, 250);

        Assert.Equal(250f / 400f, ratio, 4);
    }

    [Fact]
    public void FitShrinksAnAssemblyAsOneSoItStaysAssembled()
    {
        var left = Box(100, 50, 50, new Vector3(-100, 0, 25));
        var right = Box(100, 50, 50, new Vector3(100, 0, 25));
        var parts = new[] { left, right };

        float ratio = BedPlacement.FitRatio(BedPlacement.Reach(parts).Size, 200, 200, 200);
        BedPlacement.Fit(parts, ratio);

        // 300 mm across shrunk to 160: the gap between them shrinks with them.
        Assert.Equal(160f / 300f, ratio, 4);
        Assert.Equal(160f, BedPlacement.Reach(parts).Size.X, 2);
        Assert.Equal(200f * ratio, right.Position.X - left.Position.X, 2);
    }

    [Fact]
    public void DistributeLeavesTheGapAskedForBetweenNeighbours()
    {
        var parts = new[]
        {
            Box(20, 20, 20, new Vector3(50, 50, 50)),
            Box(30, 20, 10, new Vector3(-70, 10, -5)),
            Box(10, 20, 40, new Vector3(0, 0, 0))
        };

        var covers = BedPlacement.Distribute(parts, 5f, 200f);

        var sorted = parts.Select(p => p.WorldBounds).OrderBy(b => b.Min.X).ToList();
        Assert.Equal(5f, sorted[1].Min.X - sorted[0].Max.X, 3);
        Assert.Equal(5f, sorted[2].Min.X - sorted[1].Max.X, 3);

        Assert.All(parts, p => Assert.Equal(0f, p.WorldBounds.Min.Z, 3));
        Assert.Equal(70f, covers.X, 3);
        Assert.Equal(0f, BedPlacement.Reach(parts).Center.X, 3);
        Assert.Equal(0f, BedPlacement.Reach(parts).Center.Y, 3);
    }

    [Fact]
    public void DistributeStartsANewRowWhenTheBedIsFull()
    {
        var parts = Enumerable.Range(0, 5).Select(_ => Box(50, 40, 10, Vector3.Zero)).ToArray();

        // Three 50 mm parts and two 10 mm gaps make 170 mm; a fourth would need 230.
        var covers = BedPlacement.Distribute(parts, 10f, 200f);

        Assert.Equal(170f, covers.X, 3);
        Assert.Equal(90f, covers.Y, 3);

        var boxes = parts.Select(p => p.WorldBounds).ToList();
        for (int i = 0; i < boxes.Count; i++)
            for (int j = i + 1; j < boxes.Count; j++)
                Assert.False(Overlap(boxes[i], boxes[j]), $"parts {i} and {j} overlap");
    }

    private static bool Overlap(Bounds a, Bounds b) =>
        a.Min.X < b.Max.X - 1e-3f && b.Min.X < a.Max.X - 1e-3f &&
        a.Min.Y < b.Max.Y - 1e-3f && b.Min.Y < a.Max.Y - 1e-3f;

    [Fact]
    public void APartStandingOnTheBedGrowsUpwardOnly()
    {
        var box = Box(20, 20, 20, new Vector3(0, 0, 10));
        var before = new[] { box.WorldBounds };

        box.Scale = new Vector3(2, 2, 2);
        BedPlacement.HoldToBed([box], before, together: true, settle: true);

        Assert.Equal(0f, box.WorldBounds.Min.Z, 3);
        Assert.Equal(40f, box.WorldBounds.Max.Z, 3);
    }

    [Fact]
    public void AFloatingPartIsOnlyLiftedWhenItReachesTheBed()
    {
        var box = Box(20, 20, 20, new Vector3(0, 0, 50));
        var before = new[] { box.WorldBounds };

        box.Scale = new Vector3(2, 2, 2);
        Assert.False(BedPlacement.HoldToBed([box], before, together: true, settle: true));
        Assert.Equal(50f, box.PositionZ, 3);

        box.PositionZ = -30;
        Assert.True(BedPlacement.HoldToBed([box], before, together: true, settle: false));
        Assert.Equal(0f, box.WorldBounds.Min.Z, 3);
    }

    [Fact]
    public void TogetherTheLotIsLiftedAsOneAndKeepsItsShape()
    {
        var low = Box(10, 10, 10, new Vector3(0, 0, -8));
        var high = Box(10, 10, 10, new Vector3(20, 0, 30));
        var parts = new[] { low, high };
        var before = parts.Select(p => p.WorldBounds).ToList();

        BedPlacement.HoldToBed(parts, before, together: true, settle: false);

        Assert.Equal(0f, low.WorldBounds.Min.Z, 3);
        Assert.Equal(38f, high.PositionZ - low.PositionZ, 3);
    }

    // --- The handles --------------------------------------------------------------------

    private sealed class FrontProjector : IScreenProjector
    {
        public bool TryProject(Vector3 world, out Point screen)
        {
            screen = new Point(500 + world.X * 4, 400 - world.Z * 4);
            return true;
        }

        public Vector3 ViewDirection => Vector3.UnitY;
        public bool IsReady => true;
    }

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

    private static (GizmoController Gizmo, Canvas Canvas, SceneObject Cube) Standing(GizmoMode mode, bool keep)
    {
        var scene = new Scene();
        var cube = new SceneObject("Cube", Primitives.Box(20, 20, 20)) { IsSelected = true, Position = new Vector3(0, 0, 10) };
        scene.Objects.Add(cube);

        var canvas = new Canvas();
        var gizmo = new GizmoController(canvas, new FrontProjector(), scene, new UndoStack(scene)) { Mode = mode, KeepOnBedMove = keep, KeepOnBedScale = keep };
        gizmo.Rebuild();
        return (gizmo, canvas, cube);
    }

    [Fact]
    public void DraggedDownWithKeepOnBedAPartStopsOnTheBed()
    {
        RunSta(() =>
        {
            var (gizmo, canvas, cube) = Standing(GizmoMode.Move, keep: true);

            // The +Z arrow is the fifth handle in move mode; down the screen is down in Z.
            gizmo.TryBeginDrag(new Point(0, 0), (FrameworkElement)canvas.Children[4]);
            gizmo.ContinueDrag(new Point(0, 400));
            gizmo.EndDrag();

            Assert.Equal(0f, cube.WorldBounds.Min.Z, 3);
        });
    }

    [Theory]
    [InlineData(true, 0f)]
    [InlineData(false, -10f)]
    public void ResizedWithKeepOnBedAPartOnTheBedStaysOnIt(bool keep, float bottom)
    {
        RunSta(() =>
        {
            var (gizmo, canvas, cube) = Standing(GizmoMode.Scale, keep);

            // The +Z arrow in resize mode comes after the box outline and its eight corners and
            // the four X and Y arrows. 40 px up is 10 mm.
            gizmo.TryBeginDrag(new Point(0, 0), (FrameworkElement)canvas.Children[13]);
            gizmo.ContinueDrag(new Point(0, -40));
            gizmo.EndDrag();

            Assert.True(cube.WorldBounds.Size.Z > 20.5f, "it did not grow");
            Assert.Equal(bottom, cube.WorldBounds.Min.Z, 1);
        });
    }

    // --- The boxes ----------------------------------------------------------------------

    private static void WithModel(Action<MainViewModel, SceneObject> body)
    {
        RunSta(() =>
        {
            var model = new MainViewModel();
            var cube = new SceneObject("Cube", Primitives.Box(20, 20, 20)) { Position = new Vector3(0, 0, 10) };
            model.Scene.Objects.Add(cube);
            cube.IsSelected = true;
            model.RefreshSelection();
            body(model, cube);
        });
    }

    [Fact]
    public void KeepOnBedIsOnForResizingAndOffForMoving()
    {
        WithModel((model, cube) =>
        {
            Assert.True(model.KeepOnBedScale);
            Assert.False(model.KeepOnBedMove);

            model.ObjectPositionZ = -20f;
            Assert.Equal(-20f, cube.PositionZ, 3);
        });
    }

    [Fact]
    public void ATypedHeightGrowsAPartOnTheBedUpward()
    {
        WithModel((model, cube) =>
        {
            model.ObjectSizeZ = 60f;

            Assert.Equal(0f, cube.WorldBounds.Min.Z, 3);
            Assert.Equal(60f, cube.WorldBounds.Max.Z, 3);
        });
    }

    [Fact]
    public void WithKeepOnBedOffATypedHeightGrowsAboutTheMiddle()
    {
        WithModel((model, cube) =>
        {
            model.KeepOnBedScale = false;

            model.ObjectSizeZ = 60f;

            Assert.Equal(-20f, cube.WorldBounds.Min.Z, 3);
        });
    }

    [Fact]
    public void ATypedPositionBelowTheBedStopsOnItWhenMovingKeepsToTheBed()
    {
        WithModel((model, cube) =>
        {
            model.KeepOnBedMove = true;

            model.ObjectPositionZ = -20f;

            Assert.Equal(0f, cube.WorldBounds.Min.Z, 3);
        });
    }

    [Fact]
    public void ATurnIsNeverHeldToTheBed()
    {
        WithModel((model, cube) =>
        {
            model.KeepOnBedMove = true;
            model.GroupRoll = 45f;

            Assert.True(cube.WorldBounds.Min.Z < -1f, "the turn was lifted onto the bed");
        });
    }
}
