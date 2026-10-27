using System.IO;
using System.IO.Compression;
using System.Numerics;
using FastCraft3D.Geometry;
using FastCraft3D.Io;
using FastCraft3D.Model;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>The session kept in a .3dfp: the plate after each step, written as it goes, and the work not yet saved.</summary>
public class ProjectSessionTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "3dfp-session-" + Guid.NewGuid().ToString("N"));

    public ProjectSessionTests() => Directory.CreateDirectory(directory);

    public void Dispose()
    {
        try { Directory.Delete(directory, recursive: true); } catch { /* best effort */ }
        GC.SuppressFinalize(this);
    }

    private string At(string name) => Path.Combine(directory, name);

    /// <summary>A saved project with its .3dfp beside it, as a save leaves them.</summary>
    private string SavedProject(Scene scene, string id = "id1")
    {
        string model = At("house.3mf");
        SceneSerializer.Save(model, scene, projectFile: id);
        ProjectFile.Write(ProjectFile.Beside(model), id, model, null, null);
        return ProjectFile.Beside(model);
    }

    private static int MeshesIn(string file)
    {
        using var zip = ZipFile.OpenRead(file);
        return zip.Entries.Count(e => e.FullName.StartsWith("3DFastCraft/meshes/", StringComparison.Ordinal));
    }

    [Fact]
    public void EveryStepComesBackWithItsNameAndThePlateAfterIt()
    {
        var scene = new Scene();
        string file = SavedProject(scene);
        var session = new ProjectSession();

        scene.Objects.Add(new SceneObject("Wall", Primitives.Box(20, 2, 10)));
        session.Record(scene, "Add Wall");
        scene.Objects[0].Position = new Vector3(5, 0, 0);
        session.Record(scene, "Move");
        scene.Objects.Add(new SceneObject("Roof", Primitives.Box(20, 20, 2)));
        session.Record(scene, "Add Roof");
        Assert.True(session.Flush(file, "id1", null, null));

        var steps = ProjectSession.ReadSteps(file);
        Assert.Equal(["Add Wall", "Move", "Add Roof"], steps.Select(s => s.Label));
        Assert.Equal([1, 2, 3], steps.Select(s => s.Number));

        var afterMove = ProjectSession.LoadStep(file, 2);
        Assert.Equal(new Vector3(5, 0, 0), Assert.Single(afterMove).Position);
        Assert.Equal(scene.Objects[0].Mesh.TriangleCount, afterMove[0].Mesh.TriangleCount);
        Assert.Equal(["Wall", "Roof"], ProjectSession.LoadStep(file, 3).Select(o => o.Name));
    }

    [Fact]
    public void AMoveKeepsNoSecondCopyOfTheMesh()
    {
        var scene = new Scene();
        scene.Objects.Add(new SceneObject("Wall", Primitives.Box(20, 2, 10)));
        string file = SavedProject(scene);
        var session = new ProjectSession();

        for (int i = 1; i <= 5; i++)
        {
            scene.Objects[0].Position = new Vector3(i, 0, 0);
            session.Record(scene, "Move");
        }
        session.Flush(file, "id1", null, null);

        Assert.Equal(5, ProjectSession.ReadSteps(file).Count);
        Assert.Equal(1, MeshesIn(file));
    }

    [Fact]
    public void AStepThatChangesNothingIsNotKeptTwice()
    {
        var scene = new Scene();
        scene.Objects.Add(new SceneObject("Wall", Primitives.Box(20, 2, 10)));
        string file = SavedProject(scene);
        var session = new ProjectSession();

        session.Record(scene, "Opened", file);
        session.Record(scene, "Opened again", file);
        session.Flush(file, "id1", null, null);
        session.Record(scene, "Opened", file);
        session.Flush(file, "id1", null, null);

        Assert.Equal("Opened", Assert.Single(ProjectSession.ReadSteps(file)).Label);
    }

    [Fact]
    public void SavingTheProjectKeepsItsSession()
    {
        var scene = new Scene();
        scene.Objects.Add(new SceneObject("Wall", Primitives.Box(20, 2, 10)));
        string file = SavedProject(scene);
        var session = new ProjectSession();
        session.Record(scene, "Add Wall");
        session.Flush(file, "id1", null, null);

        scene.Objects.Add(new SceneObject("Roof", Primitives.Box(20, 20, 2)));
        SavedProject(scene);

        Assert.Equal("Add Wall", Assert.Single(ProjectSession.ReadSteps(file)).Label);
        Assert.Equal(["Wall", "Roof"], SceneSerializer.Load(file).Select(o => o.Name));
    }

    [Fact]
    public void AnotherProjectsFileIsNotWrittenTo()
    {
        var scene = new Scene();
        string file = SavedProject(scene, "theirs");
        var session = new ProjectSession();
        scene.Objects.Add(new SceneObject("Wall", Primitives.Box(20, 2, 10)));
        session.Record(scene, "Add Wall");

        Assert.False(session.Flush(file, "mine", null, null));
        Assert.Empty(ProjectSession.ReadSteps(file));
        Assert.True(session.HasPending);
    }

    [Fact]
    public void UnsavedWorkIsKeptWithItsSettingsUntilItIsSavedOrLetGo()
    {
        var scene = new Scene();
        string file = SavedProject(scene);
        var session = new ProjectSession();
        scene.Objects.Add(new SceneObject("Wall", Primitives.Box(20, 2, 10)));
        var settings = new ProjectSettings(250, 210, 220, "mm", 1, "the north wall");

        session.Flush(file, "id1", scene, settings);

        var left = ProjectSession.ReadLeftover(file);
        Assert.NotNull(left);
        Assert.Equal(1, left.Objects);
        Assert.Equal(Environment.ProcessId, left.ProcessId);
        Assert.Equal("Wall", Assert.Single(ProjectSession.LoadLeftover(file, out var kept)).Name);
        Assert.Equal("the north wall", kept?.Notes);

        session.ForgetLeftover();
        Assert.Null(ProjectSession.ReadLeftover(file));
    }

    [Fact]
    public void ForgettingOlderStepsTakesOutTheMeshesOnlyTheyUsed()
    {
        var scene = new Scene();
        string file = SavedProject(scene);
        var session = new ProjectSession();

        scene.Objects.Add(new SceneObject("Block", Primitives.Box(10, 10, 10)));
        session.Record(scene, "Add Block");
        scene.Objects[0].Mesh = Primitives.Box(12, 10, 10);
        session.Record(scene, "Stretch");
        scene.Objects[0].Mesh = Primitives.Box(14, 10, 10);
        session.Record(scene, "Stretch");
        session.Flush(file, "id1", null, null);
        Assert.Equal(3, MeshesIn(file));

        ProjectSession.ForgetBefore(file, 3);

        Assert.Equal([3], ProjectSession.ReadSteps(file).Select(s => s.Number));
        Assert.Equal(1, MeshesIn(file));
        Assert.Single(ProjectSession.LoadStep(file, 3));
    }

    [Fact]
    public void WorkKeptInAProjectFileIsOfferedAtTheNextStartAndTakenOutOfItWhenAnswered()
    {
        var scene = new Scene();
        string file = SavedProject(scene);
        scene.Objects.Add(new SceneObject("Wall", Primitives.Box(20, 2, 10)));
        new ProjectSession().Flush(file, "id1", scene, null);

        string folder = At("recovery");
        Recovery.KeepIn(file, At("house.3mf"), 1, folder);

        var point = Assert.Single(Recovery.Abandoned(folder, (_, _) => false));
        Assert.Equal(file, point.ProjectFile);

        Recovery.Discard(point);

        Assert.True(File.Exists(file));
        Assert.Null(ProjectSession.ReadLeftover(file));
        Assert.Empty(Recovery.Abandoned(folder, (_, _) => false));
    }
}
