using System.Numerics;
using System.Windows.Input;
using FastCraft3D.Model;
using FastCraft3D.Model.Commands;
using FastCraft3D.View;

namespace FastCraft3D.ViewModels;

/// <summary>Fill up: a part filled as liquid poured into it would fill it, to a level.</summary>
public sealed partial class MainViewModel
{
    private ICommand? fillUp;

    public ICommand FillUpCommand => fillUp ??= Track(AsyncRelayCommand.Simple(FillUp, () => Scene.Selection.Count == 1));

    /// <summary>The fill's own colour on the plate while the level is chosen: it reads as poured in.</summary>
    private static readonly Vector3 FillColour = new(0.36f, 0.66f, 0.92f);

    private async Task FillUp()
    {
        if (IsBusy || Scene.Selection.Count != 1) return;

        var target = Scene.Selection[0];

        // In world space, as the other grid-based tools take it: the level is a height on the plate.
        var world = target.ToWorldMesh();
        SceneObject? shown = null;

        var dialog = new FillDialog(target.Name, world, mesh =>
        {
            if (mesh is null)
            {
                if (shown is not null) Scene.Objects.Remove(shown);
                shown = null;
                PreviewOnly = null;
                return;
            }

            if (shown is null)
            {
                shown = new SceneObject("Fill", mesh) { Colour = FillColour };
                Scene.Objects.Add(shown);
                PreviewOnly = [target, shown];
            }
            else
            {
                shown.Mesh = mesh;
            }
        });

        bool accepted;
        try
        {
            accepted = dialog.ShowDialog() == true;
        }
        finally
        {
            if (shown is not null) Scene.Objects.Remove(shown);
            PreviewOnly = null;
        }

        if (!accepted || dialog.Result is not { } chosen) return;

        var token = StartWork("Filling");
        try
        {
            var made = await Task.Run(() => chosen.Analysis.Apply(world, chosen.Level, chosen.Apart, token));
            if (made is not { } result)
            {
                Status = $"The fill would not come out closed against {target.Name} - nothing was changed";
                return;
            }

            if (result.Fill is { } fill)
            {
                var o = new SceneObject(Scene.UniqueName($"{target.Name} fill"), fill) { Colour = NextAutomaticColour() }.Centred();
                Undo.Execute(new AddObjectsCommand("Fill up", [o]));
                Status = $"Filled {target.Name} to {chosen.Level:0.##} mm as a part of its own - {fill.ComputeSignedVolume() / 1000:0.#} cm³";
            }
            else
            {
                var filled = new SceneObject(target.Name, result.Part) { Colour = target.Colour, Filament = target.Filament }.Centred();
                double added = (result.Part.ComputeSignedVolume() - world.ComputeSignedVolume()) / 1000;
                Undo.Execute(new ReplaceObjectsCommand("Fill up", [target], [filled]));
                Status = $"Filled {target.Name} to {chosen.Level:0.##} mm - {added:0.#} cm³ added";
            }

            RefreshSelection();
        }
        catch (Exception abort) when (WasAborted(abort))
        {
            Status = $"{busyTitle} aborted - nothing was changed";
        }
        catch (Exception ex)
        {
            Status = $"Fill up failed: {ex.Message}";
        }
        finally
        {
            EndWork();
        }
    }
}
