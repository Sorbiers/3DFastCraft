using System.IO;
using System.Numerics;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using FastCraft3D.Geometry;
using FastCraft3D.Io;
using FastCraft3D.Model;
using FastCraft3D.View;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>The Versions list: a double click on a version restores it.</summary>
public class VersionsDialogTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "3dfc-versions-" + Guid.NewGuid().ToString("N"));

    public VersionsDialogTests() => Directory.CreateDirectory(directory);

    public void Dispose()
    {
        try { Directory.Delete(directory, recursive: true); } catch { /* best effort */ }
        GC.SuppressFinalize(this);
    }

    private string ProjectWithVersions(params string[] labels)
    {
        var scene = new Scene();
        scene.Objects.Add(new SceneObject("Box", Primitives.Box(20, 20, 20)) { Position = new Vector3(0, 0, 10) });

        string path = Path.Combine(directory, "project.3mf");
        SceneSerializer.Save(path, scene);
        foreach (string label in labels) SceneSerializer.SaveVersion(path, scene, label);
        return path;
    }

    private static void RunSta(Action body)
    {
        ExceptionDispatchInfo? error = null;
        var thread = new Thread(() =>
        {
            try { body(); }
            catch (Exception ex) { error = ExceptionDispatchInfo.Capture(ex); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        error?.Throw();
    }

    /// <summary>
    /// Shows the dialog, selects the newest version, double-clicks where <paramref name="on"/> says
    /// once it is up, and gives back what it came to.
    /// </summary>
    private static (bool? Result, int? Restored) DoubleClick(string path, Func<ListBox, DependencyObject> on)
    {
        bool? result = null;
        int? restored = null;

        RunSta(() =>
        {
            var dialog = new VersionsDialog(path);
            var list = (ListBox)dialog.FindName("VersionList");

            dialog.ContentRendered += (_, _) =>
            {
                list.SelectedIndex = 0;

                list.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
                {
                    RoutedEvent = Control.MouseDoubleClickEvent,
                    Source = on(list)
                });

                // A double click that did nothing leaves the dialog up, so close it rather than
                // hang the run.
                dialog.Dispatcher.BeginInvoke(() => { if (dialog.IsVisible) dialog.Close(); }, DispatcherPriority.Background);
            };

            result = dialog.ShowDialog();
            restored = dialog.RestoreIndex;
        });

        return (result, restored);
    }

    [Fact]
    public void ADoubleClickOnAVersionRestoresIt()
    {
        string path = ProjectWithVersions("first", "second");

        var (result, restored) = DoubleClick(path, list => list.ItemContainerGenerator.ContainerFromIndex(0));

        Assert.True(result);
        Assert.Equal(1, restored); // the list is newest first, so its first row is the second one kept
    }

    [Fact]
    public void ADoubleClickOnTheListAndNotOnAVersionRestoresNothing()
    {
        string path = ProjectWithVersions("first", "second");

        var (result, restored) = DoubleClick(path, list => list);

        Assert.NotEqual(true, result);
        Assert.Null(restored);
    }
}
