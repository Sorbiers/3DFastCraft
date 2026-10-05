using System.IO;
using System.Runtime.ExceptionServices;
using FastCraft3D.Io;
using FastCraft3D.View;
using FastCraft3D.ViewModels;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>
/// The two levels of ribbon: Classic with only the tools 3D Builder had, and Advanced with every tool.
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
        Assert.False(model.Remembered.ClassicMode);
    });

    [Fact]
    public void TheSwitchOffersTwoAndPicksTheOneItIsOn() => WithModel(model =>
    {
        Assert.Equal(2, model.UiModes.Count);

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
    /// A settings file written before the mode existed has no field, the reader fills it with
    /// false, and false has to go on meaning Advanced - which is where everybody already was.
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

            WithModel(model =>
            {
                model.ApplySettings(old);
                Assert.Equal(UiLevel.Advanced, model.UiLevel);
            });

            foreach (var (settings, wanted) in new (RememberedSettings, UiLevel)[]
            {
                (new RememberedSettings(200f, 200f, 200f, "mm", ClassicMode: true), UiLevel.Classic),
                (new RememberedSettings(200f, 200f, 200f, "mm"), UiLevel.Advanced)
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

    /// <summary>
    /// There was a third level, Extended, and a file written then has its flag. It is not read:
    /// whoever chose it is in Advanced, which now has every tool Extended had.
    /// </summary>
    [Fact]
    public void AFileWrittenWhenThereWasAnExtendedLevelOpensAdvanced()
    {
        string store = Path.Combine(Path.GetTempPath(), $"modes-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(store, """{"PlateWidth":200,"PlateDepth":200,"PlateHeight":200,"Unit":"mm","ClassicMode":false,"ExtendedMode":true}""");

            var old = LocalSettings.Load(store)!.Value;

            WithModel(model =>
            {
                model.ApplySettings(old);
                Assert.Equal(UiLevel.Advanced, model.UiLevel);
                Assert.True(model.IsAdvancedMode);
            });
        }
        finally
        {
            File.Delete(store);
        }
    }
}
