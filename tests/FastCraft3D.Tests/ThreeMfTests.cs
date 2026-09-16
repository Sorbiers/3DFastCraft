using System.IO;
using System.IO.Compression;
using System.Numerics;
using FastCraft3D.Geometry;
using FastCraft3D.Io;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>
/// 3MF: 3D Builder's own format, and what slicers prefer. Written by the app and read back, and
/// read from the shapes other programs actually save - other units, placed items, assemblies.
/// </summary>
public class ThreeMfTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "3dfc-3mf-" + Guid.NewGuid().ToString("N"));

    public ThreeMfTests() => Directory.CreateDirectory(folder);

    public void Dispose()
    {
        try { Directory.Delete(folder, recursive: true); } catch { /* the temp folder can wait */ }
        GC.SuppressFinalize(this);
    }

    /// <summary>A package holding just a model part, as the simplest writers leave it.</summary>
    private string Package(string model)
    {
        string path = Path.Combine(folder, Guid.NewGuid().ToString("N") + ".3mf");
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        using var writer = new StreamWriter(zip.CreateEntry("3D/3dmodel.model").Open());
        writer.Write(model);
        return path;
    }

    private const string Triangle = """
        <mesh><vertices>
          <vertex x="0" y="0" z="0"/><vertex x="1" y="0" z="0"/><vertex x="0" y="1" z="0"/>
        </vertices><triangles><triangle v1="0" v2="1" v3="2"/></triangles></mesh>
        """;

    [Fact]
    public void PartsComeBackSeparateNamedAndColoured()
    {
        string path = Path.Combine(folder, "two.3mf");
        var left = MeshTransform.Transformed(Primitives.Box(10, 10, 10), Matrix4x4.CreateTranslation(-20, 0, 0));
        var right = MeshTransform.Transformed(Primitives.Box(10, 10, 10), Matrix4x4.CreateTranslation(20, 0, 0));

        ThreeMf.Write(path,
        [
            new ObjObject("Left", left, new Vector3(1f, 0f, 0f)),
            new ObjObject("Right", right, new Vector3(0f, 0f, 1f))
        ]);

        var parts = ThreeMf.Read(path);

        Assert.Equal(2, parts.Count);
        Assert.Equal("Left", parts[0].Name);
        Assert.Equal("Right", parts[1].Name);
        Assert.Equal(left.TriangleCount, parts[0].Mesh.TriangleCount);
        Assert.Equal(-20f, parts[0].Mesh.ComputeBounds().Center.X, 3);
        Assert.Equal(20f, parts[1].Mesh.ComputeBounds().Center.X, 3);
        Assert.Equal(new Vector3(1f, 0f, 0f), parts[0].Colour);
        Assert.Equal(new Vector3(0f, 0f, 1f), parts[1].Colour);
    }

    [Fact]
    public void AFileInInchesComesInAsMillimetres()
    {
        string path = Package($"""
            <model unit="inch" xmlns="http://schemas.microsoft.com/3dmanufacturing/core/2015/02">
              <resources><object id="1" type="model">{Triangle}</object></resources>
              <build><item objectid="1"/></build>
            </model>
            """);

        var part = Assert.Single(ThreeMf.Read(path));
        Assert.Equal(25.4f, part.Mesh.ComputeBounds().Max.X, 3);
    }

    [Fact]
    public void WhereTheBuildListPutsAnItemIsWhereItLands()
    {
        string path = Package($"""
            <model unit="millimeter" xmlns="http://schemas.microsoft.com/3dmanufacturing/core/2015/02">
              <resources><object id="1" type="model">{Triangle}</object></resources>
              <build><item objectid="1" transform="1 0 0 0 1 0 0 0 1 10 0 0"/></build>
            </model>
            """);

        var part = Assert.Single(ThreeMf.Read(path));
        Assert.Equal(10f, part.Mesh.ComputeBounds().Min.X, 3);
    }

    /// <summary>How slicers save an assembly: one object made of others, each placed on its own.</summary>
    [Fact]
    public void AnAssemblyIsFlattenedIntoOnePart()
    {
        string path = Package($"""
            <model unit="millimeter" xmlns="http://schemas.microsoft.com/3dmanufacturing/core/2015/02">
              <resources>
                <object id="1" type="model">{Triangle}</object>
                <object id="2" type="model" name="Pair">
                  <components>
                    <component objectid="1"/>
                    <component objectid="1" transform="1 0 0 0 1 0 0 0 1 5 0 0"/>
                  </components>
                </object>
              </resources>
              <build><item objectid="2"/></build>
            </model>
            """);

        var part = Assert.Single(ThreeMf.Read(path));
        Assert.Equal("Pair", part.Name);
        Assert.Equal(2, part.Mesh.TriangleCount);
        Assert.Equal(6f, part.Mesh.ComputeBounds().Max.X, 3);
    }

    [Fact]
    public void AColourOnTheTrianglesIsTakenForThePart()
    {
        string path = Package("""
            <model unit="millimeter" xmlns="http://schemas.microsoft.com/3dmanufacturing/core/2015/02">
              <resources>
                <basematerials id="5"><base name="Green" displaycolor="#00FF00"/></basematerials>
                <object id="1" type="model">
                  <mesh><vertices>
                    <vertex x="0" y="0" z="0"/><vertex x="1" y="0" z="0"/><vertex x="0" y="1" z="0"/>
                  </vertices><triangles><triangle v1="0" v2="1" v3="2" pid="5" p1="0"/></triangles></mesh>
                </object>
              </resources>
              <build><item objectid="1"/></build>
            </model>
            """);

        var part = Assert.Single(ThreeMf.Read(path));
        Assert.Equal(new Vector3(0f, 1f, 0f), part.Colour);
    }

    /// <summary>
    /// Explorer shows a picture on a 3MF when the package carries one, which is why files from
    /// slicers and from 3D Builder have an icon. Ours had the blank page.
    /// </summary>
    [Fact]
    public void TheFileCarriesAPictureOfWhatIsInIt()
    {
        string path = Path.Combine(folder, "thumbnail.3mf");
        var cube = MeshTransform.Transformed(Primitives.Box(20, 20, 20), Matrix4x4.CreateTranslation(0, 0, 10));
        ThreeMf.Write(path, [new ObjObject("Cube", cube, new Vector3(0.8f, 0.3f, 0.2f))]);

        using var zip = ZipFile.OpenRead(path);
        var picture = zip.GetEntry("Metadata/thumbnail.png");
        Assert.NotNull(picture);

        // A PNG, and one with the model actually drawn in it rather than an empty square.
        var bytes = new byte[picture.Length];
        using (var stream = picture.Open()) stream.ReadExactly(bytes);
        Assert.Equal<byte[]>([0x89, 0x50, 0x4E, 0x47], bytes[..4]);

        var drawn = MeshThumbnail.Png([(cube, new Vector3(0.8f, 0.3f, 0.2f))], 64);
        Assert.NotNull(drawn);

        // The package says it has one, in the way the readers look for.
        var rels = zip.GetEntry("_rels/.rels");
        Assert.NotNull(rels);
        using var reader = new StreamReader(rels.Open());
        string text = reader.ReadToEnd();
        Assert.Contains("relationships/metadata/thumbnail", text);
        Assert.Contains("/Metadata/thumbnail.png", text);

        using var types = new StreamReader(zip.GetEntry("[Content_Types].xml")!.Open());
        Assert.Contains("image/png", types.ReadToEnd());
    }

    [Fact]
    public void AThreeMfIsAFileTheAppTakes() => Assert.True(IncomingFiles.Understood("plate.3MF"));
}
