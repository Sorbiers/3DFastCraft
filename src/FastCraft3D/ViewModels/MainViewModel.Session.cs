using System.IO;
using System.Windows;
using System.Windows.Input;
using FastCraft3D.Io;
using FastCraft3D.Model.Commands;
using FastCraft3D.View;

namespace FastCraft3D.ViewModels;

/// <summary>
/// The session kept in a project's .3dfp - the plate after every step, and the work not yet saved.
/// See <see cref="ProjectSession"/> and docs/plan-project-mode.md.
/// </summary>
public partial class MainViewModel
{
    private readonly ProjectSession session = new();

    // Export session walks the history back and forth to reach each step; none of that is a step.
    private bool sessionPaused;

    private ICommand? showSession;

    public ICommand SessionCommand => showSession ??= Track(RelayCommand.Simple(ShowSession, () => NothingInHand && KeepsProjectFile && projectPath is not null));

    /// <summary>
    /// The .3dfp the session goes into, or null for a project that keeps none - or keeps one but
    /// has not been saved yet, when the steps are held until it is.
    /// </summary>
    private string? SessionFile() =>
        keepProjectFile == true && projectFileId is not null && projectPath is not null ? ProjectFile.Beside(projectPath) : null;

    private void RecordStep(string label)
    {
        if (sessionPaused || keepProjectFile == false) return;
        session.Record(Scene, label);
    }

    /// <summary>The plate as a project opened, as a step of its own - unless it is the step the session ended on.</summary>
    private void RecordOpened(string label)
    {
        if (SessionFile() is { } file) session.Record(Scene, label, file);
    }

    /// <summary>
    /// The project is being put down - for another, a new one, or the app closing. What its session
    /// holds unwritten goes into its .3dfp, and the work there is nothing to recover any more: it was
    /// saved, or the user said not to.
    /// </summary>
    public void LeaveSession()
    {
        if (SessionFile() is { } file && projectFileId is { } id) session.Flush(file, id, null, null, CameraNow());
        session.Drop();
        session.ForgetLeftover();
    }

    /// <summary>
    /// Writes the session and the unsaved work into the .3dfp, when the project has one. Returns
    /// false when it has not, and the crash file has to keep the work instead.
    /// </summary>
    private bool KeepInProjectFile()
    {
        if (SessionFile() is not { } file || projectFileId is not { } id) return false;
        if (!session.Flush(file, id, IsDirty ? Scene : null, ViewSettings, CameraNow())) return false;

        if (IsDirty) Recovery.KeepIn(file, projectPath, Scene.Objects.Count);
        return true;
    }

    /// <summary>The camera now, or null - on the way out, the window may have let go of it already.</summary>
    private ViewState? CameraNow()
    {
        try { return CaptureView?.Invoke(); }
        catch { return null; }
    }

    /// <summary>
    /// A project whose .3dfp holds work a run left unsaved: one that stopped without closing, and was
    /// not offered it back at the next start. Offers it back; the steps that led to it stay in the
    /// session whichever way it is answered.
    /// </summary>
    private bool OfferLeftover(string file, string modelPath)
    {
        if (ProjectSession.ReadLeftover(file) is not { } left) return false;

        // Another window has this project open with changes: its work, live, not left over.
        if (Recovery.IsRunning(left.ProcessId, left.StartedUtcTicks)) return false;

        string name = Path.GetFileName(modelPath);
        bool taken = false;

        // Saved since, by something that did not know to take it out: there is nothing newer in it.
        if (left.Utc > File.GetLastWriteTimeUtc(modelPath))
        {
            var answer = Prompt.Show(
                $"3DFastCraft stopped on {left.Utc.ToLocalTime():d MMMM 'at' HH:mm} without closing, while {name} had changes not saved. "
                + $"Its project file kept them: {left.Objects} object(s).\n\nOpen them instead of what was last saved?",
                "Unsaved work", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (answer == MessageBoxResult.Yes)
            {
                try
                {
                    var loaded = ProjectSession.LoadLeftover(file, out var settings);
                    if (settings is { } kept) ApplySettings(kept);

                    Scene.Objects.Clear();
                    foreach (var o in loaded) Scene.Objects.Add(o);
                    Undo.Clear();
                    IsDirty = true;
                    RefreshSelection();
                    Status = $"Back to the changes left unsaved in {name} - save to keep them";
                    taken = true;
                }
                catch (Exception ex)
                {
                    Prompt.Show(ex.Message, "Could not open the unsaved changes", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        // Taken, it is this run's unsaved work now, and written again as that.
        ProjectSession.ClearLeftover(file);
        return taken;
    }

    private void ShowSession()
    {
        string? file = SessionFile();
        if (file is null || !File.Exists(file))
        {
            Status = "The history is kept in the project file - save the project to start it";
            return;
        }

        // Written first, so the list ends with the step just taken.
        KeepRecovery(now: true);

        // One object selected: its own history is offered, and shown first.
        (Guid Id, string Name)? subject = Scene.Selection is [var one] ? (one.Id, one.Name) : null;

        var dialog = new SessionDialog(file, Path.GetFileName(projectPath!), subject) { Owner = Application.Current?.MainWindow };
        if (dialog.ShowDialog() != true) return;

        if (dialog.RestoreFrom is { } from && subject is { } s)
            RestoreObject(file, from, s.Id, s.Name);
        else if (dialog.GoBackTo is { } number)
            Run(IsBig(file), "Going back...", () => GoBackToStep(file, number));
    }

    /// <summary>
    /// One object as it was after a step, in place of what it is now, or back on the plate if it
    /// has gone since; nothing else changes. Through undo, as going back is.
    /// </summary>
    private void RestoreObject(string file, int number, Guid id, string name)
    {
        try
        {
            if (ProjectSession.LoadObject(file, number, id) is not { } restored)
            {
                Status = $"{name} was not on the plate after step {number}";
                return;
            }

            if (Scene.Objects.FirstOrDefault(o => o.Id == id) is { } now)
                Undo.Execute(new ReplaceObjectsCommand($"Restore {name} from step {number}", [now], [restored], [(now, restored)]));
            else
                Undo.Execute(Added($"Bring back {name} from step {number}", [restored]));

            RefreshSelection();
            Status = $"{restored.Name} is back as it was after step {number} - Ctrl+Z to undo";
        }
        catch (Exception ex)
        {
            Prompt.Show(ex.Message, "Could not restore the object", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void GoBackToStep(string file, int number)
    {
        try
        {
            var restored = ProjectSession.LoadStep(file, number);

            // Through undo, as restoring a version is, so going back is as reversible as anything else.
            Undo.Execute(new ReplaceObjectsCommand($"Go back to step {number}", Scene.Objects.ToList(), restored, carryAssemblies: false));
            RefreshSelection();
            Status = $"Back to step {number} - Ctrl+Z to undo";
        }
        catch (Exception ex)
        {
            StopWaiting(now: true);
            Prompt.Show(ex.Message, "Could not go back to that step", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
