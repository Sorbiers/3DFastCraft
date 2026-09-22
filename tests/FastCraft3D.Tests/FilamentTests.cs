using System.IO;
using System.IO.Compression;
using System.Numerics;
using System.Xml.Linq;
using FastCraft3D.Geometry;
using FastCraft3D.Geometry.Engraving;
using FastCraft3D.Io;
using FastCraft3D.Model;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>
/// Printing part of a model in another filament: the lettering has to come out as a part of its
/// own, and the part has to say which spool it wants all the way through to the 3MF.
/// </summary>
public class FilamentTests : IDisposable
{
    private readonly List<string> written = [];

    public void Dispose()
    {
        foreach (string path in written)
        {
            try { File.Delete(path); } catch { }
        }

        GC.SuppressFinalize(this);
    }

    private string TempFile(string extension)
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + extension);
        written.Add(path);
        return path;
    }

    private static TextShape Square(float size) => new(
        [new Vector2(-size / 2, -size / 2), new Vector2(size / 2, -size / 2),
         new Vector2(size / 2, size / 2), new Vector2(-size / 2, size / 2)], []);

    private static (Mesh Plate, PlanarSurface Face) Plate()
    {
        var plate = Primitives.Box(60, 60, 10);
        return (plate, new PlanarSurface(FacePatch.Find(plate, new Vector3(0, 0, 5), Vector3.UnitZ)!));
    }

    // --- The lettering as its own part ---------------------------------------------------

    [Fact]
    public void CutLetteringKeptApartGivesTheRecessAndThePlugThatFillsIt()
    {
        var (plate, face) = Plate();

        var made = TextCutter.Separate(plate, [Square(20)], face, raised: false, depthMm: 1.5f);

        Assert.NotNull(made);
        Assert.True(made.Value.Body.CheckHealth().IsWatertight);
        Assert.True(made.Value.Lettering.CheckHealth().IsWatertight);

        // The recess is 20 x 20 x 1.5 out of the plate, and the plug is the same volume back -
        // give or take the hair it stands proud, which is the clearance the cutter was stood off
        // the face by.
        double gone = plate.ComputeSignedVolume() - made.Value.Body.ComputeSignedVolume();
        Assert.Equal(20 * 20 * 1.5, gone, 1);
        Assert.InRange(made.Value.Lettering.ComputeSignedVolume(), gone, gone + 20 * 20 * 0.05);

        // The plug's floor is the recess's floor: they mate, rather than nearly mating.
        Assert.Equal(3.5f, made.Value.Lettering.ComputeBounds().Min.Z, 3);
    }

    [Fact]
    public void RaisedLetteringKeptApartLeavesTheObjectAlone()
    {
        var (plate, face) = Plate();

        var made = TextCutter.Separate(plate, [Square(20)], face, raised: true, depthMm: 2f);

        Assert.NotNull(made);
        Assert.True(made.Value.Lettering.CheckHealth().IsWatertight);

        // Nothing was cut, so the body is the very mesh that went in.
        Assert.Equal(plate.ComputeSignedVolume(), made.Value.Body.ComputeSignedVolume(), 3);
        Assert.Equal(7f, made.Value.Lettering.ComputeBounds().Max.Z, 3);

        // It dips into the face rather than balancing on it, or the two would meet on a shared
        // plane with nothing holding them together.
        Assert.True(made.Value.Lettering.ComputeBounds().Min.Z < 5f, "the letters should reach into the face");
    }

    [Fact]
    public void LetteringKeptApartIsTheSameShapeAsLetteringBuiltIn()
    {
        var (plate, face) = Plate();

        var together = TextCutter.Apply(plate, [Square(20)], face, raised: false, depthMm: 1.5f)!;
        var apart = TextCutter.Separate(plate, [Square(20)], face, raised: false, depthMm: 1.5f)!.Value;

        Assert.Equal(together.ComputeSignedVolume(), apart.Body.ComputeSignedVolume(), 1);
    }

    // --- Which filament, all the way through ---------------------------------------------

    [Fact]
    public void AnObjectRemembersWhichFilamentPrintsIt()
    {
        var o = new SceneObject("Letters", Primitives.Box(10, 10, 10));

        Assert.Equal(1, o.Filament);

        o.Filament = 3;
        Assert.Equal(3, o.Clone().Filament);

        // Held to what a machine could have.
        o.Filament = 99;
        Assert.Equal(SceneObject.MostFilaments, o.Filament);

        o.Filament = 0;
        Assert.Equal(1, o.Filament);
    }

    [Fact]
    public void TheFilamentSurvivesTheProjectFile()
    {
        var scene = new Scene();
        scene.Objects.Add(new SceneObject("Body", Primitives.Box(20, 20, 4)));
        scene.Objects.Add(new SceneObject("Letters", Primitives.Box(8, 3, 1)) { Filament = 4 });

        string path = TempFile(SceneSerializer.Extension);
        SceneSerializer.Save(path, scene);

        var loaded = SceneSerializer.Load(path);

        Assert.Equal(1, loaded[0].Filament);
        Assert.Equal(4, loaded[1].Filament);
    }

    [Fact]
    public void AFileFromBeforeFilamentsReadsAsTheFirstOne()
    {
        var scene = new Scene();
        scene.Objects.Add(new SceneObject("Body", Primitives.Box(20, 20, 4)));

        string path = TempFile(SceneSerializer.Extension);
        SceneSerializer.Save(path, scene);

        // Nothing is written for the first filament, so an old file and a new one are the same
        // file - which is the point of leaving it out.
        Assert.DoesNotContain("Filament", Unzipped(path));
        Assert.Equal(1, SceneSerializer.Load(path)[0].Filament);
    }

    // --- What the slicer is handed -------------------------------------------------------

    private static string Unzipped(string path)
    {
        using var file = File.OpenRead(path);
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip);
        return reader.ReadToEnd();
    }

    private static string Entry(string path, string name)
    {
        using var zip = ZipFile.OpenRead(path);
        var entry = zip.GetEntry(name);
        if (entry is null) return string.Empty;

        using var reader = new StreamReader(entry.Open());
        return reader.ReadToEnd();
    }

    [Fact]
    public void AThreeMfSaysWhichFilamentEachPartWants()
    {
        string path = TempFile(".3mf");
        ThreeMf.Write(path, [
            new ObjObject("Body", Primitives.Box(20, 20, 4), new Vector3(0.2f, 0.2f, 0.2f)),
            new ObjObject("Letters", Primitives.Box(8, 3, 1), new Vector3(0.9f, 0.9f, 0.9f), 2)
        ]);

        var config = XDocument.Parse(Entry(path, "Metadata/Slic3r_PE_model.config"));
        var extruders = config.Descendants("metadata")
            .Where(m => (string?)m.Attribute("key") == "extruder")
            .Select(m => (string?)m.Attribute("value"))
            .ToList();

        // Object and part for each: a slicer reads the filament off the part it prints.
        Assert.Equal(["1", "1", "2", "2"], extruders);

        // And on the object in the model itself, for a reader that looks there instead.
        Assert.Contains("extruder", Entry(path, "3D/3dmodel.model"));
    }

    [Fact]
    public void AllOnOneFilamentWritesTheFileItAlwaysWrote()
    {
        string path = TempFile(".3mf");
        ThreeMf.Write(path, [new ObjObject("Body", Primitives.Box(20, 20, 4), Vector3.One)]);

        Assert.Equal(string.Empty, Entry(path, "Metadata/Slic3r_PE_model.config"));
        Assert.DoesNotContain("extruder", Entry(path, "3D/3dmodel.model"));
        Assert.DoesNotContain("config", Entry(path, "[Content_Types].xml"));
    }

    [Fact]
    public void AThreeMfWithFilamentsStillReadsBackAsTheSameParts()
    {
        string path = TempFile(".3mf");
        ThreeMf.Write(path, [
            new ObjObject("Body", Primitives.Box(20, 20, 4), new Vector3(0.2f, 0.2f, 0.2f)),
            new ObjObject("Letters", Primitives.Box(8, 3, 1), new Vector3(0.9f, 0.9f, 0.9f), 2)
        ]);

        var read = ThreeMf.Read(path);

        Assert.Equal(2, read.Count);
        Assert.Equal("Letters", read[1].Name);
        Assert.Equal(8f, read[1].Mesh.ComputeBounds().Size.X, 3);
    }

    [Fact]
    public void ExportCarriesTheFilamentOffTheObject()
    {
        var scene = new Scene();
        scene.Objects.Add(new SceneObject("Body", Primitives.Box(20, 20, 4)));
        scene.Objects.Add(new SceneObject("Letters", Primitives.Box(8, 3, 1)) { Filament = 5 });

        var parts = ExportComposer.ComposeForObj(scene.Objects);

        Assert.Equal(1, parts[0].Filament);
        Assert.Equal(5, parts[1].Filament);
    }
}
