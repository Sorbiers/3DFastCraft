using System.Numerics;
using FastCraft3D.Geometry.Sketches;
using Xunit;

namespace FastCraft3D.Tests;

public class SketchArcEndTests
{
    [Fact]
    public void RightClickingWhileAnArcIsBendingKeepsTheArcAsShownAndClosesTheOutline()
    {
        var sketch = new Sketch();
        sketch.Place(new Vector2(0, 0), SketchTool.Arc, false);
        sketch.Place(new Vector2(20, 0), SketchTool.Arc, false);

        sketch.EndLine(new Vector2(10, 10));

        Assert.False(sketch.IsDrawing);
        var loop = Assert.Single(sketch.Loops);
        Assert.Contains(loop, p => p.Y > 9.9f);
    }

    [Fact]
    public void EnterWhileAnArcIsBendingDoesTheSame()
    {
        var sketch = new Sketch();
        sketch.Place(new Vector2(0, 0), SketchTool.Line, false);
        sketch.Place(new Vector2(20, 0), SketchTool.Line, false);
        sketch.Place(new Vector2(20, 20), SketchTool.Arc, false);

        sketch.Close(new Vector2(25, 10));

        Assert.False(sketch.IsDrawing);
        Assert.Single(sketch.Loops);
    }

    [Fact]
    public void AClosingArcThatCrossesTheOutlineComesOffAndCanBeBentAgain()
    {
        var sketch = new Sketch();
        sketch.Place(new Vector2(0, 0), SketchTool.Line, false);
        sketch.Place(new Vector2(20, 0), SketchTool.Line, false);
        sketch.Place(new Vector2(20, 20), SketchTool.Line, false);
        int before = sketch.Chain.Count;

        sketch.Place(new Vector2(0, 0), SketchTool.Arc, true);
        sketch.Place(new Vector2(18, 5), SketchTool.Arc, false);

        Assert.True(sketch.IsDrawing);
        Assert.Equal(before, sketch.Chain.Count);
        Assert.NotNull(sketch.ArcEnd);

        sketch.Place(new Vector2(3, 15), SketchTool.Arc, false);

        Assert.False(sketch.IsDrawing);
        Assert.Single(sketch.Loops);
    }
}
