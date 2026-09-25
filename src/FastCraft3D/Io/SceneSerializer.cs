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

/// <summary>How the project is looked at and measured: none of it touches the geometry.</summary>
/// <param name="PlateWidth">The printable area across X, in millimetres.</param>
/// <param name="PlateDepth">Across Y.</param>
/// <param name="PlateHeight">How tall a part the printer takes.</param>
/// <param name="Unit">What the boxes read in - "mm", "in" and so on.</param>
/// <param name="ModelScale">87 for a model drawn at 1:87.</param>
public readonly record struct ProjectSettings(float PlateWidth, float PlateDepth, float PlateHeight, string Unit, float ModelScale);

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
    public static void Save(string path, Scene scene, ProjectSettings? settings = null)
    {
        var dto = ReadIfPresent(path) ?? new SceneDto();
        dto.Version = CurrentVersion;
        dto.Objects = scene.Objects.Select(ToDto).ToList();
        Keep(dto, settings);
        Write(path, dto);
    }

    /// <summary>
    /// Writes the scene on its own, without reading back whatever is at the path already.
    ///
    /// For the crash file, which is rewritten over and over as the work goes on and has no kept
    /// versions to preserve. The ordinary save reads the file first precisely so that it cannot
    /// lose them; reading half a megabyte back every half minute to preserve nothing is work for
    /// nothing.
    /// </summary>
    public static void SaveSnapshot(string path, Scene scene, ProjectSettings? settings = null)
    {
        var dto = new SceneDto { Version = CurrentVersion, Objects = scene.Objects.Select(ToDto).ToList() };
        Keep(dto, settings);
        Write(path, dto);
    }

    /// <summary>Keeps a labelled snapshot in the file, and saves the scene as current.</summary>
    public static void SaveVersion(string path, Scene scene, string label, ProjectSettings? settings = null)
    {
        var dto = ReadIfPresent(path) ?? new SceneDto();
        dto.Version = CurrentVersion;
        dto.Objects = scene.Objects.Select(ToDto).ToList();
        Keep(dto, settings);

        (dto.Versions ??= []).Add(new VersionDto
        {
            Label = string.IsNullOrWhiteSpace(label) ? "Version" : label.Trim(),
            SavedUtc = DateTime.UtcNow,
            Objects = dto.Objects // the snapshot is the state being saved
        });

        Write(path, dto);
    }

    public static List<SceneObject> Load(string path) => Load(path, out _);

    /// <summary>
    /// The scene, and the bed, unit and scale it was saved with - null for a file from before
    /// they were kept, which then opens with whatever is set now.
    /// </summary>
    public static List<SceneObject> Load(string path, out ProjectSettings? settings)
    {
        var dto = Read(path);

        // A file from before the printable area had a depth and a height kept one square bed size.
        float? width = dto.PlateWidth ?? dto.PlateSize;
        float? depth = dto.PlateDepth ?? dto.PlateSize;
        settings = width is { } w && depth is { } d && dto.Unit is { } unit && dto.ModelScale is { } scale
            ? new ProjectSettings(w, d, dto.PlateHeight ?? Scene.PrintHeight, unit, scale)
            : null;
        return dto.Objects?.Select(FromDto).ToList() ?? [];
    }

    /// <summary>Settings given are written; none given leaves what the file already held.</summary>
    private static void Keep(SceneDto dto, ProjectSettings? settings)
    {
        if (settings is not { } s) return;
        dto.PlateSize = null;
        dto.PlateWidth = s.PlateWidth;
        dto.PlateDepth = s.PlateDepth;
        dto.PlateHeight = s.PlateHeight;
        dto.Unit = s.Unit;
        dto.ModelScale = s.ModelScale;
    }

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
        Filament = o.Filament > 1 ? o.Filament : null,
        Anchors = o.Anchors.Count > 0 ? o.Anchors.Select(ToDto).ToArray() : null,
        Pivot = o.PivotIsOwn ? true : null,
        Origin = o.Origin?.ToString(),
        Pristine = o.IsPristine,
        PiecesTakeClearance = o.PiecesTakeClearance,
        Hidden = o.IsHidden ? true : null,
        Locked = o.IsLocked ? true : null,
        Recipe = o.Recipe is { } r
            ? new RecipeDto { Generator = r.Generator, Version = r.Version, Settings = r.Settings, Role = r.Role, Set = r.Set, Origin = ToArray(r.Origin) }
            : null,
        Vertices = Flatten(o.Mesh.Positions),
        Triangles = o.Mesh.Indices.ToArray()
    };

    private static SceneObject FromDto(ObjectDto o)
    {
        var mesh = new Mesh(Unflatten(o.Vertices), o.Triangles ?? []);
        PrimitiveKind? origin = Enum.TryParse<PrimitiveKind>(o.Origin, out var kind) ? kind : null;

        return new SceneObject(o.Name ?? "Object", mesh)
        {
            Position = ToVector(o.Position),
            Rotation = ToVector(o.Rotation),
            Scale = o.Scale is { Length: 3 } ? ToVector(o.Scale) : Vector3.One,
            Colour = o.Colour is { Length: 3 } ? ToVector(o.Colour) : new Vector3(0.3f, 0.55f, 0.85f),
            Filament = o.Filament ?? 1,
            Anchors = o.Anchors?.Select(FromDto).ToList() ?? [],
            PivotIsOwn = o.Pivot == true,
            Origin = origin,
            IsPristine = origin is not null && (o.Pristine ?? IsConvex(mesh)),
            PiecesTakeClearance = o.PiecesTakeClearance,
            IsHidden = o.Hidden == true,
            IsLocked = o.Locked == true,
            Recipe = o.Recipe is { Generator: { } generator, Settings: { } settings } r
                ? new Recipe(generator, r.Version, settings, r.Role, r.Set, r.Origin is { Length: 3 } ? ToVector(r.Origin) : Vector3.Zero)
                : null
        };
    }

    /// <summary>
    /// A guess at whether a file written before the pristine flag still holds the primitive.
    ///
    /// Such a file kept the origin on a cylinder with a hole subtracted from it, and nothing else
    /// says whether the hole is there. Every shape that can be rounded is convex, rounded or not,
    /// and a hole or a notch is not - so a convex mesh is taken as untouched. Turning everything
    /// old into "not roundable" would have been safe, and would have taken Round away from every
    /// plain cube in every existing project.
    /// </summary>
    private static bool IsConvex(Mesh mesh)
    {
        var positions = mesh.Positions;
        var indices = mesh.Indices;
        if (indices.Count < 12) return false;

        float tolerance = 1e-4f * mesh.ComputeBounds().Size.Length() + 1e-5f;

        for (int t = 0; t + 2 < indices.Count; t += 3)
        {
            Vector3 a = positions[indices[t]];
            Vector3 normal = Vector3.Cross(positions[indices[t + 1]] - a, positions[indices[t + 2]] - a);
            float length = normal.Length();
            if (length < 1e-9f) continue;
            normal /= length;

            foreach (var p in positions)
                if (Vector3.Dot(p - a, normal) > tolerance) return false;
        }

        return true;
    }

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

        // Added without a format bump: a file without them still reads, and an older copy of
        // the app skips names it does not know.
        public float? PlateSize { get; set; }
        public float? PlateWidth { get; set; }
        public float? PlateDepth { get; set; }
        public float? PlateHeight { get; set; }
        public string? Unit { get; set; }
        public float? ModelScale { get; set; }
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

        /// <summary>Written only when it is not the first filament, so old files read as they did.</summary>
        public int? Filament { get; set; }

        /// <summary>What a tool marked on the part. Null for everything nothing was marked on.</summary>
        public AnchorDto[]? Anchors { get; set; }

        /// <summary>Written only when the origin was chosen, so an ordinary object reads as it did.</summary>
        public bool? Pivot { get; set; }

        public string? Origin { get; set; }

        /// <summary>Null in files written before it existed, which are then judged by shape.</summary>
        public bool? Pristine { get; set; }

        /// <summary>A group every piece of which can be grown by a clearance.</summary>
        public bool PiecesTakeClearance { get; set; }

        /// <summary>Written only when set, so a file with nothing hidden or locked reads as it did.</summary>
        public bool? Hidden { get; set; }

        public bool? Locked { get; set; }

        /// <summary>How a generator made it. Null for everything else, so older files read as they did.</summary>
        public RecipeDto? Recipe { get; set; }

        public float[]? Vertices { get; set; }
        public int[]? Triangles { get; set; }
    }

    /// <summary>See <see cref="Geometry.Recipe"/>. Field by field, as the anchors are, so a field added later reads back as its default.</summary>
    private sealed class RecipeDto
    {
        public string? Generator { get; set; }
        public int Version { get; set; }
        public string? Settings { get; set; }
        public string? Role { get; set; }
        public string? Set { get; set; }
        public float[]? Origin { get; set; }
    }

    /// <summary>
    /// One marked feature. Written out field by field rather than as the record itself so that a
    /// field added later reads back as its default instead of making the file unreadable.
    /// </summary>
    private sealed class AnchorDto
    {
        public string? Kind { get; set; }
        public string? Name { get; set; }
        public float[]? At { get; set; }
        public float[]? Along { get; set; }
        public float[]? Across { get; set; }
        public float Size { get; set; }
        public float Length { get; set; }
        public string? Shape { get; set; }
        public float Flat { get; set; }
    }

    private static AnchorDto ToDto(Anchor a) => new()
    {
        Kind = a.Kind.ToString(),
        Name = a.Name,
        At = ToArray(a.At),
        Along = ToArray(a.Along),
        Across = ToArray(a.Across),
        Size = a.Size,
        Length = a.Length,
        Shape = a.Shape.ToString(),
        Flat = a.Flat
    };

    private static Anchor FromDto(AnchorDto a) => new(
        Enum.TryParse<AnchorKind>(a.Kind, out var kind) ? kind : AnchorKind.Bore,
        a.Name ?? "Feature",
        ToVector(a.At),
        a.Along is { Length: 3 } ? ToVector(a.Along) : Vector3.UnitZ,
        ToVector(a.Across),
        a.Size,
        a.Length,
        Enum.TryParse<BoreShape>(a.Shape, out var shape) ? shape : BoreShape.Round,
        a.Flat);
}
