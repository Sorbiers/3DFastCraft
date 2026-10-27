using System.Windows.Input;
using FastCraft3D.Geometry;
using FastCraft3D.Model;
using FastCraft3D.Model.Commands;
using FastCraft3D.View;

namespace FastCraft3D.ViewModels;

/// <summary>Kit sprue: the selection, or everything shown, bound into a model kit's frame of runners and gates.</summary>
public sealed partial class MainViewModel
{
    private ICommand? kitSprue;

    public ICommand KitSprueCommand => kitSprue ??= Track(RelayCommand.Simple(MakeKitSprue, () => Scene.Shown.Count > 0));

    private void MakeKitSprue()
    {
        if (IsBusy) return;

        // A locked part stays where it was put.
        var parts = (Scene.Selection.Count > 0 ? Scene.Selection : Scene.Shown).Where(o => !o.IsLocked).ToList();
        if (parts.Count == 0)
        {
            Status = "Every part is locked - unlock the ones to bind into a sprue";
            return;
        }

        var worlds = parts.Select(p => p.ToWorldMesh()).ToList();
        var sprue = new KitSprue(worlds);
        var shown = new List<SceneObject>();
        var from = new List<System.Numerics.Vector3>();
        SceneObject? frame = null;

        void Clear()
        {
            foreach (var o in shown) Scene.Objects.Remove(o);
            if (frame is not null) Scene.Objects.Remove(frame);
            shown.Clear();
            from.Clear();
            frame = null;
            PreviewOnly = null;
        }

        var dialog = new KitSprueDialog(parts.Count, sprue, layout =>
        {
            if (layout is null)
            {
                Clear();
                return;
            }

            if (frame is null)
            {
                for (int i = 0; i < parts.Count; i++)
                {
                    var o = new SceneObject(parts[i].Name, worlds[i]) { Colour = parts[i].Colour };
                    shown.Add(o);
                    from.Add(o.Position);
                    Scene.Objects.Add(o);
                }

                frame = new SceneObject("Kit sprue", layout.Mesh) { Colour = parts[0].Colour };
                Scene.Objects.Add(frame);
                PreviewOnly = [.. shown, frame];
            }
            else
            {
                frame.Mesh = layout.Mesh;
            }

            for (int i = 0; i < shown.Count; i++) shown[i].Position = from[i] + layout.Moves[i];
        });

        bool accepted;
        try
        {
            accepted = dialog.ShowDialog() == true;
        }
        finally
        {
            Clear();
        }

        if (!accepted || dialog.Result is not { } made) return;

        var before = parts.Select(TransformState.Capture).ToList();
        for (int i = 0; i < parts.Count; i++) parts[i].Position += made.Moves[i];

        var steps = new List<IUndoableCommand>();
        if (TransformCommand.CreateIfChanged("Lay out for the sprue", parts, before) is { } moved) steps.Add(moved);

        var lead = parts[0];
        var body = new SceneObject(Scene.UniqueName("Kit sprue"), made.Mesh)
        {
            Colour = lead.Colour,
            Filament = lead.Filament,
            Plate = lead.Plate
        }.Centred();
        steps.Add(Added("Kit sprue", [body]));

        // One kit, moved and selected as one - unless the parts belong to assemblies of their own,
        // which keep them, at home where they now stand.
        var homes = parts.Select(p => p.Assembly).Distinct().ToList();
        Assembly? kit = null;
        if (homes is [null])
        {
            steps.Add(AssemblyTools.Assemble(Scene, [.. parts, body], out kit));
            kit.Name = Scene.UniqueAssemblyName("Kit");
        }
        else if (homes is [{ } only])
        {
            steps.Add(AssemblyTools.Join(only, [.. parts, body]));
            kit = only;
        }
        else
        {
            foreach (var home in homes.OfType<Assembly>())
                steps.Add(AssemblyTools.Join(home, parts.Where(p => p.Assembly == home).ToList()));
        }

        Undo.Execute(new CompoundCommand("Kit sprue", steps));
        if (kit is not null) Scene.SelectAssembly(kit);
        RefreshSelection();

        string held = parts.Count == 1 ? "1 part" : $"{parts.Count} parts";
        Status = made.Loose.Count == 0
            ? $"Bound {held} into a kit sprue {made.Size.X:0} x {made.Size.Y:0} mm, held by {made.Gates} gates"
            : $"Bound {held} into a kit sprue - {made.Loose.Count} left loose, with nothing at the foot for a gate to reach";
    }
}
