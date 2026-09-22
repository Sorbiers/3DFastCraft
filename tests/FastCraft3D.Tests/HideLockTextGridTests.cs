using System.IO;
using System.Numerics;
using System.Runtime.ExceptionServices;
using FastCraft3D.Geometry;
using FastCraft3D.Io;
using FastCraft3D.Model;
using FastCraft3D.Text;
using FastCraft3D.View;
using FastCraft3D.ViewModels;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>Hidden and locked objects, lettering as an object, and repeating in a grid.</summary>
public class HideLockTextGridTests
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

    private static SceneObject Cube(string name, float x = 0) =>
        new(name, Primitives.Box(10, 10, 10)) { Position = new Vector3(x, 0, 5) };

    // --- Hide and lock -----------------------------------------------------------------

    [Fact]
    public void AHiddenOrLockedObjectCannotBeSelected()
    {
        var hidden = Cube("Hidden");
        var locked = Cube("Locked");
        hidden.IsHidden = true;
        locked.IsLocked = true;

        hidden.IsSelected = true;
        locked.IsSelected = true;

        Assert.False(hidden.IsSelected);
        Assert.False(locked.IsSelected);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void HidingOrLockingASelectedObjectLetsGoOfIt(bool hide)
    {
        var cube = Cube("Cube");
        cube.IsSelected = true;

        if (hide) cube.IsHidden = true;
        else cube.IsLocked = true;

        Assert.False(cube.IsSelected);
    }

    [Fact]
    public void SelectAllPassesOverWhatIsHiddenOrLocked()
    {
        RunSta(() =>
        {
            var model = new MainViewModel();
            var shown = Cube("Shown");
            var hidden = Cube("Hidden", 20);
            var locked = Cube("Locked", 40);
            foreach (var o in new[] { shown, hidden, locked }) model.Scene.Objects.Add(o);
            hidden.IsHidden = true;
            locked.IsLocked = true;

            model.SelectAllCommand.Execute(null);

            Assert.Equal([shown], model.Scene.Selection);
        });
    }

    [Fact]
    public void TheKeysHideAndShowTheSelectionAndLockAndUnlockIt()
    {
        RunSta(() =>
        {
            var model = new MainViewModel();
            var cube = Cube("Cube");
            model.Scene.Objects.Add(cube);
            cube.IsSelected = true;
            model.RefreshSelection();

            model.HideSelection();
            Assert.True(cube.IsHidden);
            Assert.True(model.AnyHidden);
            Assert.Empty(model.Scene.Selection);

            model.ShowAll();
            Assert.False(cube.IsHidden);
            Assert.False(model.AnyHidden);

            cube.IsSelected = true;
            model.LockSelection();
            Assert.True(cube.IsLocked);
            Assert.True(model.AnyLocked);

            model.UnlockAll();
            Assert.False(cube.IsLocked);
        });
    }

    [Fact]
    public void ExportingEverythingLeavesTheHiddenOut()
    {
        var scene = new Scene();
        var shown = Cube("Shown");
        var hidden = Cube("Hidden", 20);
        var locked = Cube("Locked", 40);
        foreach (var o in new[] { shown, hidden, locked }) scene.Objects.Add(o);
        hidden.IsHidden = true;
        locked.IsLocked = true;

        var subjects = ExportComposer.Subjects(scene, new ExportOptions(ExportFormat.BinaryStl, SelectedOnly: false, DropToPlate: false));

        Assert.Equal([shown, locked], subjects);
    }

    [Fact]
    public void HiddenAndLockedAreKeptInTheProjectFile()
    {
        string path = Path.Combine(Path.GetTempPath(), $"hide-lock-{Guid.NewGuid():N}.3dfc");
        try
        {
            var scene = new Scene();
            var hidden = Cube("Hidden");
            var locked = Cube("Locked", 20);
            scene.Objects.Add(hidden);
            scene.Objects.Add(locked);
            scene.Objects.Add(Cube("Plain", 40));
            hidden.IsHidden = true;
            locked.IsLocked = true;

            SceneSerializer.Save(path, scene);
            var loaded = SceneSerializer.Load(path).ToDictionary(o => o.Name);

            Assert.True(loaded["Hidden"].IsHidden);
            Assert.False(loaded["Hidden"].IsLocked);
            Assert.True(loaded["Locked"].IsLocked);
            Assert.False(loaded["Plain"].IsHidden || loaded["Plain"].IsLocked);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void HideAndLockHaveKeysInTheList()
    {
        Assert.Equal(KeyAction.HideSelection, Shortcuts.ActionFor(System.Windows.Input.Key.H, System.Windows.Input.ModifierKeys.None));
        Assert.Equal(KeyAction.ShowAll, Shortcuts.ActionFor(System.Windows.Input.Key.H, System.Windows.Input.ModifierKeys.Alt));
        Assert.Equal(KeyAction.LockSelection, Shortcuts.ActionFor(System.Windows.Input.Key.L, System.Windows.Input.ModifierKeys.None));
        Assert.Equal(KeyAction.UnlockAll, Shortcuts.ActionFor(System.Windows.Input.Key.L, System.Windows.Input.ModifierKeys.Alt));
    }

    // --- Text --------------------------------------------------------------------------

    [Fact]
    public void LetteringIsAClosedSolidAsTallAndThickAsAsked()
    {
        RunSta(() =>
        {
            var mesh = TextObject.Build(new TextOptions { Text = "HELLO", Font = "Arial", Height = 12f, Depth = 4f, Bold = true });

            Assert.True(mesh.TriangleCount > 0);
            Assert.True(mesh.Welded().CheckHealth().IsWatertight);

            var size = mesh.ComputeBounds().Size;
            Assert.Equal(12f, size.Y, 0.6f);
            Assert.Equal(4f, size.Z, 3);
            Assert.True(size.X > 40f, $"five capitals only {size.X:0.#} mm wide");
        });
    }

    [Fact]
    public void UprightLetteringStandsUpToFaceTheFront()
    {
        RunSta(() =>
        {
            var flat = TextObject.Build(new TextOptions { Text = "HI", Font = "Arial", Height = 10f, Depth = 2f }).ComputeBounds().Size;
            var upright = TextObject.Build(new TextOptions { Text = "HI", Font = "Arial", Height = 10f, Depth = 2f, Layout = TextLayout.Upright }).ComputeBounds().Size;

            Assert.Equal(flat.X, upright.X, 3);
            Assert.Equal(flat.Y, upright.Z, 3);
            Assert.Equal(flat.Z, upright.Y, 3);
        });
    }

    [Fact]
    public void NoLettersMakeNothing()
    {
        RunSta(() => Assert.Equal(0, TextObject.Build(new TextOptions { Text = "   " }).TriangleCount));
    }

    [Fact]
    public void TheTextPanelOpensShowingItsLettering()
    {
        RunSta(() =>
        {
            Mesh? shown = null;
            var dialog = new TextDialog(new TextOptions { Text = "Sign", Font = "Arial" }, ["Arial", "Segoe UI"], mesh => shown = mesh);

            Assert.NotNull(shown);
            dialog.Close();
            Assert.Null(shown);
        });
    }

    // --- Grid --------------------------------------------------------------------------

    private static string Namer(string name) => name + "'";

    [Fact]
    public void AGridMakesEveryPlaceButTheOriginalsGapApart()
    {
        var cube = Cube("Cube");

        var copies = RepeatArray.MakeGrid([cube], new GridSettings(3, 2, 1, new Vector3(5, 7, 0), Gaps: true), Namer);

        Assert.Equal(5, copies.Count);
        var places = copies.Select(c => (c.Position.X, c.Position.Y)).ToHashSet();
        Assert.Contains((15f, 0f), places);
        Assert.Contains((30f, 17f), places);
        Assert.DoesNotContain((0f, 0f), places);
        Assert.All(copies, c => Assert.Equal(5f, c.Position.Z, 3));
    }

    [Fact]
    public void CentreToCentreTheSpacingIsThePitch()
    {
        var copies = RepeatArray.MakeGrid([Cube("Cube")], new GridSettings(2, 1, 3, new Vector3(8, 0, 12), Gaps: false), Namer);

        Assert.Equal(5, copies.Count);
        Assert.Contains(copies, c => c.Position == new Vector3(8, 0, 29));
    }

    [Fact]
    public void ASelectionIsRepeatedWholeAndTooManyIsNothing()
    {
        var parts = new[] { Cube("A"), Cube("B", 12) };

        var copies = RepeatArray.MakeGrid(parts, new GridSettings(2, 2, 1, new Vector3(3, 3, 0), Gaps: true), Namer);
        Assert.Equal(6, copies.Count);

        // The pair is 22 mm wide, so the next column starts 25 mm on - and both parts move by it.
        Assert.Contains(copies, c => c.Name == "A'" && c.Position == new Vector3(25, 0, 5));
        Assert.Contains(copies, c => c.Name == "B'" && c.Position == new Vector3(37, 0, 5));

        Assert.Empty(RepeatArray.MakeGrid(parts, new GridSettings(30, 30, 1, Vector3.One, Gaps: true), Namer));
    }
}
