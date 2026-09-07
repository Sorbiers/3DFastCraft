using System.Runtime.ExceptionServices;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using FastCraft3D.Geometry;
using FastCraft3D.Model;
using FastCraft3D.View;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>
/// Selecting more than one row through UI Automation.
///
/// Every boolean needs two objects selected, and the object list is the only way a screen reader
/// can do it - the viewport exposes nothing to automation at all. It did not work: adding a
/// second row replaced the first, and the list went on reporting that it can select several, so
/// nothing anywhere said the call had not done what it promised. Ctrl+click was fine throughout,
/// which is why it took a build script failing to notice.
///
/// Read what these tests do and do not prove. They hold <see cref="ObjectListBox"/> to the
/// behaviour the pattern promises, and they will catch it if that breaks. They do **not**
/// reproduce the original failure: a stock ListBox passes all of them, even with
/// <see cref="SelectionListSync"/> mirroring into a scene and the same push-then-clear the app
/// does on the way in. Whatever else the running window brings - a binding, the renderer's own
/// subscriptions, focus, a scroll viewer - was not isolated. The fix is verified where the fault
/// is: in the application, where five rows now select one at a time and did not before.
///
/// A test that has never been seen to fail is worth exactly what it can be shown to catch.
/// </summary>
public class ObjectListAutomationTests
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

    /// <summary>The provider for one row, the way an automation client reaches it.</summary>
    private static ISelectionItemProvider RowOf(ListBox list, string name)
    {
        var peer = (ListBoxAutomationPeer)UIElementAutomationPeer.CreatePeerForElement(list);
        var row = peer.GetChildren()!
            .Cast<ItemAutomationPeer>()
            .Single(p => ((SceneObject)p.Item).Name == name);

        return (ISelectionItemProvider)row.GetPattern(PatternInterface.SelectionItem)!;
    }

    private static IEnumerable<string> SelectedIn(Scene scene) =>
        scene.Selection.Select(o => o.Name);

    /// <summary>
    /// Five objects, a list showing them, and the sync that keeps the two in agreement.
    ///
    /// The window is needed because an ItemsControl generates its item containers during layout
    /// and its automation peer reports no children until they exist - measuring and arranging by
    /// hand is not enough. It is never shown to anyone: off the side of the screen, no taskbar
    /// entry, not activated.
    /// </summary>
    private static void WithFiveObjects(Action<ListBox, Scene> body)
    {
        var scene = new Scene();
        foreach (var name in new[] { "a", "b", "c", "d", "e" })
            scene.Objects.Add(new SceneObject(name, Primitives.Box(10, 10, 10)));

        var list = new ObjectListBox
        {
            ItemsSource = scene.Objects,
            SelectionMode = SelectionMode.Extended
        };

        var sync = new SelectionListSync(list, scene);
        sync.ChangedFromList += sync.PushToList;   // as the window wires it, through RefreshSelection

        var window = new System.Windows.Window
        {
            Content = list,
            Width = 200,
            Height = 400,
            Left = -4000,
            Top = -4000,
            ShowActivated = false,
            ShowInTaskbar = false,
            WindowStyle = System.Windows.WindowStyle.None
        };

        window.Show();
        try
        {
            list.UpdateLayout();

            // As the app arrives at it: inserting a shape leaves that shape selected, and the
            // driver clears the selection before picking what it wants. Both go through
            // PushToList, so the list has been written to from the scene side before any
            // automation call is made.
            scene.Objects[^1].IsSelected = true;
            sync.PushToList();
            foreach (var o in scene.Objects) o.IsSelected = false;
            sync.PushToList();

            body(list, scene);
        }
        finally
        {
            sync.Dispose();
            window.Close();
        }
    }

    [Fact]
    public void AddingRowsBuildsUpASelectionRatherThanReplacingIt()
    {
        RunSta(() => WithFiveObjects((list, scene) =>
        {
            RowOf(list, "a").Select();
            RowOf(list, "b").AddToSelection();
            RowOf(list, "c").AddToSelection();

            Assert.Equal(new[] { "a", "b", "c" }, SelectedIn(scene));
        }));
    }

    [Fact]
    public void SelectStillMeansOnlyThisOne()
    {
        RunSta(() => WithFiveObjects((list, scene) =>
        {
            RowOf(list, "a").AddToSelection();
            RowOf(list, "b").AddToSelection();
            RowOf(list, "d").Select();

            Assert.Equal(new[] { "d" }, SelectedIn(scene));
        }));
    }

    [Fact]
    public void ARowCanBeTakenBackOutOfTheSelection()
    {
        RunSta(() => WithFiveObjects((list, scene) =>
        {
            RowOf(list, "a").Select();
            RowOf(list, "b").AddToSelection();
            RowOf(list, "c").AddToSelection();
            RowOf(list, "b").RemoveFromSelection();

            Assert.Equal(new[] { "a", "c" }, SelectedIn(scene));
            Assert.False(RowOf(list, "b").IsSelected);
            Assert.True(RowOf(list, "c").IsSelected);
        }));
    }

    /// <summary>Adding the same row twice must not put it in the selection twice.</summary>
    [Fact]
    public void AddingARowAlreadyInTheSelectionChangesNothing()
    {
        RunSta(() => WithFiveObjects((list, scene) =>
        {
            RowOf(list, "a").Select();
            RowOf(list, "a").AddToSelection();

            Assert.Equal(new[] { "a" }, SelectedIn(scene));
            Assert.Single(list.SelectedItems);
        }));
    }

    /// <summary>The whole point: a selection built one row at a time stays built.</summary>
    [Fact]
    public void AllFiveCanBeSelectedOneAtATime()
    {
        RunSta(() => WithFiveObjects((list, scene) =>
        {
            RowOf(list, "a").Select();
            foreach (var name in new[] { "b", "c", "d", "e" }) RowOf(list, name).AddToSelection();

            Assert.Equal(new[] { "a", "b", "c", "d", "e" }, SelectedIn(scene));
        }));
    }
}
