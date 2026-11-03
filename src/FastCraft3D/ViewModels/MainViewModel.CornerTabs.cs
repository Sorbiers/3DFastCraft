using System.Numerics;
using System.Windows.Input;
using FastCraft3D.Geometry;
using FastCraft3D.Model;
using FastCraft3D.Model.Commands;
using FastCraft3D.View;

namespace FastCraft3D.ViewModels;

/// <summary>Corner tabs: the corners of the selection that lift held down by tabs, to snip off after printing.</summary>
public sealed partial class MainViewModel
{
    private ICommand? cornerTabs;

    public ICommand CornerTabsCommand => cornerTabs ??= Track(RelayCommand.Simple(MakeCornerTabs, () => Scene.Shown.Count > 0));

    /// <summary>
    /// What a click in the view means while a tool's panel is open and wants one - a corner to hold
    /// for Corner tabs - given the point on the part or the plate under it. Null otherwise, when a
    /// panel leaves the view for looking round its preview.
    /// </summary>
    public Action<Vector3>? PanelClick { get; private set; }

    private void MakeCornerTabs()
    {
        if (IsBusy) return;

        var parts = (Scene.Selection.Count > 0 ? Scene.Selection : Scene.Shown).ToList();
        var tabs = parts.Select(p => new CornerTabs(p.ToWorldMesh())).ToList();
        var shown = new SceneObject?[parts.Count];

        void Clear()
        {
            for (int i = 0; i < shown.Length; i++)
            {
                if (shown[i] is { } o) Scene.Objects.Remove(o);
                shown[i] = null;
            }

            PreviewOnly = null;
        }

        var dialog = new CornerTabsDialog(parts.Select(p => p.Name).ToList(), tabs, layouts =>
        {
            if (layouts is null)
            {
                Clear();
                return;
            }

            for (int i = 0; i < parts.Count; i++)
            {
                if (layouts[i].Mesh is not { } mesh)
                {
                    if (shown[i] is { } gone) Scene.Objects.Remove(gone);
                    shown[i] = null;
                }
                else if (shown[i] is { } o)
                {
                    o.Mesh = mesh;
                }
                else
                {
                    shown[i] = new SceneObject($"{parts[i].Name} tabs", mesh) { Colour = parts[i].Colour };
                    Scene.Objects.Add(shown[i]!);
                }
            }

            PreviewOnly = [.. parts, .. shown.OfType<SceneObject>()];
        });

        bool accepted;
        PanelClick = dialog.Pick;
        try
        {
            accepted = dialog.ShowDialog() == true;
        }
        finally
        {
            PanelClick = null;
            Clear();
        }

        if (!accepted || dialog.Result is not { } made) return;

        var added = new List<SceneObject>();
        var steps = new List<IUndoableCommand>();
        var homes = new List<IUndoableCommand>();
        for (int i = 0; i < parts.Count; i++)
        {
            if (made[i].Mesh is not { } mesh) continue;

            var part = parts[i];
            var body = new SceneObject(Scene.UniqueName($"{part.Name} tabs"), mesh)
            {
                Colour = part.Colour,
                Filament = part.Filament,
                Plate = part.Plate
            }.Centred();
            added.Add(body);

            // Moved with its part, as a kit sprue is with its parts: in the part's assembly, or in one of the two.
            if (part.Assembly is { } home)
            {
                homes.Add(AssemblyTools.Join(home, [body]));
            }
            else
            {
                homes.Add(AssemblyTools.Assemble(Scene, [part, body], out var pair));
                pair.Name = Scene.UniqueAssemblyName($"{part.Name} with tabs");
            }
        }

        if (added.Count == 0) return;

        // On the part's own plate, whichever plate the list shows.
        steps.Add(new AddObjectsCommand("Corner tabs", added));
        steps.AddRange(homes);
        Undo.Execute(new CompoundCommand("Corner tabs", steps));
        RefreshSelection();

        int count = made.Sum(l => l.Tabs), held = made.Sum(l => l.Held);
        string on = parts.Count == 1 ? parts[0].Name : $"{added.Count} parts";
        Status = $"Put {count} tabs on {held} {(held == 1 ? "corner" : "corners")} of {on} - snip them off at the gates after printing";
    }
}
