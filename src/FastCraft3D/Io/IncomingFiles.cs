using System.IO;

namespace FastCraft3D.Io;

/// <summary>
/// Files arriving from outside the app: dropped on the window, or handed over by Windows when
/// something is opened with the exe.
///
/// One list of what the app will take, in one place, because the two ways in have to agree. A
/// file type the window accepts on a drop but ignores on the command line is a bug nobody sees
/// until they associate the extension.
/// </summary>
public static class IncomingFiles
{
    private static readonly string[] Known = [".stl", ".obj", SceneSerializer.Extension];

    /// <summary>Whether this app has anything to say about a file with this name.</summary>
    public static bool Understood(string path) =>
        Path.GetExtension(path) is { Length: > 0 } extension
        && Known.Contains(extension, StringComparer.OrdinalIgnoreCase);

    /// <summary>Whether opening this one means replacing the plate rather than adding to it.</summary>
    public static bool IsProject(string path) =>
        Path.GetExtension(path).Equals(SceneSerializer.Extension, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The files among these that are worth opening, in the order they were given.
    ///
    /// Anything else is dropped silently rather than reported. A command line can carry switches
    /// the app knows nothing about, a shortcut can point at a file that has since been moved, and
    /// neither is worth a dialog in front of someone who only double-clicked a model.
    /// </summary>
    public static List<string> Wanted(IEnumerable<string>? paths) =>
        paths is null
            ? []
            : paths.Where(p => !string.IsNullOrWhiteSpace(p) && Understood(p) && File.Exists(p))
                   .ToList();
}
