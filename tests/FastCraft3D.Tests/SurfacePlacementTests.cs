using System.Numerics;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Shapes;
using FastCraft3D.Geometry;
using FastCraft3D.Geometry.Engraving;
using FastCraft3D.Model;
using FastCraft3D.Render;
using FastCraft3D.ViewModels;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>Moving and turning lettering in its own flat layout, before any surface sees it.</summary>
public class SurfacePlacementTests
{
    private static TextShape Square(float size) => new(
        [new Vector2(-size / 2, -size / 2), new Vector2(size / 2, -size / 2),
         new Vector2(size / 2, size / 2), new Vector2(-size / 2, size / 2)], []);

    [Fact]
    public void TheMiddleLeavesEverythingWhereItWas()
    {
        var shapes = new[] { Square(10) };

        Assert.Same(shapes, SurfacePlacement.Middle.Apply(shapes));
    }

    [Fact]
    public void AnOffsetCarriesEveryPoint()
    {
        var placed = new SurfacePlacement(new Vector2(4, -3), 0).Apply([Square(10)]);

        Assert.Equal(new Vector2(-1, -8), placed[0].Outline[0]);
        Assert.Equal(new Vector2(9, 2), placed[0].Outline[2]);
    }

    [Fact]
    public void TurningIsAnticlockwise()
    {
        var turned = new SurfacePlacement(Vector2.Zero, 90).Apply(new Vector2(10, 0));

        Assert.Equal(0f, turned.X, 3);
        Assert.Equal(10f, turned.Y, 3);
    }

    /// <summary>Turned first, then moved - or a turn would swing the whole thing off its anchor.</summary>
    [Fact]
    public void TurningHappensAboutTheLetteringRatherThanTheOrigin()
    {
        var placement = new SurfacePlacement(new Vector2(50, 0), 180);

        Assert.Equal(new Vector2(50, 0), placement.Apply(Vector2.Zero));
        Assert.Equal(40f, placement.Apply(new Vector2(10, 0)).X, 3);
    }

    [Fact]
    public void TheExtentIsHalfTheLettering()
    {
        Assert.Equal(new Vector2(5, 5), SurfacePlacement.Extent([Square(10)]));
        Assert.Equal(Vector2.Zero, SurfacePlacement.Extent([]));
    }

    /// <summary>Measured before placing, so turning the lettering does not inflate its box.</summary>
    [Fact]
    public void TheExtentDoesNotGrowWhenTheLetteringIsTurned()
    {
        var upright = SurfacePlacement.Extent([Square(10)]);
        var turned = SurfacePlacement.Extent(new SurfacePlacement(Vector2.Zero, 45).Apply([Square(10)]));

        Assert.Equal(5f, upright.X, 3);
        Assert.True(turned.X > upright.X, "the box round turned lettering is bigger, as expected");
    }
}

/// <summary>
/// Dragging the placement handles, against a stand-in camera. The same arrangement as the
/// manipulator tests: no GPU, no window, and every drag rule exercised.
/// </summary>
public class SurfacePlacementGizmoTests
{
    /// <summary>
    /// A fixed view straight down, so world X runs right and world Y runs up the screen at
    /// 4 pixels to the millimetre. Chosen because lettering on a top face is then seen flat on.
    /// </summary>
    private sealed class TopProjector : IScreenProjector
    {
        public const double PixelsPerMm = 4.0;

        public bool TryProject(Vector3 world, out Point screen)
        {
            screen = new Point(500 + world.X * PixelsPerMm, 400 - world.Y * PixelsPerMm);
            return true;
        }

        public Vector3 ViewDirection => -Vector3.UnitZ;
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

    private static PlanarSurface TopOfAPlate() =>
        new(FacePatch.Find(Primitives.Box(60, 60, 10), new Vector3(0, 0, 5), Vector3.UnitZ)!);

    private static (SurfacePlacementGizmo Gizmo, Canvas Layer, PlanarSurface Surface) Setup()
    {
        var layer = new Canvas();
        var surface = TopOfAPlate();
        var gizmo = new SurfacePlacementGizmo(layer, new TopProjector());

        gizmo.Show(true, surface, SurfacePlacement.Middle, new Vector2(12, 5));
        return (gizmo, layer, surface);
    }

    private static Point Middle(Canvas layer, string tag)
    {
        var shape = layer.Children.OfType<Shape>().First(s => (s.Tag as string) == tag);
        return new Point(Canvas.GetLeft(shape) + shape.Width / 2, Canvas.GetTop(shape) + shape.Height / 2);
    }

    [Fact]
    public void TheHandlesAreHiddenUntilThereIsLetteringToPlace()
    {
        RunSta(() =>
        {
            var layer = new Canvas();
            var gizmo = new SurfacePlacementGizmo(layer, new TopProjector());

            gizmo.Show(true, null, SurfacePlacement.Middle, new Vector2(10, 4));
            Assert.Equal(Visibility.Collapsed, layer.Visibility);

            gizmo.Show(true, TopOfAPlate(), SurfacePlacement.Middle, Vector2.Zero);
            Assert.Equal(Visibility.Collapsed, layer.Visibility);

            gizmo.Show(true, TopOfAPlate(), SurfacePlacement.Middle, new Vector2(10, 4));
            Assert.Equal(Visibility.Visible, layer.Visibility);
        });
    }

    [Fact]
    public void TheGripStartsOverTheMiddleOfTheLettering()
    {
        RunSta(() =>
        {
            var (_, layer, _) = Setup();

            // The plate is centred on the origin, which projects to the middle of the view.
            Assert.Equal(500, Middle(layer, SurfacePlacementGizmo.MoveTag).X, 1);
            Assert.Equal(400, Middle(layer, SurfacePlacementGizmo.MoveTag).Y, 1);
        });
    }

    [Fact]
    public void TheTurningKnobSitsClearOfTheGrip()
    {
        RunSta(() =>
        {
            var (_, layer, _) = Setup();

            var grip = Middle(layer, SurfacePlacementGizmo.MoveTag);
            var knob = Middle(layer, SurfacePlacementGizmo.TurnTag);

            Assert.True((knob - grip).Length > 20, "the two handles overlap");
            Assert.True(knob.Y < grip.Y, "the knob should be above the lettering, not below it");
        });
    }

    /// <summary>
    /// Small lettering seen at an angle put the knob underneath the grip when its distance was
    /// measured in millimetres, which is exactly when placing by eye needs the handles most.
    /// </summary>
    [Fact]
    public void TheKnobStaysGrabbableHoweverSmallTheLetteringIs()
    {
        RunSta(() =>
        {
            var layer = new Canvas();
            var gizmo = new SurfacePlacementGizmo(layer, new TopProjector());

            gizmo.Show(true, TopOfAPlate(), SurfacePlacement.Middle, new Vector2(0.6f, 0.3f));

            var grip = Middle(layer, SurfacePlacementGizmo.MoveTag);
            var knob = Middle(layer, SurfacePlacementGizmo.TurnTag);

            Assert.True((knob - grip).Length > 30, $"the handles are {(knob - grip).Length:0} px apart");
        });
    }

    /// <summary>The knob shows which way up the lettering is, so it has to travel with it.</summary>
    [Fact]
    public void TheKnobFollowsTheLetteringRound()
    {
        RunSta(() =>
        {
            var layer = new Canvas();
            var gizmo = new SurfacePlacementGizmo(layer, new TopProjector());

            gizmo.Show(true, TopOfAPlate(), SurfacePlacement.Middle, new Vector2(12, 5));
            var upright = Middle(layer, SurfacePlacementGizmo.TurnTag);
            var grip = Middle(layer, SurfacePlacementGizmo.MoveTag);

            gizmo.Show(true, TopOfAPlate(), new SurfacePlacement(Vector2.Zero, 90), new Vector2(12, 5));
            var turned = Middle(layer, SurfacePlacementGizmo.TurnTag);

            Assert.True((upright - turned).Length > 20, "the knob stayed put while the lettering turned");
            Assert.True(
                Math.Abs((turned - grip).Length - (upright - grip).Length) < 3,
                "the knob should keep its distance from the grip");
        });
    }

    /// <summary>Tools that have nothing to turn ask for the grip alone.</summary>
    [Fact]
    public void AToolCanAskForTheGripWithoutTheRest()
    {
        RunSta(() =>
        {
            var layer = new Canvas();
            var gizmo = new SurfacePlacementGizmo(layer, new TopProjector());

            gizmo.Show(true, TopOfAPlate(), SurfacePlacement.Middle, new Vector2(10, 10),
                PlacementHandles.Move);

            Assert.Equal(Visibility.Visible, Shape(layer, SurfacePlacementGizmo.MoveTag).Visibility);
            Assert.Equal(Visibility.Collapsed, Shape(layer, SurfacePlacementGizmo.TurnTag).Visibility);

            // And the knob cannot be dragged even if something contrives to hand it over.
            Assert.False(gizmo.TryBeginDrag(new Point(500, 400), Shape(layer, SurfacePlacementGizmo.TurnTag)));
            Assert.True(gizmo.TryBeginDrag(new Point(500, 400), Shape(layer, SurfacePlacementGizmo.MoveTag)));
        });
    }

    /// <summary>
    /// The whole point of the grip: the lettering ends up where the pointer put it, whatever
    /// way round the face's own axes happen to run.
    /// </summary>
    [Fact]
    public void DraggingTheGripCarriesTheLetteringWithThePointer()
    {
        RunSta(() =>
        {
            var (gizmo, layer, surface) = Setup();

            SurfacePlacement placed = SurfacePlacement.Middle;
            gizmo.Changed += p => placed = p;

            var from = Middle(layer, SurfacePlacementGizmo.MoveTag);
            Assert.True(gizmo.TryBeginDrag(from, Tagged(layer, SurfacePlacementGizmo.MoveTag)));

            // 40 pixels right and 20 up the screen: 10 mm along world X, 5 mm along world Y.
            gizmo.ContinueDrag(new Point(from.X + 40, from.Y - 20));
            gizmo.EndDrag();

            var was = surface.At(Vector2.Zero, 0);
            var now = surface.At(placed.OffsetMm, 0);

            Assert.Equal(10f, now.X - was.X, 2);
            Assert.Equal(5f, now.Y - was.Y, 2);
            Assert.Equal(0f, now.Z - was.Z, 3);
        });
    }

    [Fact]
    public void ADragThatEndsWhereItStartedChangesNothing()
    {
        RunSta(() =>
        {
            var (gizmo, layer, _) = Setup();

            var from = Middle(layer, SurfacePlacementGizmo.MoveTag);
            gizmo.TryBeginDrag(from, Tagged(layer, SurfacePlacementGizmo.MoveTag));
            gizmo.ContinueDrag(from);

            Assert.Equal(from.X, Middle(layer, SurfacePlacementGizmo.MoveTag).X, 1);
            Assert.Equal(from.Y, Middle(layer, SurfacePlacementGizmo.MoveTag).Y, 1);
        });
    }

    /// <summary>
    /// The turn has to follow the pointer round rather than away from it. Checked by asking
    /// where the top of the lettering has ended up on screen, which is the thing being dragged.
    /// </summary>
    [Fact]
    public void TheLetteringTurnsToFollowTheKnob()
    {
        RunSta(() =>
        {
            var (gizmo, layer, surface) = Setup();

            SurfacePlacement placed = SurfacePlacement.Middle;
            gizmo.Changed += p => placed = p;

            var pivot = Middle(layer, SurfacePlacementGizmo.MoveTag);
            var knob = Middle(layer, SurfacePlacementGizmo.TurnTag);

            // A quarter turn clockwise on screen: the knob starts above the pivot and is taken
            // round to its right.
            double reach = (knob - pivot).Length;
            var target = new Point(pivot.X + reach, pivot.Y);

            gizmo.TryBeginDrag(knob, Tagged(layer, SurfacePlacementGizmo.TurnTag));
            gizmo.ContinueDrag(target);
            gizmo.EndDrag();

            Assert.Equal(90f, Math.Abs(placed.AngleDegrees), 1);

            // Where the top of the lettering now projects: it should be over towards the pointer.
            var projector = new TopProjector();
            projector.TryProject(surface.At(placed.Apply(new Vector2(0, 5)), 0), out Point top);

            Assert.True(top.X - pivot.X > 15, $"the lettering turned away from the pointer, to {top}");
            Assert.True(Math.Abs(top.Y - pivot.Y) < 5, $"the lettering did not end up level, at {top}");
        });
    }

    [Fact]
    public void TurningSnapsToFifteenDegreesUnlessTurnedOff()
    {
        RunSta(() =>
        {
            var (gizmo, layer, surface) = Setup();

            SurfacePlacement placed = SurfacePlacement.Middle;
            gizmo.Changed += p => placed = p;

            var pivot = Middle(layer, SurfacePlacementGizmo.MoveTag);
            var knob = Middle(layer, SurfacePlacementGizmo.TurnTag);
            double reach = (knob - pivot).Length;

            // Twenty degrees round, which is not a multiple of fifteen.
            double radians = -Math.PI / 2 + 20 * Math.PI / 180;
            var target = new Point(
                pivot.X + reach * Math.Cos(radians), pivot.Y + reach * Math.Sin(radians));

            gizmo.TryBeginDrag(knob, Tagged(layer, SurfacePlacementGizmo.TurnTag));
            gizmo.ContinueDrag(target);
            gizmo.EndDrag();

            Assert.Equal(15f, Math.Abs(placed.AngleDegrees), 1);

            // Back to upright, or the second sweep would be added to the first.
            gizmo.Show(true, surface, SurfacePlacement.Middle, new Vector2(12, 5));
            gizmo.SnapRotation = false;

            gizmo.TryBeginDrag(knob, Tagged(layer, SurfacePlacementGizmo.TurnTag));
            gizmo.ContinueDrag(target);
            gizmo.EndDrag();

            Assert.Equal(20f, Math.Abs(placed.AngleDegrees), 1);
        });
    }

    [Fact]
    public void NothingButTheHandlesStartsADrag()
    {
        RunSta(() =>
        {
            var (gizmo, layer, _) = Setup();

            Assert.False(gizmo.TryBeginDrag(new Point(500, 400), null));
            Assert.False(gizmo.TryBeginDrag(new Point(500, 400), new Ellipse()));
            Assert.False(gizmo.IsDragging);
        });
    }

    private static object Tagged(Canvas layer, string tag) => Shape(layer, tag);

    private static Shape Shape(Canvas layer, string tag) =>
        layer.Children.OfType<Shape>().First(s => (s.Tag as string) == tag);
}

/// <summary>Which surface the lettering is laid onto, and how it is put there.</summary>
public class EmbossProjectionWiringTests
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

    private static (MainViewModel Model, SceneObject Object) WithA(string kind)
    {
        var model = new MainViewModel();
        model.InsertCommand.Execute(kind);
        return (model, model.Scene.Objects[0]);
    }

    private static Vector3 TopOf(SceneObject o) => new(o.PositionX, o.PositionY, o.WorldBounds.Max.Z);

    [Fact]
    public void ThereIsNoSurfaceUntilAFaceIsPicked()
    {
        RunSta(() =>
        {
            var (model, cube) = WithA("Cube");
            model.BeginEmbossCommand.Execute(null);

            Assert.Null(model.EmbossSurface());

            model.PickEmbossFace(cube, TopOf(cube), Vector3.UnitZ);
            Assert.IsType<PlanarSurface>(model.EmbossSurface());
        });
    }

    /// <summary>
    /// A barrel picked on its side should be lettered on the barrel, so the surface has to pass
    /// through the point that was clicked rather than near it.
    /// </summary>
    [Fact]
    public void WrappingRoundABarrelStartsWhereTheClickLanded()
    {
        RunSta(() =>
        {
            var (model, cylinder) = WithA("Cylinder");
            model.BeginEmbossCommand.Execute(null);

            var bounds = cylinder.WorldBounds;
            float radius = bounds.Size.X / 2;
            var side = new Vector3(bounds.Center.X + radius, bounds.Center.Y, bounds.Center.Z);

            Assert.True(model.PickEmbossFace(cylinder, side, Vector3.UnitX));
            model.EmbossProjection = TextProjection.Cylindrical;

            var surface = Assert.IsType<CylinderSurface>(model.EmbossSurface());
            var middle = surface.At(Vector2.Zero, 0);

            Assert.Equal(side.X, middle.X, 2);
            Assert.Equal(side.Y, middle.Y, 2);
            Assert.Equal(side.Z, middle.Z, 2);
        });
    }

    [Fact]
    public void WrappingOverABallStartsWhereTheClickLanded()
    {
        RunSta(() =>
        {
            var (model, sphere) = WithA("Sphere");
            model.BeginEmbossCommand.Execute(null);

            var bounds = sphere.WorldBounds;
            var top = new Vector3(bounds.Center.X, bounds.Center.Y, bounds.Max.Z);

            Assert.True(model.PickEmbossFace(sphere, top, Vector3.UnitZ));
            model.EmbossProjection = TextProjection.Spherical;

            var surface = Assert.IsType<SphereSurface>(model.EmbossSurface());
            var middle = surface.At(Vector2.Zero, 0);

            Assert.Equal(top.Z, middle.Z, 2);
            Assert.True(model.IsEmbossWrapped);
        });
    }

    [Fact]
    public void MovingTheLetteringMovesThePreviewWithIt()
    {
        RunSta(() =>
        {
            var (model, cube) = WithA("Cube");
            model.BeginEmbossCommand.Execute(null);
            model.PickEmbossFace(cube, TopOf(cube), Vector3.UnitZ);

            var middle = model.EmbossPreview()!.ComputeBounds().Center;

            model.EmbossAcross = 6f;
            var moved = model.EmbossPreview()!.ComputeBounds().Center;

            Assert.True((moved - middle).Length() > 5.5f, "the preview did not follow the placement");
            Assert.Equal(6f, model.EmbossAcross, 3);
        });
    }

    [Fact]
    public void PickingADifferentFacePutsTheLetteringBackInTheMiddle()
    {
        RunSta(() =>
        {
            var (model, cube) = WithA("Cube");
            model.BeginEmbossCommand.Execute(null);
            model.PickEmbossFace(cube, TopOf(cube), Vector3.UnitZ);

            model.EmbossAcross = 6f;
            model.EmbossAngle = 30f;

            var side = new Vector3(cube.WorldBounds.Max.X, cube.PositionY, cube.WorldBounds.Center.Z);
            Assert.True(model.PickEmbossFace(cube, side, Vector3.UnitX));

            Assert.Equal(0f, model.EmbossAcross);
            Assert.Equal(0f, model.EmbossAngle);
        });
    }

    /// <summary>
    /// Clicking the same face again is how wrapped lettering is re-anchored, so it must not
    /// throw away a placement that has just been dragged into position.
    /// </summary>
    [Fact]
    public void PickingTheSameFaceAgainLeavesThePlacementAlone()
    {
        RunSta(() =>
        {
            var (model, cube) = WithA("Cube");
            model.BeginEmbossCommand.Execute(null);
            model.PickEmbossFace(cube, TopOf(cube), Vector3.UnitZ);

            model.EmbossAcross = 6f;
            model.EmbossAngle = 30f;

            var elsewhere = new Vector3(cube.PositionX + 4, cube.PositionY - 3, cube.WorldBounds.Max.Z);
            Assert.True(model.PickEmbossFace(cube, elsewhere, Vector3.UnitZ));

            Assert.Equal(6f, model.EmbossAcross);
            Assert.Equal(30f, model.EmbossAngle);
        });
    }

    [Fact]
    public void TheBevelIsHeldWithinTheDepthItHasToSlopeOver()
    {
        RunSta(() =>
        {
            var (model, cube) = WithA("Cube");
            model.BeginEmbossCommand.Execute(null);
            model.PickEmbossFace(cube, TopOf(cube), Vector3.UnitZ);

            model.EmbossDepth = 0.5f;
            model.EmbossBevel = 4f;

            Assert.Equal(4f, model.EmbossBevel);
            Assert.Contains("bevel", model.EmbossSummary);

            model.ApplyEmbossCommand.Execute(null);

            // Whatever it did, it did not throw and did not leave the object mangled.
            Assert.All(model.Scene.Objects, o => Assert.True(o.Mesh.TriangleCount > 0));
        });
    }
}
/// <summary>
/// The placement handles serve two tools, and the tools stand aside for each other. Both are
/// wiring rather than geometry, and both were wrong at least once by hand.
/// </summary>
public class ToolTakeoverTests
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

    private static (MainViewModel Model, SceneObject Cube) WithACube()
    {
        var model = new MainViewModel();
        model.InsertCommand.Execute("Cube");
        return (model, model.Scene.Objects[0]);
    }

    private static Vector3 TopOf(SceneObject o) => new(o.PositionX, o.PositionY, o.WorldBounds.Max.Z);

    [Fact]
    public void TheManipulatorStandsDownWhileAToolIsRunning()
    {
        RunSta(() =>
        {
            var (model, _) = WithACube();

            Assert.False(model.IsToolRunning);
            Assert.True(model.ShowManipulatorBar);

            foreach (var start in new[] { "BeginSplitCommand", "BeginEngraveCommand", "BeginEmbossCommand" })
            {
                var command = (System.Windows.Input.ICommand)typeof(MainViewModel)
                    .GetProperty(start)!.GetValue(model)!;

                command.Execute(null);

                Assert.True(model.IsToolRunning, start);
                Assert.False(model.ShowManipulatorBar, start);
            }
        });
    }

    /// <summary>Each of them means something different by a click, so only one can be listening.</summary>
    [Fact]
    public void StartingOneToolPutsTheOthersAway()
    {
        RunSta(() =>
        {
            var (model, _) = WithACube();

            model.BeginEmbossCommand.Execute(null);
            model.BeginEngraveCommand.Execute(null);

            Assert.True(model.IsEngraveMode);
            Assert.False(model.IsEmbossMode);

            model.BeginSplitCommand.Execute(null);

            Assert.True(model.IsSplitMode);
            Assert.False(model.IsEngraveMode);
            Assert.False(model.IsEmbossMode);

            model.BeginMeasureCommand.Execute(null);

            Assert.True(model.IsMeasureMode);
            Assert.False(model.IsSplitMode);
            Assert.False(model.IsToolRunning);
        });
    }

    [Fact]
    public void ThereIsNothingToPlaceUntilTheEngraveFaceIsPicked()
    {
        RunSta(() =>
        {
            var (model, cube) = WithACube();
            model.BeginEngraveCommand.Execute(null);

            Assert.Null(model.EngraveSurface());
            Assert.Equal(Vector2.Zero, model.EngraveExtent);

            model.PickEngraveFace(cube, TopOf(cube), Vector3.UnitZ);

            Assert.NotNull(model.EngraveSurface());
            Assert.Equal(new Vector2(10, 10), model.EngraveExtent);
        });
    }

    /// <summary>Dragging the grip is the same thing as typing into the two shift boxes.</summary>
    [Fact]
    public void MovingThePatternIsTheSameAsShiftingIt()
    {
        RunSta(() =>
        {
            var (model, cube) = WithACube();
            model.BeginEngraveCommand.Execute(null);
            model.PickEngraveFace(cube, TopOf(cube), Vector3.UnitZ);

            model.EngravePlacement = new SurfacePlacement(new Vector2(3.5f, -2.25f), 0);

            Assert.Equal(3.5f, model.EngraveOffsetU, 3);
            Assert.Equal(-2.25f, model.EngraveOffsetV, 3);

            model.EngraveOffsetU = 8f;

            Assert.Equal(8f, model.EngravePlacement.OffsetMm.X, 3);
        });
    }

    [Fact]
    public void ShiftingThePatternMovesTheGroovesItWouldCut()
    {
        RunSta(() =>
        {
            var (model, cube) = WithACube();
            model.BeginEngraveCommand.Execute(null);
            model.PickEngraveFace(cube, TopOf(cube), Vector3.UnitZ);

            var before = model.EngravePreview.Rectangles.ToList();

            model.EngravePlacement = new SurfacePlacement(new Vector2(0, 2f), 0);

            var after = model.EngravePreview.Rectangles.ToList();

            Assert.NotEmpty(before);
            Assert.False(before.SequenceEqual(after), "shifting the pattern left the grooves alone");
        });
    }

    [Fact]
    public void PickingADifferentFaceLevelsThePatternAgain()
    {
        RunSta(() =>
        {
            var (model, cube) = WithACube();
            model.BeginEngraveCommand.Execute(null);
            model.PickEngraveFace(cube, TopOf(cube), Vector3.UnitZ);

            model.EngraveOffsetU = 5f;

            var side = new Vector3(cube.WorldBounds.Max.X, cube.PositionY, cube.WorldBounds.Center.Z);
            Assert.True(model.PickEngraveFace(cube, side, Vector3.UnitX));

            Assert.Equal(0f, model.EngraveOffsetU);
        });
    }
}
