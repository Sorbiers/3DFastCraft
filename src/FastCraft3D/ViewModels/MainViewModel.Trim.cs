using System.Windows.Input;
using FastCraft3D.Geometry;
using FastCraft3D.Model;
using FastCraft3D.Model.Commands;
using FastCraft3D.View;

namespace FastCraft3D.ViewModels;

/// <summary>
/// Trim: a part cut back round its outer edge by a distance, along the axes chosen, everything
/// inside that edge left as it was. See <see cref="EdgeTrim"/>.
/// </summary>
public partial class MainViewModel
{
    private ICommand? trimEdges;

    public ICommand TrimCommand => trimEdges ??= Track(RelayCommand.Simple(TrimSelection, () => Scene.Selection.Any(o => !o.IsLocked)));

    private void TrimSelection()
    {
        var targets = Scene.Selection.Where(o => !o.IsLocked).ToList();
        if (targets.Count == 0) return;

        string subject = targets.Count == 1 ? $"Trim {targets[0].Name} round its outside." : $"Trim {targets.Count} parts round their outside.";
        var dialog = new TrimDialog(subject);
        if (dialog.ShowDialog() != true || dialog.Result is not { } choice) return;

        string axes = string.Concat(choice.X ? "X" : "", choice.Y ? "Y" : "", choice.Z ? "Z" : "");
        long bytes = targets.Sum(o => (long)o.Mesh.Positions.Count * 12 + (long)o.Mesh.Indices.Count * 4);

        Run(bytes >= SlowFromBytes / 4, "Trimming...", () =>
        {
            var removed = new List<SceneObject>();
            var added = new List<SceneObject>();
            var successors = new List<(SceneObject From, SceneObject To)>();
            var refused = new List<string>();

            foreach (var o in targets)
            {
                var result = EdgeTrim.Trim(o.ToWorldMesh(), choice.Distance, choice.X, choice.Y, choice.Z);
                if (result.Mesh is not { } mesh)
                {
                    refused.Add($"{o.Name}: {result.Refusal}");
                    continue;
                }

                var made = new SceneObject(o.Name, mesh) { Colour = o.Colour, Filament = o.Filament }.Centred();
                removed.Add(o);
                added.Add(made);
                successors.Add((o, made));
            }

            if (added.Count == 0)
            {
                Status = $"Left as it was - {refused[0]}";
                return;
            }

            Undo.Execute(new ReplaceObjectsCommand("Trim", removed, added, successors));
            RefreshSelection();

            Status = $"Trimmed {added.Count} part(s) by {unit.From(choice.Distance):0.###} {unit.Label} along {axes}"
                   + (refused.Count > 0 ? $" - {refused.Count} left as they were: {string.Join("; ", refused)}" : "");
        });
    }
}
