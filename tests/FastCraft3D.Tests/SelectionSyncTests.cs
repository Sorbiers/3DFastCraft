using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using FastCraft3D.Geometry;
using FastCraft3D.Model;
using FastCraft3D.View;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>
/// The object list and the 3D view both change one selection. These pin down that the scene
/// stays the single source of truth and that nothing silently undoes a selection made in code.
/// </summary>
public class SelectionSyncTests
{
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

    private static Scene SceneWith(params string[] names)
    {
        var scene = new Scene();
        foreach (var name in names)
            scene.Objects.Add(new SceneObject(name, Primitives.Box(10, 10, 10)));
        return scene;
    }

    /// <summary>The real list, realised so item containers actually exist.</summary>
    private static (Window Window, ListBox List) BuildList(Scene scene)
    {
        var list = new ListBox
        {
            ItemsSource = scene.Objects,
            SelectionMode = SelectionMode.Extended
        };
        var window = new Window { Width = 300, Height = 300, Content = list, ShowInTaskbar = false };
        window.Show();
        window.UpdateLayout();
        return (window, list);
    }

    private static void Run(Scene scene, Action<Window, ListBox, SelectionListSync> body)
    {
        var (window, list) = BuildList(scene);
        using var sync = new SelectionListSync(list, scene);
        try { body(window, list, sync); }
        finally { window.Close(); }
    }

    [Fact]
    public void SelectingInTheModelShowsUpInTheList()
    {
        RunSta(() => Run(SceneWith("A", "B"), (window, list, sync) =>
        {
            var scene = (System.Collections.IList)list.ItemsSource;
            ((SceneObject)scene[1]!).IsSelected = true;
            sync.PushToList();
            window.UpdateLayout();

            Assert.Single(list.SelectedItems);
            Assert.Same(scene[1], list.SelectedItems[0]);
        }));
    }

    /// <summary>
    /// The regression this class exists for. An item added to the collection has its container
    /// generated on a later layout pass, and that container starts out unselected. With a
    /// two-way IsSelected binding the initial false was pushed back into the model, so a newly
    /// inserted object never stayed selected and a click was undone on mouse-up.
    /// </summary>
    [Fact]
    public void SelectingAFreshlyAddedObjectSurvivesContainerGeneration()
    {
        RunSta(() =>
        {
            var scene = SceneWith("A");
            Run(scene, (window, list, sync) =>
            {
                // Exactly the order AddObjectsCommand uses: add first, then select.
                var added = new SceneObject("B", Primitives.Box(10, 10, 10));
                scene.Objects.Add(added);
                added.IsSelected = true;
                sync.PushToList();

                // The layout pass that generates the new container.
                window.UpdateLayout();

                Assert.True(added.IsSelected, "the new object was deselected when its row appeared");
                Assert.Single(scene.Selection);
                Assert.Same(added, scene.Selection[0]);
            });
        });
    }

    /// <summary>
    /// The other half of the same trap: a model selection must survive repeated layout passes,
    /// which is what happens between pressing and releasing the mouse.
    /// </summary>
    [Fact]
    public void AModelSelectionSurvivesRepeatedLayoutPasses()
    {
        RunSta(() =>
        {
            var scene = SceneWith("A", "B", "C");
            Run(scene, (window, list, sync) =>
            {
                scene.ClearSelection();
                scene.Objects[1].IsSelected = true;
                sync.PushToList();

                for (int i = 0; i < 5; i++) window.UpdateLayout();

                Assert.Single(scene.Selection);
                Assert.Same(scene.Objects[1], scene.Selection[0]);
            });
        });
    }

    [Fact]
    public void ClickingInTheListSelectsInTheModel()
    {
        RunSta(() =>
        {
            var scene = SceneWith("A", "B");
            Run(scene, (window, list, sync) =>
            {
                bool notified = false;
                sync.ChangedFromList += () => notified = true;

                // What a click in the list does.
                list.SelectedItems.Add(scene.Objects[0]);
                window.UpdateLayout();

                Assert.True(notified, "the list change was not reported");
                Assert.Single(scene.Selection);
                Assert.Same(scene.Objects[0], scene.Selection[0]);
            });
        });
    }

    [Fact]
    public void DeselectingInTheListClearsTheModel()
    {
        RunSta(() =>
        {
            var scene = SceneWith("A", "B");
            Run(scene, (window, list, sync) =>
            {
                scene.Objects[0].IsSelected = true;
                sync.PushToList();
                window.UpdateLayout();

                list.SelectedItems.Remove(scene.Objects[0]);
                window.UpdateLayout();

                Assert.Empty(scene.Selection);
                Assert.False(scene.Objects[0].IsSelected);
            });
        });
    }

    [Fact]
    public void MultiSelectionMirrorsBothWays()
    {
        RunSta(() =>
        {
            var scene = SceneWith("A", "B", "C");
            Run(scene, (window, list, sync) =>
            {
                scene.Objects[0].IsSelected = true;
                scene.Objects[2].IsSelected = true;
                sync.PushToList();
                window.UpdateLayout();
                Assert.Equal(2, list.SelectedItems.Count);

                list.SelectedItems.Add(scene.Objects[1]);
                window.UpdateLayout();

                Assert.Equal(3, scene.Selection.Count);
            });
        });
    }

    /// <summary>Pushing the model into the list must not bounce back and re-enter the sync.</summary>
    [Fact]
    public void MirroringDoesNotFeedBackOnItself()
    {
        RunSta(() =>
        {
            var scene = SceneWith("A", "B");
            Run(scene, (window, list, sync) =>
            {
                int reports = 0;
                sync.ChangedFromList += () => reports++;

                scene.Objects[0].IsSelected = true;
                sync.PushToList();
                window.UpdateLayout();

                Assert.Equal(0, reports);
                Assert.Single(scene.Selection);
            });
        });
    }

    [Fact]
    public void ClearingTheModelClearsTheList()
    {
        RunSta(() =>
        {
            var scene = SceneWith("A", "B");
            Run(scene, (window, list, sync) =>
            {
                scene.Objects[0].IsSelected = true;
                sync.PushToList();
                window.UpdateLayout();

                scene.ClearSelection();
                sync.PushToList();
                window.UpdateLayout();

                Assert.Empty(list.SelectedItems);
                Assert.Empty(scene.Selection);
            });
        });
    }
}
