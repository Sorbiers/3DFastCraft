using System.IO;
using System.IO.Compression;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using FastCraft3D.Geometry;
using FastCraft3D.Model;

namespace FastCraft3D.Io;

/// <summary>
/// The project format (.3dfc): GZip-compressed JSON.
///
/// Unlike an STL export, this keeps objects separate and their transforms live, so a scene can
/// be reopened and kept editing. Meshes are stored as flat float arrays - readable enough to
/// debug, and GZip removes the cost of the verbosity (mesh coordinates compress well).
/// </summary>
public static class SceneSerializer
{
    public const string Extension = ".3dfc";
    private const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions Options = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    public static void Save(string path, Scene scene)
    {
        var dto = new SceneDto
        {
            Version = CurrentVersion,
            Objects = scene.Objects.Select(o => new ObjectDto
            {
                Name = o.Name,
                Position = ToArray(o.Position),
                Rotation = ToArray(o.Rotation),
                Scale = ToArray(o.Scale),
                Colour = ToArray(o.Colour),
                Origin = o.Origin?.ToString(),
                Vertices = Flatten(o.Mesh.Positions),
                Triangles = o.Mesh.Indices.ToArray()
            }).ToList()
        };

        using var file = File.Create(path);
        using var gzip = new GZipStream(file, CompressionLevel.Optimal);
        JsonSerializer.Serialize(gzip, dto, Options);
    }

    public static List<SceneObject> Load(string path)
    {
        using var file = File.OpenRead(path);
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        var dto = JsonSerializer.Deserialize<SceneDto>(gzip, Options)
                  ?? throw new InvalidDataException("The project file is empty or unreadable.");

        if (dto.Version > CurrentVersion)
            throw new InvalidDataException(
                $"This project was saved by a newer version of 3DFastCraft (format {dto.Version}).");

        var result = new List<SceneObject>();
        foreach (var o in dto.Objects ?? [])
        {
            var mesh = new Mesh(Unflatten(o.Vertices), o.Triangles ?? []);
            result.Add(new SceneObject(o.Name ?? "Object", mesh)
            {
                Position = ToVector(o.Position),
                Rotation = ToVector(o.Rotation),
                Scale = o.Scale is { Length: 3 } ? ToVector(o.Scale) : Vector3.One,
                Colour = o.Colour is { Length: 3 } ? ToVector(o.Colour) : new Vector3(0.3f, 0.55f, 0.85f),
                Origin = Enum.TryParse<PrimitiveKind>(o.Origin, out var kind) ? kind : null
            });
        }

        return result;
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
    }

    private sealed class ObjectDto
    {
        public string? Name { get; set; }
        public float[]? Position { get; set; }
        public float[]? Rotation { get; set; }
        public float[]? Scale { get; set; }
        public float[]? Colour { get; set; }
        public string? Origin { get; set; }
        public float[]? Vertices { get; set; }
        public int[]? Triangles { get; set; }
    }
}
