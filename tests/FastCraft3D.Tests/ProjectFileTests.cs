using System.IO;
using System.IO.Compression;
using FastCraft3D.Geometry;
using FastCraft3D.Io;
using FastCraft3D.Model;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>The .3dfp kept beside a project's .3mf: what it holds, how it is found, and what it brings back.</summary>
public class ProjectFileTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "3dfp-" + Guid.NewGuid().ToString("N"));

    public ProjectFileTests() => Directory.CreateDirectory(directory);

    public void Dispose()
    {
        try { Directory.Delete(directory, recursive: true); } catch { /* best effort */ }
        GC.SuppressFinalize(this);
    }

    private string At(string name) => Path.Combine(directory, name);

    private static Scene SceneWith(params string[] names)
    {
        var scene = new Scene();
        foreach (var name in names)
            scene.Objects.Add(new SceneObject(name, Primitives.Box(20, 20, 20)));
        return scene;
    }

    /// <summary>What a slicer does on saving over a project: the model stays, the project part goes.</summary>
    private static void SaveOverAsASlicerWould(string path)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Update);
        zip.GetEntry("3DFastCraft/project.json")!.Delete();
    }

    [Fact]
    public void TheProjectRemembersWhichProjectFileItIsKeptWith()
    {
        string model = At("house.3mf");
        SceneSerializer.Save(model, SceneWith("Wall"), projectFile: "abc123");
        Assert.Equal("abc123", SceneSerializer.ReadProjectFileId(model));

        SceneSerializer.Save(model, SceneWith("Wall"));
        Assert.Null(SceneSerializer.ReadProjectFileId(model));
    }

    [Fact]
    public void AProjectFileKeepsTheProjectTheViewAndAFingerprintOfTheModel()
    {
        string model = At("house.3mf");
        SceneSerializer.Save(model, SceneWith("Wall", "Roof"), projectFile: "id1");
        var view = new ViewState([1, 2, 3], [0, 1, 0], [0, 0, 1], true, 120);
        ProjectFile.Write(ProjectFile.Beside(model), "id1", model, view, "test");

        var info = ProjectFile.ReadInfo(At("house.3dfp"));
        Assert.NotNull(info);
        Assert.Equal("id1", info.Id);
        Assert.Equal("house.3mf", info.ModelName);
        Assert.Equal(ProjectFile.Fingerprint(model), info.Fingerprint);

        var kept = ProjectFile.ReadView(At("house.3dfp"));
        Assert.NotNull(kept);
        Assert.Equal([1.0, 2.0, 3.0], kept.Position);
        Assert.True(kept.Orthographic);

        Assert.Equal(["Wall", "Roof"], SceneSerializer.Load(At("house.3dfp")).Select(o => o.Name));
    }

    [Fact]
    public void AProjectFileRenamedBesideItsModelIsStillFoundByItsId()
    {
        string model = At("house.3mf");
        SceneSerializer.Save(model, SceneWith("Wall"), projectFile: "id2");
        ProjectFile.Write(At("old name.3dfp"), "id2", model, null, null);

        Assert.Equal(At("old name.3dfp"), ProjectFile.Find(model, "id2"));
        Assert.Null(ProjectFile.Find(model, "another"));
        Assert.Null(ProjectFile.Find(model, null));
    }

    [Fact]
    public void AProjectFileOfTheSameNameButAnotherProjectIsNotThisOnes()
    {
        string model = At("house.3mf");
        SceneSerializer.Save(model, SceneWith("Wall"), projectFile: "mine");
        ProjectFile.Write(ProjectFile.Beside(model), "theirs", model, null, null);

        Assert.Null(ProjectFile.Find(model, "mine"));
        Assert.Equal(ProjectFile.Beside(model), ProjectFile.Find(model, null));
    }

    [Fact]
    public void AProjectASlicerSavedOverComesBackFromItsProjectFileWithItsVersions()
    {
        string model = At("house.3mf");
        SceneSerializer.Save(model, SceneWith("Wall"), projectFile: "id3");
        SceneSerializer.SaveVersion(model, SceneWith("Wall", "Roof"), "with the roof");
        ProjectFile.Write(ProjectFile.Beside(model), "id3", model, null, null);
        string before = ProjectFile.Fingerprint(model);

        SaveOverAsASlicerWould(model);
        Assert.False(SceneSerializer.IsProject(model));
        Assert.NotEqual(before, ProjectFile.Fingerprint(model));

        // What Bring the project back does: open the scene from the .3dfp, then save it into the
        // .3mf with the versions taken from the .3dfp.
        var scene = new Scene();
        foreach (var o in SceneSerializer.Load(ProjectFile.Beside(model))) scene.Objects.Add(o);
        SceneSerializer.Save(model, scene, versionsFrom: ProjectFile.Beside(model), projectFile: "id3");

        Assert.True(SceneSerializer.IsProject(model));
        Assert.Equal(["Wall", "Roof"], SceneSerializer.Load(model).Select(o => o.Name));
        Assert.Equal("with the roof", Assert.Single(SceneSerializer.ReadVersions(model)).Label);
        Assert.Equal("id3", SceneSerializer.ReadProjectFileId(model));
    }

    [Fact]
    public void SomethingThatIsNotAProjectFileReadsAsNone()
    {
        File.WriteAllText(At("junk.3dfp"), "not a zip");
        Assert.Null(ProjectFile.ReadInfo(At("junk.3dfp")));
        Assert.Null(ProjectFile.ReadView(At("junk.3dfp")));
    }
}
