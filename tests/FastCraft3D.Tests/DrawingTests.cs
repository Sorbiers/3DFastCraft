using System.Numerics;
using System.Runtime.ExceptionServices;
using System.Windows;
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

    /// <summary>Several small boxes spread far apart along X: wide and shallow head-on, but tall in isometric, which shows every axis at once.</summary>
    private static Mesh ScatteredBoxes() => Mesh.Combine(Enumerable.Range(0, 5)
        .Select(i => MeshTransform.Transformed(Primitives.Box(20, 20, 20), Matrix4x4.CreateTranslation(i * 50f, 0, 10))));

    /// <summary>A stepped block: a 60 x 40 x 20 base with a narrower 30 x 40 x 20 block on top of its left end.</summary>
    private static Mesh SteppedBlock()
    {
        var baseBlock = MeshTransform.Transformed(Primitives.Box(60, 40, 20), Matrix4x4.CreateTranslation(30, 20, 10));
        var step = MeshTransform.Transformed(Primitives.Box(30, 40, 20), Matrix4x4.CreateTranslation(15, 20, 30));
        var whole = ManifoldCsg.Union(baseBlock, step);
        Assert.NotNull(whole);
        return whole;
    }

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
    public void EveryDimensionChainsTheStepAndAddsTheOverallOnceMore()
    {
        var views = AllViews(SteppedBlock());

        var plain = DrawingSheet.Layout(views, new Vector2(297, 210), new DrawingOptions());
        Assert.Equal(["60", "40", "40"], plain.Dimensions.Select(d => d.Text));

        var chained = DrawingSheet.Layout(views, new Vector2(297, 210), new DrawingOptions { AllDimensions = true });
        var texts = chained.Dimensions.Select(d => d.Text).ToList();

        // The step's two segments - 30 mm across it, 20 mm up it - are chained wherever the
        // step is a real edge of the part, which turns out to be more than one view: it is a
        // genuine corner, not just a front-on illusion, so the right view sees the same step in
        // height peeking past the base's nearer end exactly as looking at the real part would.
        Assert.Contains("30", texts);
        Assert.Contains("20", texts);
        Assert.Contains("60", texts);
        Assert.Contains("40", texts);

        // Chained several times over rather than once: every view now carries its own dimensions.
        Assert.True(chained.Dimensions.Count > plain.Dimensions.Count + 3);
    }

    [Fact]
    public void EveryDimensionLeavesACurvedHoleAloneAndOnlyAddsTheStraightEdges()
    {
        var views = AllViews(DrilledBlock());

        var chained = DrawingSheet.Layout(views, new Vector2(297, 210), new DrawingOptions { AllDimensions = true });
        var texts = chained.Dimensions.Select(d => d.Text).ToList();

        // Front and top each give their own width and height, and the right view - which the
        // plain drawing never dimensions at all - now gets its own two as well: six in all, and
        // none of them from the hole, which is round from above and dashed from the front and so
        // never comes out exactly level or plumb.
        Assert.Equal(6, texts.Count);
        Assert.All(texts, text => Assert.True(text is "60" or "30" or "40", $"an unexpected dimension: {text}"));
    }

    [Theory]
    [InlineData(ScaleFormat.ModelOnly, "60")]
    [InlineData(ScaleFormat.ModelWithReal, "60 mm (5.22 m)")]
    [InlineData(ScaleFormat.RealWithModel, "5.22 m (60 mm)")]
    public void AScaledModelWritesWhicheverFormatIsAsked(ScaleFormat format, string expected)
    {
        var views = AllViews(DrilledBlock());
        var sheet = DrawingSheet.Layout(views, new Vector2(297, 210), new DrawingOptions { ModelScale = 87f, ScaleFormat = format });

        Assert.Equal(expected, sheet.Dimensions[0].Text);
    }

    [Fact]
    public void AnUnscaledModelIgnoresTheFormatEntirely()
    {
        var views = AllViews(DrilledBlock());
        var sheet = DrawingSheet.Layout(views, new Vector2(297, 210), new DrawingOptions { ScaleFormat = ScaleFormat.RealWithModel });

        Assert.Equal("60", sheet.Dimensions[0].Text);
    }

    [Theory]
    [InlineData(1f, false)]
    [InlineData(87f, true)]
    public void TheScaleWordingOnlyShowsOnceTheModelStandsForSomethingElse(float modelScale, bool expectVisible)
    {
        ExceptionDispatchInfo? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                var window = new DrawingWindow([DrilledBlock()], "Drilled block", "mm", 1f, modelScale);
                var panel = (FrameworkElement)window.FindName("ScaleFormatPanel")!;
                Assert.Equal(expectVisible ? Visibility.Visible : Visibility.Collapsed, panel.Visibility);
                window.Close();
            }
            catch (Exception ex) { error = ExceptionDispatchInfo.Capture(ex); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        error?.Throw();
    }

    /// <summary>
    /// A wide, flat scatter of parts has an isometric far taller than any of the front, top or
    /// right views - showing every axis at once, it cannot help but be - and that used to shrink
    /// the whole sheet to fit that one corner. The isometric now finds its own scale instead,
    /// smaller if it must be, and everything that actually carries a dimension is drawn as large
    /// as the page allows.
    /// </summary>
    [Fact]
    public void TheIsometricDoesNotShrinkTheOtherViewsToFitItsOwnCorner()
    {
        var views = AllViews(ScatteredBoxes());
        var sheet = DrawingSheet.Layout(views, new Vector2(297, 210), new DrawingOptions());
        var byView = sheet.Views.ToDictionary(v => v.Lines.View, v => v);

        // The isometric shares a row with the view from above: its own height, on the page,
        // ought to be well past what the view from above needs on its own - a wide scatter of
        // parts spreads across the isometric's height as well as its width, where the view from
        // above only ever reads their shallow depth.
        Assert.True(views[DrawingView.Isometric].Size.Y > views[DrawingView.Top].Size.Y * 1.3f,
            "the fixture does not make an isometric taller than the view from above needs - nothing to prove here");

        Assert.Equal(sheet.Scale, byView[DrawingView.Front].Scale, 4);
        Assert.Equal(sheet.Scale, byView[DrawingView.Top].Scale, 4);
        Assert.Equal(sheet.Scale, byView[DrawingView.Right].Scale, 4);
        Assert.True(byView[DrawingView.Isometric].Scale <= sheet.Scale + 1e-4f,
            "the isometric was drawn larger than everything else");
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
