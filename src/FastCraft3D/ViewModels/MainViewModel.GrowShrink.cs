using System.Numerics;
using System.Windows.Input;
using FastCraft3D.Geometry;
using FastCraft3D.Model;
using FastCraft3D.Model.Commands;
using FastCraft3D.View;

namespace FastCraft3D.ViewModels;

/// <summary>
/// Grow and Shrink: every face of the selection moved out or in by the same distance, along the
/// axes chosen. See <see cref="MeshOffset"/>.
/// </summary>
public partial class MainViewModel
{
    private ICommand? growShrink;

    /// <summary>One button: the panel was the same for both, with Grow and Shrink to choose in it.</summary>
    public ICommand GrowShrinkCommand => growShrink ??= Track(RelayCommand.Simple(GrowOrShrink, () => Scene.Selection.Any(o => !o.IsLocked)));

    private void GrowOrShrink()
    {
        var targets = Scene.Selection.Where(o => !o.IsLocked).ToList();
        if (targets.Count == 0) return;

        var dialog = new GrowShrinkDialog(targets.Count);
        if (dialog.ShowDialog() != true || dialog.Result is not { } choice) return;

        string label = choice.Distance > 0 ? "Grow" : "Shrink";
        long bytes = targets.Sum(o => (long)o.Mesh.Positions.Count * 12 + (long)o.Mesh.Indices.Count * 4);

        Run(bytes >= SlowFromBytes, choice.Distance > 0 ? "Growing..." : "Shrinking...", () =>
        {
            var removed = new List<SceneObject>();
            var added = new List<SceneObject>();
            var successors = new List<(SceneObject From, SceneObject To)>();
            var refused = new List<string>();

            foreach (var o in targets)
            {
                // On the plate, where the axes and the millimetres mean what they say, whatever
                // the part's own turn and scale.
                var result = MeshOffset.Offset(o.ToWorldMesh(), choice.Distance, choice.X, choice.Y, choice.Z);
                if (!result.Changed || !Matrix4x4.Invert(o.Transform, out var toLocal))
                {
                    refused.Add($"{o.Name}: {result.Refusal ?? "it is flattened to nothing"}");
                    continue;
                }

                // Back into its own frame, so it keeps its place, its turn and its scale.
                var made = new SceneObject(o.Name, MeshTransform.Transformed(result.Mesh, toLocal))
                {
                    Position = o.Position,
                    Rotation = o.Rotation,
                    Scale = o.Scale,
                    Colour = o.Colour,
                    Filament = o.Filament,
                    Plate = o.Plate
                };

                removed.Add(o);
                added.Add(made);
                successors.Add((o, made));
            }

            if (added.Count == 0)
            {
                Status = $"Left as it was - {refused[0]}";
                return;
            }

            Undo.Execute(new ReplaceObjectsCommand(label, removed, added, successors));
            RefreshSelection();

            string axes = string.Concat(choice.X ? "X" : "", choice.Y ? "Y" : "", choice.Z ? "Z" : "");
            Status = $"{label} {added.Count} object(s) by {unit.From(MathF.Abs(choice.Distance)):0.###} {unit.Label} along {axes}"
                   + (refused.Count > 0 ? $" - {refused.Count} left as they were: {string.Join("; ", refused)}" : "");
        });
    }
}
