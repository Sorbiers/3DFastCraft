using System.IO;
using System.Runtime.ExceptionServices;
using FastCraft3D.Geometry;
using FastCraft3D.Io;
using FastCraft3D.Model;
using FastCraft3D.ViewModels;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>
/// What a file dropped on the window turns into.
///
/// The window asks which of the two it means and then calls one of these, so the drop and the
/// Import button cannot come to disagree about what a .stl, an .obj or a project is worth.
/// </summary>
public class DroppedFilesTests : IDisposable
{
    private readonly string folder =
        Path.Combine(Path.GetTempPath(), "3dfc-drop-" + Guid.NewGuid().ToString("N")[..8]);

    public DroppedFilesTests() => Directory.CreateDirectory(folder);

    public void Dispose()
    {
        try { Directory.Delete(folder, recursive: true); } catch { /* a temp folder, not worth failing over */ }
    }

    private string WriteStl(string name, float size)
    {
        string path = Path.Combine(folder, name + ".stl");
        StlWriter.Write(path, Primitives.Box(size, size, size));
        return path;
    }

    private string WriteProject(params string[] names)
    {
        var scene = new Scene();
        foreach (string name in names) scene.Objects.Add(new SceneObject(name, Primitives.Box(8, 8, 8)));

        string path = Path.Combine(folder, "plate" + SceneSerializer.Extension);
        SceneSerializer.Save(path, scene);
        return path;
    }

    private static void WithModel(Action<MainViewModel> body)
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

    [Fact]
    public void AModelFileLandsOnThePlate()
    {
        string stl = WriteStl("block", 20);

        WithModel(model =>
        {
            model.ImportFiles([stl]);

            Assert.Single(model.Scene.Objects);
            Assert.Equal("block", model.Scene.Objects[0].Name);
        });
    }

    /// <summary>
    /// Several at once, in one step. A house arriving in five parts and then not being wanted is
    /// one Ctrl+Z, not five.
    /// </summary>
    [Fact]
    public void SeveralFilesArriveTogetherAndLeaveTogether()
    {
        string[] files = [WriteStl("one", 10), WriteStl("two", 12), WriteStl("three", 14)];

        WithModel(model =>
        {
            model.ImportFiles(files);
            Assert.Equal(3, model.Scene.Objects.Count);

            model.Undo.Undo();
            Assert.Empty(model.Scene.Objects);
        });
    }

    /// <summary>
    /// A project imported joins the plate rather than replacing it. Opening it would have thrown
    /// away whatever was already there, which is the one mistake a drop must not make quietly.
    /// </summary>
    [Fact]
    public void AProjectImportedJoinsWhatIsAlreadyThere()
    {
        string project = WriteProject("left", "right");

        WithModel(model =>
        {
            model.InsertCommand.Execute("Cube");
            Assert.Single(model.Scene.Objects);

            model.ImportFiles([project]);

            Assert.Equal(3, model.Scene.Objects.Count);
            Assert.Contains(model.Scene.Objects, o => o.Name == "left");
            Assert.Contains(model.Scene.Objects, o => o.Name == "right");
        });
    }

    /// <summary>
    /// Opening puts the project's own plate up.
    ///
    /// On a plate with unsaved work it asks first, through the same prompt the Open button uses -
    /// which is why this one starts from an untouched plate. A test cannot answer a message box,
    /// and one that raises it waits for a click that never comes.
    /// </summary>
    [Fact]
    public void AProjectOpenedPutsItsOwnPlateUp()
    {
        string project = WriteProject("left", "right");

        WithModel(model =>
        {
            model.OpenDropped(project);

            Assert.Equal(2, model.Scene.Objects.Count);
            Assert.Contains(model.Scene.Objects, o => o.Name == "left");
        });
    }

    /// <summary>Names still come out apart, however many arrive at once.</summary>
    [Fact]
    public void TwoFilesOfTheSameNameAreNotBothCalledTheSameThing()
    {
        string first = WriteStl("part", 10);

        var second = Path.Combine(folder, "again");
        Directory.CreateDirectory(second);
        string twin = Path.Combine(second, "part.stl");
        StlWriter.Write(twin, Primitives.Box(14, 14, 14));

        WithModel(model =>
        {
            model.ImportFiles([first, twin]);

            Assert.Equal(2, model.Scene.Objects.Count);
            Assert.NotEqual(model.Scene.Objects[0].Name, model.Scene.Objects[1].Name);
        });
    }
}
