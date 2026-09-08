using System.Collections.ObjectModel;
using FastCraft3D.Geometry;

namespace FastCraft3D.Model;

public sealed class Scene
{
    /// <summary>Build plate edge length in millimetres, matching a common desktop printer bed.</summary>
    public const float PlateSize = 200f;

    public ObservableCollection<SceneObject> Objects { get; } = new();

    public IReadOnlyList<SceneObject> Selection =>
        Objects.Where(o => o.IsSelected).ToList();

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
