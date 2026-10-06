using System.IO;
using System.IO.Compression;
using System.Numerics;
using FastCraft3D.Geometry;
using FastCraft3D.Io;
using FastCraft3D.Model;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>
/// The project saved as a 3MF: the plate as a model any slicer opens, with the project and its
/// versions inside for this app alone.
/// </summary>
public class ProjectThreeMfTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "3dfc-3mfproject-" + Guid.NewGuid().ToString("N"));

    public ProjectThreeMfTests() => Directory.CreateDirectory(directory);

    public void Dispose()
    {
        try { Directory.Delete(directory, recursive: true); } catch { /* best effort */ }
        GC.SuppressFinalize(this);
    }

    private string File_(string name) => Path.Combine(directory, name);

    private static Scene SceneWith(params string[] names)
    {
        var scene = new Scene();
        float x = 0;
        foreach (var name in names)
        {
            scene.Objects.Add(new SceneObject(name, Primitives.Box(20, 20, 20)) { Position = new Vector3(x, 0, 10) });
            x += 30;
        }
        return scene;
    }

    [Fact]
    public void AProjectIsA3mfThatASlicerReadsAsThePlate()
    {
        string path = File_("house.3mf");
        var scene = SceneWith("Wall", "Roof");
        scene.Objects[1].IsHidden = true;

        SceneSerializer.Save(path, scene);

        var parts = ThreeMf.Read(path);
        Assert.Equal("Wall", Assert.Single(parts).Name); // what shows, as an export would give it
        Assert.Equal(2, SceneSerializer.Load(path).Count); // and the whole project for this app
    }

    [Fact]
    public void AProjectCarriesAThumbnailForExplorer()
    {
        string path = File_("thumb.3mf");
        SceneSerializer.Save(path, SceneWith("Box"));

        using var zip = ZipFile.OpenRead(path);
        Assert.NotNull(zip.GetEntry("Metadata/thumbnail.png"));
    }

    [Fact]
    public void VersionsLiveInsideThe3mfAndForgettingOneKeepsTheModel()
    {
        string path = File_("versions.3mf");
        SceneSerializer.SaveVersion(path, SceneWith("A"), "one");
        SceneSerializer.SaveVersion(path, SceneWith("A", "B"), "two");
        SceneSerializer.Save(path, SceneWith("A", "B", "C"));

        SceneSerializer.DeleteVersion(path, 0);

        Assert.Equal("two", Assert.Single(SceneSerializer.ReadVersions(path)).Label);
        Assert.Equal(3, SceneSerializer.Load(path).Count);
        Assert.Equal(3, ThreeMf.Read(path).Count);
    }

    [Fact]
    public void A3mfFromElsewhereIsAModelNotAProject()
    {
        string path = File_("slicer.3mf");
        ThreeMf.Write(path, [new ObjObject("Part", Primitives.Box(10, 10, 10), new Vector3(1, 0, 0))]);

        Assert.False(SceneSerializer.IsProject(path));
        Assert.False(IncomingFiles.IsProject(path));
        Assert.Throws<InvalidDataException>(() => SceneSerializer.Load(path));
        Assert.Empty(SceneSerializer.ReadVersions(path));
    }

    [Fact]
    public void AProject3mfReplacesThePlateWhenOpened()
    {
        string path = File_("mine.3mf");
        SceneSerializer.Save(path, SceneWith("A"));

        Assert.True(SceneSerializer.IsProject(path));
        Assert.True(IncomingFiles.IsProject(path));
    }

    /// <summary>
    /// A dropped model has one thing to mean, so it is imported without a dialog whose Open button
    /// would be greyed. Only a single project is asked about, since it could replace the plate or join it.
    /// </summary>
    [Fact]
    public void OnlyASingleProjectDroppedIsAskedAbout()
    {
        string project = File_("mine.3mf");
        SceneSerializer.Save(project, SceneWith("A"));

        string slicer = File_("slicer.3mf");
        ThreeMf.Write(slicer, [new ObjObject("Part", Primitives.Box(10, 10, 10), new Vector3(1, 0, 0))]);

        string stl = File_("part.stl");
        File.WriteAllText(stl, string.Empty);

        Assert.True(IncomingFiles.NeedsAsking([project]));
        Assert.False(IncomingFiles.NeedsAsking([slicer]));
        Assert.False(IncomingFiles.NeedsAsking([stl]));

        // Several at once can only be imported, whatever is among them.
        Assert.False(IncomingFiles.NeedsAsking([project, slicer]));
    }

    [Fact]
    public void AnOld3dfcIsNoProjectButItsPartsStillRead()
    {
        string path = File_("old.3dfc");
        var scene = SceneWith("A", "B");
        scene.Objects[1].Rotation = new Vector3(0, 0, 45);
        SceneSerializer.Save(path, scene);

        Assert.False(SceneSerializer.IsProject(path));

        // What Import brings in: the parts where they stood, as they were turned.
        var parts = SceneSerializer.Load(path);
        Assert.Equal(new Vector3(30, 0, 10), parts[1].Position);
        Assert.Equal(45f, parts[1].Rotation.Z, 3);
    }

    [Fact]
    public void SavingAsAnotherNameTakesTheVersionsAlong()
    {
        string first = File_("first.3mf");
        SceneSerializer.SaveVersion(first, SceneWith("A"), "first idea");

        string second = File_("second.3mf");
        SceneSerializer.Save(second, SceneWith("A", "B"), versionsFrom: first);

        Assert.Equal("first idea", Assert.Single(SceneSerializer.ReadVersions(second)).Label);
        Assert.Single(SceneSerializer.LoadVersion(second, 0));
        Assert.Equal(2, SceneSerializer.Load(second).Count);
    }

    [Fact]
    public void TheSettingsComeBackFromA3mf()
    {
        string path = File_("settings.3mf");
        SceneSerializer.Save(path, SceneWith("A"), new ProjectSettings(250f, 210f, 180f, "in", 87f));

        SceneSerializer.Load(path, out var settings);

        Assert.Equal(new ProjectSettings(250f, 210f, 180f, "in", 87f), settings);
    }
}
