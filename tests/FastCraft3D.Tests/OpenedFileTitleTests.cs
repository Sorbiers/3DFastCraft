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
/// </summary>
public class OpenedFileTitleTests : IDisposable
{
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
            Assert.StartsWith("Untitled", model.WindowTitle);

            model.OpenFromWindows([path]);

            Assert.StartsWith("bracket.3mf", model.WindowTitle);
            Assert.DoesNotContain("*", model.WindowTitle);
            Assert.Single(model.Scene.Objects);

            // And a new scene is untitled again.
            model.NewCommand.Execute(null);
            Assert.StartsWith("Untitled", model.WindowTitle);
        });
    }
}
