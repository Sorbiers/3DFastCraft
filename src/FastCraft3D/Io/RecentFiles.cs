using System.IO;
using System.Text.Json;

namespace FastCraft3D.Io;

/// <summary>
/// The projects opened most recently, remembered between sessions.
///
/// Kept in the user's application data rather than beside the executable: the standalone build
/// is a single file that may well live in Downloads or on a memory stick, and writing settings
/// next to it would either fail or litter.
/// </summary>
public sealed class RecentFiles
{
    private const int Limit = 8;

    private readonly string storePath;
    private readonly List<string> paths = new();

    public RecentFiles(string? storePath = null)
    {
        this.storePath = storePath ?? DefaultStorePath();
        Load();
    }

    /// <summary>Most recent first.</summary>
    public IReadOnlyList<string> Paths => paths;

    public event Action? Changed;

    private static string DefaultStorePath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "3DFastCraft", "recent.json");

    /// <summary>Moves a file to the top of the list, adding it if it is new.</summary>
    public void Add(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;

        string full = SafeFullPath(path);
        paths.RemoveAll(p => string.Equals(p, full, StringComparison.OrdinalIgnoreCase));
        paths.Insert(0, full);

        while (paths.Count > Limit) paths.RemoveAt(paths.Count - 1);

        Save();
        Changed?.Invoke();
    }

    /// <summary>Drops a file, for when it turns out to have been moved or deleted.</summary>
    public void Remove(string path)
    {
        string full = SafeFullPath(path);
        if (paths.RemoveAll(p => string.Equals(p, full, StringComparison.OrdinalIgnoreCase)) == 0) return;

        Save();
        Changed?.Invoke();
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(storePath)) return;

            var stored = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(storePath));
            if (stored is not null) paths.AddRange(stored.Take(Limit));
        }
        catch
        {
            // A missing or damaged list is not worth interrupting the user over; it simply
            // starts empty again.
        }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(storePath)!);
            File.WriteAllText(storePath, JsonSerializer.Serialize(paths));
        }
        catch
        {
            // Read-only or roaming profiles must not break saving the actual model.
        }
    }

    private static string SafeFullPath(string path)
    {
        try { return Path.GetFullPath(path); }
        catch { return path; }
    }
}
