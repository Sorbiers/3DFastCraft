using System.Collections.ObjectModel;
using FastCraft3D.Io;

namespace FastCraft3D.ViewModels;

/// <summary>The Light section of the Scene panel: how bright each lamp is, where the key light stands, and the presets.</summary>
public partial class MainViewModel
{
    private LightSettings light = LightSettings.Standard;
    private LightPreset? selectedLightPreset = LightPreset.BuiltIn[0];
    private string lightPresetName = LightPreset.BuiltIn[0].Name;
    private RelayCommand? saveLightPresetCommand;
    private RelayCommand? deleteLightPresetCommand;
    private RelayCommand? resetLightCommand;

    /// <summary>The built-in presets first, then the ones saved by name.</summary>
    public ObservableCollection<LightPreset> LightPresets { get; } = new(LightPreset.BuiltIn);

    /// <summary>How the scene is lit. The window hands it to the viewport; remembered between sessions.</summary>
    public LightSettings Light
    {
        get => light;
        private set
        {
            if (light == value) return;

            light = value;
            Raise(nameof(Light));
            Raise(nameof(LightAmbient));
            Raise(nameof(LightKey));
            Raise(nameof(LightFill));
            Raise(nameof(LightShadow));
            Raise(nameof(LightShine));
            Raise(nameof(LightFollowsView));
            Raise(nameof(LightTurn));
            Raise(nameof(LightHeight));
            MatchLightPreset();
            SettingsChanged?.Invoke();
        }
    }

    /// <summary>
    /// The preset the light is, if it is one. Picking one lights the scene with it; moving a slider
    /// after that leaves none picked, so picking the same one again puts it back.
    /// </summary>
    public LightPreset? SelectedLightPreset
    {
        get => selectedLightPreset;
        set
        {
            if (ReferenceEquals(selectedLightPreset, value)) return;

            selectedLightPreset = value;
            Raise(nameof(SelectedLightPreset));
            if (value is not null) Light = value.Light;
        }
    }

    private void MatchLightPreset()
    {
        if (selectedLightPreset?.Light == light) return;

        selectedLightPreset = LightPresets.FirstOrDefault(p => p.Light == light);
        Raise(nameof(SelectedLightPreset));
    }

    /// <summary>What is typed in the preset box: the name Save keeps the light under, and Delete forgets.</summary>
    public string LightPresetName
    {
        get => lightPresetName;
        set => Set(ref lightPresetName, value ?? "");
    }

    /// <summary>Keeps the light as it is now under the name in the box - over an own preset of that name, never over a built-in one.</summary>
    public RelayCommand SaveLightPresetCommand => saveLightPresetCommand ??= RelayCommand.Simple(SaveLightPreset,
        () => !string.IsNullOrWhiteSpace(lightPresetName));

    /// <summary>Forgets the own preset of the name in the box.</summary>
    public RelayCommand DeleteLightPresetCommand => deleteLightPresetCommand ??= RelayCommand.Simple(DeleteLightPreset,
        () => OwnLightPreset(lightPresetName) is not null);

    /// <summary>Back to the light every version up to 4.7 had.</summary>
    public RelayCommand ResetLightCommand => resetLightCommand ??= RelayCommand.Simple(() => Light = LightSettings.Standard);

    private LightPreset? OwnLightPreset(string name) =>
        LightPresets.FirstOrDefault(p => !p.IsBuiltIn && string.Equals(p.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));

    private void SaveLightPreset()
    {
        string name = lightPresetName.Trim();
        if (name.Length == 0) return;

        var saved = new LightPreset(name, light);
        if (saved.IsBuiltIn)
        {
            Status = $"{name} is one of the built-in presets - save under another name";
            return;
        }

        if (OwnLightPreset(name) is { } old) LightPresets[LightPresets.IndexOf(old)] = saved;
        else LightPresets.Add(saved);

        selectedLightPreset = saved;
        Raise(nameof(SelectedLightPreset));
        LightPresetName = name;
        SettingsChanged?.Invoke();
        Status = $"Saved the light as {name}";
    }

    private void DeleteLightPreset()
    {
        if (OwnLightPreset(lightPresetName) is not { } gone) return;

        LightPresets.Remove(gone);
        if (ReferenceEquals(selectedLightPreset, gone))
        {
            selectedLightPreset = null;
            Raise(nameof(SelectedLightPreset));
        }

        LightPresetName = "";
        SettingsChanged?.Invoke();
        Status = $"Deleted the light preset {gone.Name}";
    }

    /// <summary>The presets saved by name, for the settings file; the built-in ones are not stored.</summary>
    private LightPreset[] OwnLightPresets => LightPresets.Where(p => !p.IsBuiltIn).ToArray();

    private void RestoreLightPresets(LightPreset[]? saved)
    {
        foreach (var own in LightPresets.Where(p => !p.IsBuiltIn).ToList()) LightPresets.Remove(own);
        foreach (var preset in saved ?? [])
            if (!string.IsNullOrWhiteSpace(preset.Name) && !preset.IsBuiltIn && OwnLightPreset(preset.Name) is null)
                LightPresets.Add(preset);
    }

    public double LightAmbient
    {
        get => light.Ambient;
        set => Light = light with { Ambient = Share(value) };
    }

    public double LightKey
    {
        get => light.Key;
        set => Light = light with { Key = Share(value) };
    }

    public double LightFill
    {
        get => light.Fill;
        set => Light = light with { Fill = Share(value) };
    }

    public double LightShadow
    {
        get => light.Shadow;
        set => Light = light with { Shadow = Share(value) };
    }

    public double LightShine
    {
        get => light.Shine;
        set => Light = light with { Shine = Share(value) };
    }

    /// <summary>
    /// Whether the key light follows the view. Switching keeps the light where it is in the scene
    /// at that moment - the window works out the new angles, since only it has the camera.
    /// </summary>
    public bool LightFollowsView
    {
        get => !light.FixedInScene;
        set
        {
            if (value == !light.FixedInScene) return;
            Light = Reframe?.Invoke(light, !value) ?? light with { FixedInScene = !value };
        }
    }

    /// <summary>Given the light and whether it is to be fixed in the scene, the same light in the other frame.</summary>
    public Func<LightSettings, bool, LightSettings>? Reframe { get; set; }

    public double LightTurn
    {
        get => light.Turn;
        set => SwingLight(value, light.Height);
    }

    public double LightHeight
    {
        get => light.Height;
        set => SwingLight(light.Turn, value);
    }

    /// <summary>Puts the key light at these angles: round, wrapped to a half turn either way, and up, from the horizon to overhead.</summary>
    public void SwingLight(double turn, double height)
    {
        if (!double.IsFinite(turn) || !double.IsFinite(height)) return;

        double wrapped = Math.IEEERemainder(turn, 360);
        Light = light with { Turn = (float)wrapped, Height = (float)Math.Clamp(height, 0, 90) };
    }

    private static float Share(double value) => double.IsFinite(value) ? (float)Math.Clamp(value, 0, 1) : 0f;
}
