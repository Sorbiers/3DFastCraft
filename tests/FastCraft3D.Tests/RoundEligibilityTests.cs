using System.IO;
using System.IO.Compression;
using System.Numerics;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using FastCraft3D.Geometry;
using FastCraft3D.Io;
using FastCraft3D.Model;
using FastCraft3D.ViewModels;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>
/// Round edges is offered only while the mesh is still the primitive it was generated as.
///
/// It used to be offered on anything that remembered its origin, and subtracting, repairing and
/// aligning all remember it - so rounding a cylinder with a hole in it rebuilt a plain cylinder
/// and the hole was gone.
/// </summary>
public class RoundEligibilityTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "3dfc-round-" + Guid.NewGuid().ToString("N"));

    public RoundEligibilityTests() => Directory.CreateDirectory(directory);

    public void Dispose()
    {
        try { Directory.Delete(directory, recursive: true); } catch { /* best effort */ }
        GC.SuppressFinalize(this);
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

    private static void Invoke(MainViewModel model, string method)
    {
        var result = typeof(MainViewModel)
            .GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(model, null);
        (result as Task)?.GetAwaiter().GetResult();
    }

    /// <summary>A cylinder with a narrow cylinder subtracted through its middle.</summary>
    private static SceneObject HoledCylinder(MainViewModel model)
    {
        model.InsertCommand.Execute("Cylinder");
        model.InsertCommand.Execute("Cylinder");

        var target = model.Scene.Objects[0];
        var cutter = model.Scene.Objects[1];
        cutter.SizeX = 5f;
        cutter.SizeY = 5f;
        cutter.SizeZ = target.SizeZ * 3f;

        model.Scene.ClearSelection();
        target.IsSelected = true;
        cutter.IsSelected = true;
        Invoke(model, "ApplySubtract");

        var holed = model.Scene.Objects[0];
        Assert.NotSame(target, holed);
        return holed;
    }

    [Theory]
    [InlineData("Cube")]
    [InlineData("Cylinder")]
    public void AFreshlyInsertedCubeOrCylinderCanBeRounded(string kind) => RunSta(() =>
    {
        var model = new MainViewModel();
        model.InsertCommand.Execute(kind);

        Assert.True(model.Scene.Objects[0].CanRound);
    });

    [Fact]
    public void ACylinderWithAHoleSubtractedCannotBeRounded() => RunSta(() =>
    {
        var holed = HoledCylinder(new MainViewModel());

        Assert.False(holed.CanRound);
    });

    /// <summary>The origin is still wanted for growing it by a clearance, so only Round is refused.</summary>
    [Fact]
    public void ASubtractedCylinderCanStillTakeAClearance() => RunSta(() =>
    {
        var holed = HoledCylinder(new MainViewModel());

        Assert.Equal(PrimitiveKind.Cylinder, holed.Origin);
        Assert.True(holed.CanTakeClearance);
    });

    [Fact]
    public void ACubeAlignedToTheAxesAfterTurningCannotBeRounded() => RunSta(() =>
    {
        var model = new MainViewModel();
        model.InsertCommand.Execute("Cube");
        var cube = model.Scene.Objects[0];
        cube.Rotation = new Vector3(0f, 0f, 30f);
        cube.IsSelected = true;

        Invoke(model, "AlignToAxes");

        Assert.False(model.Scene.Objects[0].CanRound);
    });

    [Fact]
    public void ACopyOfAFreshCubeCanStillBeRounded() => RunSta(() =>
    {
        var model = new MainViewModel();
        model.InsertCommand.Execute("Cube");

        Assert.True(model.Scene.Objects[0].Clone().CanRound);
    });

    [Fact]
    public void SavingAndLoadingKeepsWhetherAShapeCanBeRounded() => RunSta(() =>
    {
        var model = new MainViewModel();
        var holed = HoledCylinder(model);
        model.InsertCommand.Execute("Cube");
        var cube = model.Scene.Objects[^1];

        var scene = new Scene();
        scene.Objects.Add(holed);
        scene.Objects.Add(cube);

        string path = Path.Combine(directory, "saved" + SceneSerializer.Extension);
        SceneSerializer.Save(path, scene);
        var loaded = SceneSerializer.Load(path);

        Assert.False(loaded[0].CanRound);
        Assert.True(loaded[1].CanRound);
    });

    /// <summary>
    /// A file from before the flag existed says only what each object started as, so the loader
    /// has to tell a plain cube from a cylinder with a hole in it by looking.
    /// </summary>
    [Fact]
    public void AnOlderFileRoundsItsPlainShapesButNotOnesWithAHoleInThem() => RunSta(() =>
    {
        var holed = HoledCylinder(new MainViewModel());

        string path = Path.Combine(directory, "old" + SceneSerializer.Extension);
        WriteOldFile(path,
            ("Cylinder", holed.Mesh),
            ("Cube", Primitives.Create(PrimitiveKind.Cube)),
            ("Cylinder", Primitives.Create(PrimitiveKind.Cylinder)),
            ("Cube", RoundedPrimitives.Create(PrimitiveKind.Cube, new Vector3(20f), 3f)));

        var loaded = SceneSerializer.Load(path);

        Assert.False(loaded[0].CanRound, "the hole would be lost");
        Assert.True(loaded[1].CanRound);
        Assert.True(loaded[2].CanRound);
        Assert.True(loaded[3].CanRound, "a rounded cube is still a cube to round again");
    });

    private static void WriteOldFile(string path, params (string Origin, Mesh Mesh)[] objects)
    {
        var dto = new
        {
            Version = 2,
            Objects = objects.Select(o => new
            {
                Name = o.Origin,
                Position = new[] { 0f, 0f, 0f },
                Rotation = new[] { 0f, 0f, 0f },
                Scale = new[] { 1f, 1f, 1f },
                o.Origin,
                Vertices = o.Mesh.Positions.SelectMany(p => new[] { p.X, p.Y, p.Z }).ToArray(),
                Triangles = o.Mesh.Indices.ToArray()
            }).ToList()
        };

        using var file = File.Create(path);
        using var gzip = new GZipStream(file, CompressionLevel.Fastest);
        JsonSerializer.Serialize(gzip, dto);
    }
}
