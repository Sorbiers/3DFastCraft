using System.Windows.Controls;
using FastCraft3D.Model;

namespace FastCraft3D.View;

/// <summary>
/// Keeps the object list and the scene's selection in agreement, with the scene as the single
/// source of truth.
///
/// The obvious approach - binding <c>ListBoxItem.IsSelected</c> two-way in an ItemContainerStyle -
/// is quietly broken here. A ListBox generates item containers on a later layout pass, and a
/// freshly generated container starts out unselected because the Selector's own SelectedItems
/// does not know about it yet. With a two-way binding that initial <c>false</c> is pushed back
/// into the model, silently undoing a selection that was made in code. It shows up as an object
/// that highlights when you press the mouse and goes dark when you release, and as a newly
/// inserted shape that never appears selected at all.
///
/// So the binding is gone and the two sides are copied explicitly, one direction at a time,
/// behind a re-entrancy flag so an update never triggers its own mirror image.
/// </summary>
public sealed class SelectionListSync : IDisposable
{
    private readonly ListBox list;
    private readonly Scene scene;
    private bool syncing;

    public SelectionListSync(ListBox list, Scene scene)
    {
        this.list = list;
        this.scene = scene;
        list.SelectionChanged += OnListSelectionChanged;
    }

    /// <summary>Raised when the user changed the selection by clicking in the list.</summary>
    public event Action? ChangedFromList;

    /// <summary>Copies the scene's selection into the list. Safe to call after any model change.</summary>
    public void PushToList()
    {
        if (syncing) return;

        syncing = true;
        try
        {
            // Materialised before the clear: a deferred query would be re-evaluated afterwards,
            // and any future change to how clearing behaves would then silently empty it.
            var selected = scene.Objects.Where(o => o.IsSelected).ToList();

            list.SelectedItems.Clear();
            foreach (var o in selected)
                list.SelectedItems.Add(o);
        }
        finally
        {
            syncing = false;
        }
    }

    private void OnListSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (syncing) return;

        syncing = true;
        try
        {
            foreach (var o in e.RemovedItems.OfType<SceneObject>()) o.IsSelected = false;
            foreach (var o in e.AddedItems.OfType<SceneObject>()) o.IsSelected = true;

            // Raised inside the guard so the refresh it triggers cannot bounce straight back.
            ChangedFromList?.Invoke();
        }
        finally
        {
            syncing = false;
        }
    }

    public void Dispose() => list.SelectionChanged -= OnListSelectionChanged;
}
