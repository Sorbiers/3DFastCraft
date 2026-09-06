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

/// <summary>
/// Reading three angles back out of a turn. Needed because turning something about a ring on
/// screen is not the same as adding to one of its three angles: they are applied in order, so
/// only the last of them lines up with the world.
/// </summary>
public class EulerTests
{
    private static void Same(Matrix4x4 expected, Matrix4x4 actual, string what)
    {
        foreach (var v in new[] { Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ, new Vector3(3, -7, 11) })
        {
            var a = Vector3.Transform(v, expected);
            var b = Vector3.Transform(v, actual);

            Assert.True((a - b).Length() < 1e-3f, $"{what}: {v} went to {a} rather than {b}");
        }
    }

    [Theory]
    [InlineData(0f, 0f, 0f)]
    [InlineData(30f, 0f, 0f)]
    [InlineData(0f, 45f, 0f)]
    [InlineData(0f, 0f, 90f)]
    [InlineData(-30f, 40f, 120f)]
    [InlineData(15f, -75f, -160f)]
    [InlineData(179f, 12f, -3f)]
    public void TheAnglesComeBackAsTheTurnTheyBuilt(float x, float y, float z)
    {
        var turn = MeshTransform.Rotation(new Vector3(x, y, z));

        Same(turn, MeshTransform.Rotation(MeshTransform.EulerFrom(turn)), "round trip");
    }

    /// <summary>
    /// Straight up or straight down, the first and last angles turn about the same line and only
    /// their sum can be known. Any answer will do so long as it is the same turn.
    /// </summary>
    [Theory]
    [InlineData(0f, 90f, 0f)]
    [InlineData(40f, 90f, 25f)]
    [InlineData(-30f, -90f, 90f)]
    public void EvenStraightUpTheTurnComesBackRight(float x, float y, float z)
    {
        var turn = MeshTransform.Rotation(new Vector3(x, y, z));

        Same(turn, MeshTransform.Rotation(MeshTransform.EulerFrom(turn)), "gimbal lock");
    }

    [Fact]
    public void AQuarterTurnReadsAsNinetyRatherThanEightyNinePointNine()
    {
        var angles = MeshTransform.EulerFrom(MeshTransform.Rotation(new Vector3(0, 0, 90)));

        Assert.Equal(90f, angles.Z);
        Assert.Equal(0f, angles.X);
    }
}

/// <summary>
/// Turning an object with the rings. The ring you grab has to be the axis it turns about, on the
/// second turn as much as the first.
/// </summary>
public class RotateHandleTests
{
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

    /// <summary>Where a corner of the object ends up, which is what the eye is judging.</summary>
    private static Vector3 CornerOf(SceneObject o) =>
        Vector3.Transform(new Vector3(10, 0, 0), MeshTransform.Rotation(o.Rotation));

    /// <summary>
    /// The reported case: turn it once, then grab a different ring. Whatever it had before, a
    /// quarter turn about world Z has to send a corner on +X round to +Y.
    /// </summary>
    [Theory]
    [InlineData(0f, 0f, 0f)]
    [InlineData(0f, 90f, 90f)]
    [InlineData(-30f, 90f, 90f)]
    [InlineData(45f, 30f, 60f)]
    public void TurningAboutZSendsXRoundToYWhateverCameBefore(float x, float y, float z)
    {
        var start = new Vector3(x, y, z);
        var turned = MeshTransform.EulerFrom(
            MeshTransform.Rotation(start) * Matrix4x4.CreateRotationZ(MathF.PI / 2));

        var was = Vector3.Transform(new Vector3(10, 0, 0), MeshTransform.Rotation(start));
        var now = Vector3.Transform(new Vector3(10, 0, 0), MeshTransform.Rotation(turned));

        // A quarter turn about Z: x goes to y, y goes to -x, z stays put.
        Assert.Equal(-was.Y, now.X, 2);
        Assert.Equal(was.X, now.Y, 2);
        Assert.Equal(was.Z, now.Z, 2);
    }

    [Fact]
    public void ASecondTurnFollowsTheRingItWasDraggedOn()
    {
        RunSta(() =>
        {
            var scene = new Scene();
            var cube = new SceneObject("Cube", Primitives.Box(20, 20, 20))
            {
                IsSelected = true,
                Rotation = new Vector3(-30, 90, 90)      // the state in the report
            };
            scene.Objects.Add(cube);

            var canvas = new Canvas();
            var gizmo = new GizmoController(canvas, new FrontProjector(), scene, new UndoStack(scene))
            {
                Mode = GizmoMode.Rotate,
                SnapRotation = false
            };
            gizmo.Rebuild();

            var before = CornerOf(cube);

            // The first ring built is X. Drag it a good way round.
            gizmo.TryBeginDrag(new Point(500, 300), (FrameworkElement)canvas.Children[0]);
            gizmo.ContinueDrag(new Point(600, 400));
            gizmo.EndDrag();

            var after = CornerOf(cube);

            // Whatever it turned by, it turned about world X: the x component cannot move, and
            // the distance from the X axis cannot change.
            Assert.Equal(before.X, after.X, 2);
            Assert.Equal(
                MathF.Sqrt(before.Y * before.Y + before.Z * before.Z),
                MathF.Sqrt(after.Y * after.Y + after.Z * after.Z), 2);

            Assert.True((before - after).Length() > 1f, "the drag did nothing at all");
        });
    }
}

/// <summary>
/// Resizing something that has been turned. The arrow you grab has to stretch the object the way
/// it points, which on anything turned is the object's own axis and not the world's.
/// </summary>
public class ResizeHandleTests
{
    /// <summary>
    /// A corner view, so all three axes point somewhere different on screen. A straight-on view
    /// will not do here: a quarter turn about Z puts the object's own X along the world's Y,
    /// which points at the camera, and an arrow pointing at the camera cannot be dragged at all.
    /// </summary>
    private sealed class CornerProjector : IScreenProjector
    {
        public bool TryProject(Vector3 world, out Point screen)
        {
            screen = new Point(
                500 + (world.X - world.Y) * 3,
                400 - world.Z * 4 - (world.X + world.Y) * 1.5);
            return true;
        }

        public Vector3 ViewDirection => Vector3.Normalize(new Vector3(1, 1, -1));
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

    /// <summary>
    /// The +X resize arrow. Not simply the first child: in resize mode the box outline and its
    /// corner dots are built first, and a drag on those is refused.
    /// </summary>
    private static FrameworkElement PlusXArrow(Canvas canvas) =>
        canvas.Children.OfType<FrameworkElement>()
            .First(e => (e.ToolTip as string) == "Drag to resize along X");

    private static (GizmoController Gizmo, Canvas Canvas, SceneObject Part) Turned(Vector3 rotation)
    {
        var scene = new Scene();
        var part = new SceneObject("Part", Primitives.Box(20, 10, 6))
        {
            IsSelected = true,
            Rotation = rotation
        };
        scene.Objects.Add(part);

        var canvas = new Canvas();
        var gizmo = new GizmoController(canvas, new CornerProjector(), scene, new UndoStack(scene))
        {
            Mode = GizmoMode.Scale,
            UniformScale = false
        };
        gizmo.Rebuild();

        return (gizmo, canvas, part);
    }

    /// <summary>
    /// A quarter turn about Z puts the object's own X along the world's Y. Dragging the X arrow
    /// then has to change the object's X - which is what the size box calls X too.
    /// </summary>
    [Fact]
    public void TheXArrowStretchesTheObjectsOwnXWhenItIsTurned()
    {
        RunSta(() =>
        {
            var (gizmo, canvas, part) = Turned(new Vector3(0, 0, 90));

            float wasX = part.SizeX, wasY = part.SizeY, wasZ = part.SizeZ;

            Assert.True(gizmo.TryBeginDrag(new Point(500, 400), PlusXArrow(canvas)));
            gizmo.ContinueDrag(new Point(560, 400));
            gizmo.EndDrag();

            Assert.NotEqual(wasX, part.SizeX, 2);
            Assert.Equal(wasY, part.SizeY, 3);
            Assert.Equal(wasZ, part.SizeZ, 3);
        });
    }

    /// <summary>
    /// The one that was reported: the object has to actually grow along the direction the arrow
    /// points on screen, not along some other one.
    /// </summary>
    [Theory]
    [InlineData(0f, 0f, 0f)]
    [InlineData(0f, 0f, 45f)]
    [InlineData(0f, 0f, 90f)]
    [InlineData(30f, 20f, 60f)]
    public void TheObjectGrowsAlongTheArrowThatWasDragged(float x, float y, float z)
    {
        RunSta(() =>
        {
            var (gizmo, canvas, part) = Turned(new Vector3(x, y, z));

            // Where the object's own X points in the world, before and after.
            var direction = Vector3.Normalize(
                Vector3.TransformNormal(Vector3.UnitX, MeshTransform.Rotation(part.Rotation)));

            float before = Reach(part, direction);

            // Dragged along where that axis actually points on screen. Any other direction is a
            // drag against the arrow, which shrinks the object - correctly, but not what is
            // being checked here.
            var camera = new CornerProjector();
            camera.TryProject(Vector3.Zero, out Point at);
            camera.TryProject(direction * 10f, out Point ahead);

            var outward = new System.Windows.Vector(ahead.X - at.X, ahead.Y - at.Y);
            outward.Normalize();

            var from = new Point(500, 400);
            Assert.True(gizmo.TryBeginDrag(from, PlusXArrow(canvas)));
            gizmo.ContinueDrag(from + outward * 80);
            gizmo.EndDrag();

            float after = Reach(part, direction);

            Assert.True(after > before + 0.5f, $"reached {before:0.##} then {after:0.##} mm");

            // And nothing moved across it: the two other directions are untouched.
            var across = Vector3.Normalize(
                Vector3.TransformNormal(Vector3.UnitY, MeshTransform.Rotation(part.Rotation)));
            Assert.Equal(10f, Reach(part, across), 2);
        });
    }

    /// <summary>How far the object reaches along a world direction, corner to corner.</summary>
    private static float Reach(SceneObject part, Vector3 direction)
    {
        var mesh = part.ToWorldMesh();
        float low = float.MaxValue, high = float.MinValue;

        foreach (var p in mesh.Positions)
        {
            float at = Vector3.Dot(p, direction);
            low = MathF.Min(low, at);
            high = MathF.Max(high, at);
        }

        return high - low;
    }
}

/// <summary>
/// Putting the three angles back to zero without moving the object, by folding the turn into the
/// geometry. What it buys is resize arrows that line up with the plate again rather than leaning
/// with the object.
/// </summary>
public class AlignToAxesTests
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

    private static MainViewModel WithObjects(params Vector3[] rotations)
    {
        var model = new MainViewModel();

        foreach (var r in rotations)
        {
            model.InsertCommand.Execute("Wedge");
            model.Scene.Objects[^1].Rotation = r;
        }

        // Selected after they are all in: inserting picks the new object and drops the rest.
        foreach (var o in model.Scene.Objects) o.IsSelected = true;
        model.RefreshSelection();

        return model;
    }

    /// <summary>Every corner of the object, in build-plate coordinates.</summary>
    private static List<Vector3> WorldPoints(SceneObject o) => o.ToWorldMesh().Positions.ToList();

    private static void SameShape(List<Vector3> before, List<Vector3> after, string what)
    {
        Assert.Equal(before.Count, after.Count);

        for (int i = 0; i < before.Count; i++)
            Assert.True((before[i] - after[i]).Length() < 1e-3f,
                $"{what}: a corner moved from {before[i]} to {after[i]}");
    }

    [Fact]
    public void TheAnglesComeBackToZero()
    {
        RunSta(() =>
        {
            var model = WithObjects(new Vector3(-180, 45, -90));

            model.AlignToAxesCommand.Execute(null);

            Assert.Equal(Vector3.Zero, model.Scene.Objects[0].Rotation);
        });
    }

    /// <summary>The whole point: the object stays exactly where it was being drawn.</summary>
    [Theory]
    [InlineData(-180f, 45f, -90f)]
    [InlineData(0f, 0f, 30f)]
    [InlineData(15f, -75f, 160f)]
    public void TheObjectDoesNotBudge(float x, float y, float z)
    {
        RunSta(() =>
        {
            var model = WithObjects(new Vector3(x, y, z));
            var before = WorldPoints(model.Scene.Objects[0]);

            model.AlignToAxesCommand.Execute(null);

            SameShape(before, WorldPoints(model.Scene.Objects[0]), "aligning moved it");
        });
    }

    /// <summary>
    /// Scaling and turning do not commute, so the scale has to be folded in as well or a
    /// stretched object springs out of shape the moment its turn is taken away.
    /// </summary>
    [Fact]
    public void SomethingStretchedUnevenlyStaysWhereItWas()
    {
        RunSta(() =>
        {
            var model = WithObjects(new Vector3(0, 0, 45));
            var part = model.Scene.Objects[0];

            part.SizeX = 40f;
            part.SizeZ = 5f;

            var before = WorldPoints(part);
            model.AlignToAxesCommand.Execute(null);

            SameShape(before, WorldPoints(model.Scene.Objects[0]), "a stretched object moved");
        });
    }

    /// <summary>
    /// What it is for: with nothing left in the turn, the object's own axes are the world's, so
    /// the resize arrows point along the plate again.
    /// </summary>
    [Fact]
    public void TheObjectsOwnAxesBecomeTheWorlds()
    {
        RunSta(() =>
        {
            var model = WithObjects(new Vector3(-180, 45, -90));

            model.AlignToAxesCommand.Execute(null);

            var turn = MeshTransform.Rotation(model.Scene.Objects[0].Rotation);

            Assert.Equal(Vector3.UnitX, Vector3.TransformNormal(Vector3.UnitX, turn));
            Assert.Equal(Vector3.UnitY, Vector3.TransformNormal(Vector3.UnitY, turn));
            Assert.Equal(Vector3.UnitZ, Vector3.TransformNormal(Vector3.UnitZ, turn));
        });
    }

    [Fact]
    public void ItKeepsItsNameAndColour()
    {
        RunSta(() =>
        {
            var model = WithObjects(new Vector3(0, 0, 30));
            var was = model.Scene.Objects[0];
            string name = was.Name;
            var colour = was.Colour;

            model.AlignToAxesCommand.Execute(null);

            Assert.Equal(name, model.Scene.Objects[0].Name);
            Assert.Equal(colour, model.Scene.Objects[0].Colour);
        });
    }

    [Fact]
    public void EverythingSelectedIsAlignedTogether()
    {
        RunSta(() =>
        {
            var model = WithObjects(new Vector3(10, 20, 30), new Vector3(-44, 3, 15));

            model.AlignToAxesCommand.Execute(null);

            Assert.All(model.Scene.Objects, o => Assert.Equal(Vector3.Zero, o.Rotation));
        });
    }

    [Fact]
    public void AligningUndoesInOneStep()
    {
        RunSta(() =>
        {
            var turns = new[] { new Vector3(10, 20, 30), new Vector3(-44, 3, 15) };
            var model = WithObjects(turns);

            model.AlignToAxesCommand.Execute(null);
            model.Undo.Undo();

            Assert.Equal(turns[0], model.Scene.Objects[0].Rotation);
            Assert.Equal(turns[1], model.Scene.Objects[1].Rotation);
        });
    }

    [Fact]
    public void ThereIsNothingToAlignOnSomethingAlreadySquare()
    {
        RunSta(() =>
        {
            var model = WithObjects(Vector3.Zero);

            Assert.False(model.AlignToAxesCommand.CanExecute(null));

            model.Scene.Objects[0].Rotation = new Vector3(0, 0, 30);
            model.RefreshSelection();

            Assert.True(model.AlignToAxesCommand.CanExecute(null));
        });
    }

    [Fact]
    public void NothingSelectedMeansNothingToAlign()
    {
        RunSta(() => Assert.False(new MainViewModel().AlignToAxesCommand.CanExecute(null)));
    }
}
