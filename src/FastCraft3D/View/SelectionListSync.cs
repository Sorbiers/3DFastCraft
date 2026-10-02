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
///
/// The list can also hold an assembly's heading, above its parts. Picking the heading picks every
/// part as one pick; letting go of the heading on its own - Ctrl+click - lets go of them all.
/// </summary>
public sealed class SelectionListSync : IDisposable
{
    private readonly ListBox list;
    private readonly Scene scene;
    private bool syncing;
    private int holding;

    public SelectionListSync(ListBox list, Scene scene)
    {
        this.list = list;
        this.scene = scene;
        list.SelectionChanged += OnListSelectionChanged;
    }

    /// <summary>Raised when the user changed the selection by clicking in the list.</summary>
    public event Action? ChangedFromList;

    /// <summary>
    /// Stops listening while the rows are rebuilt, and puts the selection back when they are done.
    ///
    /// Folding an assembly takes its rows out of the list, and the list lets go of a row it no
    /// longer holds - which, heard as a click, let go of the parts themselves. Folding is not
    /// deselecting, and neither is a heading going because its assembly was ungrouped.
    /// </summary>
    public void Hold() => holding++;

    public void Release()
    {
        if (holding > 0 && --holding == 0) PushToList();
    }

    /// <summary>Copies the scene's selection into the list. Safe to call after any model change.</summary>
    public void PushToList()
    {
        if (syncing || holding > 0) return;

        syncing = true;
        try
        {
            // Materialised before the clear: a deferred query would be re-evaluated afterwards,
            // and any future change to how clearing behaves would then silently empty it. Read
            // off the rows rather than the scene, since a folded assembly's parts have none.
            var selected = list.Items.Cast<object>().Where(Chosen).ToList();

            list.SelectedItems.Clear();
            foreach (var item in selected)
                list.SelectedItems.Add(item);
        }
        finally
        {
            syncing = false;
        }
    }

    private static bool Chosen(object item) => item switch
    {
        SceneObject o => o.IsSelected,
        Assembly a => a.IsSelected,
        _ => false
    };

    private void OnListSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (syncing || holding > 0) return;

        syncing = true;
        try
        {
            var letGo = e.RemovedItems.OfType<SceneObject>().ToList();

            foreach (var a in e.RemovedItems.OfType<Assembly>())
            {
                a.IsSelected = false;

                // The heading let go of on its own is the whole assembly let go of. Let go of
                // along with its parts' rows, it is a click on something else, and those rows
                // say for themselves which of them are staying.
                if (!letGo.Any(o => o.Assembly == a))
                    foreach (var o in scene.MembersOf(a)) o.IsSelected = false;
            }

            foreach (var o in letGo) o.IsSelected = false;
            foreach (var o in e.AddedItems.OfType<SceneObject>()) o.IsSelected = true;
            foreach (var a in e.AddedItems.OfType<Assembly>()) scene.SelectAssembly(a);

            // Raised inside the guard so the refresh it triggers cannot bounce straight back.
            ChangedFromList?.Invoke();
        }
        finally
        {
            syncing = false;
        }

        // The rows now say something the scene does not: a heading picked, whose parts' rows
        // need lighting up; a part let go of, whose heading has to go dark; a hidden or locked
        // row that refused to be selected and should not stay highlighted.
        if (Disagrees()) PushToList();
    }

    private bool Disagrees()
    {
        var shown = new HashSet<object>(list.SelectedItems.Cast<object>());
        return list.Items.Cast<object>().Any(item => Chosen(item) != shown.Contains(item));
    }

    public void Dispose() => list.SelectionChanged -= OnListSelectionChanged;
}
