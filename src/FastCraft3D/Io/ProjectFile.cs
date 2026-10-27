using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;

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
/// again. It is where what only this app needs, and a slicer has no use for, goes - the view, and
/// the session as it goes (see <see cref="ProjectSession"/>).
///
/// It is the .3mf as last saved, every part of it, with this app's own parts added - so it is a 3MF
/// package itself. The project part is at the same path, so the project reads out of either the
/// same way; and Windows' own 3MF thumbnail and preview handler draws a .3dfp as it does a .3mf,
/// which spares the app a shell extension of its own (see <see cref="ShellRegistration"/>).
/// </summary>
public static class ProjectFile
{
    public const string Extension = ".3dfp";

    private const string ManifestPath = "3DFastCraft/manifest.json";
    private const string ViewPath = "3DFastCraft/view.json";
    private const string ContentTypesPath = "[Content_Types].xml";
    private const int CurrentFormat = 1;

    public static string NewId() => Guid.NewGuid().ToString("N");

    /// <summary>Where the project file of a .3mf goes: the same name, beside it.</summary>
    public static string Beside(string modelPath) => Path.ChangeExtension(modelPath, Extension);

    /// <summary>
    /// Writes the project file for a .3mf just saved: the .3mf's parts, the view, and the
    /// fingerprint of the .3mf as it now is. A new one goes to a temporary file first and then over
    /// any old one, so a save cut short never leaves half a file where the last good one was.
    /// </summary>
    public static void Write(string path, string id, string modelPath, ViewState? view, string? app)
    {
        var package = PackageOf(modelPath);
        if (!package.Any(p => p.Name == SceneSerializer.PartPath))
            throw new InvalidDataException("This file holds no 3DFastCraft project.");

        byte[] manifest = JsonSerializer.SerializeToUtf8Bytes(new ManifestDto
        {
            Format = CurrentFormat,
            Id = id,
            ModelName = Path.GetFileName(modelPath),
            Fingerprint = Fingerprint(modelPath),
            SavedUtc = DateTime.UtcNow,
            App = app
        });

        // This project's own file holds its session too, which can run to many megabytes: the
        // parts that change are replaced where it lies rather than the lot copied to a new file at
        // every save. That gives up the temporary file for those parts, but they are all in the
        // .3mf just written, and the .3dfp can be made again from it.
        if (File.Exists(path) && ReadInfo(path)?.Id == id)
        {
            using var zip = ZipFile.Open(path, ZipArchiveMode.Update);

            // What the last .3mf had and this one has not: the picture of a plate since emptied.
            var fresh = package.Select(p => p.Name).ToHashSet();
            foreach (var entry in zip.Entries.ToList())
                if (!IsOwn(entry.FullName) && !fresh.Contains(entry.FullName)) entry.Delete();

            foreach (var (name, data) in package) Replace(zip, name, data);
            Replace(zip, ManifestPath, manifest);
            if (view is not null) Replace(zip, ViewPath, JsonSerializer.SerializeToUtf8Bytes(view));
            return;
        }

        string temporary = path + ".tmp";
        using (var file = File.Create(temporary))
        using (var zip = new ZipArchive(file, ZipArchiveMode.Create))
        {
            foreach (var (name, data) in package) Entry(zip, name, data);
            Entry(zip, ManifestPath, manifest);
            if (view is not null) Entry(zip, ViewPath, JsonSerializer.SerializeToUtf8Bytes(view));
        }

        File.Move(temporary, path, overwrite: true);
    }

    /// <summary>The parts only a .3dfp has: its own, and the session's. Everything else came from the .3mf.</summary>
    private static bool IsOwn(string name) => name == ManifestPath || name == ViewPath || ProjectSession.Holds(name);

    /// <summary>
    /// Every part of the .3mf, with its content types made to cover what only a .3dfp holds - the
    /// session's JSON and meshes. A package with a part of no declared type is refused whole by a
    /// strict reader, and Windows' thumbnail handler is one.
    /// </summary>
    private static List<(string Name, byte[] Data)> PackageOf(string modelPath)
    {
        using var zip = ZipFile.OpenRead(modelPath);
        var parts = new List<(string Name, byte[] Data)>();
        foreach (var entry in zip.Entries)
        {
            using var stream = entry.Open();
            using var copy = new MemoryStream();
            stream.CopyTo(copy);
            parts.Add((entry.FullName, entry.FullName == ContentTypesPath ? WithOwnTypes(copy.ToArray()) : copy.ToArray()));
        }
        return parts;
    }

    private static byte[] WithOwnTypes(byte[] xml)
    {
        var document = XDocument.Load(new MemoryStream(xml));
        var root = document.Root ?? throw new InvalidDataException("The .3mf's content types are empty.");
        XNamespace types = root.Name.Namespace;

        foreach (var (extension, type) in new[] { ("json", "application/json"), (ProjectSession.MeshExtension, "application/octet-stream") })
        {
            if (!root.Elements(types + "Default").Any(d => string.Equals((string?)d.Attribute("Extension"), extension, StringComparison.OrdinalIgnoreCase)))
                root.Add(new XElement(types + "Default", new XAttribute("Extension", extension), new XAttribute("ContentType", type)));
        }

        using var output = new MemoryStream();
        document.Save(output);
        return output.ToArray();
    }

    private static void Replace(ZipArchive zip, string name, byte[] data)
    {
        zip.GetEntry(name)?.Delete();
        Entry(zip, name, data);
    }

    /// <summary>Fastest, as the session is: the model is copied in at every save, and it can be large.</summary>
    private static void Entry(ZipArchive zip, string name, byte[] data)
    {
        using var stream = zip.CreateEntry(name, CompressionLevel.Fastest).Open();
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

    /// <summary>The view kept with a project, or null: as the session last left it, else as last saved.</summary>
    public static ViewState? ReadView(string path)
    {
        try { return ProjectSession.ReadView(path) ?? Read<ViewState>(path, ViewPath); }
        catch { return null; }
    }

    /// <summary>The id in a project file already open, or null for one that is not one.</summary>
    internal static string? IdIn(ZipArchive zip)
    {
        if (zip.GetEntry(ManifestPath) is not { } entry) return null;
        using var stream = entry.Open();
        var manifest = JsonSerializer.Deserialize<ManifestDto>(stream);
        return manifest is { Format: <= CurrentFormat } ? manifest.Id : null;
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
