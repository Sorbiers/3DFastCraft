using System.Numerics;
using System.Runtime.ExceptionServices;
using System.Windows.Input;
using FastCraft3D.Geometry;
using FastCraft3D.Model;
using FastCraft3D.View;
using FastCraft3D.ViewModels;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>The one table the keys are bound from and F1 lists: every key in it does something, and no two collide.</summary>
public class ShortcutsTests
{
    [Fact]
    public void EveryCommandKeyRunsACommandTheViewModelHas()
    {
        foreach (var shortcut in Shortcuts.Keyed.Where(s => s.Command is not null))
        {
            var property = typeof(MainViewModel).GetProperty(shortcut.Command!);
            Assert.True(property is not null && typeof(ICommand).IsAssignableFrom(property.PropertyType),
                $"{shortcut.Keys} names {shortcut.Command}, which the view model does not have");
        }
    }

    [Fact]
    public void NoTwoShortcutsShareAKey()
    {
        var shared = Shortcuts.Keyed
            .GroupBy(s => (s.Key, s.Modifiers))
            .Where(g => g.Count() > 1)
            .Select(g => g.First().Keys)
            .ToList();

        Assert.Empty(shared);
    }

    [Fact]
    public void EverythingTheWindowDoesHasAKeyInTheList()
    {
        var listed = Shortcuts.Keyed.Select(s => s.Action).ToHashSet();

        foreach (var action in Enum.GetValues<KeyAction>().Where(a => a != KeyAction.None))
            Assert.Contains(action, listed);
    }

    [Fact]
    public void EveryLineSaysWhichKeysAndWhatTheyDo()
    {
        Assert.All(Shortcuts.All, s =>
        {
            Assert.False(string.IsNullOrWhiteSpace(s.Keys));
            Assert.False(string.IsNullOrWhiteSpace(s.What));
        });
    }

    [Theory]
    [InlineData(Key.S, ModifierKeys.None, KeyAction.Resize)]
    [InlineData(Key.S, ModifierKeys.Control, KeyAction.None)]
    [InlineData(Key.C, ModifierKeys.Control, KeyAction.None)]
    [InlineData(Key.NumPad1, ModifierKeys.None, KeyAction.ViewTop)]
    [InlineData(Key.D4, ModifierKeys.None, KeyAction.ViewIsometric)]
    [InlineData(Key.Left, ModifierKeys.Shift, KeyAction.NudgeLeft)]
    [InlineData(Key.M, ModifierKeys.Shift, KeyAction.None)]
    [InlineData(Key.F1, ModifierKeys.None, KeyAction.ShowShortcuts)]
    public void AKeyIsMatchedWithItsModifiers(Key key, ModifierKeys modifiers, KeyAction expected) =>
        Assert.Equal(expected, Shortcuts.ActionFor(key, modifiers));

    [Theory]
    [InlineData(Key.S, ModifierKeys.Control | ModifierKeys.Shift, "Ctrl+Shift+S")]
    [InlineData(Key.D1, ModifierKeys.None, "1")]
    [InlineData(Key.Delete, ModifierKeys.None, "Del")]
    [InlineData(Key.PageUp, ModifierKeys.None, "Page Up")]
    public void KeysAreWrittenAsTheyAreOnTheKeyboard(Key key, ModifierKeys modifiers, string written) =>
        Assert.Equal(written, Shortcuts.Describe(key, modifiers));

    // --- Nudging ---------------------------------------------------------------------

    private static void WithCube(Action<MainViewModel, SceneObject> body)
    {
        ExceptionDispatchInfo? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                var model = new MainViewModel();
                var cube = new SceneObject("Cube", Primitives.Box(10, 10, 10)) { Position = new Vector3(0, 0, 5) };
                model.Scene.Objects.Add(cube);
                cube.IsSelected = true;
                model.RefreshSelection();
                body(model, cube);
            }
            catch (Exception ex) { error = ExceptionDispatchInfo.Capture(ex); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        error?.Throw();
    }

    [Fact]
    public void AHeldArrowKeyMovesByTheStepAndUndoesAsOne()
    {
        WithCube((model, cube) =>
        {
            Assert.Equal(1f, model.NudgeStep);
            model.SnapStep = 5;
            Assert.Equal(5f, model.NudgeStep);

            for (int i = 0; i < 4; i++) model.Nudge(new Vector3(model.NudgeStep, 0, 0));
            model.EndNudge();

            Assert.Equal(20f, cube.PositionX, 3);

            model.UndoCommand.Execute(null);
            Assert.Equal(0f, cube.PositionX, 3);
            Assert.False(model.UndoCommand.CanExecute(null));
        });
    }

    [Fact]
    public void NudgingDownStopsOnTheBedWhenMovingKeepsToIt()
    {
        WithCube((model, cube) =>
        {
            model.KeepOnBedMove = true;

            model.Nudge(new Vector3(0, 0, -10));
            model.EndNudge();

            Assert.Equal(0f, cube.WorldBounds.Min.Z, 3);
        });
    }
}
