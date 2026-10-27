using System.Windows.Input;
using FastCraft3D.Model;

namespace FastCraft3D.ViewModels;

/// <summary>
/// The menu over the object list: every object shown, hidden or locked at once, where the eye and
/// the lock on a row do one object and H and L the selection.
/// </summary>
public partial class MainViewModel
{
    private ICommand? hideAll, hideSelection, showOnlySelection, swapHidden, lockAll, lockSelection, swapLocked;

    public ICommand HideAllCommand => hideAll ??= RelayCommand.Simple(
        () => SetHidden(Scene.Objects.Where(o => !o.IsHidden).ToList(), true), () => Scene.Objects.Any(o => !o.IsHidden));

    public ICommand HideSelectionCommand => hideSelection ??= RelayCommand.Simple(HideSelection, () => Scene.Selection.Count > 0);

    public ICommand ShowOnlySelectionCommand => showOnlySelection ??= RelayCommand.Simple(ShowOnlySelection, () => Scene.Selection.Count > 0);

    public ICommand SwapHiddenCommand => swapHidden ??= RelayCommand.Simple(SwapHidden, () => Scene.Objects.Count > 0);

    public ICommand LockAllCommand => lockAll ??= RelayCommand.Simple(
        () => SetLocked(Scene.Objects.Where(o => !o.IsLocked).ToList(), true), () => Scene.Objects.Any(o => !o.IsLocked));

    public ICommand LockSelectionCommand => lockSelection ??= RelayCommand.Simple(LockSelection, () => Scene.Selection.Count > 0);

    public ICommand SwapLockedCommand => swapLocked ??= RelayCommand.Simple(SwapLocked, () => Scene.Objects.Count > 0);

    private void ShowOnlySelection()
    {
        var selected = Scene.Selection.ToHashSet();
        SetHidden(Scene.Objects.Where(o => !o.IsHidden && !selected.Contains(o)).ToList(), true);
        Status = "Showing only the selection - Alt+H shows everything again";
    }

    private void SwapHidden()
    {
        var (was, wasNot) = Split(o => o.IsHidden);
        foreach (var o in was) o.IsHidden = false;
        foreach (var o in wasNot) o.IsHidden = true;
        AfterHidingOrLocking();
        Status = $"Showing {Count(was)}, hid {Count(wasNot)}";
    }

    private void SwapLocked()
    {
        var (was, wasNot) = Split(o => o.IsLocked);
        foreach (var o in was) o.IsLocked = false;
        foreach (var o in wasNot) o.IsLocked = true;
        AfterHidingOrLocking();
        Status = $"Unlocked {Count(was)}, locked {Count(wasNot)}";
    }

    private (List<SceneObject> Was, List<SceneObject> WasNot) Split(Func<SceneObject, bool> test) =>
        (Scene.Objects.Where(test).ToList(), Scene.Objects.Where(o => !test(o)).ToList());
}
