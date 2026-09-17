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
        // Deep enough, next to how wide they are together, that one row across is already close
        // to square - stacking them instead would only make the result taller and thinner - so
        // all three end up side by side rather than the arrangement splitting them into rows.
        var parts = new[]
        {
            Box(20, 60, 20, new Vector3(50, 50, 50)),
            Box(30, 60, 10, new Vector3(-70, 10, -5)),
            Box(10, 60, 40, new Vector3(0, 0, 0))
        };

        var covers = BedPlacement.Distribute(parts, 5f, 200f);

        var sorted = parts.Select(p => p.WorldBounds).OrderBy(b => b.Min.X).ToList();
        Assert.Equal(5f, sorted[1].Min.X - sorted[0].Max.X, 3);
        Assert.Equal(5f, sorted[2].Min.X - sorted[1].Max.X, 3);
        Assert.Equal(sorted[0].Center.Y, sorted[1].Center.Y, 3);
        Assert.Equal(sorted[1].Center.Y, sorted[2].Center.Y, 3);

        Assert.All(parts, p => Assert.Equal(0f, p.WorldBounds.Min.Z, 3));
        Assert.Equal(70f, covers.X, 3);
        Assert.Equal(0f, BedPlacement.Reach(parts).Center.X, 3);
        Assert.Equal(0f, BedPlacement.Reach(parts).Center.Y, 3);
    }

    /// <summary>
    /// Five identical parts on a 200 mm bed used to pack three across before wrapping - 170 x
    /// 90, a wide, shallow strip - purely because that is as many as the width allows. Packed
    /// two a row instead, 110 x 140 comes out closer to square, with a more even margin left
    /// round every side once it is centred.
    /// </summary>
    [Fact]
    public void DistributeStartsANewRowWhenTheBedIsFull()
    {
        var parts = Enumerable.Range(0, 5).Select(_ => Box(50, 40, 10, Vector3.Zero)).ToArray();

        var covers = BedPlacement.Distribute(parts, 10f, 200f);

        Assert.Equal(110f, covers.X, 3);
        Assert.Equal(140f, covers.Y, 3);

        var boxes = parts.Select(p => p.WorldBounds).ToList();
        for (int i = 0; i < boxes.Count; i++)
            for (int j = i + 1; j < boxes.Count; j++)
                Assert.False(Overlap(boxes[i], boxes[j]), $"parts {i} and {j} overlap");
    }

    /// <summary>
    /// A handful of small parts on a bed much bigger than any of them used to pack across its
    /// whole width, leaving most of its depth empty - a shape nothing like the bed's own, so it
    /// read as hugging one edge rather than sitting in the middle. A squarer arrangement leaves
    /// a comparable margin on every side instead.
    /// </summary>
    [Fact]
    public void SmallPartsOnABigBedPackCloseToSquareRatherThanAcrossTheWholeWidth()
    {
        var sizes = new[] { 90f, 70f, 60f, 55f, 50f, 45f, 40f, 35f, 30f, 25f, 20f };
        var parts = sizes.Select(s => Box(s, s * 0.8f, 10f, Vector3.Zero)).ToArray();

        var covers = BedPlacement.Distribute(parts, 10f, 280f);

        // Not the roughly 280 mm wide, 100 mm deep strip a full-width pack would have made:
        // within a couple of times as wide as it is deep, not an order of magnitude.
        Assert.True(covers.X < 280f * 0.7f, $"packed {covers.X:0} mm across a 280 mm bed - barely narrower than the whole of it");
        Assert.True(MathF.Max(covers.X, covers.Y) / MathF.Min(covers.X, covers.Y) < 2.5f,
            $"{covers.X:0} x {covers.Y:0} is not close to square");

        var boxes = parts.Select(p => p.WorldBounds).ToList();
        for (int i = 0; i < boxes.Count; i++)
            for (int j = i + 1; j < boxes.Count; j++)
                Assert.False(Overlap(boxes[i], boxes[j]), $"parts {i} and {j} overlap");

        Assert.Equal(0f, BedPlacement.Reach(parts).Center.X, 3);
        Assert.Equal(0f, BedPlacement.Reach(parts).Center.Y, 3);
    }

    private static bool Overlap(Bounds a, Bounds b) =>
        a.Min.X < b.Max.X - 1e-3f && b.Min.X < a.Max.X - 1e-3f &&
        a.Min.Y < b.Max.Y - 1e-3f && b.Min.Y < a.Max.Y - 1e-3f;

    /// <summary>
    /// A small part goes into the gap a taller neighbour left behind in an earlier row, rather
    /// than starting a shallow row of its own: two rows deep, not three, even though the row it
    /// would have extended is already full.
    /// </summary>
    [Fact]
    public void ASmallPartFillsTheRoomATallerNeighbourLeftInAnEarlierRow()
    {
        var parts = new[]
        {
            Box(70, 50, 10, Vector3.Zero),  // opens the first row, 30 mm of room left in it
            Box(95, 40, 10, Vector3.Zero),  // does not fit the first row, opens the second, nearly full
            Box(20, 10, 10, Vector3.Zero)   // fits neither row's end, but fits the room the first still has
        };

        var covers = BedPlacement.Distribute(parts, 5f, 100f);

        // Next fit alone would have had to open a third, shallow row for the last part: 50 + 40
        // + 10 deep plus two gaps, 110 mm. Best fit keeps it to the two rows already open.
        Assert.Equal(95f, covers.Y, 3);

        var boxes = parts.Select(p => p.WorldBounds).ToList();
        Assert.Equal(boxes[0].Center.Y, boxes[2].Center.Y, 3);

        for (int i = 0; i < boxes.Count; i++)
            for (int j = i + 1; j < boxes.Count; j++)
                Assert.False(Overlap(boxes[i], boxes[j]), $"parts {i} and {j} overlap");
    }

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
