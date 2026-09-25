using System.IO;
using System.Numerics;
using System.Runtime.ExceptionServices;
using FastCraft3D.Geometry;
using FastCraft3D.Io;
using FastCraft3D.ViewModels;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>
/// What the window is called. A model opened by double-clicking it in Windows is the thing being
/// worked on even though it is not a project, and the title said "Untitled" for all of them.
///
/// A project nobody has named yet now carries the name it was given when it was started, which
/// is the one Save will offer - so the title bar and the save dialog are not two answers.
/// </summary>
public class OpenedFileTitleTests : IDisposable
{
    /// <summary>Two lower-case words and the day, as ProjectNames makes them.</summary>
    private const string Named = @"^[a-z]+-[a-z]+_\d{8}";

    private readonly string folder = Path.Combine(Path.GetTempPath(), "3dfc-title-" + Guid.NewGuid().ToString("N"));

    public OpenedFileTitleTests() => Directory.CreateDirectory(folder);

    public void Dispose()
    {
        try { Directory.Delete(folder, recursive: true); } catch { /* the temp folder can wait */ }
        GC.SuppressFinalize(this);
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
    public void AModelOpenedFromWindowsNamesTheWindow()
    {
        string path = Path.Combine(folder, "bracket.3mf");
        ThreeMf.Write(path, [new ObjObject("Bracket", Primitives.Box(10, 10, 10), new Vector3(0.5f, 0.5f, 0.5f))]);

        WithModel(model =>
        {
            Assert.Matches(Named, model.WindowTitle);

            model.OpenFromWindows([path]);

            Assert.StartsWith("bracket.3mf", model.WindowTitle);
            Assert.DoesNotContain("*", model.WindowTitle);
            Assert.Single(model.Scene.Objects);

            // And a new scene is a new project, with a name of its own again.
            model.NewCommand.Execute(null);
            Assert.Matches(Named, model.WindowTitle);
        });
    }
}
