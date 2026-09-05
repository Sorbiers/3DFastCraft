using System.IO;
using System.Numerics;
using System.Text;
using FastCraft3D.Geometry;
using FastCraft3D.Io;
using Xunit;

namespace FastCraft3D.Tests;

public class IoTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "3dfastcraft-tests-" + Guid.NewGuid().ToString("N"));

    public IoTests() => Directory.CreateDirectory(directory);

    public void Dispose()
    {
        try { Directory.Delete(directory, recursive: true); } catch { /* best effort */ }
        GC.SuppressFinalize(this);
    }

    private string Path_(string name) => Path.Combine(directory, name);

    [Fact]
    public void BinaryStlRoundTripsGeometry()
    {
        var original = Primitives.Create(PrimitiveKind.Cylinder);
        string file = Path_("round.stl");

        StlWriter.Write(file, original, binary: true);
        var reloaded = StlReader.Read(file);

        Assert.Equal(original.TriangleCount, reloaded.TriangleCount);
        Assert.Equal(original.ComputeSignedVolume(), reloaded.ComputeSignedVolume(), 2);
        Assert.True(reloaded.CheckHealth().IsWatertight, reloaded.CheckHealth().Describe());
    }

    [Fact]
    public void BinaryStlHasTheExactFileSizeTheFormatMandates()
    {
        var mesh = Primitives.Create(PrimitiveKind.Cube);
        string file = Path_("size.stl");

        StlWriter.Write(file, mesh, binary: true);

        // 80-byte header + 4-byte count + 50 bytes per triangle.
        Assert.Equal(84 + 50 * mesh.TriangleCount, new FileInfo(file).Length);
    }

    [Fact]
    public void AsciiStlRoundTripsGeometry()
    {
        var original = Primitives.Create(PrimitiveKind.Pyramid);
        string file = Path_("round-ascii.stl");

        StlWriter.Write(file, original, binary: false);
        var reloaded = StlReader.Read(file);

        Assert.Equal(original.TriangleCount, reloaded.TriangleCount);
        Assert.Equal(original.ComputeSignedVolume(), reloaded.ComputeSignedVolume(), 2);
    }

    /// <summary>
    /// Plenty of binary exporters write the word "solid" into the 80-byte header, so sniffing
    /// on that prefix misreads them as ASCII. Detection must rely on the size arithmetic.
    /// </summary>
    [Fact]
    public void BinaryStlBeginningWithSolidIsNotMistakenForAscii()
    {
        var mesh = Primitives.Create(PrimitiveKind.Tetrahedron);
        string file = Path_("tricky.stl");

        using (var stream = File.Create(file))
        {
            StlWriter.WriteBinary(stream, mesh);
        }

        // Overwrite the header so the file starts with "solid ".
        using (var stream = new FileStream(file, FileMode.Open, FileAccess.Write))
        {
            stream.Write(Encoding.ASCII.GetBytes("solid tricky-header"));
        }

        var reloaded = StlReader.Read(file);

        Assert.Equal(mesh.TriangleCount, reloaded.TriangleCount);
    }

    [Fact]
    public void ExportedNormalsFollowTheWindingNotTheStoredValue()
    {
        var mesh = Primitives.Create(PrimitiveKind.Cube);
        string file = Path_("normals.stl");
        StlWriter.Write(file, mesh, binary: true);

        using var reader = new BinaryReader(File.OpenRead(file));
        reader.ReadBytes(80);
        uint count = reader.ReadUInt32();

        for (uint i = 0; i < count; i++)
        {
            var n = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            var a = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            var b = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            var c = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            reader.ReadUInt16();

            Vector3 expected = Vector3.Normalize(Vector3.Cross(b - a, c - a));
            Assert.True(Vector3.Dot(expected, n) > 0.999f, $"facet {i} normal disagrees with its winding");
        }
    }

    /// <summary>OBJ indices are 1-based and global to the file, not per object.</summary>
    [Fact]
    public void ObjRoundTripsMultipleObjectsWithCorrectIndexOffsets()
    {
        var first = Primitives.Create(PrimitiveKind.Cube);
        var second = MeshTransform.Transformed(
            Primitives.Create(PrimitiveKind.Sphere), Matrix4x4.CreateTranslation(50, 0, 0));
        string file = Path_("scene.obj");

        ObjWriter.Write(file, [
            new ObjObject("block", first, new Vector3(0.8f, 0.2f, 0.2f)),
            new ObjObject("ball", second, new Vector3(0.2f, 0.4f, 0.9f))
        ]);

        var reloaded = ObjReader.Read(file);

        Assert.Equal(2, reloaded.Count);
        Assert.Equal("block", reloaded[0].Name);
        Assert.Equal("ball", reloaded[1].Name);
        Assert.Equal(first.TriangleCount, reloaded[0].Mesh.TriangleCount);
        Assert.Equal(second.TriangleCount, reloaded[1].Mesh.TriangleCount);

        // The second object must land where it was written, which only holds if the running
        // index offset was applied correctly.
        var bounds = reloaded[1].Mesh.ComputeBounds();
        Assert.Equal(50f, bounds.Center.X, 3);
    }

    [Fact]
    public void ObjMaterialSidecarIsWritten()
    {
        var mesh = Primitives.Create(PrimitiveKind.Cone);
        string file = Path_("mat.obj");

        ObjWriter.Write(file, [new ObjObject("cone", mesh, new Vector3(1, 0.5f, 0))]);

        Assert.True(File.Exists(Path_("mat.mtl")));
        Assert.Contains("Kd 1 0.5 0", File.ReadAllText(Path_("mat.mtl")));
    }

    [Fact]
    public void ImportedStlIsUsableForBooleans()
    {
        // The real reason welding on import matters: unwelded triangle soup breaks CSG.
        var block = Primitives.Box(20, 20, 20);
        string file = Path_("block.stl");
        StlWriter.Write(file, block, binary: true);

        var imported = StlReader.Read(file);
        var result = FastCraft3D.Geometry.Csg.CsgSolid.Subtract(imported, Primitives.Prism(5, 40, 32));

        Assert.True(result.CheckHealth().IsWatertight, result.CheckHealth().Describe());
    }
}
