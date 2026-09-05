using System.IO;
using System.Numerics;
using FastCraft3D.Geometry;
using FastCraft3D.Geometry.Csg;
using FastCraft3D.Io;
using FastCraft3D.Model;
using FastCraft3D.Model.Commands;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>
/// Covers the path a real edit takes through the app: scene objects, transforms, undo, and
/// what finally lands in an exported file.
/// </summary>
public class WorkflowTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "3dfc-flow-" + Guid.NewGuid().ToString("N"));

    public WorkflowTests() => Directory.CreateDirectory(directory);

    public void Dispose()
    {
        try { Directory.Delete(directory, recursive: true); } catch { /* best effort */ }
        GC.SuppressFinalize(this);
    }

    private static SceneObject Cube(string name = "Cube", float size = 20f) =>
        new(name, Primitives.Box(size, size, size));

    [Fact]
    public void SizeIsExpressedInMillimetresNotScaleFactor()
    {
        var o = Cube();

        Assert.Equal(20f, o.SizeX, 3);

        o.SizeX = 10f;

        Assert.Equal(10f, o.SizeX, 3);
        Assert.Equal(0.5f, o.Scale.X, 3);
        Assert.Equal(10f, o.WorldBounds.Size.X, 3);
    }

    /// <summary>
    /// Mirroring uses a negative scale. Without the winding flip the mesh would export
    /// inside-out, which is the difference between a printable file and a broken one.
    /// </summary>
    [Fact]
    public void MirroringKeepsNormalsFacingOutwards()
    {
        var o = Cube();
        o.Scale = new Vector3(-1, 1, 1);

        var world = o.ToWorldMesh();

        Assert.True(world.ComputeSignedVolume() > 0, "mirrored mesh exports inside-out");
        Assert.True(world.CheckHealth().IsWatertight);
    }

    [Fact]
    public void ResizingAMirroredObjectKeepsItMirrored()
    {
        var o = Cube();
        o.Scale = new Vector3(-1, 1, 1);

        o.SizeX = 5f;

        Assert.True(o.Scale.X < 0, "the mirror was lost when the object was resized");
        Assert.Equal(5f, o.SizeX, 3);
        Assert.True(o.ToWorldMesh().ComputeSignedVolume() > 0);
    }

    [Fact]
    public void DropToPlateRestsTheObjectOnZeroWithoutMovingItSideways()
    {
        var o = Cube();
        o.Position = new Vector3(7, -3, 55);

        var lifted = MeshTransform.AlignedToPlate(o.ToWorldMesh());
        var bounds = lifted.ComputeBounds();

        Assert.Equal(0f, bounds.Min.Z, 3);
        Assert.Equal(7f, bounds.Center.X, 3);
        Assert.Equal(-3f, bounds.Center.Y, 3);
    }

    [Fact]
    public void UndoRestoresGeometryAfterABoolean()
    {
        var scene = new Scene();
        var undo = new UndoStack(scene);
        var block = Cube();
        var drill = new SceneObject("Drill", Primitives.Prism(5, 40, 32));
        scene.Objects.Add(block);
        scene.Objects.Add(drill);

        var result = new SceneObject("Subtract", CsgSolid.Subtract(block.ToWorldMesh(), drill.ToWorldMesh()));
        undo.Execute(new ReplaceObjectsCommand("Subtract", [block, drill], [result]));

        Assert.Single(scene.Objects);

        undo.Undo();

        Assert.Equal(2, scene.Objects.Count);
        Assert.Contains(block, scene.Objects);
        Assert.Contains(drill, scene.Objects);

        undo.Redo();

        Assert.Single(scene.Objects);
        Assert.Same(result, scene.Objects[0]);
    }

    [Fact]
    public void UndoRestoresTheOriginalObjectOrder()
    {
        var scene = new Scene();
        var undo = new UndoStack(scene);
        var first = Cube("First");
        var second = Cube("Second");
        var third = Cube("Third");
        foreach (var o in new[] { first, second, third }) scene.Objects.Add(o);

        undo.Execute(new DeleteObjectsCommand([first, second]));
        undo.Undo();

        Assert.Equal(["First", "Second", "Third"], scene.Objects.Select(o => o.Name));
    }

    [Fact]
    public void TransformCommandIgnoresEditsThatChangeNothing()
    {
        var o = Cube();
        var before = new[] { TransformState.Capture(o) };

        Assert.Null(TransformCommand.CreateIfChanged("Edit", [o], before));

        o.PositionX = 5;

        Assert.NotNull(TransformCommand.CreateIfChanged("Edit", [o], before));
    }

    /// <summary>Rounding is only offered while the object is still described by its parameters.</summary>
    [Fact]
    public void OnlyPrimitivesStillKnownAsSuchCanBeRounded()
    {
        var cube = new SceneObject("Cube", Primitives.Box(20, 20, 20)) { Origin = PrimitiveKind.Cube };
        var sphere = new SceneObject("Ball", Primitives.Create(PrimitiveKind.Sphere)) { Origin = PrimitiveKind.Sphere };
        var booleanResult = new SceneObject("Subtract", Primitives.Box(20, 20, 20));

        Assert.True(cube.CanRound);
        Assert.False(sphere.CanRound, "a sphere has no edges to round");
        Assert.False(booleanResult.CanRound, "a boolean result is no longer a primitive");
    }

    [Fact]
    public void CopyingAnObjectCarriesItsOriginSoTheCopyCanStillBeRounded()
    {
        var cube = new SceneObject("Cube", Primitives.Box(20, 20, 20)) { Origin = PrimitiveKind.Cube };

        Assert.True(cube.Clone().CanRound);
    }

    [Fact]
    public void ARoundedObjectKeepsItsPlaceAndSize()
    {
        var cube = new SceneObject("Cube", Primitives.Box(20, 20, 20)) { Origin = PrimitiveKind.Cube };
        cube.Position = new Vector3(30, 10, 10);
        cube.SizeX = 40f;

        // What the round command does: rebuild at the current size, scale back to one.
        var size = new Vector3(cube.SizeX, cube.SizeY, cube.SizeZ);
        var rounded = new SceneObject(cube.Name, RoundedPrimitives.Create(PrimitiveKind.Cube, size, 4f))
        {
            Position = cube.Position,
            Rotation = cube.Rotation,
            Origin = cube.Origin
        };

        Assert.Equal(40f, rounded.SizeX, 2);
        Assert.Equal(20f, rounded.SizeY, 2);
        Assert.Equal(new Vector3(30, 10, 10), rounded.Position);
        Assert.Equal(Vector3.One, rounded.Scale);
        Assert.True(rounded.Mesh.CheckHealth().IsWatertight);
    }

    [Fact]
    public void UniqueNameAvoidsCollisions()
    {
        var scene = new Scene();
        scene.Objects.Add(Cube("Cube"));
        scene.Objects.Add(Cube("Cube 2"));

        Assert.Equal("Cube 3", scene.UniqueName("Cube"));
        Assert.Equal("Sphere", scene.UniqueName("Sphere"));
    }

    /// <summary>The full modelling round trip: cut a solid, export both halves, read them back.</summary>
    [Fact]
    public void CutHalvesExportAndReimportWithTheOriginalVolume()
    {
        var block = Primitives.Box(20, 20, 20);
        var drilled = CsgSolid.Subtract(block, Primitives.Prism(5, 40, 32));
        double originalVolume = drilled.ComputeSignedVolume();

        var (front, back) = PlaneSplit.Split(drilled, Axis.X, 0f, SplitKeep.Both);
        Assert.NotNull(front);
        Assert.NotNull(back);
        Assert.True(front!.CheckHealth().IsWatertight, "the front half is not capped");
        Assert.True(back!.CheckHealth().IsWatertight, "the back half is not capped");

        var scene = new Scene();
        scene.Objects.Add(new SceneObject("A", front));
        // Lay the halves out side by side, as anyone would before printing them. Left exactly
        // face-to-face their coincident cut caps would fuse when any importer welds the file,
        // which is inherent to touching solids rather than a defect in the export.
        scene.Objects.Add(new SceneObject("B", back) { Position = new Vector3(60, 0, 0) });

        var merged = ExportComposer.MergeForStl(scene.Objects);
        string file = Path.Combine(directory, "halves.stl");
        StlWriter.Write(file, merged, binary: true);

        var reloaded = StlReader.Read(file);

        // The two halves together are the original solid, to within CSG rounding.
        Assert.Equal(originalVolume, reloaded.ComputeSignedVolume(), 1);
        Assert.Equal(merged.TriangleCount, reloaded.TriangleCount);
        Assert.True(reloaded.CheckHealth().IsWatertight, reloaded.CheckHealth().Describe());
    }

    [Fact]
    public void ExportAppliesObjectTransformsRatherThanLocalGeometry()
    {
        var o = Cube();
        o.Position = new Vector3(40, 0, 10);
        o.SizeX = 10f;

        var exported = ExportComposer.MergeForStl([o]);
        var bounds = exported.ComputeBounds();

        Assert.Equal(40f, bounds.Center.X, 3);
        Assert.Equal(10f, bounds.Size.X, 3);
        Assert.Equal(0f, bounds.Min.Z, 3);
    }

    [Fact]
    public void ProjectFileRoundTripsObjectsAndTransforms()
    {
        var scene = new Scene();
        var o = new SceneObject("Widget", Primitives.Create(PrimitiveKind.Torus))
        {
            Position = new Vector3(12, -4, 8),
            Rotation = new Vector3(0, 45, 90),
            Colour = new Vector3(0.1f, 0.2f, 0.3f)
        };
        o.SizeX = 33f;
        scene.Objects.Add(o);

        string file = Path.Combine(directory, "project.3dfc");
        SceneSerializer.Save(file, scene);
        var loaded = SceneSerializer.Load(file);

        Assert.Single(loaded);
        var back = loaded[0];
        Assert.Equal("Widget", back.Name);
        Assert.Equal(o.Position, back.Position);
        Assert.Equal(o.Rotation, back.Rotation);
        Assert.Equal(o.Colour, back.Colour);
        Assert.Equal(o.SizeX, back.SizeX, 3);
        Assert.Equal(o.Mesh.TriangleCount, back.Mesh.TriangleCount);
    }

    [Fact]
    public void ProjectFileIsSmallerThanTheEquivalentStl()
    {
        var scene = new Scene();
        scene.Objects.Add(new SceneObject("Ball", Primitives.Create(PrimitiveKind.Sphere)));

        string project = Path.Combine(directory, "compare.3dfc");
        string stl = Path.Combine(directory, "compare.stl");
        SceneSerializer.Save(project, scene);
        StlWriter.Write(stl, scene.Objects[0].ToWorldMesh(), binary: true);

        // GZip plus shared vertices should comfortably beat STL's unshared triangle soup.
        Assert.True(new FileInfo(project).Length < new FileInfo(stl).Length,
            "the compressed project format is not smaller than binary STL");
    }
}
