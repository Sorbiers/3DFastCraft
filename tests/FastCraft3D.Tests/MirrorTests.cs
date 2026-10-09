using System.Numerics;
using System.Runtime.ExceptionServices;
using FastCraft3D.Geometry;
using FastCraft3D.Model;
using FastCraft3D.ViewModels;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>
/// Mirror reflects what is selected on the plate: along the plate's axis whichever way a part is
/// turned, and several parts as one arrangement unless Each is on.
/// </summary>
public class MirrorTests
{
    /// <summary>A box that is all to one side of its origin, so a flip the wrong way shows.</summary>
    private static Mesh OffCentre(float x, float y, float z) =>
        MeshTransform.Transformed(Primitives.Box(x, y, z), Matrix4x4.CreateTranslation(x / 2, y / 3, z / 4));

    private static Vector3 Reflected(Vector3 p, Axis axis, float plane) => axis switch
    {
        Axis.X => p with { X = 2 * plane - p.X },
        Axis.Y => p with { Y = 2 * plane - p.Y },
        _ => p with { Z = 2 * plane - p.Z }
    };

    public static TheoryData<float, float, float> Turns => new()
    {
        { 0, 0, 0 }, { 0, 0, 90 }, { 0, 0, 37 }, { 25, 0, 0 }, { 0, -40, 0 },
        { 30, 50, 70 }, { -110, 20, 165 }, { 12, 90, 48 }, { 180, 0, 180 }
    };

    [Theory]
    [MemberData(nameof(Turns))]
    public void AMirroredPartIsItsReflectionOnThePlateHoweverItIsTurned(float roll, float pitch, float yaw)
    {
        foreach (var axis in new[] { Axis.X, Axis.Y, Axis.Z })
        foreach (var scale in new[] { Vector3.One, new Vector3(1.5f, 0.5f, 2f), new Vector3(-1f, 2f, 1f) })
        {
            var o = new SceneObject("Part", OffCentre(10, 20, 30))
            {
                Position = new Vector3(7, -11, 23),
                Rotation = new Vector3(roll, pitch, yaw),
                Scale = scale
            };

            var before = o.ToWorldMesh().Positions.ToList();
            const float plane = 4.5f;

            o.MirrorAcross(axis, plane);

            var after = o.ToWorldMesh();
            for (int i = 0; i < before.Count; i++)
            {
                var wanted = Reflected(before[i], axis, plane);
                Assert.True(Vector3.Distance(wanted, after.Positions[i]) < 1e-3f,
                    $"{axis}, turned {roll} {pitch} {yaw}, scaled {scale}: {after.Positions[i]} and not {wanted}");
            }

            Assert.True(after.ComputeSignedVolume() > 0, "mirrored, the part is inside out");
        }
    }

    [Fact]
    public void MirroredTwiceAPartIsBackWhereItWasAndReadsTheSame()
    {
        var o = new SceneObject("Part", OffCentre(10, 20, 30))
        {
            Position = new Vector3(7, -11, 23),
            Rotation = new Vector3(30, 0, 180)
        };

        o.MirrorAcross(Axis.X, 2f);
        o.MirrorAcross(Axis.X, 2f);

        Assert.Equal(new Vector3(7, -11, 23), o.Position);
        Assert.Equal(new Vector3(30, 0, 180), o.Rotation);
        Assert.Equal(Vector3.One, o.Scale);

        // No "-0" in a box for an angle that was nought.
        Assert.False(float.IsNegative(o.Rotation.Y));
    }

    private static void Stage(Action<MainViewModel> body)
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

    private static SceneObject Put(MainViewModel model, string name, Mesh mesh, Vector3 at, Vector3 turned = default)
    {
        var o = new SceneObject(name, mesh) { Position = at, Rotation = turned };
        model.Scene.Objects.Add(o);
        return o;
    }

    private static void Select(MainViewModel model, params SceneObject[] objects)
    {
        foreach (var o in model.Scene.Objects) o.IsSelected = objects.Contains(o);
        model.RefreshSelection();
    }

    private static Bounds Box(params SceneObject[] objects)
    {
        var bounds = Bounds.Empty;
        foreach (var o in objects) bounds = bounds.Union(o.WorldBounds);
        return bounds;
    }

    private static void Same(Bounds wanted, Bounds got)
    {
        Assert.True(Vector3.Distance(wanted.Min, got.Min) < 1e-3f, $"from {got.Min} and not {wanted.Min}");
        Assert.True(Vector3.Distance(wanted.Max, got.Max) < 1e-3f, $"to {got.Max} and not {wanted.Max}");
    }

    [Fact]
    public void ASelectionIsMirroredAsOneSoItsPartsChangeSides() => Stage(model =>
    {
        // A wall with a small door at its left end.
        var wall = Put(model, "Wall", Primitives.Box(60, 4, 30), new Vector3(0, 0, 15));
        var door = Put(model, "Door", Primitives.Box(10, 6, 20), new Vector3(-20, 0, 10));
        Select(model, wall, door);
        var lot = Box(wall, door);

        model.MirrorCommand.Execute("X");

        Assert.Equal(20f, door.WorldBounds.Center.X, 3);
        Assert.Equal(0f, wall.WorldBounds.Center.X, 3);
        Same(lot, Box(wall, door));
    });

    [Fact]
    public void AnAssemblyIsMirroredAsOneLikeAnyOtherSelection() => Stage(model =>
    {
        var walls = Put(model, "Walls", Primitives.Box(40, 40, 20), new Vector3(0, 0, 10));
        var chimney = Put(model, "Chimney", Primitives.Box(6, 6, 30), new Vector3(12, -8, 15), new Vector3(0, 0, 30));
        model.Undo.Execute(AssemblyTools.Assemble(model.Scene, [walls, chimney], out _));
        Select(model, walls, chimney);
        var lot = Box(walls, chimney);
        float middle = lot.Center.Y;
        float was = chimney.WorldBounds.Center.Y;

        model.MirrorCommand.Execute("Y");

        Assert.Equal(2 * middle - was, chimney.WorldBounds.Center.Y, 3);
        Same(lot, Box(walls, chimney));
    });

    [Fact]
    public void WithEachOnEveryPartIsMirroredWhereItStands() => Stage(model =>
    {
        var left = Put(model, "Left", OffCentre(10, 10, 10), new Vector3(-30, 0, 0), new Vector3(0, 0, 20));
        var right = Put(model, "Right", OffCentre(20, 10, 10), new Vector3(30, 5, 0));
        Select(model, left, right);
        model.EachOnItsOwn = true;
        var (leftBox, rightBox) = (left.WorldBounds, right.WorldBounds);

        model.MirrorCommand.Execute("X");

        Same(leftBox, left.WorldBounds);
        Same(rightBox, right.WorldBounds);
        Assert.True(left.Scale.X < 0 && right.Scale.X < 0);
    });

    [Fact]
    public void APartOnItsOwnIsMirroredInItsBoxWhereverItsPivotIs() => Stage(model =>
    {
        // Its pivot on one corner and turned: reflected through the pivot it would land beside
        // where it stood, and on Z it would go under the plate.
        var part = Put(model, "Part", OffCentre(10, 20, 30), new Vector3(15, 5, 0), new Vector3(0, 0, 40));
        Select(model, part);
        var box = part.WorldBounds;

        foreach (string axis in new[] { "X", "Y", "Z" })
        {
            model.MirrorCommand.Execute(axis);
            Same(box, part.WorldBounds);
        }
    });

    [Fact]
    public void MirroringASelectionIsOneUndoStep() => Stage(model =>
    {
        var wall = Put(model, "Wall", Primitives.Box(60, 4, 30), new Vector3(0, 0, 15));
        var door = Put(model, "Door", Primitives.Box(10, 6, 20), new Vector3(-20, 0, 10), new Vector3(0, 0, 15));
        Select(model, wall, door);

        model.MirrorCommand.Execute("X");
        model.UndoCommand.Execute(null);

        Assert.Equal(new Vector3(-20, 0, 10), door.Position);
        Assert.Equal(new Vector3(0, 0, 15), door.Rotation);
        Assert.Equal(Vector3.One, door.Scale);
        Assert.Equal(Vector3.One, wall.Scale);
    });
}
