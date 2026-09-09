using System.IO;
using FastCraft3D.Io;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>
/// What the app will take from outside itself - dropped on the window, or passed by Windows when
/// a file is opened with the exe.
/// </summary>
public class IncomingFileTests : IDisposable
{
    private readonly string folder = Path.Combine(
        Path.GetTempPath(), "3dfc-incoming-" + Guid.NewGuid().ToString("N"));

    public IncomingFileTests() => Directory.CreateDirectory(folder);

    public void Dispose()
    {
        try { Directory.Delete(folder, recursive: true); } catch { /* the temp folder can wait */ }
        GC.SuppressFinalize(this);
    }

    private string Make(string name)
    {
        string path = Path.Combine(folder, name);
        File.WriteAllText(path, string.Empty);
        return path;
    }

    [Fact]
    public void ModelsAndProjectsAreUnderstood()
    {
        Assert.True(IncomingFiles.Understood("part.stl"));
        Assert.True(IncomingFiles.Understood("part.obj"));
        Assert.True(IncomingFiles.Understood("house.3dfc"));
    }

    /// <summary>Whatever the case Windows hands it over in.</summary>
    [Fact]
    public void TheExtensionIsReadWithoutRegardToCase()
    {
        Assert.True(IncomingFiles.Understood("PART.STL"));
        Assert.True(IncomingFiles.Understood("House.3DFC"));
    }

    [Fact]
    public void AnythingElseIsNot()
    {
        Assert.False(IncomingFiles.Understood("photo.jpg"));
        Assert.False(IncomingFiles.Understood("notes"));
        Assert.False(IncomingFiles.Understood("archive.stl.zip"));
    }

    [Fact]
    public void OnlyAProjectReplacesThePlate()
    {
        Assert.True(IncomingFiles.IsProject("house.3dfc"));
        Assert.False(IncomingFiles.IsProject("house.stl"));
    }

    [Fact]
    public void TheFilesWantedAreTheOnesThatAreThereAndUnderstood()
    {
        string model = Make("part.stl");
        string project = Make("house.3dfc");
        Make("photo.jpg");

        var wanted = IncomingFiles.Wanted(
            [project, model, Path.Combine(folder, "photo.jpg"), Path.Combine(folder, "gone.stl")]);

        Assert.Equal([project, model], wanted);
    }

    /// <summary>
    /// A command line can carry switches, and a shortcut can point at a file that has moved.
    /// Neither is worth a dialog in front of someone who only double-clicked a model.
    /// </summary>
    [Fact]
    public void SwitchesAndMissingFilesAreDroppedRatherThanReported()
    {
        Assert.Empty(IncomingFiles.Wanted(["--safe-mode", "", "  "]));
        Assert.Empty(IncomingFiles.Wanted([Path.Combine(folder, "never-existed.3dfc")]));
        Assert.Empty(IncomingFiles.Wanted(null));
    }
}
