using System.Collections.Specialized;
using System.Windows.Input;
using FastCraft3D.Model;
using FastCraft3D.Model.Commands;

namespace FastCraft3D.ViewModels;

/// <summary>
/// The object list narrowed to one plate, one filament, or both, from the menu over it.
///
/// Only the list: the view still shows everything, and "Show only the listed objects" hides the
/// rest for a look at one plate on its own. Not kept with the project - a filter left on is the
/// kind of thing that makes a list look as if parts have gone - and a new or opened project
/// starts without one. While it shows one plate, what is added goes onto that plate.
/// </summary>
public partial class MainViewModel
{
    private int? listPlate, listFilament;
    private ICommand? filterPlate, filterFilament, clearListFilter, showOnlyListed;

    public int? ListPlate => listPlate;

    public int? ListFilament => listFilament;

    public bool IsListFiltered => listPlate is not null || listFilament is not null;

    /// <summary>What the strip over the list says while it is filtered: by what, and how many of how many.</summary>
    public string ListFilterSummary
    {
        get
        {
            var by = new List<string>();
            if (listPlate is { } plate) by.Add($"Plate {plate}");
            if (listFilament is { } filament) by.Add($"filament {filament}");
            return $"{string.Join(", ", by)} - {Scene.Objects.Count(Listed)} of {Scene.Objects.Count} objects";
        }
    }

    /// <summary>The plates in use and how many objects are on each, for the menu to offer.</summary>
    public IReadOnlyList<(int Number, int Objects)> PlatesInUse => InUse(o => o.Plate);

    public IReadOnlyList<(int Number, int Objects)> FilamentsInUse => InUse(o => o.Filament);

    /// <summary>A plate's number, or null for every plate.</summary>
    public ICommand FilterPlateCommand => filterPlate ??= new RelayCommand(p =>
    {
        listPlate = p as int?;
        ListFilterChanged();
    });

    public ICommand FilterFilamentCommand => filterFilament ??= new RelayCommand(p =>
    {
        listFilament = p as int?;
        ListFilterChanged();
    });

    public ICommand ClearListFilterCommand => clearListFilter ??= RelayCommand.Simple(ClearListFilter, () => IsListFiltered);

    public ICommand ShowOnlyListedCommand => showOnlyListed ??= RelayCommand.Simple(ShowOnlyListed, () => IsListFiltered);

    /// <summary>
    /// Adding objects, onto the plate the list shows if it shows one. They went to the first plate
    /// and so were missing from the list they were added to. Set before the step, so undo and redo
    /// carry it; a part made in place of another keeps that one's plate and does not come here.
    /// </summary>
    private AddObjectsCommand Added(string label, IReadOnlyList<SceneObject> objects)
    {
        if (listPlate is { } plate)
        {
            foreach (var o in objects) o.Plate = plate;
        }

        return new AddObjectsCommand(label, objects);
    }

    private bool Listed(SceneObject o) =>
        (listPlate is not { } plate || o.Plate == plate) && (listFilament is not { } filament || o.Filament == filament);

    private List<(int Number, int Objects)> InUse(Func<SceneObject, int> of) =>
        Scene.Objects.GroupBy(of).OrderBy(g => g.Key).Select(g => (g.Key, g.Count())).ToList();

    private void WireListFilter()
    {
        // A part moved to another plate leaves a list filtered to this one.
        Undo.Changed += () =>
        {
            if (IsListFiltered) RebuildListRows();
        };

        // The plate cleared for a new or opened project: the filter was the last one's.
        Scene.Objects.CollectionChanged += (_, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Reset && IsListFiltered) ClearListFilter();
        };
    }

    private void ClearListFilter()
    {
        listPlate = null;
        listFilament = null;
        ListFilterChanged();
    }

    private void ShowOnlyListed()
    {
        var change = Scene.Objects.Where(o => o.IsHidden == Listed(o)).ToList();
        if (change.Count == 0) return;

        foreach (var o in change) o.IsHidden = !o.IsHidden;
        AfterHidingOrLocking();
        Status = "Showing only the objects in the list - Alt+H shows everything again";
    }

    private void ListFilterChanged()
    {
        RebuildListRows();
        Raise(nameof(ListPlate));
        Raise(nameof(ListFilament));
        Raise(nameof(IsListFiltered));
        Raise(nameof(ListFilterSummary));
    }
}
