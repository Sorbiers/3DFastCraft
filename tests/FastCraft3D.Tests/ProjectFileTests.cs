using System.IO;
using System.IO.Compression;
using System.Xml.Linq;
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
    public void AProjectFileIsA3mfPackageWithEveryPartTyped()
    {
        string model = At("house.3mf");
        var scene = SceneWith("Wall", "Roof");
        SceneSerializer.Save(model, scene, projectFile: "id4");
        string file = ProjectFile.Beside(model);
        ProjectFile.Write(file, "id4", model, null, null);
        var session = new ProjectSession();
        session.Record(scene, "Add Roof");
        session.Flush(file, "id4", scene, null);

        // What Windows' 3MF handler draws Explorer's picture from.
        Assert.Equal(2, ThreeMf.Read(file).Count);
        Assert.NotNull(ThreeMf.ReadThumbnail(file));

        using var zip = ZipFile.OpenRead(file);
        XDocument types;
        using (var stream = zip.GetEntry("[Content_Types].xml")!.Open()) types = XDocument.Load(stream);
        var typed = types.Root!.Elements().Where(e => e.Name.LocalName == "Default")
            .Select(e => (string)e.Attribute("Extension")!).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in zip.Entries.Where(e => e.FullName != "[Content_Types].xml"))
            Assert.Contains(Path.GetExtension(entry.FullName).TrimStart('.'), typed);
    }

    [Fact]
    public void WhatALaterSaveNoLongerHasGoesFromTheProjectFileAndTheSessionStays()
    {
        string model = At("house.3mf");
        var scene = SceneWith("Wall");
        SceneSerializer.Save(model, scene, projectFile: "id5");
        string file = ProjectFile.Beside(model);
        ProjectFile.Write(file, "id5", model, null, null);
        var session = new ProjectSession();
        session.Record(scene, "Add Wall");
        session.Flush(file, "id5", null, null);

        // An empty plate has no picture.
        SceneSerializer.Save(model, new Scene(), projectFile: "id5");
        ProjectFile.Write(file, "id5", model, null, null);

        Assert.Null(ThreeMf.ReadThumbnail(file));
        Assert.Empty(SceneSerializer.Load(file));
        Assert.Equal("Add Wall", Assert.Single(ProjectSession.ReadSteps(file)).Label);
    }

    [Fact]
    public void SomethingThatIsNotAProjectFileReadsAsNone()
    {
        File.WriteAllText(At("junk.3dfp"), "not a zip");
        Assert.Null(ProjectFile.ReadInfo(At("junk.3dfp")));
        Assert.Null(ProjectFile.ReadView(At("junk.3dfp")));
    }
}
