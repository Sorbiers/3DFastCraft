namespace FastCraft3D.Io;

/// <summary>
/// What Export session writes one of, per undo step.
///
/// No screenshot option: the "Exporting session" progress dialog itself would be sitting in
/// every frame it captured, since the export runs under the same busy overlay every long-running
/// operation shows. Record takes screenshots without that problem, because it never puts one up.
/// </summary>
public enum SessionExportFormat
{
    Project,
    Stl,
    ThreeMf
}
