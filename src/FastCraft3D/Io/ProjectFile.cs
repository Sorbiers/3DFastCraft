using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace FastCraft3D.Io;

/// <summary>Where the camera was, as the project was left: the scene's own coordinates, nothing of the renderer's.</summary>
public sealed record ViewState(double[] Position, double[] Look, double[] Up, bool Orthographic, double Width);

/// <summary>What a .3dfp says about itself.</summary>
/// <param name="Id">Ties it to its .3mf, which carries the same id.</param>
/// <param name="ModelName">The .3mf it was kept beside, by name.</param>
/// <param name="Fingerprint">The .3mf as this app last saved it, to tell whether something else has written it since.</param>
public sealed record ProjectFileInfo(string Id, string ModelName, string Fingerprint, DateTime SavedUtc, string? App);

/// <summary>
/// The project file kept beside a project's .3mf: house.3dfp beside house.3mf.
///
/// A slicer that saves over a .3mf writes a new one with only what it understands, and the
/// project this app keeps inside it is gone - every editable object, every kept version. Nothing
/// put into the .3mf can stop that, so a copy of the project goes into a file no slicer opens. It
/// is never the only copy: the .3mf stays whole on its own, and this one can be deleted and made
/// again. It is where what only this app needs, and a slicer has no use for, will go - the view
/// now, the session as it goes later.
///
/// A zip, as a 3MF is, with the project part at the same path as in the .3mf, so the project
/// reads out of either the same way.
/// </summary>
public static class ProjectFile
{
    public const string Extension = ".3dfp";

    private const string ManifestPath = "3DFastCraft/manifest.json";
    private const string ViewPath = "3DFastCraft/view.json";
    private const int CurrentFormat = 1;

    public static string NewId() => Guid.NewGuid().ToString("N");

    /// <summary>Where the project file of a .3mf goes: the same name, beside it.</summary>
    public static string Beside(string modelPath) => Path.ChangeExtension(modelPath, Extension);

    /// <summary>
    /// Writes the project file for a .3mf just saved: its project part, the view, and the
    /// fingerprint of the .3mf as it now is. To a temporary file first and then over the old one,
    /// so a save cut short never leaves half a file where the last good one was.
    /// </summary>
    public static void Write(string path, string id, string modelPath, ViewState? view, string? app)
    {
        byte[] part = SceneSerializer.ReadProjectPart(modelPath);
        var manifest = new ManifestDto
        {
            Format = CurrentFormat,
            Id = id,
            ModelName = Path.GetFileName(modelPath),
            Fingerprint = Fingerprint(modelPath),
            SavedUtc = DateTime.UtcNow,
            App = app
        };

        string temporary = path + ".tmp";
        using (var file = File.Create(temporary))
        using (var zip = new ZipArchive(file, ZipArchiveMode.Create))
        {
            Entry(zip, ManifestPath, JsonSerializer.SerializeToUtf8Bytes(manifest));
            Entry(zip, SceneSerializer.PartPath, part);
            if (view is not null) Entry(zip, ViewPath, JsonSerializer.SerializeToUtf8Bytes(view));
        }

        File.Move(temporary, path, overwrite: true);
    }

    private static void Entry(ZipArchive zip, string name, byte[] data)
    {
        using var stream = zip.CreateEntry(name, CompressionLevel.Optimal).Open();
        stream.Write(data);
    }

    /// <summary>What a project file says about itself, or null for one that is not one or cannot be read.</summary>
    public static ProjectFileInfo? ReadInfo(string path)
    {
        try
        {
            var manifest = Read<ManifestDto>(path, ManifestPath);
            if (manifest?.Id is not { Length: > 0 } id || manifest.Format > CurrentFormat) return null;
            return new ProjectFileInfo(id, manifest.ModelName ?? "", manifest.Fingerprint ?? "", manifest.SavedUtc, manifest.App);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>The view kept with a project, or null.</summary>
    public static ViewState? ReadView(string path)
    {
        try { return Read<ViewState>(path, ViewPath); }
        catch { return null; }
    }

    private static T? Read<T>(string path, string entry)
    {
        using var zip = ZipFile.OpenRead(path);
        if (zip.GetEntry(entry) is not { } part) return default;
        using var stream = part.Open();
        return JsonSerializer.Deserialize<T>(stream);
    }

    /// <summary>
    /// The project file of a .3mf: the one of the same name beside it, or - when an id is known - one
    /// with that id anywhere in the same folder, for a pair renamed one at a time. A file of the
    /// same name with another project's id in it is not this one's.
    /// </summary>
    public static string? Find(string modelPath, string? id)
    {
        string beside = Beside(modelPath);
        if (File.Exists(beside) && ReadInfo(beside) is { } info && (id is null || info.Id == id)) return beside;
        if (id is null) return null;

        try
        {
            string folder = Path.GetDirectoryName(Path.GetFullPath(modelPath)) ?? ".";
            foreach (string other in Directory.EnumerateFiles(folder, "*" + Extension))
                if (ReadInfo(other)?.Id == id) return other;
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }

        return null;
    }

    /// <summary>A hash of the whole file, to tell whether it is still byte for byte what was saved.</summary>
    public static string Fingerprint(string path)
    {
        using var file = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(file));
    }

    private sealed class ManifestDto
    {
        public int Format { get; set; }
        public string? Id { get; set; }
        public string? ModelName { get; set; }
        public string? Fingerprint { get; set; }
        public DateTime SavedUtc { get; set; }
        public string? App { get; set; }
    }
}
