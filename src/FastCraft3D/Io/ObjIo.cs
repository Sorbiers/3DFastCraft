using System.IO;
using System.Globalization;
using System.Numerics;
using System.Text;
using FastCraft3D.Geometry;

namespace FastCraft3D.Io;

/// <param name="Colour">Diffuse colour, components in 0..1. Written to a .mtl sidecar.</param>
public readonly record struct ObjObject(string Name, Mesh Mesh, Vector3 Colour);

/// <summary>
/// Wavefront OBJ writing.
///
/// Unlike STL, OBJ keeps objects separate and named, so a multi-part scene survives the round
/// trip. The pitfall it is built around: OBJ indices are 1-based and global to the file, not
/// per-object, so every object has to be written at a running offset.
/// </summary>
public static class ObjWriter
{
    public static void Write(string path, IReadOnlyList<ObjObject> objects, bool writeMaterials = true)
    {
        var ci = CultureInfo.InvariantCulture;
        string materialFile = Path.GetFileNameWithoutExtension(path) + ".mtl";

        using (var writer = new StreamWriter(path, false, new UTF8Encoding(false)))
        {
            writer.WriteLine("# Exported by 3DFastCraft - units are millimetres");
            if (writeMaterials) writer.WriteLine($"mtllib {materialFile}");

            int positionOffset = 1;
            int normalOffset = 1;

            for (int i = 0; i < objects.Count; i++)
            {
                var (name, mesh, _) = objects[i];
                var (positions, normals, indices) = ShadingNormals.Build(mesh);

                writer.WriteLine($"o {Sanitise(name)}");
                if (writeMaterials) writer.WriteLine($"usemtl material_{i}");

                foreach (var p in positions)
                    writer.WriteLine(string.Format(ci, "v {0:G9} {1:G9} {2:G9}", p.X, p.Y, p.Z));
                foreach (var n in normals)
                    writer.WriteLine(string.Format(ci, "vn {0:G9} {1:G9} {2:G9}", n.X, n.Y, n.Z));

                for (int t = 0; t + 2 < indices.Length; t += 3)
                {
                    int a = indices[t] + positionOffset;
                    int b = indices[t + 1] + positionOffset;
                    int c = indices[t + 2] + positionOffset;
                    int na = indices[t] + normalOffset;
                    int nb = indices[t + 1] + normalOffset;
                    int nc = indices[t + 2] + normalOffset;
                    writer.WriteLine($"f {a}//{na} {b}//{nb} {c}//{nc}");
                }

                positionOffset += positions.Length;
                normalOffset += normals.Length;
            }
        }

        if (!writeMaterials) return;

        using var mtl = new StreamWriter(Path.Combine(Path.GetDirectoryName(path) ?? ".", materialFile),
            false, new UTF8Encoding(false));
        for (int i = 0; i < objects.Count; i++)
        {
            var c = objects[i].Colour;
            mtl.WriteLine($"newmtl material_{i}");
            mtl.WriteLine(string.Format(ci, "Kd {0:G6} {1:G6} {2:G6}", c.X, c.Y, c.Z));
            mtl.WriteLine("Ka 0 0 0");
            mtl.WriteLine("d 1");
            mtl.WriteLine("illum 2");
            mtl.WriteLine();
        }

        static string Sanitise(string s) =>
            string.IsNullOrWhiteSpace(s) ? "object" : s.Replace(' ', '_').Replace('\n', '_').Replace('\r', '_');
    }
}

public static class ObjReader
{
    /// <summary>
    /// Reads an OBJ into one mesh per <c>o</c> group. Vertices are file-global, so groups are
    /// resolved against a single shared vertex list and only re-indexed at the end.
    /// </summary>
    public static List<(string Name, Mesh Mesh)> Read(string path)
    {
        var positions = new List<Vector3>();
        var groups = new List<(string Name, List<int> Indices)>();
        var current = ("default", new List<int>());
        bool currentUsed = false;

        foreach (string raw in File.ReadLines(path))
        {
            var line = raw.AsSpan().Trim();
            if (line.IsEmpty || line[0] == '#') continue;

            var parts = raw.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) continue;

            switch (parts[0])
            {
                case "v" when parts.Length >= 4:
                    positions.Add(new Vector3(Parse(parts[1]), Parse(parts[2]), Parse(parts[3])));
                    break;

                case "o":
                case "g":
                    if (currentUsed)
                    {
                        groups.Add(current);
                        current = (parts.Length > 1 ? parts[1] : "object", new List<int>());
                        currentUsed = false;
                    }
                    else
                    {
                        current = (parts.Length > 1 ? parts[1] : "object", current.Item2);
                    }
                    break;

                case "f" when parts.Length >= 4:
                    // Fan-triangulate n-gons; resolve negative indices relative to the end.
                    int Resolve(string token)
                    {
                        int slash = token.IndexOf('/');
                        string first = slash >= 0 ? token[..slash] : token;
                        if (!int.TryParse(first, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index))
                            return -1;
                        return index > 0 ? index - 1 : positions.Count + index;
                    }

                    int v0 = Resolve(parts[1]);
                    for (int i = 2; i + 1 < parts.Length; i++)
                    {
                        int v1 = Resolve(parts[i]);
                        int v2 = Resolve(parts[i + 1]);
                        if (v0 < 0 || v1 < 0 || v2 < 0) continue;
                        if (v0 >= positions.Count || v1 >= positions.Count || v2 >= positions.Count) continue;
                        current.Item2.Add(v0);
                        current.Item2.Add(v1);
                        current.Item2.Add(v2);
                        currentUsed = true;
                    }
                    break;
            }
        }

        if (currentUsed) groups.Add(current);

        var result = new List<(string, Mesh)>(groups.Count);
        foreach (var (name, indices) in groups)
        {
            // Compact the shared vertex list down to the ones this group actually uses.
            var remap = new Dictionary<int, int>();
            var groupPositions = new List<Vector3>();
            var groupIndices = new List<int>(indices.Count);

            foreach (int index in indices)
            {
                if (!remap.TryGetValue(index, out int local))
                {
                    local = groupPositions.Count;
                    remap[index] = local;
                    groupPositions.Add(positions[index]);
                }
                groupIndices.Add(local);
            }

            result.Add((name, new Mesh(groupPositions, groupIndices).Welded()));
        }

        return result;

        static float Parse(string s) =>
            float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : 0f;
    }
}
