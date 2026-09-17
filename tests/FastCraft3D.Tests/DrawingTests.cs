using System.Numerics;
using System.Runtime.ExceptionServices;
using FastCraft3D.Geometry;
using FastCraft3D.Geometry.Csg;
using FastCraft3D.Geometry.Drawings;
using FastCraft3D.View;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>Three-view drawings: the lines each view is drawn with, and how the views go on a page.</summary>
public class DrawingTests
{
    /// <summary>A 60 by 40 by 30 block standing on the plate, a 16 mm hole straight down through it.</summary>
    private static Mesh DrilledBlock()
    {
        var block = MeshTransform.Transformed(Primitives.Box(60, 40, 30), Matrix4x4.CreateTranslation(0, 0, 15));
        var drill = MeshTransform.Transformed(Primitives.Prism(8, 40, 48), Matrix4x4.CreateTranslation(0, 0, 15));
        var drilled = ManifoldCsg.Subtract(block, drill);
        Assert.NotNull(drilled);
        return drilled;
    }

    private static float Length(IEnumerable<Line2> lines) => lines.Sum(l => Vector2.Distance(l.A, l.B));

    private static Dictionary<DrawingView, ViewLines> AllViews(Mesh mesh) =>
        Enum.GetValues<DrawingView>().ToDictionary(v => v, v => ViewDrawing.Build([mesh], v));

    [Fact]
    public void AViewIsAsBigAsThePartSeenFromThatSide()
    {
        var block = DrilledBlock();

        Assert.Equal(new Vector2(60, 30), ViewDrawing.Build([block], DrawingView.Front).Size);
        Assert.Equal(new Vector2(60, 40), ViewDrawing.Build([block], DrawingView.Top).Size);
        Assert.Equal(new Vector2(40, 30), ViewDrawing.Build([block], DrawingView.Right).Size);
    }

    [Fact]
    public void AHoleSeenFromTheFrontIsDashedAndFromAboveIsDrawn()
    {
        var block = DrilledBlock();
        var front = ViewDrawing.Build([block], DrawingView.Front);
        var top = ViewDrawing.Build([block], DrawingView.Top);

        // From the front the block is a rectangle, 180 mm of outline, and the hole two dashed
        // lines the height of the block.
        Assert.Equal(180f, Length(front.Visible), 180f * 0.03f);
        Assert.Equal(60f, Length(front.Hidden), 60f * 0.1f);

        // From above the hole is a circle, seen, inside the rectangle's 200 mm.
        Assert.Equal(200f + 2f * MathF.PI * 8f, Length(top.Visible), 250f * 0.05f);
        Assert.True(Length(top.Hidden) < 1f, $"{Length(top.Hidden):0.##} mm of hidden line from above");
    }

    [Fact]
    public void TheBackEdgesOfABoxAreNotDashedOverItsFrontEdges()
    {
        var box = MeshTransform.Transformed(Primitives.Box(20, 20, 20), Matrix4x4.CreateTranslation(0, 0, 10));
        var front = ViewDrawing.Build([box], DrawingView.Front);

        Assert.Equal(80f, Length(front.Visible), 80f * 0.03f);
        Assert.True(Length(front.Hidden) < 0.5f, $"{Length(front.Hidden):0.##} mm dashed over the outline");
    }

    [Fact]
    public void AnIsometricBoxShowsNineEdgesAndHidesThree()
    {
        var box = MeshTransform.Transformed(Primitives.Box(20, 20, 20), Matrix4x4.CreateTranslation(0, 0, 10));
        var iso = ViewDrawing.Build([box], DrawingView.Isometric);

        // Seen from a corner, every edge is square on to the view at the same slant.
        float edge = 20f * MathF.Sqrt(2f / 3f);
        Assert.Equal(9f * edge, Length(iso.Visible), 9f * edge * 0.04f);
        Assert.Equal(3f * edge, Length(iso.Hidden), 3f * edge * 0.08f);
    }

    [Fact]
    public void ASmallPartIsDrawnFullSizeAndABigOneScaledDown()
    {
        var a4 = new Vector2(297, 210);

        var small = DrawingSheet.Layout(AllViews(DrilledBlock()), a4, new DrawingOptions());
        Assert.Equal("1:1", small.ScaleText);

        var big = MeshTransform.Transformed(DrilledBlock(), Matrix4x4.CreateScale(5f));
        var scaled = DrawingSheet.Layout(AllViews(big), a4, new DrawingOptions());
        Assert.Equal("1:5", scaled.ScaleText);
    }

    [Theory]
    [InlineData(Projection.FirstAngle)]
    [InlineData(Projection.ThirdAngle)]
    public void TheViewFromAboveGoesBelowOrAboveTheFrontAsTheProjectionSays(Projection projection)
    {
        var sheet = DrawingSheet.Layout(AllViews(DrilledBlock()), new Vector2(297, 210), new DrawingOptions { Projection = projection });
        var at = sheet.Views.ToDictionary(v => v.Lines.View, v => v);

        var front = at[DrawingView.Front];
        var top = at[DrawingView.Top];
        var right = at[DrawingView.Right];

        // Lined up with the front: the same left edge above or below, the same bottom beside it.
        Assert.Equal(front.Corner.X, top.Corner.X, 3);
        Assert.Equal(front.Corner.Y, right.Corner.Y, 3);

        if (projection == Projection.ThirdAngle)
        {
            Assert.True(top.Corner.Y > front.Corner.Y);
            Assert.True(right.Corner.X > front.Corner.X);
        }
        else
        {
            Assert.True(top.Corner.Y < front.Corner.Y);
            Assert.True(right.Corner.X < front.Corner.X);
        }

        // Everything on the paper, clear of the title block.
        foreach (var placed in sheet.Views)
        {
            var far = placed.Corner + placed.Lines.Size * sheet.Scale;
            Assert.True(placed.Corner.X >= DrawingSheet.Margin && far.X <= 297 - DrawingSheet.Margin);
            Assert.True(placed.Corner.Y >= DrawingSheet.Margin + DrawingSheet.TitleHeight && far.Y <= 210 - DrawingSheet.Margin);
        }
    }

    [Fact]
    public void TheOverallSizesAreGivenOnceEachInTheUnitAskedFor()
    {
        var views = AllViews(DrilledBlock());

        var mm = DrawingSheet.Layout(views, new Vector2(297, 210), new DrawingOptions());
        Assert.Equal(["60", "30", "40"], mm.Dimensions.Select(d => d.Text));

        var inches = DrawingSheet.Layout(views, new Vector2(297, 210), new DrawingOptions { UnitLabel = "in", UnitMillimetres = 25.4f });
        Assert.Equal((60f / 25.4f).ToString("0.##"), inches.Dimensions[0].Text);

        Assert.Empty(DrawingSheet.Layout(views, new Vector2(297, 210), new DrawingOptions { Dimensions = false }).Dimensions);
    }

    [Fact]
    public void TheDrawingWindowOpensOnAPart()
    {
        ExceptionDispatchInfo? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                var window = new DrawingWindow([DrilledBlock()], "Drilled block", "mm", 1f);
                Assert.Equal("Blueprint", window.Title);
                window.Close();
            }
            catch (Exception ex) { error = ExceptionDispatchInfo.Capture(ex); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        error?.Throw();
    }

    [Fact]
    public void TheSheetIsDrawnWithoutComplaint()
    {
        ExceptionDispatchInfo? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                var options = new DrawingOptions { Title = "Drilled block" };
                var sheet = DrawingSheet.Layout(AllViews(DrilledBlock()), new Vector2(297, 210), options);
                var visual = DrawingRenderer.Render(sheet, options, new DateTime(2026, 9, 17));

                Assert.True(visual.ContentBounds.Width > 1000);
            }
            catch (Exception ex) { error = ExceptionDispatchInfo.Capture(ex); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        error?.Throw();
    }
}
