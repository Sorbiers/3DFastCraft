using System.IO;
using System.Numerics;
using System.Runtime.ExceptionServices;
using FastCraft3D.Geometry;
using FastCraft3D.Geometry.Sketches;
using FastCraft3D.ViewModels;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>Outlines drawn on the plate, and the solids extruded and turned from them.</summary>
public class SketchTests
{
    private static Sketch Rectangle(float x0, float y0, float x1, float y1, Sketch? into = null)
    {
        var sketch = into ?? new Sketch();
        sketch.Place(new Vector2(x0, y0), SketchTool.Rectangle, false);
        sketch.Place(new Vector2(x1, y1), SketchTool.Rectangle, false);
        return sketch;
    }

    /// <summary>The area an outline encloses: <see cref="Polygon2.SignedArea"/> is twice it.</summary>
    private static float Area(List<Vector2> loop) => MathF.Abs(Polygon2.SignedArea(loop)) / 2f;

    private static void Closed(Mesh mesh)
    {
        var health = mesh.CheckHealth();
        Assert.True(health.IsWatertight, health.Describe());
        Assert.True(mesh.ComputeSignedVolume() > 0, "inside out");
    }

    [Fact]
    public void ALineClosesOnItsFirstPoint()
    {
        var sketch = new Sketch();
        foreach (var p in new Vector2[] { new(0, 0), new(20, 0), new(20, 10) })
            sketch.Place(p, SketchTool.Line, false);

        Assert.Empty(sketch.Loops);
        sketch.Place(new Vector2(0, 0), SketchTool.Line, onFirst: true);

        Assert.Single(sketch.Loops);
        Assert.Empty(sketch.Chain);
        Assert.Equal(3, sketch.Loops[0].Count);
    }

    [Fact]
    public void ALineAndAnArcBackToTheStartMakeAHalfDisc()
    {
        var sketch = new Sketch();
        sketch.Place(new Vector2(0, 0), SketchTool.Line, false);
        sketch.Place(new Vector2(0, 20), SketchTool.Line, false);

        Assert.False(sketch.Retool(SketchTool.Line, SketchTool.Arc));
        sketch.Place(new Vector2(0, 0), SketchTool.Arc, onFirst: true);
        Assert.Empty(sketch.Loops);
        sketch.Place(new Vector2(10, 10), SketchTool.Arc, false);

        var loop = Assert.Single(sketch.Loops);
        Assert.Empty(sketch.Chain);
        Assert.Equal(MathF.PI * 100f / 2f, Area(loop), MathF.PI * 100f / 2f * 0.01f);
        Assert.All(loop, p => Assert.True(p.X >= -1e-3f && Vector2.Distance(p, new Vector2(0, 10)) <= 10.01f));
    }

    [Theory]
    [InlineData(-10f)]
    [InlineData(10f)]
    public void AnArcBendsTheWayOfThePointItPassesThrough(float side)
    {
        var arc = Sketch.Arc(new Vector2(0, 0), new Vector2(10, side), new Vector2(20, 0), out float radius);

        Assert.Equal(10f, radius, 3);
        Assert.Equal(new Vector2(20, 0), arc[^1]);
        Assert.All(arc, p => Assert.True(p.Y * side >= -1e-3f, $"{p} is on the wrong side"));
        Assert.Contains(arc, p => Vector2.Distance(p, new Vector2(10, side)) < 0.3f);
    }

    [Fact]
    public void UndoTakesBackAWholeArc()
    {
        var sketch = new Sketch();
        sketch.Place(new Vector2(0, 0), SketchTool.Arc, false);
        sketch.Place(new Vector2(20, 0), SketchTool.Arc, false);
        sketch.Place(new Vector2(10, 5), SketchTool.Arc, false);
        Assert.True(sketch.Chain.Count > 3);
        Assert.Equal(2, sketch.Corners.Count);

        sketch.Undo();

        Assert.Equal([new Vector2(0, 0)], sketch.Chain);
    }

    [Fact]
    public void ACurvePassesSmoothlyThroughEveryPointClicked()
    {
        var sketch = new Sketch();
        var corners = new Vector2[] { new(-10, -10), new(10, -10), new(10, 10), new(-10, 10) };
        foreach (var p in corners) sketch.Place(p, SketchTool.Curve, false);
        sketch.Close();

        var loop = Assert.Single(sketch.Loops);
        Assert.True(loop.Count > 40);
        foreach (var p in corners) Assert.Contains(p, loop);

        // Rounder than the square through the same points, and not so far out as the circle round it.
        float area = Area(loop);
        Assert.InRange(area, 400f, MathF.PI * 200f);
    }

    [Fact]
    public void AFreehandStrokeThatRunsOnPastItsStartClosesWhereItCrosses()
    {
        var sketch = new Sketch();
        var at = (float degrees) => 10f * new Vector2(MathF.Cos(degrees * MathF.PI / 180f), MathF.Sin(degrees * MathF.PI / 180f));

        sketch.BeginStroke(at(0));
        for (float d = 2; d <= 400; d += 2)
        {
            // A wobbling hand, a little inside and outside the circle, and running on past where it began.
            sketch.ExtendStroke(at(d) * (d > 360 ? 0.97f : 1f + 0.005f * MathF.Sin(d)));
        }

        Assert.True(sketch.IsDrawingStroke);
        sketch.EndStroke();

        var loop = Assert.Single(sketch.Loops);
        Assert.Empty(sketch.Chain);
        Assert.True(Sketch.IsSimple(loop));
        Assert.Equal(MathF.PI * 100f, Area(loop), MathF.PI * 100f * 0.03f);
    }

    [Fact]
    public void AScribbleTooSmallToBeAnOutlineIsDropped()
    {
        var sketch = new Sketch();
        sketch.BeginStroke(new Vector2(0, 0));
        sketch.ExtendStroke(new Vector2(0.3f, 0));
        sketch.ExtendStroke(new Vector2(0.6f, 0.2f));

        Assert.Contains("Too small", sketch.EndStroke());
        Assert.True(sketch.IsEmpty);
    }

    [Fact]
    public void ChangingToolsKeepsLinesAndArcsTogetherButStartsAfreshOtherwise()
    {
        var sketch = new Sketch();
        sketch.Place(new Vector2(0, 0), SketchTool.Line, false);
        sketch.Place(new Vector2(10, 0), SketchTool.Line, false);

        Assert.False(sketch.Retool(SketchTool.Line, SketchTool.Arc));
        Assert.Equal(2, sketch.Chain.Count);

        Assert.True(sketch.Retool(SketchTool.Arc, SketchTool.Curve));
        Assert.Empty(sketch.Chain);
    }

    [Fact]
    public void ADrawingLoadsAsOutlinesWithItsCornerOnTheOriginAndUndoesWhole() => WithModel(model =>
    {
        // A frame with a window in it, and a disc overlapping its right edge, which cannot be kept.
        string file = Path.Combine(Path.GetTempPath(), $"sketch-{Guid.NewGuid():N}.svg");
        File.WriteAllText(file, """
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 110 50">
              <path d="M0 0 H100 V50 H0 Z M10 10 V40 H40 V10 Z" />
              <circle cx="100" cy="25" r="10" />
            </svg>
            """);

        try
        {
            model.BeginSketchCommand.Execute(null);
            model.SketchDrawingWidth = 44f;
            model.LoadSketchDrawing(file);

            var loops = model.CurrentSketch.Loops;
            Assert.Equal(2, loops.Count);
            Assert.Contains("1 left out", model.SketchMessage);

            var points = loops.SelectMany(l => l).ToList();
            Assert.Equal(0f, points.Min(p => p.X), 2);
            Assert.Equal(0f, points.Min(p => p.Y), 2);
            Assert.Equal(40f, points.Max(p => p.X), 1);
            Assert.Equal(4, loops[0].Count);

            var shape = Assert.Single(model.CurrentSketch.Shapes());
            Assert.Single(shape.Holes);

            model.SketchUndoCommand.Execute(null);
            Assert.Empty(model.CurrentSketch.Loops);
        }
        finally
        {
            File.Delete(file);
        }
    });

    [Fact]
    public void AnOutlineThatCrossesItselfIsRefused()
    {
        var sketch = new Sketch();
        foreach (var p in new Vector2[] { new(0, 0), new(10, 10), new(10, 0), new(0, 10) })
            sketch.Place(p, SketchTool.Line, false);

        string said = sketch.Close();

        Assert.Empty(sketch.Loops);
        Assert.Contains("crosses itself", said);
    }

    [Fact]
    public void OutlinesMayNestButNotCross()
    {
        var sketch = Rectangle(0, 0, 40, 40);
        Rectangle(10, 10, 30, 30, sketch);
        Assert.Equal(2, sketch.Loops.Count);

        var shape = Assert.Single(sketch.Shapes());
        Assert.Single(shape.Holes);

        string said = Rectangle(35, 35, 60, 60, sketch).Loops.Count == 2 ? "refused" : "kept";
        Assert.Equal("refused", said);
    }

    [Fact]
    public void UndoTakesBackAPointThenAnOutline()
    {
        var sketch = Rectangle(0, 0, 10, 10);
        sketch.Place(new Vector2(20, 20), SketchTool.Line, false);

        sketch.Undo();
        Assert.Empty(sketch.Chain);
        Assert.Single(sketch.Loops);

        sketch.Undo();
        Assert.True(sketch.IsEmpty);
    }

    [Fact]
    public void AnExtrusionIsTheSketchsAreaTimesItsHeightHolesAndAll()
    {
        var sketch = Rectangle(0, 0, 40, 30);
        Rectangle(10, 10, 20, 20, sketch);

        var solid = SketchSolids.Extrude(sketch, 5f);

        Closed(solid);
        Assert.Equal((40.0 * 30 - 10 * 10) * 5, solid.ComputeSignedVolume(), 1.0);
        Assert.Equal(5f, solid.ComputeBounds().Max.Z, 3);
    }

    [Fact]
    public void ARingTurnedAboutYIsATube()
    {
        var solid = SketchSolids.Revolve(Rectangle(5, 0, 10, 20), RevolveAxis.Y, 360f, 128, out var why);

        Assert.Null(why);
        Assert.NotNull(solid);
        Closed(solid);

        double tube = Math.PI * (100 - 25) * 20;
        Assert.Equal(tube, solid.ComputeSignedVolume(), tube * 0.01);
        Assert.Equal(20f, solid.ComputeBounds().Size.Z, 3);
    }

    [Fact]
    public void AnOutlineOnTheAxisClosesUpIntoACylinder()
    {
        var solid = SketchSolids.Revolve(Rectangle(0, 0, 10, 15), RevolveAxis.Y, 360f, 128, out _);

        Assert.NotNull(solid);
        Closed(solid);
        double cylinder = Math.PI * 100 * 15;
        Assert.Equal(cylinder, solid.ComputeSignedVolume(), cylinder * 0.01);
    }

    [Fact]
    public void PartOfATurnIsClosedAtBothEnds()
    {
        var solid = SketchSolids.Revolve(Rectangle(5, 0, 10, 20), RevolveAxis.Y, 90f, 128, out _);

        Assert.NotNull(solid);
        Closed(solid);
        double quarter = Math.PI * (100 - 25) * 20 / 4;
        Assert.Equal(quarter, solid.ComputeSignedVolume(), quarter * 0.01);
    }

    [Fact]
    public void TurnedAboutXTheSolidLiesAlongX()
    {
        var solid = SketchSolids.Revolve(Rectangle(0, 2, 30, 6), RevolveAxis.X, 360f, 64, out _);

        Assert.NotNull(solid);
        Closed(solid);
        var size = solid.ComputeBounds().Size;
        Assert.Equal(30f, size.X, 3);
        Assert.Equal(12f, size.Y, 0.1f);
        Assert.Equal(12f, size.Z, 0.1f);
    }

    [Fact]
    public void AnOutlineAcrossTheAxisIsRefused()
    {
        var solid = SketchSolids.Revolve(Rectangle(-5, 0, 5, 10), RevolveAxis.Y, 360f, 64, out var why);

        Assert.Null(solid);
        Assert.Contains("right of the Y axis", why);
    }

    [Fact]
    public void AnOutlineWithAHoleTurnsIntoAHollowRing()
    {
        var sketch = Rectangle(10, 0, 30, 20);
        Rectangle(15, 5, 25, 15, sketch);

        var solid = SketchSolids.Revolve(sketch, RevolveAxis.Y, 360f, 128, out _);

        Assert.NotNull(solid);
        Closed(solid);
        double full = Math.PI * (900 - 100) * 20 - Math.PI * (625 - 225) * 10;
        Assert.Equal(full, solid.ComputeSignedVolume(), full * 0.01);
    }

    // --- In the app ----------------------------------------------------------------------

    private static void WithModel(Action<MainViewModel> body)
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
    public void SketchingARectangleAndExtrudingItPutsASolidOnThePlate()
    {
        WithModel(model =>
        {
            model.BeginSketchCommand.Execute("Rectangle");
            Assert.True(model.IsSketchMode);
            Assert.True(model.IsToolInHand);

            // Snapped to the millimetre grid.
            model.PlaceSketchPoint(new Vector2(-10.3f, -5.2f), false);
            model.PlaceSketchPoint(new Vector2(9.8f, 4.9f), false);
            Assert.Equal(new Vector2(-10, -5), model.CurrentSketch.Loops[0][0]);

            model.SketchHeight = 8f;
            model.SketchExtrudeCommand.Execute(null);

            Assert.False(model.IsSketchMode);
            var made = Assert.Single(model.Scene.Objects);
            Assert.Equal(new Vector3(20, 10, 8), made.WorldBounds.Size);
            Assert.Equal(0f, made.WorldBounds.Min.Z, 3);

            // The sketch is kept for another go.
            Assert.Single(model.CurrentSketch.Loops);
        });
    }

    [Fact]
    public void EscapeDropsTheOutlineInProgressThenLeavesTheSketch()
    {
        WithModel(model =>
        {
            model.BeginSketchCommand.Execute("Line");
            model.PlaceSketchPoint(new Vector2(0, 0), false);
            model.PlaceSketchPoint(new Vector2(10, 0), false);

            Assert.True(model.CancelActiveTool());
            Assert.True(model.IsSketchMode);
            Assert.Empty(model.CurrentSketch.Chain);

            Assert.True(model.CancelActiveTool());
            Assert.False(model.IsSketchMode);
        });
    }

    [Fact]
    public void RevolvingInTheAppStandsTheSolidOnThePlate()
    {
        WithModel(model =>
        {
            model.BeginSketchCommand.Execute("Rectangle");
            model.PlaceSketchPoint(new Vector2(0, 30), false);
            model.PlaceSketchPoint(new Vector2(12, 50), false);

            model.SketchRevolveCommand.Execute(null);

            var made = Assert.Single(model.Scene.Objects);
            Assert.Equal(0f, made.WorldBounds.Min.Z, 3);
            Assert.Equal(20f, made.WorldBounds.Size.Z, 3);
            Assert.Equal(24f, made.WorldBounds.Size.X, 0.1f);
        });
    }

    // --- Editing what has been drawn ---------------------------------------------------

    [Fact]
    public void EveryCornerOfADrawnRectangleCanBeDragged()
    {
        var sketch = Rectangle(0, 0, 20, 10);

        var handles = sketch.Handles();

        Assert.Equal(4, handles.Count);
        Assert.All(handles, h => Assert.Equal(0, h.Loop));
    }

    [Fact]
    public void ThePiecesACircleIsLaidOutWithAreNotOfferedToDrag()
    {
        var sketch = new Sketch();
        sketch.Place(new Vector2(0, 0), SketchTool.Circle, false);
        sketch.Place(new Vector2(10, 0), SketchTool.Circle, false);

        Assert.Single(sketch.Loops);
        Assert.True(sketch.Loops[0].Count > 50, "a circle is drawn as many short pieces");

        // Dragging one of them would dent the circle rather than edit it.
        Assert.Empty(sketch.Handles());
    }

    [Fact]
    public void ThePointsOfTheLineBeingDrawnCanBeDraggedBeforeItCloses()
    {
        var sketch = new Sketch();
        foreach (var p in new Vector2[] { new(0, 0), new(20, 0), new(20, 10) })
            sketch.Place(p, SketchTool.Line, false);

        var handles = sketch.Handles();

        Assert.Equal(3, handles.Count);
        Assert.All(handles, h => Assert.Equal(-1, h.Loop));

        sketch.MoveHandle(-1, 1, new Vector2(30, 0));
        Assert.Equal(new Vector2(30, 0), sketch.Chain[1]);
    }

    [Fact]
    public void DraggingACornerChangesTheOutlineItIsIn()
    {
        var sketch = Rectangle(0, 0, 20, 10);
        var corner = sketch.Handles().Single(h => h.At == new Vector2(0, 0));

        sketch.MoveHandle(corner.Loop, corner.Index, new Vector2(-10, 0));
        string said = sketch.SettleHandle(corner.Loop, corner.Index, corner.At);

        Assert.Equal(new Vector2(-10, 0), sketch.Loops[0][corner.Index]);
        Assert.Contains("Moved", said);

        // A trapezoid now: 30 mm along the bottom, 20 along the top, 10 deep.
        Assert.Equal(250f, Area(sketch.Loops[0]), 0.01f);
    }

    [Fact]
    public void ACornerDraggedAcrossTheOutlineIsPutBack()
    {
        var sketch = Rectangle(0, 0, 20, 10);
        var corner = sketch.Handles().Single(h => h.At == new Vector2(0, 0));

        // Over the far side: the edges either side of the corner now cross it.
        sketch.MoveHandle(corner.Loop, corner.Index, new Vector2(10, 15));
        string said = sketch.SettleHandle(corner.Loop, corner.Index, corner.At);

        Assert.Equal(new Vector2(0, 0), sketch.Loops[0][corner.Index]);
        Assert.Contains("Put back", said);
        Assert.Equal(200f, Area(sketch.Loops[0]), 0.01f);
    }

    [Fact]
    public void ACornerDraggedOverAnotherOutlineIsPutBack()
    {
        var sketch = Rectangle(0, 0, 10, 10);
        Rectangle(30, 0, 40, 10, sketch);

        var corner = sketch.Handles().Single(h => h.Loop == 0 && h.At == new Vector2(10, 10));

        // Into the middle of the second rectangle, whose edges it now cuts through.
        sketch.MoveHandle(0, corner.Index, new Vector2(35, 5));
        string said = sketch.SettleHandle(0, corner.Index, corner.At);

        Assert.Equal(new Vector2(10, 10), sketch.Loops[0][corner.Index]);
        Assert.Contains("cross another", said);
    }

    // --- The right button finishes the line ---------------------------------------------

    [Fact]
    public void TheRightButtonClosesTheLineWhereItStands()
    {
        var sketch = new Sketch();
        foreach (var p in new Vector2[] { new(0, 0), new(20, 0), new(20, 10) })
            sketch.Place(p, SketchTool.Line, false);

        sketch.EndLine();

        Assert.Single(sketch.Loops);
        Assert.Empty(sketch.Chain);
        Assert.Equal(100f, Area(sketch.Loops[0]), 0.01f);
    }

    [Fact]
    public void TheRightButtonDropsALineWithTooFewPointsToClose()
    {
        var sketch = new Sketch();
        sketch.Place(new Vector2(0, 0), SketchTool.Line, false);
        sketch.Place(new Vector2(20, 0), SketchTool.Line, false);

        string said = sketch.EndLine();

        Assert.Empty(sketch.Loops);
        Assert.Empty(sketch.Chain);
        Assert.Contains("three points", said);
    }

    [Fact]
    public void TheRightButtonOnNothingBeingDrawnLeavesTheSketchAlone()
    {
        var sketch = Rectangle(0, 0, 20, 10);

        Assert.Equal("", sketch.EndLine());
        Assert.Single(sketch.Loops);
    }
}
