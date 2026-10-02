using System.Collections.ObjectModel;
using FastCraft3D.Geometry;

namespace FastCraft3D.Model;

public sealed class Scene
{
    /// <summary>Build plate edge length in millimetres, matching a common desktop printer bed.</summary>
    public const float PlateSize = 200f;

    /// <summary>How tall a part the printer takes, by default: a cube with the bed.</summary>
    public const float PrintHeight = 200f;

    public ObservableCollection<SceneObject> Objects { get; } = new();

    /// <summary>
    /// Raised when parts join or leave an assembly. Adding and removing objects is already
    /// announced by <see cref="Objects"/>; this is for the changes that leave the list as it was.
    /// </summary>
    public event Action? AssembliesChanged;

    public void NotifyAssembliesChanged() => AssembliesChanged?.Invoke();

    /// <summary>
    /// The assemblies with a part on the plate, in the order their first parts are listed. Worked
    /// out from the parts each time, because that is where membership is kept - an assembly whose
    /// parts have all been deleted is simply not here, and is back when the delete is undone.
    /// </summary>
    public IReadOnlyList<Assembly> Assemblies =>
        Objects.Select(o => o.Assembly).OfType<Assembly>().Distinct().ToList();

    /// <summary>The parts of an assembly that are on the plate, in list order.</summary>
    public IReadOnlyList<SceneObject> MembersOf(Assembly assembly) =>
        Objects.Where(o => o.Assembly == assembly).ToList();

    /// <summary>
    /// Selects every part of an assembly as one pick, and marks the assembly itself selected.
    /// Hidden and locked parts stay out of it, as they stay out of every selection.
    /// </summary>
    public void SelectAssembly(Assembly assembly)
    {
        var members = MembersOf(assembly);
        SceneObject.PickTogether(members);
        assembly.IsSelected = members.Any(o => o.IsSelected);
    }

    /// <summary>Whether every part of it that can be selected is, which an assembly needs to stay selected.</summary>
    public bool IsWhollySelected(Assembly assembly)
    {
        var reachable = MembersOf(assembly).Where(o => o.CanBeSelected).ToList();
        return reachable.Count > 0 && reachable.All(o => o.IsSelected);
    }

    /// <summary>
    /// The selection as the picks that made it, oldest first: one object each, except an assembly
    /// picked by its name, which is all its parts at once.
    ///
    /// For Align, which moves an assembly as one block so its parts keep their places relative to
    /// each other, and for Subtract, which cuts with every part of an assembly picked last. Parts
    /// that shared a pick but have since left the assembly count one by one again.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<SceneObject>> SelectionInPicks =>
        Objects.Where(o => o.IsSelected)
            .GroupBy(o => o.Assembly is null ? (object)o : (o.PickedAt, o.Assembly))
            .OrderBy(g => g.First().PickedAt)
            .Select(g => (IReadOnlyList<SceneObject>)g.ToList())
            .ToList();

    /// <summary>
    /// The last pick, and what was picked before it: the cutter and what it cuts. With the whole
    /// selection one pick - an assembly on its own - the last object is the last pick, as it is
    /// without assemblies.
    /// </summary>
    public (IReadOnlyList<SceneObject> Earlier, IReadOnlyList<SceneObject> Last) SplitLastPick()
    {
        var picks = SelectionInPicks;
        if (picks.Count >= 2) return (picks.Take(picks.Count - 1).SelectMany(p => p).ToList(), picks[^1]);

        var picked = SelectionInPickOrder;
        if (picked.Count == 0) return ([], []);
        return (picked.Take(picked.Count - 1).ToList(), [picked[^1]]);
    }

    /// <summary>An assembly name nothing else on the plate has.</summary>
    public string UniqueAssemblyName(string baseName)
    {
        var taken = new HashSet<string>(Assemblies.Select(a => a.Name), StringComparer.OrdinalIgnoreCase);
        if (!taken.Contains(baseName)) return baseName;

        for (int i = 2; ; i++)
        {
            string candidate = $"{baseName} {i}";
            if (!taken.Contains(candidate)) return candidate;
        }
    }

    public IReadOnlyList<SceneObject> Selection =>
        Objects.Where(o => o.IsSelected).ToList();

    /// <summary>Everything not hidden: what "everything" means to Export and Drawing.</summary>
    public IReadOnlyList<SceneObject> Shown =>
        Objects.Where(o => !o.IsHidden).ToList();

    /// <summary>Everything a tool may change when nothing is selected: neither hidden nor locked.</summary>
    public IReadOnlyList<SceneObject> Workable =>
        Objects.Where(o => o.CanBeSelected).ToList();

    /// <summary>
    /// The same objects in the order they were picked, oldest first.
    ///
    /// Only the tools where the order carries meaning use this - a boolean, where the first is
    /// the one kept and the rest are applied to it. Everything else wants the list order, which
    /// is stable and matches what is on screen.
    /// </summary>
    public IReadOnlyList<SceneObject> SelectionInPickOrder =>
        Objects.Where(o => o.IsSelected).OrderBy(o => o.PickedAt).ToList();

    public Bounds ComputeBounds()
    {
        var bounds = Bounds.Empty;
        foreach (var o in Objects)
            bounds = bounds.Union(o.WorldBounds);
        return bounds;
    }

    /// <summary>
    /// The bounds of what is actually on screen - a boolean's consumed history stays in Objects,
    /// hidden but not gone, so a stale gear rack from three operations ago does not drag Zoom to
    /// fit out to a size nothing visible occupies.
    /// </summary>
    public Bounds ComputeShownBounds()
    {
        var bounds = Bounds.Empty;
        foreach (var o in Shown)
            bounds = bounds.Union(o.WorldBounds);
        return bounds;
    }

    public void SelectOnly(SceneObject? target)
    {
        foreach (var o in Objects)
            o.IsSelected = ReferenceEquals(o, target);
    }

    public void ClearSelection()
    {
        foreach (var o in Objects)
            o.IsSelected = false;
    }

    /// <summary>
    /// Gives a new object a name that collides with nothing already in the scene.
    ///
    /// <paramref name="alsoTaken"/> is for an operation that makes several objects at once. This
    /// only knows what is on the plate, and Repeat asks for every name before any of the copies
    /// joins it - so a twelve-tread spiral stair came out as twelve treads all called "Tread 2".
    /// </summary>
    public string UniqueName(string baseName, IEnumerable<string>? alsoTaken = null)
    {
        var taken = new HashSet<string>(Objects.Select(o => o.Name), StringComparer.OrdinalIgnoreCase);
        if (alsoTaken is not null) taken.UnionWith(alsoTaken);

        if (!taken.Contains(baseName)) return baseName;

        for (int i = 2; ; i++)
        {
            string candidate = $"{baseName} {i}";
            if (!taken.Contains(candidate)) return candidate;
        }
    }
}
