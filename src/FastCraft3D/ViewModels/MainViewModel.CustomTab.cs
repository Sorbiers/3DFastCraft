namespace FastCraft3D.ViewModels;

/// <summary>
/// The Custom tab: whichever ribbon buttons somebody uses most, gathered on a tab of their own.
/// The view model keeps only which buttons and in what order; the window knows what a button is
/// and builds the tab. A button is named by its tab and its label ("Edit/Hollow"), which is what a
/// person would call it and what survives a button moving about the markup.
/// </summary>
public sealed partial class MainViewModel
{
    private readonly List<string> customButtons = [];

    /// <summary>The buttons chosen, in the order they are shown.</summary>
    public IReadOnlyList<string> CustomButtons => customButtons;

    /// <summary>Raised when the choice or its order changes, so the tab is built again.</summary>
    public event Action? CustomButtonsChanged;

    public bool HasCustomButton(string id) => customButtons.Contains(id);

    public void AddCustomButton(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || HasCustomButton(id)) return;

        customButtons.Add(id);
        CustomButtonsChanged?.Invoke();
        SettingsChanged?.Invoke();
    }

    public void RemoveCustomButton(string id)
    {
        if (!customButtons.Remove(id)) return;

        CustomButtonsChanged?.Invoke();
        SettingsChanged?.Invoke();
    }

    /// <summary>Moves a button along the tab: -1 a place towards the start, 1 towards the end. At either end it stays.</summary>
    public void MoveCustomButton(string id, int by)
    {
        int from = customButtons.IndexOf(id);
        int to = Math.Clamp(from + by, 0, customButtons.Count - 1);
        if (from < 0 || to == from) return;

        customButtons.RemoveAt(from);
        customButtons.Insert(to, id);
        CustomButtonsChanged?.Invoke();
        SettingsChanged?.Invoke();
    }

    /// <summary>
    /// The choice read back from the settings file. Without a word to the window and without
    /// saving: the tab is built once after this, and a name that no longer matches a button is
    /// kept rather than dropped, so a button renamed in one version and renamed back is not lost.
    /// </summary>
    private void RestoreCustomButtons(IEnumerable<string>? ids)
    {
        customButtons.Clear();
        if (ids is null) return;

        foreach (string id in ids)
            if (!string.IsNullOrWhiteSpace(id) && !customButtons.Contains(id)) customButtons.Add(id);
    }
}
