using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Input;
using FastCraft3D.Geometry;
using FastCraft3D.Io;
using FastCraft3D.View;

namespace FastCraft3D.ViewModels;

/// <summary>
/// The Project button: notes kept in the project file, and a summary of what the project holds.
/// </summary>
public partial class MainViewModel
{
    private ICommand? showProject;
    private string projectNotes = "";

    public ICommand ProjectCommand => showProject ??= Track(RelayCommand.Simple(ShowProject, () => NothingInHand));

    /// <summary>Free text kept in the project file. Changing it is a change to the project, to be saved.</summary>
    public string ProjectNotes
    {
        get => projectNotes;
        set
        {
            value ??= "";
            if (projectNotes == value) return;

            projectNotes = value;
            Raise(nameof(ProjectNotes));
            IsDirty = true;
        }
    }

    private void ShowProject()
    {
        string name = projectPath is not null ? Path.GetFileNameWithoutExtension(projectPath) : "Untitled project";
        string projectFile = projectPath is not null ? Path.GetFileName(ProjectFile.Beside(projectPath)) : name + ProjectFile.Extension;
        var dialog = new ProjectDialog(name, ProjectSummary(), projectNotes, projectFile, KeepsProjectFile, ProjectFileDefault)
        {
            Owner = Application.Current?.MainWindow
        };
        if (dialog.ShowDialog() != true) return;

        ProjectFileDefault = dialog.NewProjects;
        if (dialog.KeepsProjectFile != KeepsProjectFile) SetKeepsProjectFile(dialog.KeepsProjectFile);

        string notes = dialog.Notes.TrimEnd();
        if (notes == projectNotes) return;

        ProjectNotes = notes;
        Status = projectPath is not null
            ? "Notes changed - save the project to keep them"
            : "Notes changed - they are kept when the project is saved";
    }

    /// <summary>What the project holds, line by line, for the Project page.</summary>
    private string ProjectSummary()
    {
        var text = new StringBuilder();
        void Line(string label, string value) => text.Append(label).Append(": ").AppendLine(value);

        if (projectPath is not null)
        {
            Line("File", projectPath);
            if (File.Exists(projectPath))
                Line("Saved", File.GetLastWriteTime(projectPath).ToString("d MMM yyyy HH:mm"));
        }
        else
        {
            Line("File", "not saved yet");
        }

        if (IsDirty) Line("Changes", "not saved");

        var objects = Scene.Objects;
        int hidden = objects.Count(o => o.IsHidden), locked = objects.Count(o => o.IsLocked);
        string extra = string.Join(", ", new[] { hidden > 0 ? $"{hidden} hidden" : null, locked > 0 ? $"{locked} locked" : null }.Where(s => s is not null));
        Line("Objects", objects.Count + (extra.Length > 0 ? $" ({extra})" : ""));

        if (Scene.Assemblies.Count > 0) Line("Assemblies", Scene.Assemblies.Count.ToString());

        long triangles = objects.Sum(o => (long)o.Mesh.TriangleCount);
        Line("Triangles", triangles.ToString("N0"));

        if (objects.Count > 0)
        {
            var box = Bounds.Empty;
            foreach (var o in objects) box = box.Union(o.WorldBounds);
            Line("Size of it all", $"{unit.From(box.Size.X):0.##} × {unit.From(box.Size.Y):0.##} × {unit.From(box.Size.Z):0.##} {unit.Label}");

            // A turn keeps a volume; only the scale changes it.
            double volume = objects.Sum(o => Math.Abs(o.Mesh.ComputeSignedVolume() * o.Scale.X * o.Scale.Y * o.Scale.Z));
            Line("Volume", $"{volume / 1000:0.##} cm³");
        }

        if (projectPath is not null && File.Exists(projectPath))
        {
            try { Line("Versions kept", SceneSerializer.ReadVersions(projectPath).Count.ToString()); }
            catch { /* a file that will not say is not worth failing the page over */ }
        }

        Line("Printer", $"{plateWidth:0.#} × {plateDepth:0.#} × {plateHeight:0.#} mm");
        if (modelScale > 1.001f) Line("Model scale", $"1:{modelScale:0.##}");

        return text.ToString().TrimEnd();
    }
}
