using System.IO;
using System.Runtime.ExceptionServices;
using FastCraft3D.Io;
using FastCraft3D.ViewModels;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>The Light section's presets: the built-in ones, and those saved by name.</summary>
public class LightPresetTests
{
    private static void RunSta(Action body)
    {
        ExceptionDispatchInfo? error = null;
        var thread = new Thread(() =>
        {
            try { body(); }
            catch (Exception e) { error = ExceptionDispatchInfo.Capture(e); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        error?.Throw();
    }

    [Fact]
    public void ALightSavedByNameIsRememberedAndPickingItLightsTheSceneWithIt() => RunSta(() =>
    {
        string store = Path.Combine(Path.GetTempPath(), $"light-{Guid.NewGuid():N}.json");
        try
        {
            var model = new MainViewModel();
            model.LightAmbient = 0.4;
            model.LightTurn = 45;
            model.LightPresetName = "Workshop";
            model.SaveLightPresetCommand.Execute(null);
            var workshop = model.Light;
            LocalSettings.Save(model.Remembered, store);

            var another = new MainViewModel();
            another.ApplySettings(LocalSettings.Load(store)!.Value);
            Assert.Equal(["Standard", "Soft", "Contrast", "Workshop"], another.LightPresets.Select(p => p.Name));
            Assert.Equal("Workshop", another.SelectedLightPreset?.Name);

            another.SelectedLightPreset = another.LightPresets[1];
            Assert.Equal(LightSettings.Soft, another.Light);

            another.SelectedLightPreset = another.LightPresets[3];
            Assert.Equal(workshop, another.Light);
        }
        finally
        {
            File.Delete(store);
        }
    });

    [Fact]
    public void BuiltInPresetsAreNeitherSavedOverNorDeleted() => RunSta(() =>
    {
        var model = new MainViewModel();
        model.LightAmbient = 0.9;

        model.LightPresetName = "soft";
        model.SaveLightPresetCommand.Execute(null);
        Assert.Equal(LightSettings.Soft, model.LightPresets[1].Light);
        Assert.Equal(3, model.LightPresets.Count);
        Assert.False(model.DeleteLightPresetCommand.CanExecute(null));

        model.LightPresetName = "Mine";
        model.SaveLightPresetCommand.Execute(null);
        model.LightAmbient = 0.2;
        model.SaveLightPresetCommand.Execute(null);
        Assert.Equal(4, model.LightPresets.Count);
        Assert.Equal(0.2f, model.LightPresets[3].Light.Ambient);

        model.DeleteLightPresetCommand.Execute(null);
        Assert.Equal(3, model.LightPresets.Count);
    });

    [Fact]
    public void MovingASliderAfterPickingAPresetLeavesNoneOfThemPicked() => RunSta(() =>
    {
        var model = new MainViewModel();
        model.SelectedLightPreset = model.LightPresets[2];
        Assert.Equal(LightSettings.Contrast, model.Light);

        model.LightKey = 0.5;
        Assert.Null(model.SelectedLightPreset);

        model.ResetLightCommand.Execute(null);
        Assert.Equal("Standard", model.SelectedLightPreset?.Name);
    });
}
