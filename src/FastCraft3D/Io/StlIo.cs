using System.IO;
using System.Globalization;
using System.Numerics;
using System.Text;
using FastCraft3D.Geometry;

namespace FastCraft3D.Io;

/// <summary>
/// STL reading and writing.
///
/// STL carries no units; every slicer assumes millimetres, which is what the whole app works
/// in, so no conversion happens here. Facet normals are always recomputed from the vertices
/// by the right-hand rule rather than copied from the source - files in the wild frequently
/// disagree with their own winding, and slicers trust the winding.
/// </summary>
public static class StlWriter
{
    private const int BinaryHeaderSize = 80;

    public static void Write(string path, Mesh mesh, bool binary = true)
    {
        using var stream = File.Create(path);
        if (binary)
        {
            WriteBinary(stream, mesh);
        }
        else
        {
            using var writer = new StreamWriter(stream, new UTF8Encoding(false));
            WriteAscii(writer, mesh, Path.GetFileNameWithoutExtension(path));
        }
    }

    public static void WriteBinary(Stream stream, Mesh mesh)
    {
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);

        var header = new byte[BinaryHeaderSize];
        var tag = Encoding.ASCII.GetBytes("Binary STL exported by 3DFastCraft (millimetres)");
        Array.Copy(tag, header, Math.Min(tag.Length, BinaryHeaderSize));
        writer.Write(header);

        int triangles = mesh.TriangleCount;
        writer.Write((uint)triangles);

        for (int t = 0; t < triangles; t++)
        {
            Vector3 a = mesh.Positions[mesh.Indices[t * 3]];
            Vector3 b = mesh.Positions[mesh.Indices[t * 3 + 1]];
            Vector3 c = mesh.Positions[mesh.Indices[t * 3 + 2]];
            Vector3 normal = FaceNormal(a, b, c);

            WriteVector(writer, normal);
            WriteVector(writer, a);
            WriteVector(writer, b);
            WriteVector(writer, c);
            writer.Write((ushort)0); // attribute byte count, unused
        }

        static void WriteVector(BinaryWriter w, Vector3 v)
        {
            w.Write(v.X);
            w.Write(v.Y);
            w.Write(v.Z);
        }
    }

    public static void WriteAscii(TextWriter writer, Mesh mesh, string name)
    {
        var ci = CultureInfo.InvariantCulture;
        writer.WriteLine($"solid {Sanitise(name)}");

        for (int t = 0; t < mesh.TriangleCount; t++)
        {
            Vector3 a = mesh.Positions[mesh.Indices[t * 3]];
            Vector3 b = mesh.Positions[mesh.Indices[t * 3 + 1]];
            Vector3 c = mesh.Positions[mesh.Indices[t * 3 + 2]];
            Vector3 n = FaceNormal(a, b, c);

            writer.WriteLine(string.Format(ci, "  facet normal {0:G9} {1:G9} {2:G9}", n.X, n.Y, n.Z));
            writer.WriteLine("    outer loop");
            foreach (var v in new[] { a, b, c })
                writer.WriteLine(string.Format(ci, "      vertex {0:G9} {1:G9} {2:G9}", v.X, v.Y, v.Z));
            writer.WriteLine("    endloop");
            writer.WriteLine("  endfacet");
        }

        writer.WriteLine($"endsolid {Sanitise(name)}");

        static string Sanitise(string s) => string.IsNullOrWhiteSpace(s) ? "model" : s.Replace('\n', ' ').Replace('\r', ' ');
    }

    internal static Vector3 FaceNormal(Vector3 a, Vector3 b, Vector3 c)
    {
        Vector3 n = Vector3.Cross(b - a, c - a);
        float length = n.Length();
        return length > 1e-20f ? n / length : Vector3.Zero;
    }
}

public static class StlReader
{
    public static Mesh Read(string path)
    {
        using var stream = File.OpenRead(path);
        return Read(stream, stream.Length);
    }

    public static Mesh Read(Stream stream, long length)
    {
        // "solid" is not a reliable ASCII marker - plenty of binary exporters write it into
        // the 80-byte header. The size arithmetic is definitive: a binary file is exactly
        // 84 bytes of preamble plus 50 bytes per triangle.
        bool binary = false;
        if (length >= 84)
        {
            var preamble = new byte[84];
            ReadExactly(stream, preamble, 84);
            uint declared = BitConverter.ToUInt32(preamble, 80);
            binary = 84L + 50L * declared == length;
            stream.Seek(0, SeekOrigin.Begin);
        }

        return binary ? ReadBinary(stream) : ReadAscii(stream);
    }

    private static Mesh ReadBinary(Stream stream)
    {
        using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);
        reader.ReadBytes(80);
        uint count = reader.ReadUInt32();

        var mesh = new Mesh();
        for (uint i = 0; i < count; i++)
        {
            reader.ReadSingle();
            reader.ReadSingle();
            reader.ReadSingle(); // stored normal, deliberately ignored
            var a = ReadVector(reader);
            var b = ReadVector(reader);
            var c = ReadVector(reader);
            reader.ReadUInt16();
            mesh.AddTriangle(a, b, c);
        }

        // Binary STL stores three unshared vertices per triangle, so welding is mandatory
        // before the result can be used for CSG or checked for watertightness.
        return mesh.Welded();

        static Vector3 ReadVector(BinaryReader r) => new(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
    }

    private static Mesh ReadAscii(Stream stream)
    {
        var mesh = new Mesh();
        var corners = new List<Vector3>(3);
        using var reader = new StreamReader(stream, Encoding.UTF8, true, 1 << 16, leaveOpen: true);

        while (reader.ReadLine() is { } line)
        {
            var span = line.AsSpan().Trim();
            if (!span.StartsWith("vertex", StringComparison.OrdinalIgnoreCase)) continue;

            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 4) continue;

            if (TryParse(parts[1], out float x) && TryParse(parts[2], out float y) && TryParse(parts[3], out float z))
            {
                corners.Add(new Vector3(x, y, z));
                if (corners.Count == 3)
                {
                    mesh.AddTriangle(corners[0], corners[1], corners[2]);
                    corners.Clear();
                }
            }
        }

        return mesh.Welded();

        static bool TryParse(string s, out float value) =>
            float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private static void ReadExactly(Stream stream, byte[] buffer, int count)
    {
        int read = 0;
        while (read < count)
        {
            int n = stream.Read(buffer, read, count - read);
            if (n <= 0) throw new EndOfStreamException("STL file ended unexpectedly.");
            read += n;
        }
    }
}
