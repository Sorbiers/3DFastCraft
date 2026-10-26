using System.IO;
using System.Windows;
using FastCraft3D.Io;
using FastCraft3D.View;

namespace FastCraft3D.ViewModels;

/// <summary>
/// The .3dfp kept beside a project's .3mf - see <see cref="ProjectFile"/> and docs/plan-project-mode.md.
/// </summary>
public partial class MainViewModel
{
    // Whether this project keeps a .3dfp: null until it is decided, which is at New, or at the
    // first save of a project started some other way.
    private bool? keepProjectFile;
    private string? projectFileId;

    // Where the versions come from at the next save, when that is not the .3mf itself: a project
    // brought back from its .3dfp, whose .3mf a slicer has written without them.
    private string? versionsSource;

    // What new projects do without asking, once the user has said: null asks each time.
    private bool? projectFileDefault;

    /// <summary>How the window's camera is now, for the .3dfp to keep. Set by the window.</summary>
    public Func<ViewState?>? CaptureView { get; set; }

    /// <summary>Asks the window to put the camera where a project was left.</summary>
    public event Action<ViewState>? ProjectViewRestoreRequested;

    /// <summary>Whether this project keeps a .3dfp. Set in File > Project; written at the next save.</summary>
    public bool KeepsProjectFile => keepProjectFile == true;

    /// <summary>What new projects do: null asks, true keeps a .3dfp, false does not.</summary>
    public bool? ProjectFileDefault
    {
        get => projectFileDefault;
        set
        {
            if (projectFileDefault == value) return;
            projectFileDefault = value;
            SettingsChanged?.Invoke();
        }
    }

    /// <summary>A project started from nothing: whether it keeps a .3dfp, asked now unless the answer was remembered.</summary>
    public void BeginNewProject()
    {
        projectFileId = null;
        versionsSource = null;
        keepProjectFile = projectFileDefault ?? AskProjectFile();
    }

    private bool AskProjectFile()
    {
        var dialog = new ProjectFileDialog { Owner = Application.Current?.MainWindow };
        dialog.ShowDialog();
        if (dialog.Remember) ProjectFileDefault = dialog.Keep;
        return dialog.Keep;
    }

    /// <summary>Turns the .3dfp on or off for this project, from File > Project. Takes effect at the next save.</summary>
    public void SetKeepsProjectFile(bool keep)
    {
        if (keepProjectFile == keep) return;

        keepProjectFile = keep;
        if (keep) projectFileId ??= ProjectFile.NewId();
        IsDirty = true;

        string name = projectPath is null ? "the project" : Path.GetFileName(projectPath);
        Status = keep
            ? $"Save {name} to start keeping {ProjectFileName()} beside it"
            : $"Save {name} to stop keeping a project file - {ProjectFileName()} can then be deleted";
    }

    private string ProjectFileName() =>
        projectPath is null ? "a .3dfp" : Path.GetFileName(ProjectFile.Beside(projectPath));

    /// <summary>
    /// What a save writes into the .3mf for the .3dfp, decided before it is written: whether one is
    /// kept, asking if that was never decided, and a new id for a project saved under a new name -
    /// two copies sharing one .3dfp would each write over the other's.
    /// </summary>
    private string? ProjectFileIdForSave(string target)
    {
        keepProjectFile ??= projectFileDefault ?? AskProjectFile();
        if (keepProjectFile != true) return null;

        bool newName = projectPath is null
                       || !string.Equals(Path.GetFullPath(target), Path.GetFullPath(projectPath), StringComparison.OrdinalIgnoreCase);
        if (newName || projectFileId is null) projectFileId = ProjectFile.NewId();
        return projectFileId;
    }

    /// <summary>Brings the .3dfp up to date with the .3mf just written. A failure here is said, not thrown: the .3mf is saved.</summary>
    private void UpdateProjectFile(string modelPath)
    {
        if (keepProjectFile != true || projectFileId is null) return;

        try
        {
            ProjectFile.Write(ProjectFile.Beside(modelPath), projectFileId, modelPath, CaptureView?.Invoke(), Version);
        }
        catch (Exception ex)
        {
            Status = $"Saved {Path.GetFileName(modelPath)}, but {Path.GetFileName(ProjectFile.Beside(modelPath))} could not be written: {ex.Message}";
        }
    }

    /// <summary>
    /// Opening a project's .3mf: whether it has its .3dfp, and what to do when it has not, or has
    /// one it does not know about. Returns whether the view was put back from it.
    /// </summary>
    private bool CheckProjectFile(string modelPath)
    {
        string? id = SceneSerializer.ReadProjectFileId(modelPath);
        projectFileId = id;
        keepProjectFile = id is not null;
        versionsSource = null;

        string? found = ProjectFile.Find(modelPath, id);
        string name = Path.GetFileName(modelPath);

        if (id is not null && found is null)
        {
            var make = Prompt.Show(
                $"{name} is kept with a project file, {Path.GetFileName(ProjectFile.Beside(modelPath))}, and it is not beside it.\n\n"
                + "Make it again now? It holds a copy of the project and the view, and nothing that is not also in the .3mf.",
                "Project file missing", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (make == MessageBoxResult.Yes) UpdateProjectFile(modelPath);
            else keepProjectFile = false;
            return false;
        }

        if (id is null && found is not null && ProjectFile.ReadInfo(found) is { } info)
        {
            var link = Prompt.Show(
                $"There is a project file beside {name}: {Path.GetFileName(found)}, last saved {info.SavedUtc.ToLocalTime():d MMMM 'at' HH:mm}.\n\n"
                + $"Keep it up to date with this project from now on? {name} is saved again to say so.",
                "Project file found", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (link != MessageBoxResult.Yes) return false;

            keepProjectFile = true;
            projectFileId = info.Id;
            WriteProject(modelPath, info.Id);
        }

        if (found is not null && ProjectFile.ReadView(found) is { } view)
        {
            ProjectViewRestoreRequested?.Invoke(view);
            return true;
        }

        return false;
    }

    /// <summary>
    /// A .3mf that is not a project, with a project file beside it that names it: a slicer saved over
    /// the project. Offers to bring the project back; returns whether it was.
    /// </summary>
    private bool OfferRestore(string modelPath)
    {
        string beside = ProjectFile.Beside(modelPath);
        if (!File.Exists(beside) || ProjectFile.ReadInfo(beside) is not { } info) return false;

        string name = Path.GetFileName(modelPath);
        var answer = Prompt.Show(
            $"{name} no longer holds its 3DFastCraft project - another app, most likely a slicer, saved over it and left only the model.\n\n"
            + $"{Path.GetFileName(beside)} still has the project as it was saved on {info.SavedUtc.ToLocalTime():d MMMM 'at' HH:mm}, with its versions.\n\n"
            + "Bring the project back? It opens unsaved: save it to write it back into the .3mf, which replaces what the other app saved there - or Save as, to keep both.",
            "Bring the project back?", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return false;

        OpenFromProjectFile(beside, info, modelPath);
        return true;
    }

    /// <summary>
    /// A .3dfp opened on its own: its .3mf if that is still there, and the project rebuilt from the
    /// .3dfp if it is not.
    /// </summary>
    private void OpenProjectFile(string path)
    {
        if (ProjectFile.ReadInfo(path) is not { } info)
            throw new InvalidDataException("This is not a 3DFastCraft project file, or it was written by a newer version.");

        string model = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".",
            info.ModelName.Length > 0 ? info.ModelName : Path.GetFileNameWithoutExtension(path) + SceneSerializer.Extension);

        if (File.Exists(model))
        {
            LoadProjectNow(model);
            return;
        }

        OpenFromProjectFile(path, info, model);
        Status = $"{Path.GetFileName(model)} is gone - the project is back from {Path.GetFileName(path)}; Save writes the .3mf again";
    }

    /// <summary>The project as kept in a .3dfp, opened as the project of <paramref name="modelPath"/>, unsaved.</summary>
    private void OpenFromProjectFile(string path, ProjectFileInfo info, string modelPath)
    {
        var loaded = SceneSerializer.Load(path, out var settings);
        if (settings is { } kept) ApplySettings(kept);
        else
        {
            ModelScale = 1f;
            projectNotes = "";
        }

        Scene.Objects.Clear();
        foreach (var o in loaded) Scene.Objects.Add(o);
        Undo.Clear();
        projectPath = modelPath;
        openedFrom = null;
        keepProjectFile = true;
        projectFileId = info.Id;
        versionsSource = path;
        IsDirty = true;
        RefreshSelection();
        ZoomExtentsRequested?.Invoke();
        if (ProjectFile.ReadView(path) is { } view) ProjectViewRestoreRequested?.Invoke(view);
        Status = $"The project is back from {Path.GetFileName(path)} - save to write it into {Path.GetFileName(modelPath)}";
    }
}
