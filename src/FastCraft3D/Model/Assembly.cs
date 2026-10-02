using System.ComponentModel;
using System.Numerics;
using System.Runtime.CompilerServices;
using FastCraft3D.Geometry;
using FastCraft3D.Model.Commands;

namespace FastCraft3D.Model;

/// <summary>
/// Objects kept together under one name in the list, with the place each of them was put in when
/// they were assembled.
///
/// Not a group: the parts stay separate objects, each selectable, movable and editable on its own.
/// What the assembly adds is a heading to select them all by, a colour to see them by while it is
/// selected, and a home for each part to go back to - Reassemble.
///
/// Membership is kept on the parts rather than in a list here. A part that is deleted takes its
/// membership with it, and undoing the delete brings both back - with nothing to tidy up in
/// between, and no list here still holding on to a part the history has long let go of. The
/// assemblies in a scene are simply the ones its objects name; see <see cref="Scene.Assemblies"/>.
/// </summary>
public sealed class Assembly(string name) : INotifyPropertyChanged
{
    private string name = name;
    private Vector3? colour;
    private bool isSelected;
    private bool isExpanded = true;
    private int memberCount;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name
    {
        get => name;
        set => Set(ref name, value);
    }

    /// <summary>What the parts are drawn in while the assembly is selected. Null leaves them as they are.</summary>
    public Vector3? Colour
    {
        get => colour;
        set
        {
            Set(ref colour, value);
            Raise(nameof(HasColour));
        }
    }

    public bool HasColour => colour is not null;

    /// <summary>
    /// Picked by its name in the list - not merely having every part selected, which Select all
    /// would do to every assembly at once. Let go of as soon as any part is.
    /// </summary>
    public bool IsSelected
    {
        get => isSelected;
        set => Set(ref isSelected, value);
    }

    /// <summary>Whether the list shows the parts under the heading.</summary>
    public bool IsExpanded
    {
        get => isExpanded;
        set => Set(ref isExpanded, value);
    }

    /// <summary>How many parts are on the plate, for the heading. Kept by whatever builds the list.</summary>
    public int MemberCount
    {
        get => memberCount;
        set => Set(ref memberCount, value);
    }

    /// <summary>
    /// What the heading's eye and lock show of the parts: whether any of them is hidden or locked,
    /// and whether all of them are. Kept by whatever keeps the selection - hiding and locking are
    /// the parts' own, and this only reads them back.
    /// </summary>
    public bool AnyHidden
    {
        get => anyHidden;
        set => Set(ref anyHidden, value);
    }

    public bool AllHidden
    {
        get => allHidden;
        set => Set(ref allHidden, value);
    }

    public bool AnyLocked
    {
        get => anyLocked;
        set => Set(ref anyLocked, value);
    }

    public bool AllLocked
    {
        get => allLocked;
        set => Set(ref allLocked, value);
    }

    private bool anyHidden, allHidden, anyLocked, allLocked;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? property = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        Raise(property);
    }

    private void Raise(string? property) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));

    /// <summary>For automation, which names a row by this.</summary>
    public override string ToString() => Name;
}

/// <summary>Which assembly a part is in, and where it goes back to.</summary>
public readonly record struct Membership(Assembly? Assembly, Vector3 HomePosition, Vector3 HomeRotation)
{
    public static Membership Of(SceneObject o) => new(o.Assembly, o.HomePosition, o.HomeRotation);

    /// <summary>In the assembly, at home where it stands now.</summary>
    public static Membership AtHome(Assembly assembly, SceneObject o) => new(assembly, o.Position, o.Rotation);

    public static Membership None => new(null, Vector3.Zero, Vector3.Zero);

    public void ApplyTo(SceneObject o)
    {
        o.Assembly = Assembly;
        o.HomePosition = HomePosition;
        o.HomeRotation = HomeRotation;
    }
}

/// <summary>
/// Joining, leaving and re-homing, as one step. Making an assembly, Update and Ungroup are all
/// this: the parts do not move, only what they belong to and where home is.
/// </summary>
public sealed class MembershipCommand(
    string label,
    IReadOnlyList<SceneObject> objects,
    IReadOnlyList<Membership> before,
    IReadOnlyList<Membership> after) : IUndoableCommand
{
    public string Label { get; } = label;

    public void Apply(Scene scene)
    {
        for (int i = 0; i < objects.Count; i++) after[i].ApplyTo(objects[i]);
        scene.NotifyAssembliesChanged();
    }

    public void Revert(Scene scene)
    {
        for (int i = 0; i < objects.Count; i++) before[i].ApplyTo(objects[i]);
        scene.NotifyAssembliesChanged();
    }
}

/// <summary>The colour an assembly shows its parts in. Its own step, as painting the parts is.</summary>
public sealed class AssemblyColourCommand(Assembly assembly, Vector3? before, Vector3? after) : IUndoableCommand
{
    public string Label => "Assembly color";

    public void Apply(Scene scene) => assembly.Colour = after;

    public void Revert(Scene scene) => assembly.Colour = before;
}

/// <summary>What the assembly buttons do, as steps for the undo stack.</summary>
public static class AssemblyTools
{
    /// <summary>
    /// The selection as a new assembly, each part at home where it stands. Parts already in
    /// another assembly move to this one; an assembly left with nothing in it is gone from the list.
    /// </summary>
    public static MembershipCommand Assemble(Scene scene, IReadOnlyList<SceneObject> objects, out Assembly made)
    {
        made = new Assembly(scene.UniqueAssemblyName("Assembly"));
        var assembly = made;

        return new MembershipCommand("Make assembly", objects,
            objects.Select(Membership.Of).ToList(),
            objects.Select(o => Membership.AtHome(assembly, o)).ToList());
    }

    /// <summary>
    /// More parts into an assembly that is already there, each at home where it stands, keeping
    /// the assembly's name and colour. A part from another assembly leaves that one.
    /// </summary>
    public static MembershipCommand Join(Assembly assembly, IReadOnlyList<SceneObject> objects) =>
        new($"Add to {assembly.Name}", objects,
            objects.Select(Membership.Of).ToList(),
            objects.Select(o => Membership.AtHome(assembly, o)).ToList());

    /// <summary>Home is where each part stands now.</summary>
    public static MembershipCommand Update(Scene scene, Assembly assembly)
    {
        var members = scene.MembersOf(assembly);
        return new MembershipCommand("Update assembly", members,
            members.Select(Membership.Of).ToList(),
            members.Select(o => Membership.AtHome(assembly, o)).ToList());
    }

    /// <summary>The assembly goes; its parts stay where they are, as objects on their own.</summary>
    public static MembershipCommand Ungroup(Scene scene, Assembly assembly) => Ungroup(scene, [assembly]);

    /// <summary>Several at once, as one step.</summary>
    public static MembershipCommand Ungroup(Scene scene, IReadOnlyList<Assembly> assemblies)
    {
        var members = assemblies.SelectMany(scene.MembersOf).ToList();
        return new MembershipCommand(assemblies.Count == 1 ? "Ungroup assembly" : "Ungroup assemblies", members,
            members.Select(Membership.Of).ToList(),
            members.Select(_ => Membership.None).ToList());
    }

    /// <summary>
    /// Every part back home: its position and turn, at its own pivot. Its size is its own business
    /// and is left alone, and a locked part stays where it is, since locked means nothing moves it.
    /// Null when everything is home already.
    ///
    /// Not called Reset: that read as putting back the shape and the colour as well.
    /// </summary>
    public static TransformCommand? Reassemble(Scene scene, Assembly assembly)
    {
        var members = scene.MembersOf(assembly).Where(o => !o.IsLocked).ToList();
        var before = members.Select(TransformState.Capture).ToList();

        foreach (var o in members)
        {
            o.Position = o.HomePosition;
            o.Rotation = o.HomeRotation;
        }

        return TransformCommand.CreateIfChanged($"Reassemble {assembly.Name}", members, before);
    }

    /// <summary>
    /// Where a part that takes another's place should go home to: wherever Reassemble would have carried
    /// the one it replaces.
    ///
    /// A tool that remakes a part - a cut, a smooth, a repair - builds the new one with its turn
    /// worked into the shape and its pivot at the middle of the new box. It stands exactly where
    /// the old part stood, but its position and angles read nothing like the old ones, and copying
    /// the old home across would turn it a second time on Reassemble. So the move Reassemble would
    /// have made is worked out on the old part and made again from the new one's own placement.
    ///
    /// The size cancels out of that move - it sits next to its own inverse - so only the position
    /// and the turn are needed, which is all a home is.
    /// </summary>
    public static Membership Succeed(SceneObject from, SceneObject to)
    {
        if (from.Assembly is not { } assembly) return Membership.Of(to);

        var now = MeshTransform.Compose(from.Position, from.Rotation, Vector3.One);
        var home = MeshTransform.Compose(from.HomePosition, from.HomeRotation, Vector3.One);
        if (!Matrix4x4.Invert(now, out var back)) return Membership.AtHome(assembly, to);

        var placed = MeshTransform.Compose(to.Position, to.Rotation, Vector3.One) * back * home;
        return new Membership(assembly, placed.Translation, MeshTransform.EulerFrom(placed));
    }

    /// <summary>
    /// Puts what a tool made into the assemblies of what it was made from, before it joins the plate.
    ///
    /// Given <paramref name="successors"/>, exactly those pairs. Otherwise worked out: a made part
    /// with the name of a consumed one is that part made again - the tools that remake each part
    /// of a selection on its own, and a generator's set made again with new settings, which keeps
    /// every part's name. Anything else joins the assembly every consumed member came from, if they
    /// all came from one: a split's halves, a part a set gained, a group of parts in one assembly.
    /// A part that was taken off and put back - a kept cutter - is the same object and keeps what
    /// it had.
    ///
    /// By name rather than by position in the two lists: a set made again lists its parts in its
    /// own order, which is not the order they happen to stand in on the plate.
    /// </summary>
    public static void Carry(
        IReadOnlyList<SceneObject> removed,
        IReadOnlyList<SceneObject> added,
        IReadOnlyList<(SceneObject From, SceneObject To)>? successors)
    {
        var kept = new HashSet<SceneObject>(removed);
        bool Fresh(SceneObject o) => !kept.Contains(o) && o.Assembly is null;

        if (successors is not null)
        {
            foreach (var (from, to) in successors)
                if (Fresh(to)) Succeed(from, to).ApplyTo(to);
            return;
        }

        // Names two consumed parts share say nothing about which is which.
        var named = removed.GroupBy(o => o.Name).Where(g => g.Count() == 1).ToDictionary(g => g.Key, g => g.Single());

        var members = removed.Where(o => o.Assembly is not null).ToList();
        var shared = members.Count > 0 && members.All(o => o.Assembly == members[0].Assembly) ? members[0] : null;

        foreach (var o in added)
        {
            if (!Fresh(o)) continue;

            if (named.TryGetValue(o.Name, out var from)) Succeed(from, o).ApplyTo(o);
            else if (shared is not null) Succeed(shared, o).ApplyTo(o);
        }
    }
}
