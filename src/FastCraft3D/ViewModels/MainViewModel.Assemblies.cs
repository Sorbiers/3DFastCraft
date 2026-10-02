using System.Collections.ObjectModel;
using System.Numerics;
using System.Windows;
using System.Windows.Input;
using FastCraft3D.Model;
using FastCraft3D.Model.Commands;
using FastCraft3D.View;

namespace FastCraft3D.ViewModels;

/// <summary>
/// Assemblies: parts kept together under a heading in the objects list. See <see cref="Assembly"/>.
/// </summary>
public sealed partial class MainViewModel
{
    private ICommand? assemble, reassemble, updateAssembly, ungroupAssembly;
    private ICommand? setAssemblyColour, pickAssemblyColour, foldAssembly, foldAll, hideAssembly, lockAssembly;

    /// <summary>
    /// What the objects list shows: loose objects, and each assembly as a heading with its parts
    /// under it, all in the order the parts are on the plate. Built here rather than bound to the
    /// scene's objects because a heading is not an object, and a folded one hides its parts.
    /// </summary>
    public ObservableCollection<object> ListRows { get; } = new();

    /// <summary>Around a rebuild of <see cref="ListRows"/>, so the list does not hear a row going as a click.</summary>
    public event Action? RowsRebuilding;

    public event Action? RowsRebuilt;

    /// <summary>
    /// From one object up. An assembly of one is still a name in the list and a place to go back
    /// to, and the start of one that more parts will join: with an assembly picked by its name and
    /// more selected beside it, the rest are added to it.
    /// </summary>
    public ICommand AssembleCommand => assemble ??= RelayCommand.Simple(Assemble, () => Scene.Selection.Count > 0);

    public ICommand ReassembleCommand => reassemble ??= RelayCommand.Simple(Reassemble, () => SelectedAssembly is not null);

    public ICommand UpdateAssemblyCommand => updateAssembly ??= RelayCommand.Simple(UpdateAssembly, () => SelectedAssembly is not null);

    public ICommand UngroupAssemblyCommand => ungroupAssembly ??= RelayCommand.Simple(UngroupAssemblies, () => Scene.Assemblies.Any(a => a.IsSelected));

    /// <summary>A swatch's colour, or nothing for none.</summary>
    public ICommand SetAssemblyColourCommand => setAssemblyColour ??= new RelayCommand(SetAssemblyColour, _ => SelectedAssembly is not null);

    public ICommand PickAssemblyColourCommand => pickAssemblyColour ??= RelayCommand.Simple(PickAssemblyColour, () => SelectedAssembly is not null);

    /// <summary>The arrow on a heading. Takes the assembly, since it works on any heading, selected or not.</summary>
    public ICommand FoldAssemblyCommand => foldAssembly ??= new RelayCommand(p =>
    {
        if (p is not Assembly a) return;
        a.IsExpanded = !a.IsExpanded;
        RebuildListRows();
    });

    /// <summary>"Open" or "Close": every assembly at once, from the buttons over the list.</summary>
    public ICommand FoldAllAssembliesCommand => foldAll ??= new RelayCommand(p =>
    {
        bool open = Equals(p, "Open");
        foreach (var a in Scene.Assemblies) a.IsExpanded = open;
        RebuildListRows();
    });

    /// <summary>
    /// The eye on a heading: hides every part, or shows them all again once every one is hidden.
    /// One hidden among the rest is hidden along with them rather than shown, so a click always
    /// leaves the whole assembly one way.
    /// </summary>
    public ICommand ToggleAssemblyHiddenCommand => hideAssembly ??= new RelayCommand(p =>
    {
        if (p is not Assembly a) return;
        var members = Scene.MembersOf(a);
        SetHidden(members, !members.All(o => o.IsHidden));
    });

    /// <summary>The lock on a heading, the same way.</summary>
    public ICommand ToggleAssemblyLockedCommand => lockAssembly ??= new RelayCommand(p =>
    {
        if (p is not Assembly a) return;
        var members = Scene.MembersOf(a);
        SetLocked(members, !members.All(o => o.IsLocked));
    });

    /// <summary>Whether there is an assembly to open or fold.</summary>
    public bool HasAssemblies => Scene.Objects.Any(o => o.Assembly is not null);

    /// <summary>The one assembly picked by its name, for the panel. Null for none, or for more than one.</summary>
    public Assembly? SelectedAssembly
    {
        get
        {
            var picked = Scene.Assemblies.Where(a => a.IsSelected).Take(2).ToList();
            return picked.Count == 1 ? picked[0] : null;
        }
    }

    /// <summary>
    /// The selected assembly's name. Through here rather than bound to it, so a rename counts as a
    /// change to the project and an empty box leaves the name as it was.
    /// </summary>
    public string AssemblyName
    {
        get => SelectedAssembly?.Name ?? "";
        set
        {
            if (SelectedAssembly is not { } a || string.IsNullOrWhiteSpace(value) || a.Name == value.Trim()) return;

            a.Name = value.Trim();
            IsDirty = true;
            Raise(nameof(AssemblyName));
        }
    }

    private void WireAssemblies()
    {
        // Ahead of everything else that hears the objects change, so the rows are there by the
        // time the selection is pushed into them.
        Scene.Objects.CollectionChanged += (_, _) => RebuildListRows();
        Scene.AssembliesChanged += () =>
        {
            RebuildListRows();
            RefreshSelection();
        };
    }

    /// <summary>
    /// Brings <see cref="ListRows"/> into line with the scene by removing, inserting and moving
    /// rows rather than starting again. Clearing it and filling it back up threw the list back to
    /// the top and lost its selection every time anything was added. Rows that are gone are taken
    /// out first, so a part deleted from the top of a long list is one removal and not every row
    /// below it moved up one at a time.
    /// </summary>
    private void RebuildListRows()
    {
        var wanted = new List<object>();
        var listed = new HashSet<Assembly>();
        var objects = Scene.Objects;

        foreach (var o in objects)
        {
            if (o.Assembly is not { } a)
            {
                wanted.Add(o);
                continue;
            }

            if (!listed.Add(a)) continue;

            var members = objects.Where(m => m.Assembly == a).ToList();
            a.MemberCount = members.Count;
            wanted.Add(a);
            if (a.IsExpanded) wanted.AddRange(members);
        }

        Raise(nameof(HasAssemblies));
        if (ListRows.SequenceEqual(wanted)) return;

        RowsRebuilding?.Invoke();
        try
        {
            var keep = new HashSet<object>(wanted);
            for (int i = ListRows.Count - 1; i >= 0; i--)
                if (!keep.Contains(ListRows[i])) ListRows.RemoveAt(i);

            for (int i = 0; i < wanted.Count; i++)
            {
                if (i < ListRows.Count && ReferenceEquals(ListRows[i], wanted[i])) continue;

                int at = -1;
                for (int j = i + 1; j < ListRows.Count; j++)
                {
                    if (!ReferenceEquals(ListRows[j], wanted[i])) continue;
                    at = j;
                    break;
                }

                if (at >= 0) ListRows.Move(at, i);
                else ListRows.Insert(i, wanted[i]);
            }

            while (ListRows.Count > wanted.Count) ListRows.RemoveAt(ListRows.Count - 1);
        }
        finally
        {
            RowsRebuilt?.Invoke();
        }
    }

    /// <summary>
    /// Part of every selection refresh: an assembly stays selected only while all of its parts
    /// are, and the parts of a selected one are drawn in its colour.
    /// </summary>
    private void RefreshAssemblies()
    {
        foreach (var a in Scene.Assemblies)
        {
            if (a.IsSelected && !Scene.IsWhollySelected(a)) a.IsSelected = false;

            var members = Scene.MembersOf(a);
            a.AnyHidden = members.Any(o => o.IsHidden);
            a.AllHidden = members.All(o => o.IsHidden);
            a.AnyLocked = members.Any(o => o.IsLocked);
            a.AllLocked = members.All(o => o.IsLocked);
        }

        foreach (var o in Scene.Objects)
        {
            Vector3? tint = o.Assembly is { IsSelected: true, Colour: { } colour } ? colour : null;
            if (o.Tint != tint) o.Tint = tint;
            o.InPickedAssembly = o.IsSelected && o.Assembly is { IsSelected: true };
        }

        Raise(nameof(SelectedAssembly));
        Raise(nameof(AssemblyName));
        Raise(nameof(AssemblyRecipe));
    }

    /// <summary>How the selected assembly's parts were made, when a generator made them: its panel offers Edit settings.</summary>
    public FastCraft3D.Geometry.Recipe? AssemblyRecipe =>
        SelectedAssembly is { } a ? Scene.MembersOf(a).Select(o => o.Recipe).FirstOrDefault(r => r is not null) : null;

    private void Assemble()
    {
        var selection = Scene.Selection.ToList();
        if (selection.Count == 0) return;

        // An assembly picked by its name, with more selected beside it: the rest join it, rather
        // than all of it going into a new one and the old one's name and colour being lost.
        if (SelectedAssembly is { } target)
        {
            var joining = selection.Where(o => o.Assembly != target).ToList();
            if (joining.Count == 0)
            {
                Status = $"Everything selected is in {target.Name} already - select more objects to add them to it";
                return;
            }

            Undo.Execute(AssemblyTools.Join(target, joining));
            Scene.SelectAssembly(target);
            RefreshSelection();
            Status = joining.Count == 1
                ? $"Added {joining[0].Name} to {target.Name}"
                : $"Added {joining.Count} objects to {target.Name}";
            return;
        }

        Undo.Execute(AssemblyTools.Assemble(Scene, selection, out var made));
        Scene.SelectAssembly(made);
        RefreshSelection();
        Status = selection.Count == 1
            ? $"Made {made.Name} from {selection[0].Name} - click its name in the list to select it"
            : $"Made {made.Name} from {selection.Count} objects - click its name in the list to select them all";
    }

    private void Reassemble()
    {
        if (SelectedAssembly is not { } a) return;

        if (AssemblyTools.Reassemble(Scene, a) is { } command)
        {
            Undo.Execute(command);
            Status = $"Put the parts of {a.Name} back where they were assembled";
        }
        else
        {
            Status = $"{a.Name} is already assembled";
        }

        RefreshSelection();
    }

    private void UpdateAssembly()
    {
        if (SelectedAssembly is not { } a) return;

        Undo.Execute(AssemblyTools.Update(Scene, a));
        RefreshSelection();
        Status = $"{a.Name} now reassembles to where its parts are now";
    }

    /// <summary>
    /// Every assembly picked by its name taken apart, as one step - from the panel's button, and
    /// from Ungroup, which does this rather than split meshes while an assembly is picked.
    /// </summary>
    private void UngroupAssemblies()
    {
        var picked = Scene.Assemblies.Where(a => a.IsSelected).ToList();
        if (picked.Count == 0) return;

        int count = picked.Sum(a => Scene.MembersOf(a).Count);
        foreach (var a in picked) a.IsSelected = false;
        Undo.Execute(AssemblyTools.Ungroup(Scene, picked));
        RefreshSelection();
        Status = picked.Count == 1
            ? $"Ungrouped {picked[0].Name} - its {count} objects are on their own again, where they were"
            : $"Ungrouped {picked.Count} assemblies - their {count} objects are on their own again, where they were";
    }

    private void SetAssemblyColour(object? parameter)
    {
        if (SelectedAssembly is not { } a) return;

        Vector3? colour = TryReadColour(parameter, out var picked) ? picked : null;
        if (a.Colour == colour) return;

        Undo.Execute(new AssemblyColourCommand(a, a.Colour, colour));
        RefreshSelection();
        Status = colour is { } c
            ? $"{a.Name} shows its parts {Palette.ToHex(c)} while selected"
            : $"{a.Name} leaves its parts their own colors";
    }

    private void PickAssemblyColour()
    {
        if (SelectedAssembly is not { } a) return;

        var dialog = new ColourDialog(a.Colour ?? Scene.MembersOf(a).FirstOrDefault()?.Colour ?? Vector3.One)
        {
            Owner = Application.Current?.MainWindow
        };
        if (dialog.ShowDialog() != true || dialog.Result is not { } picked) return;

        SetAssemblyColour(picked);
    }
}
