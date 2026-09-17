using System.IO;
using System.Runtime.ExceptionServices;
using FastCraft3D.Io;
using FastCraft3D.View;
using FastCraft3D.ViewModels;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>Classic mode, with only the tools 3D Builder had, and Advanced, with all of them.</summary>
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
        Assert.True(model.IsAdvancedMode);
        Assert.True(model.UiMode.Advanced);
        Assert.False(model.Remembered.ClassicMode);
    });

    [Fact]
    public void ClassicListsNoKeysForToolsItDoesNotShowButLeavesTheRest() => WithModel(model =>
    {
        int events = 0;
        model.SettingsChanged += () => events++;

        model.UiMode = model.UiModes.Single(m => !m.Advanced);

        Assert.False(model.IsAdvancedMode);
        Assert.Equal(1, events);
        Assert.True(model.Remembered.ClassicMode);

        var listed = model.ShortcutGroups.SelectMany(g => g.Items).ToList();
        Assert.DoesNotContain(listed, s => s.Group == "Sketching" || s.Command == "PrintDrawingCommand");
        Assert.Contains(listed, s => s.Command == "SubtractCommand" || s.Command == "UndoCommand");

        // Still bound: the key works in either mode.
        Assert.Contains(Shortcuts.Keyed, s => s.Command == "PrintDrawingCommand");
    });

    [Fact]
    public void OverhangsAreTurnedOffWhenTheirButtonGoesAway() => WithModel(model =>
    {
        model.ShowOverhangs = true;

        model.IsAdvancedMode = false;

        Assert.False(model.ShowOverhangs);
    });

    [Fact]
    public void TheModeIsRememberedAndAFileFromBeforeItOpensAdvanced()
    {
        string store = Path.Combine(Path.GetTempPath(), $"modes-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(store, """{"PlateWidth":200,"PlateDepth":200,"PlateHeight":200,"Unit":"mm"}""");
            Assert.False(LocalSettings.Load(store)!.Value.ClassicMode);

            LocalSettings.Save(new RememberedSettings(200f, 200f, 200f, "mm", ClassicMode: true), store);
            var remembered = LocalSettings.Load(store)!.Value;
            Assert.True(remembered.ClassicMode);

            WithModel(model =>
            {
                model.ApplySettings(remembered);
                Assert.False(model.IsAdvancedMode);
            });
        }
        finally
        {
            File.Delete(store);
        }
    }
}
