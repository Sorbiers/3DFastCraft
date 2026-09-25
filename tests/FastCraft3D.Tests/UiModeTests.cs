using System.IO;
using System.Runtime.ExceptionServices;
using FastCraft3D.Io;
using FastCraft3D.View;
using FastCraft3D.ViewModels;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>
/// The three levels of ribbon: Classic with only the tools 3D Builder had, Advanced with the
/// working set, and Extended with the specialised and experimental ones on top.
/// </summary>
public class UiModeTests
{
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
    public void TheAppStartsInAdvancedMode() => WithModel(model =>
    {
        Assert.Equal(UiLevel.Advanced, model.UiLevel);
        Assert.True(model.IsAdvancedMode);
        Assert.False(model.IsExtendedMode);
        Assert.False(model.Remembered.ClassicMode);
        Assert.False(model.Remembered.ExtendedMode);
    });

    /// <summary>
    /// Extended is Advanced and more, not a mode beside it. Every button already marked for
    /// Advanced has to keep showing, or moving one tool up a level would take a dozen down with it.
    /// </summary>
    [Fact]
    public void ExtendedShowsEverythingAdvancedDoes() => WithModel(model =>
    {
        model.UiLevel = UiLevel.Extended;

        Assert.True(model.IsAdvancedMode);
        Assert.True(model.IsExtendedMode);
        Assert.True(model.Remembered.ExtendedMode);
        Assert.False(model.Remembered.ClassicMode);

        model.UiLevel = UiLevel.Advanced;
        Assert.True(model.IsAdvancedMode);
        Assert.False(model.IsExtendedMode);
    });

    [Fact]
    public void TheSwitchOffersThreeAndPicksTheOneItIsOn() => WithModel(model =>
    {
        Assert.Equal(3, model.UiModes.Count);

        foreach (var choice in model.UiModes)
        {
            model.UiMode = choice;

            Assert.Equal(choice.Level, model.UiLevel);
            Assert.Equal(choice, model.UiMode);
        }
    });

    [Fact]
    public void ClassicListsNoKeysForToolsItDoesNotShowButLeavesTheRest() => WithModel(model =>
    {
        int events = 0;
        model.SettingsChanged += () => events++;

        model.UiMode = model.UiModes.Single(m => m.Level == UiLevel.Classic);

        Assert.False(model.IsAdvancedMode);
        Assert.Equal(1, events);
        Assert.True(model.Remembered.ClassicMode);

        var listed = model.ShortcutGroups.SelectMany(g => g.Items).ToList();
        Assert.DoesNotContain(listed, s => s.Group == "Sketching" || s.Command == "PrintDrawingCommand");
        Assert.Contains(listed, s => s.Command == "SubtractCommand" || s.Command == "UndoCommand");

        // Still bound: the key works in every mode.
        Assert.Contains(Shortcuts.Keyed, s => s.Command == "PrintDrawingCommand");
    });

    [Fact]
    public void OverhangsAreTurnedOffWhenTheirButtonGoesAway() => WithModel(model =>
    {
        model.ShowOverhangs = true;

        model.UiLevel = UiLevel.Classic;

        Assert.False(model.ShowOverhangs);
    });

    /// <summary>
    /// The third level is stored as its own flag beside the first, rather than as one number
    /// saying which of three. A settings file written before it existed has neither field, the
    /// reader fills both with false, and false on both has to go on meaning Advanced - which is
    /// where everybody already was.
    /// </summary>
    [Fact]
    public void TheModeIsRememberedAndAFileFromBeforeItOpensAdvanced()
    {
        string store = Path.Combine(Path.GetTempPath(), $"modes-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(store, """{"PlateWidth":200,"PlateDepth":200,"PlateHeight":200,"Unit":"mm"}""");

            var old = LocalSettings.Load(store)!.Value;
            Assert.False(old.ClassicMode);
            Assert.False(old.ExtendedMode);

            WithModel(model =>
            {
                model.ApplySettings(old);
                Assert.Equal(UiLevel.Advanced, model.UiLevel);
            });

            foreach (var (settings, wanted) in new (RememberedSettings, UiLevel)[]
            {
                (new RememberedSettings(200f, 200f, 200f, "mm", ClassicMode: true), UiLevel.Classic),
                (new RememberedSettings(200f, 200f, 200f, "mm", ExtendedMode: true), UiLevel.Extended)
            })
            {
                LocalSettings.Save(settings, store);
                var remembered = LocalSettings.Load(store)!.Value;

                WithModel(model =>
                {
                    model.ApplySettings(remembered);
                    Assert.Equal(wanted, model.UiLevel);
                });
            }
        }
        finally
        {
            File.Delete(store);
        }
    }
}
