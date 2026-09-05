using System.IO;
using System.Numerics;
using FastCraft3D.Geometry;
using FastCraft3D.Io;
using FastCraft3D.Model;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>
/// Named versions kept inside the project file, so a model's history travels with the model
/// rather than spreading across files called "v2 final".
/// </summary>
public class SceneVersionTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "3dfc-ver-" + Guid.NewGuid().ToString("N"));

    public SceneVersionTests() => Directory.CreateDirectory(directory);

    public void Dispose()
    {
        try { Directory.Delete(directory, recursive: true); } catch { /* best effort */ }
        GC.SuppressFinalize(this);
    }

    private string File_(string name) => Path.Combine(directory, name);

    private static Scene SceneWith(params string[] names)
    {
        var scene = new Scene();
        foreach (var name in names)
            scene.Objects.Add(new SceneObject(name, Primitives.Box(20, 20, 20)));
        return scene;
    }

    [Fact]
    public void AFreshFileHasNoVersions()
    {
        string path = File_("plain.3dfc");
        SceneSerializer.Save(path, SceneWith("A"));

        Assert.Empty(SceneSerializer.ReadVersions(path));
    }

    [Fact]
    public void SavingAVersionKeepsItAlongsideTheCurrentScene()
    {
        string path = File_("kept.3dfc");

        SceneSerializer.SaveVersion(path, SceneWith("A"), "first idea");
        SceneSerializer.Save(path, SceneWith("A", "B"));

        var versions = SceneSerializer.ReadVersions(path);
        Assert.Single(versions);
        Assert.Equal("first idea", versions[0].Label);
        Assert.Equal(1, versions[0].ObjectCount);

        // The current scene has moved on without disturbing the version.
        Assert.Equal(2, SceneSerializer.Load(path).Count);
    }

    /// <summary>The point of the feature: going back after closing and reopening the file.</summary>
    [Fact]
    public void AVersionCanBeReadBackAfterTheFileIsReopened()
    {
        string path = File_("reopen.3dfc");

        SceneSerializer.SaveVersion(path, SceneWith("Early"), "before the hole");
        SceneSerializer.Save(path, SceneWith("Late", "Other"));

        // Nothing is cached between these calls - each one opens the file afresh.
        var restored = SceneSerializer.LoadVersion(path, 0);

        Assert.Single(restored);
        Assert.Equal("Early", restored[0].Name);
    }

    [Fact]
    public void VersionsAccumulateInOrder()
    {
        string path = File_("many.3dfc");

        SceneSerializer.SaveVersion(path, SceneWith("A"), "one");
        SceneSerializer.SaveVersion(path, SceneWith("A", "B"), "two");
        SceneSerializer.SaveVersion(path, SceneWith("A", "B", "C"), "three");

        var versions = SceneSerializer.ReadVersions(path);

        Assert.Equal(["one", "two", "three"], versions.Select(v => v.Label));
        Assert.Equal([1, 2, 3], versions.Select(v => v.ObjectCount));
    }

    [Fact]
    public void AnOrdinarySaveNeverDiscardsKeptVersions()
    {
        string path = File_("preserve.3dfc");
        SceneSerializer.SaveVersion(path, SceneWith("A"), "keep me");

        for (int i = 0; i < 5; i++)
            SceneSerializer.Save(path, SceneWith("A", "B"));

        Assert.Single(SceneSerializer.ReadVersions(path));
    }

    [Fact]
    public void AVersionCanBeForgottenWithoutTouchingTheScene()
    {
        string path = File_("forget.3dfc");
        SceneSerializer.SaveVersion(path, SceneWith("A"), "one");
        SceneSerializer.SaveVersion(path, SceneWith("A", "B"), "two");
        SceneSerializer.Save(path, SceneWith("A", "B", "C"));

        SceneSerializer.DeleteVersion(path, 0);

        Assert.Single(SceneSerializer.ReadVersions(path));
        Assert.Equal("two", SceneSerializer.ReadVersions(path)[0].Label);
        Assert.Equal(3, SceneSerializer.Load(path).Count);
    }

    [Fact]
    public void RestoringAVersionBringsBackItsGeometryAndTransforms()
    {
        string path = File_("geometry.3dfc");

        var scene = new Scene();
        var o = new SceneObject("Widget", Primitives.Create(PrimitiveKind.Cylinder))
        {
            Position = new Vector3(12, -4, 8),
            Rotation = new Vector3(0, 45, 0),
            Origin = PrimitiveKind.Cylinder
        };
        scene.Objects.Add(o);
        SceneSerializer.SaveVersion(path, scene, "with the cylinder");

        SceneSerializer.Save(path, SceneWith("Something else"));
        var restored = SceneSerializer.LoadVersion(path, 0);

        Assert.Single(restored);
        Assert.Equal(o.Position, restored[0].Position);
        Assert.Equal(o.Rotation, restored[0].Rotation);
        Assert.Equal(o.Mesh.TriangleCount, restored[0].Mesh.TriangleCount);
        Assert.Equal(PrimitiveKind.Cylinder, restored[0].Origin);
    }

    /// <summary>Older files predate versions entirely and must still open.</summary>
    [Fact]
    public void AFileWithNoVersionsStillLoads()
    {
        string path = File_("v1.3dfc");
        SceneSerializer.Save(path, SceneWith("A", "B"));

        Assert.Equal(2, SceneSerializer.Load(path).Count);
        Assert.Empty(SceneSerializer.ReadVersions(path));
    }

    /// <summary>
    /// A save is written to a temporary file and moved into place, so an interrupted save cannot
    /// destroy the scene and every version kept with it.
    /// </summary>
    [Fact]
    public void SavingLeavesNoTemporaryFileBehind()
    {
        string path = File_("atomic.3dfc");
        SceneSerializer.SaveVersion(path, SceneWith("A"), "one");
        SceneSerializer.Save(path, SceneWith("A", "B"));

        Assert.False(System.IO.File.Exists(path + ".tmp"));
        Assert.True(System.IO.File.Exists(path));
    }

    [Fact]
    public void AskingForAVersionThatIsNotThereIsRejected()
    {
        string path = File_("range.3dfc");
        SceneSerializer.SaveVersion(path, SceneWith("A"), "one");

        Assert.Throws<ArgumentOutOfRangeException>(() => SceneSerializer.LoadVersion(path, 5));
    }
}
