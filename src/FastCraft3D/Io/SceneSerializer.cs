using System.IO;
using System.IO.Compression;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using FastCraft3D.Geometry;
using FastCraft3D.Model;

namespace FastCraft3D.Io;

/// <param name="Label">What the user called this version.</param>
/// <param name="SavedUtc">When it was kept.</param>
/// <param name="ObjectCount">How many objects it holds, for the picker.</param>
public readonly record struct SceneVersion(string Label, DateTime SavedUtc, int ObjectCount);

/// <summary>
/// The project format (.3dfc): GZip-compressed JSON.
///
/// Unlike an STL export, this keeps objects separate and their transforms live, so a scene can
/// be reopened and kept editing. Meshes are stored as flat float arrays - readable enough to
/// debug, and GZip removes the cost of the verbosity (mesh coordinates compress well).
///
/// A file holds the current scene plus any number of named versions kept alongside it, so a
/// model's history travels with the model instead of spreading across files named "v2 final".
/// Versions are deliberately named snapshots rather than a serialised undo stack: undo steps are
/// fine-grained - every drag is one - so persisting them would store hundreds of near-identical
/// scenes, and none of them would tell you which was the one worth going back to.
/// </summary>
public static class SceneSerializer
{
    public const string Extension = ".3dfc";

    /// <summary>Version 1 held only a scene; version 2 added the kept versions beside it.</summary>
    private const int CurrentVersion = 2;

    private static readonly JsonSerializerOptions Options = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    /// <summary>Writes the scene as the current state, leaving any kept versions untouched.</summary>
    public static void Save(string path, Scene scene)
    {
        var dto = ReadIfPresent(path) ?? new SceneDto();
        dto.Version = CurrentVersion;
        dto.Objects = scene.Objects.Select(ToDto).ToList();
        Write(path, dto);
    }

    /// <summary>Keeps a labelled snapshot in the file, and saves the scene as current.</summary>
    public static void SaveVersion(string path, Scene scene, string label)
    {
        var dto = ReadIfPresent(path) ?? new SceneDto();
        dto.Version = CurrentVersion;
        dto.Objects = scene.Objects.Select(ToDto).ToList();

        (dto.Versions ??= []).Add(new VersionDto
        {
            Label = string.IsNullOrWhiteSpace(label) ? "Version" : label.Trim(),
            SavedUtc = DateTime.UtcNow,
            Objects = dto.Objects // the snapshot is the state being saved
        });

        Write(path, dto);
    }

    public static List<SceneObject> Load(string path) => Read(path).Objects?.Select(FromDto).ToList() ?? [];

    /// <summary>The versions kept in a file, oldest first.</summary>
    public static List<SceneVersion> ReadVersions(string path)
    {
        var dto = ReadIfPresent(path);
        if (dto?.Versions is null) return [];

        return dto.Versions
            .Select(v => new SceneVersion(v.Label ?? "Version", v.SavedUtc, v.Objects?.Count ?? 0))
            .ToList();
    }

    /// <summary>Reads one kept version back out, by its position in <see cref="ReadVersions"/>.</summary>
    public static List<SceneObject> LoadVersion(string path, int index)
    {
        var dto = Read(path);
        if (dto.Versions is null || index < 0 || index >= dto.Versions.Count)
            throw new ArgumentOutOfRangeException(nameof(index), "That version is not in this file.");

        return dto.Versions[index].Objects?.Select(FromDto).ToList() ?? [];
    }

    /// <summary>Forgets a kept version. The current scene is never touched.</summary>
    public static void DeleteVersion(string path, int index)
    {
        var dto = Read(path);
        if (dto.Versions is null || index < 0 || index >= dto.Versions.Count) return;

        dto.Versions.RemoveAt(index);
        Write(path, dto);
    }

    // --- Plumbing ---------------------------------------------------------------------

    private static SceneDto Read(string path)
    {
        using var file = File.OpenRead(path);
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        var dto = JsonSerializer.Deserialize<SceneDto>(gzip, Options)
                  ?? throw new InvalidDataException("The project file is empty or unreadable.");

        if (dto.Version > CurrentVersion)
            throw new InvalidDataException(
                $"This project was saved by a newer version of 3DFastCraft (format {dto.Version}).");

        return dto;
    }

    /// <summary>Reads a file if it exists and is readable; used to preserve versions on save.</summary>
    private static SceneDto? ReadIfPresent(string path)
    {
        if (!File.Exists(path)) return null;
        try { return Read(path); }
        catch { return null; } // a corrupt or foreign file is replaced rather than blocking the save
    }

    private static void Write(string path, SceneDto dto)
    {
        // Written to a temporary file first: a save interrupted halfway would otherwise destroy
        // both the current scene and every version kept with it.
        string temporary = path + ".tmp";

        using (var file = File.Create(temporary))
        using (var gzip = new GZipStream(file, CompressionLevel.Optimal))
        {
            JsonSerializer.Serialize(gzip, dto, Options);
        }

        File.Move(temporary, path, overwrite: true);
    }

    private static ObjectDto ToDto(SceneObject o) => new()
    {
        Name = o.Name,
        Position = ToArray(o.Position),
        Rotation = ToArray(o.Rotation),
        Scale = ToArray(o.Scale),
        Colour = ToArray(o.Colour),
        Origin = o.Origin?.ToString(),
        PiecesTakeClearance = o.PiecesTakeClearance,
        Vertices = Flatten(o.Mesh.Positions),
        Triangles = o.Mesh.Indices.ToArray()
    };

    private static SceneObject FromDto(ObjectDto o) =>
        new(o.Name ?? "Object", new Mesh(Unflatten(o.Vertices), o.Triangles ?? []))
        {
            Position = ToVector(o.Position),
            Rotation = ToVector(o.Rotation),
            Scale = o.Scale is { Length: 3 } ? ToVector(o.Scale) : Vector3.One,
            Colour = o.Colour is { Length: 3 } ? ToVector(o.Colour) : new Vector3(0.3f, 0.55f, 0.85f),
            Origin = Enum.TryParse<PrimitiveKind>(o.Origin, out var kind) ? kind : null,
            PiecesTakeClearance = o.PiecesTakeClearance
        };

    private static float[] ToArray(Vector3 v) => [v.X, v.Y, v.Z];

    private static Vector3 ToVector(float[]? a) =>
        a is { Length: 3 } ? new Vector3(a[0], a[1], a[2]) : Vector3.Zero;

    private static float[] Flatten(List<Vector3> positions)
    {
        var result = new float[positions.Count * 3];
        for (int i = 0; i < positions.Count; i++)
        {
            result[i * 3] = positions[i].X;
            result[i * 3 + 1] = positions[i].Y;
            result[i * 3 + 2] = positions[i].Z;
        }
        return result;
    }

    private static List<Vector3> Unflatten(float[]? values)
    {
        if (values is null) return [];
        var result = new List<Vector3>(values.Length / 3);
        for (int i = 0; i + 2 < values.Length; i += 3)
            result.Add(new Vector3(values[i], values[i + 1], values[i + 2]));
        return result;
    }

    private sealed class SceneDto
    {
        public int Version { get; set; }
        public List<ObjectDto>? Objects { get; set; }
        public List<VersionDto>? Versions { get; set; }
    }

    private sealed class VersionDto
    {
        public string? Label { get; set; }
        public DateTime SavedUtc { get; set; }
        public List<ObjectDto>? Objects { get; set; }
    }

    private sealed class ObjectDto
    {
        public string? Name { get; set; }
        public float[]? Position { get; set; }
        public float[]? Rotation { get; set; }
        public float[]? Scale { get; set; }
        public float[]? Colour { get; set; }
        public string? Origin { get; set; }

        /// <summary>A group every piece of which can be grown by a clearance.</summary>
        public bool PiecesTakeClearance { get; set; }

        public float[]? Vertices { get; set; }
        public int[]? Triangles { get; set; }
    }
}
