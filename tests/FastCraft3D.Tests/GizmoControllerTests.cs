using System.Numerics;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FastCraft3D.Geometry;
using FastCraft3D.Model;
using FastCraft3D.Model.Commands;
using FastCraft3D.Render;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>
/// Drives the manipulator against a stand-in camera. Because the controller only asks for a
/// projection, the handle layout and every drag can be exercised with no GPU and no window -
/// which is the only way this behaviour gets checked automatically, since WPF shapes expose
/// nothing to accessibility tools.
/// </summary>
public class GizmoControllerTests
{
    /// <summary>
    /// A fixed orthographic view down -Y: world X runs right, world Z runs up the screen, and
    /// 4 pixels is one millimetre. Simple enough that expected pixel values can be hand-checked.
    /// </summary>
    private sealed class FrontProjector : IScreenProjector
    {
        public const double PixelsPerMm = 4.0;

        /// <summary>Set false to stand in for a viewport mid-resize.</summary>
        public bool Ready { get; set; } = true;

        public bool TryProject(Vector3 world, out Point screen)
        {
            // A viewport that cannot project collapses everything onto one point.
            screen = Ready
                ? new Point(500 + world.X * PixelsPerMm, 400 - world.Z * PixelsPerMm)
                : new Point(0, 0);
            return true;
        }

        public Vector3 ViewDirection => Vector3.UnitY;

        public bool IsReady => Ready;
    }

    /// <summary>WPF objects must be created on an STA thread, which xUnit does not use.</summary>
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

    private static (GizmoController Gizmo, Scene Scene, UndoStack Undo, SceneObject Cube) Setup(GizmoMode mode)
    {
        var scene = new Scene();
        var cube = new SceneObject("Cube", Primitives.Box(20, 20, 20)) { IsSelected = true };
        scene.Objects.Add(cube);

        var undo = new UndoStack(scene);
        var gizmo = new GizmoController(new Canvas(), new FrontProjector(), scene, undo) { Mode = mode };
        gizmo.Rebuild();
        return (gizmo, scene, undo, cube);
    }

    [Fact]
    public void MoveModeLaysOutOneArrowPerAxisDirection()
    {
        RunSta(() =>
        {
            var (gizmo, _, _, _) = Setup(GizmoMode.Move);

            // Both ends of all three axes.
            Assert.Equal(6, gizmo.HandleCount);
        });
    }

    [Fact]
    public void RotateModeLaysOutOneRingPerAxis()
    {
        RunSta(() =>
        {
            var (gizmo, _, _, _) = Setup(GizmoMode.Rotate);

            Assert.Equal(3, gizmo.HandleCount);
        });
    }

    [Fact]
    public void ScaleModeAddsTheBoundingBoxAndItsCorners()
    {
        RunSta(() =>
        {
            var (gizmo, _, _, _) = Setup(GizmoMode.Scale);

            // Six arrows, one box outline, eight corner dots.
            Assert.Equal(15, gizmo.HandleCount);
        });
    }

    [Fact]
    public void NothingIsLaidOutWithoutASelection()
    {
        RunSta(() =>
        {
            var scene = new Scene();
            var gizmo = new GizmoController(new Canvas(), new FrontProjector(), scene, new UndoStack(scene));

            gizmo.Rebuild();

            Assert.Equal(0, gizmo.HandleCount);
        });
    }

    // --- Dragging ---------------------------------------------------------------------

    /// <summary>Reaches into the canvas for the handle a real click would have landed on.</summary>
    private static FrameworkElement HandleFor(Canvas canvas, int index) =>
        (FrameworkElement)canvas.Children[index];

    private static GizmoController Build(Canvas canvas, Scene scene, UndoStack undo, GizmoMode mode)
    {
        var gizmo = new GizmoController(canvas, new FrontProjector(), scene, undo) { Mode = mode };
        gizmo.Rebuild();
        return gizmo;
    }

    [Fact]
    public void DraggingTheXArrowMovesTheObjectAlongX()
    {
        RunSta(() =>
        {
            var scene = new Scene();
            var cube = new SceneObject("Cube", Primitives.Box(20, 20, 20)) { IsSelected = true };
            scene.Objects.Add(cube);
            var undo = new UndoStack(scene);
            var canvas = new Canvas();
            var gizmo = Build(canvas, scene, undo, GizmoMode.Move);

            // The first arrow built is +X (see BuildAxisArrows).
            var handle = HandleFor(canvas, 0);
            Assert.True(gizmo.TryBeginDrag(new Point(0, 0), handle));

            // 40 px to the right at 4 px/mm is 10 mm.
            gizmo.ContinueDrag(new Point(40, 0));

            Assert.Equal(10f, cube.Position.X, 3);
            Assert.Equal(0f, cube.Position.Y, 3);
            Assert.Equal(0f, cube.Position.Z, 3);
        });
    }

    /// <summary>
    /// Stop on contact, driven through the manipulator as a real drag. A cone is the case the
    /// bounding box gets worst: its box is its base, so a box sweep parks the ball a whole base
    /// radius short of the slope it was being brought up against.
    /// </summary>
    [Fact]
    public void StoppingOnContactMeetsTheShapeRatherThanItsBox()
    {
        RunSta(() =>
        {
            var scene = new Scene();
            var ball = new SceneObject("Ball", Primitives.Create(PrimitiveKind.Sphere)) { IsSelected = true };
            var cone = new SceneObject("Cone", Primitives.Create(PrimitiveKind.Cone))
            {
                Position = new Vector3(40, 0, 0)
            };

            scene.Objects.Add(ball);
            scene.Objects.Add(cone);

            var canvas = new Canvas();
            var gizmo = Build(canvas, scene, new UndoStack(scene), GizmoMode.Move);
            gizmo.StopOnContact = true;

            gizmo.TryBeginDrag(new Point(0, 0), HandleFor(canvas, 0));
            gizmo.ContinueDrag(new Point(4000, 0));   // far further than it can possibly go
            gizmo.EndDrag();

            // The box would have stopped it at 20 mm, where the boxes meet.
            Assert.True(ball.Position.X > 23f, $"stopped short at {ball.Position.X:0.##} mm");

            // And it is touching rather than through: the ball's surface meets the cone's slope.
            var both = FastCraft3D.Geometry.Csg.CsgSolid.Intersect(ball.ToWorldMesh(), cone.ToWorldMesh());
            Assert.True(
                both.TriangleCount == 0 || Math.Abs(both.ComputeSignedVolume()) < 1e-3,
                "the ball ended up inside the cone");
        });
    }

    [Fact]
    public void WithoutStopOnContactADragGoesStraightThrough()
    {
        RunSta(() =>
        {
            var scene = new Scene();
            var cube = new SceneObject("Cube", Primitives.Box(20, 20, 20)) { IsSelected = true };
            scene.Objects.Add(cube);
            scene.Objects.Add(new SceneObject("Wall", Primitives.Box(20, 20, 20))
            {
                Position = new Vector3(30, 0, 0)
            });

            var canvas = new Canvas();
            var gizmo = Build(canvas, scene, new UndoStack(scene), GizmoMode.Move);

            gizmo.TryBeginDrag(new Point(0, 0), HandleFor(canvas, 0));
            gizmo.ContinueDrag(new Point(200, 0));   // 50 mm
            gizmo.EndDrag();

            Assert.Equal(50f, cube.Position.X, 3);
        });
    }

    [Fact]
    public void AFinishedDragBecomesExactlyOneUndoStep()
    {
        RunSta(() =>
        {
            var scene = new Scene();
            var cube = new SceneObject("Cube", Primitives.Box(20, 20, 20)) { IsSelected = true };
            scene.Objects.Add(cube);
            var undo = new UndoStack(scene);
            var canvas = new Canvas();
            var gizmo = Build(canvas, scene, undo, GizmoMode.Move);

            gizmo.TryBeginDrag(new Point(0, 0), HandleFor(canvas, 0));
            // Several intermediate moves, as a real drag produces.
            gizmo.ContinueDrag(new Point(10, 0));
            gizmo.ContinueDrag(new Point(25, 0));
            gizmo.ContinueDrag(new Point(40, 0));
            gizmo.EndDrag();

            Assert.True(undo.CanUndo);
            Assert.Equal(10f, cube.Position.X, 3);

            undo.Undo();

            Assert.Equal(0f, cube.Position.X, 3);
            Assert.False(undo.CanUndo);
        });
    }

    [Fact]
    public void ADragThatMovesNothingLeavesNoUndoStep()
    {
        RunSta(() =>
        {
            var scene = new Scene();
            var cube = new SceneObject("Cube", Primitives.Box(20, 20, 20)) { IsSelected = true };
            scene.Objects.Add(cube);
            var undo = new UndoStack(scene);
            var canvas = new Canvas();
            var gizmo = Build(canvas, scene, undo, GizmoMode.Move);

            gizmo.TryBeginDrag(new Point(0, 0), HandleFor(canvas, 0));
            gizmo.EndDrag();

            Assert.False(undo.CanUndo);
        });
    }

    [Fact]
    public void ClickingSomethingThatIsNotAHandleDoesNotStartADrag()
    {
        RunSta(() =>
        {
            var scene = new Scene();
            scene.Objects.Add(new SceneObject("Cube", Primitives.Box(20, 20, 20)) { IsSelected = true });
            var canvas = new Canvas();
            var gizmo = Build(canvas, scene, new UndoStack(scene), GizmoMode.Move);

            Assert.False(gizmo.TryBeginDrag(new Point(0, 0), new Button()));
            Assert.False(gizmo.TryBeginDrag(new Point(0, 0), null));
            Assert.False(gizmo.IsDragging);
        });
    }

    /// <summary>The box outline and corner dots are decoration - they must not be draggable.</summary>
    [Fact]
    public void TheBoundingBoxDecorationIsNotDraggable()
    {
        RunSta(() =>
        {
            var scene = new Scene();
            scene.Objects.Add(new SceneObject("Cube", Primitives.Box(20, 20, 20)) { IsSelected = true });
            var canvas = new Canvas();
            var gizmo = Build(canvas, scene, new UndoStack(scene), GizmoMode.Scale);

            // In scale mode the outline is built first, then the eight corner dots.
            Assert.False(gizmo.TryBeginDrag(new Point(0, 0), HandleFor(canvas, 0)));
            Assert.False(gizmo.TryBeginDrag(new Point(0, 0), HandleFor(canvas, 4)));
        });
    }

    [Fact]
    public void DraggingAScaleArrowOutwardGrowsTheObject()
    {
        RunSta(() =>
        {
            var scene = new Scene();
            var cube = new SceneObject("Cube", Primitives.Box(20, 20, 20)) { IsSelected = true };
            scene.Objects.Add(cube);
            var canvas = new Canvas();
            var gizmo = Build(canvas, scene, new UndoStack(scene), GizmoMode.Scale);
            gizmo.UniformScale = false;

            // Index 9 is the +X arrow: outline (1) + corners (8) come first.
            Assert.True(gizmo.TryBeginDrag(new Point(0, 0), HandleFor(canvas, 9)));
            gizmo.ContinueDrag(new Point(20, 0)); // 20 px right = 5 mm outward

            // Both faces move, so 20 mm becomes 30 mm.
            Assert.Equal(30f, cube.SizeX, 2);
            Assert.Equal(20f, cube.SizeY, 2);
        });
    }

    [Fact]
    public void UniformScaleGrowsEveryAxisTogether()
    {
        RunSta(() =>
        {
            var scene = new Scene();
            var cube = new SceneObject("Cube", Primitives.Box(20, 20, 20)) { IsSelected = true };
            scene.Objects.Add(cube);
            var canvas = new Canvas();
            var gizmo = Build(canvas, scene, new UndoStack(scene), GizmoMode.Scale);
            gizmo.UniformScale = true;

            gizmo.TryBeginDrag(new Point(0, 0), HandleFor(canvas, 9));
            gizmo.ContinueDrag(new Point(20, 0));

            Assert.Equal(30f, cube.SizeX, 2);
            Assert.Equal(30f, cube.SizeY, 2);
            Assert.Equal(30f, cube.SizeZ, 2);
        });
    }

    /// <summary>Ctrl turns Keep proportions the other way for as long as it is held.</summary>
    [Fact]
    public void HoldingCtrlKeepsProportionsWhenTheLockIsOff()
    {
        RunSta(() =>
        {
            var scene = new Scene();
            var cube = new SceneObject("Cube", Primitives.Box(20, 20, 20)) { IsSelected = true };
            scene.Objects.Add(cube);
            var canvas = new Canvas();
            var gizmo = Build(canvas, scene, new UndoStack(scene), GizmoMode.Scale);
            gizmo.UniformScale = false;
            gizmo.Modifiers = ModifierKeys.Control;

            gizmo.TryBeginDrag(new Point(0, 0), HandleFor(canvas, 9));
            gizmo.ContinueDrag(new Point(20, 0));

            Assert.Equal(30f, cube.SizeX, 2);
            Assert.Equal(30f, cube.SizeY, 2);
            Assert.Equal(30f, cube.SizeZ, 2);
        });
    }

    /// <summary>Alt holds the far face still when One way only is off, and the part moves to suit.</summary>
    [Fact]
    public void HoldingAltGrowsOneWayWhenThatIsOff()
    {
        RunSta(() =>
        {
            var scene = new Scene();
            var cube = new SceneObject("Cube", Primitives.Box(20, 20, 20)) { IsSelected = true };
            scene.Objects.Add(cube);
            var canvas = new Canvas();
            var gizmo = Build(canvas, scene, new UndoStack(scene), GizmoMode.Scale);
            gizmo.UniformScale = false;
            gizmo.ScaleOneSide = false;
            gizmo.Modifiers = ModifierKeys.Alt;

            gizmo.TryBeginDrag(new Point(0, 0), HandleFor(canvas, 9));
            gizmo.ContinueDrag(new Point(20, 0)); // 5 mm outward, on one face only

            Assert.Equal(25f, cube.SizeX, 2);
            Assert.Equal(2.5f, cube.Position.X, 2); // the -X face stays at -10
        });
    }

    /// <summary>Shift lands the dragged side on a whole millimetre, whatever the pointer did.</summary>
    [Fact]
    public void HoldingShiftSnapsTheSizeToWholeMillimetres()
    {
        RunSta(() =>
        {
            var scene = new Scene();
            var cube = new SceneObject("Cube", Primitives.Box(20, 20, 20)) { IsSelected = true };
            scene.Objects.Add(cube);
            var canvas = new Canvas();
            var gizmo = Build(canvas, scene, new UndoStack(scene), GizmoMode.Scale);
            gizmo.UniformScale = false;
            gizmo.ScaleOneSide = true;
            gizmo.Modifiers = ModifierKeys.Shift;

            gizmo.TryBeginDrag(new Point(0, 0), HandleFor(canvas, 9));
            gizmo.ContinueDrag(new Point(21, 0)); // 5.25 mm: 25.25 unsnapped

            Assert.Equal(25f, cube.SizeX, 3);
        });
    }

    /// <summary>
    /// A key pressed or let go mid-drag shows at once, without waiting for the mouse to move.
    /// </summary>
    [Fact]
    public void AModifierChangesTheDragWithoutTheMouseMoving()
    {
        RunSta(() =>
        {
            var scene = new Scene();
            var cube = new SceneObject("Cube", Primitives.Box(20, 20, 20)) { IsSelected = true };
            scene.Objects.Add(cube);
            var canvas = new Canvas();
            var gizmo = Build(canvas, scene, new UndoStack(scene), GizmoMode.Scale);
            gizmo.UniformScale = false;

            gizmo.TryBeginDrag(new Point(0, 0), HandleFor(canvas, 9));
            gizmo.ContinueDrag(new Point(20, 0));
            Assert.Equal(20f, cube.SizeY, 2);

            gizmo.Modifiers = ModifierKeys.Control;
            gizmo.Refresh();
            Assert.Equal(30f, cube.SizeY, 2);

            gizmo.Modifiers = ModifierKeys.None;
            gizmo.Refresh();
            Assert.Equal(20f, cube.SizeY, 2);
        });
    }

    [Fact]
    public void DraggingARingRotatesAndSnaps()
    {
        RunSta(() =>
        {
            var scene = new Scene();
            var cube = new SceneObject("Cube", Primitives.Box(20, 20, 20)) { IsSelected = true };
            scene.Objects.Add(cube);
            var canvas = new Canvas();
            var gizmo = Build(canvas, scene, new UndoStack(scene), GizmoMode.Rotate);
            gizmo.SnapRotation = true;

            // The Y ring. The camera looks along +Y, so this axis points away from the viewer.
            var ring = HandleFor(canvas, 1);

            // Sweep from due-right of the centre to straight above it - anticlockwise on screen.
            Assert.True(gizmo.TryBeginDrag(new Point(600, 400), ring));
            gizmo.ContinueDrag(new Point(500, 300));

            // The viewer sees X to the right and Z upward. By the right-hand rule a positive
            // turn about +Y carries +Z towards +X, which reads as clockwise from here - so an
            // anticlockwise sweep must come out negative. Asserting the magnitude alone would
            // let a sign error through, and rotating the wrong way is exactly what gets noticed.
            Assert.Equal(-90f, cube.Rotation.Y, 2);
            Assert.Equal(0f, cube.Rotation.X, 3);
            Assert.Equal(0f, cube.Rotation.Z, 3);
        });
    }

    /// <summary>
    /// Handles must sit around the object wherever it is. They used to be stretched between the
    /// object and the plate centre, because the empty bounding box that the selection union
    /// started from was really a zero-size box at the origin, so every union dragged the origin
    /// in. Anything at the origin hid it, which is why it survived the first round of tests.
    /// </summary>
    [Fact]
    public void HandlesFollowAnObjectAwayFromTheOrigin()
    {
        RunSta(() =>
        {
            var scene = new Scene();
            var cube = new SceneObject("Cube", Primitives.Box(20, 20, 20))
            {
                Position = new Vector3(60, 0, 10),
                IsSelected = true
            };
            scene.Objects.Add(cube);

            var canvas = new Canvas();
            Build(canvas, scene, new UndoStack(scene), GizmoMode.Move);

            // Where the projector puts the object's centre: 500 + 60*4 = 740, 400 - 10*4 = 360.
            const double centreX = 740, centreY = 360;

            int visible = 0;
            foreach (FrameworkElement handle in canvas.Children)
            {
                // This stand-in camera looks straight down -Y, so the two Y arrows point at the
                // viewer and are deliberately hidden - they are never given a position.
                if (handle.Visibility != Visibility.Visible) continue;

                visible++;
                double x = Canvas.GetLeft(handle) + handle.Width / 2;
                double y = Canvas.GetTop(handle) + handle.Height / 2;
                double distance = Math.Sqrt(Math.Pow(x - centreX, 2) + Math.Pow(y - centreY, 2));

                // A 20 mm cube at 4 px/mm spans 80 px, so every handle belongs well inside this
                // radius. Dragging the origin in would push one out past 300 px.
                Assert.True(distance < 120,
                    $"a handle sits {distance:0} px from the object - it is being stretched towards the origin");
            }

            // The four arrows that have a direction on screen: both ends of X and of Z.
            Assert.Equal(4, visible);
        });
    }

    [Fact]
    public void RotationRingsAreSizedByTheObjectNotItsDistanceFromTheOrigin()
    {
        RunSta(() =>
        {
            var near = RingWidth(Vector3.Zero);
            var far = RingWidth(new Vector3(120, 0, 10));

            // The same cube must give the same rings wherever it stands.
            Assert.Equal(near, far, 1);
        });

        static double RingWidth(Vector3 position)
        {
            var scene = new Scene();
            scene.Objects.Add(new SceneObject("Cube", Primitives.Box(20, 20, 20))
            {
                Position = position,
                IsSelected = true
            });

            var canvas = new Canvas();
            Build(canvas, scene, new UndoStack(scene), GizmoMode.Rotate);
            return ((System.Windows.Shapes.Path)canvas.Children[0]).Data!.Bounds.Width;
        }
    }

    /// <summary>
    /// While the viewport is resizing it cannot project, and everything lands on one point.
    /// Handles must be hidden rather than placed at whatever came back - that is how they ended
    /// up stranded in the corner of the window after a resize.
    /// </summary>
    [Fact]
    public void HandlesAreHiddenWhileTheViewportCannotProject()
    {
        RunSta(() =>
        {
            var scene = new Scene();
            scene.Objects.Add(new SceneObject("Cube", Primitives.Box(20, 20, 20)) { IsSelected = true });

            var canvas = new Canvas();
            var projector = new FrontProjector { Ready = false };
            var gizmo = new GizmoController(canvas, projector, scene, new UndoStack(scene))
            {
                Mode = GizmoMode.Move
            };
            gizmo.Rebuild();

            Assert.Equal(Visibility.Collapsed, canvas.Visibility);
            Assert.True(gizmo.NeedsReposition, "the failed layout was not flagged for a retry");
        });
    }

    /// <summary>
    /// And the retry has to actually happen. The per-frame update skips work when the camera has
    /// not moved, so without this flag a layout taken mid-resize would stick indefinitely.
    /// </summary>
    [Fact]
    public void HandlesComeBackOnceTheViewportCanProjectAgain()
    {
        RunSta(() =>
        {
            var scene = new Scene();
            scene.Objects.Add(new SceneObject("Cube", Primitives.Box(20, 20, 20)) { IsSelected = true });

            var canvas = new Canvas();
            var projector = new FrontProjector { Ready = false };
            var gizmo = new GizmoController(canvas, projector, scene, new UndoStack(scene))
            {
                Mode = GizmoMode.Move
            };
            gizmo.Rebuild();
            Assert.True(gizmo.NeedsReposition);

            // The resize finishes.
            projector.Ready = true;
            gizmo.Reposition();

            Assert.False(gizmo.NeedsReposition);
            Assert.Equal(Visibility.Visible, canvas.Visibility);

            // And the handles are back around the object, not at the origin.
            var placed = canvas.Children.Cast<FrameworkElement>()
                .Where(h => h.Visibility == Visibility.Visible)
                .Select(h => Canvas.GetLeft(h) + h.Width / 2)
                .ToList();

            Assert.NotEmpty(placed);
            Assert.All(placed, x => Assert.True(Math.Abs(x - 500) < 120,
                $"a handle is at x={x:0}, nowhere near the object at x=500"));
        });
    }

    [Fact]
    public void MovingAMultipleSelectionShiftsEveryObjectEqually()
    {
        RunSta(() =>
        {
            var scene = new Scene();
            var first = new SceneObject("A", Primitives.Box(20, 20, 20)) { IsSelected = true };
            var second = new SceneObject("B", Primitives.Box(10, 10, 10))
            {
                IsSelected = true,
                Position = new Vector3(50, 0, 0)
            };
            scene.Objects.Add(first);
            scene.Objects.Add(second);

            var canvas = new Canvas();
            var gizmo = Build(canvas, scene, new UndoStack(scene), GizmoMode.Move);

            gizmo.TryBeginDrag(new Point(0, 0), HandleFor(canvas, 0));
            gizmo.ContinueDrag(new Point(40, 0));

            Assert.Equal(10f, first.Position.X, 3);
            Assert.Equal(60f, second.Position.X, 3);
        });
    }
}
