using System.Numerics;
using FastCraft3D.Geometry;
using FastCraft3D.Io;
using FastCraft3D.Model;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>
/// What actually lands in an exported file. Getting the scope wrong is expensive: a model that
/// quietly lost half its parts is only discovered in the slicer, or on the printer.
/// </summary>
public class ExportTests
{
    private static Scene SceneWithThree()
    {
        var scene = new Scene();
        scene.Objects.Add(new SceneObject("A", Primitives.Box(20, 20, 20)) { Position = new Vector3(0, 0, 10) });
        scene.Objects.Add(new SceneObject("B", Primitives.Box(10, 10, 10)) { Position = new Vector3(40, 0, 5) });
        scene.Objects.Add(new SceneObject("C", Primitives.Box(10, 10, 10)) { Position = new Vector3(80, 0, 5) });
        return scene;
    }

    private static ExportOptions Options(bool selectedOnly = false, bool dropToPlate = false) =>
        new(ExportFormat.BinaryStl, selectedOnly, dropToPlate);

    /// <summary>The default, and the point of the whole dialog.</summary>
    [Fact]
    public void ExportingEverythingIgnoresWhatHappensToBeSelected()
    {
        var scene = SceneWithThree();
        scene.Objects[1].IsSelected = true;

        var subjects = ExportComposer.Subjects(scene, Options());

        Assert.Equal(3, subjects.Count);
    }

    [Fact]
    public void AskingForTheSelectionExportsOnlyThat()
    {
        var scene = SceneWithThree();
        scene.Objects[0].IsSelected = true;
        scene.Objects[2].IsSelected = true;

        var subjects = ExportComposer.Subjects(scene, Options(selectedOnly: true));

        Assert.Equal(2, subjects.Count);
        Assert.Contains(scene.Objects[0], subjects);
        Assert.Contains(scene.Objects[2], subjects);
    }

    /// <summary>Asking for a selection that does not exist must not write an empty file.</summary>
    [Fact]
    public void AskingForTheSelectionWithNothingSelectedFallsBackToEverything()
    {
        var scene = SceneWithThree();

        var subjects = ExportComposer.Subjects(scene, Options(selectedOnly: true));

        Assert.Equal(3, subjects.Count);
    }

    [Fact]
    public void EveryObjectSurvivesTheMergeIntoOneStlMesh()
    {
        var scene = SceneWithThree();

        var merged = ExportComposer.MergeForStl(scene.Objects);

        Assert.Equal(scene.Objects.Sum(o => o.Mesh.TriangleCount), merged.TriangleCount);
        // Spanning from the first object to the last means none were dropped.
        Assert.Equal(-10f, merged.ComputeBounds().Min.X, 3);
        Assert.Equal(85f, merged.ComputeBounds().Max.X, 3);
    }

    [Fact]
    public void DropToPlateRestsTheWholeExportOnZeroWithoutFlatteningIt()
    {
        var scene = new Scene();
        scene.Objects.Add(new SceneObject("Low", Primitives.Box(20, 20, 20)) { Position = new Vector3(0, 0, 40) });
        scene.Objects.Add(new SceneObject("High", Primitives.Box(20, 20, 20)) { Position = new Vector3(40, 0, 70) });

        var merged = ExportComposer.MergeForStl(scene.Objects, dropToPlate: true);
        var bounds = merged.ComputeBounds();

        // The cubes span Z 30-50 and 60-80, so together they are 50 mm tall with a 10 mm gap.
        // Dropping must preserve that: they move as one, not each onto the plate separately.
        Assert.Equal(0f, bounds.Min.Z, 3);
        Assert.Equal(50f, bounds.Max.Z, 3);
    }

    [Fact]
    public void DropToPlateIsOptional()
    {
        var scene = new Scene();
        scene.Objects.Add(new SceneObject("Floating", Primitives.Box(20, 20, 20)) { Position = new Vector3(0, 0, 40) });

        var asIs = ExportComposer.MergeForStl(scene.Objects);

        Assert.Equal(30f, asIs.ComputeBounds().Min.Z, 3);
    }

    /// <summary>OBJ keeps the parts separate, so the shared lift has to be applied to each.</summary>
    [Fact]
    public void DroppingAnObjExportKeepsPartsInTheirRelativePlaces()
    {
        var scene = new Scene();
        scene.Objects.Add(new SceneObject("Low", Primitives.Box(20, 20, 20)) { Position = new Vector3(0, 0, 40) });
        scene.Objects.Add(new SceneObject("High", Primitives.Box(20, 20, 20)) { Position = new Vector3(40, 0, 70) });

        var parts = ExportComposer.ComposeForObj(scene.Objects, dropToPlate: true);

        Assert.Equal(2, parts.Count);
        Assert.Equal(0f, parts[0].Mesh.ComputeBounds().Min.Z, 3);
        Assert.Equal(30f, parts[1].Mesh.ComputeBounds().Min.Z, 3);
    }

    [Fact]
    public void ObjExportNamesEveryObject()
    {
        var scene = SceneWithThree();

        var parts = ExportComposer.ComposeForObj(scene.Objects);

        Assert.Equal(["A", "B", "C"], parts.Select(p => p.Name));
    }

    [Fact]
    public void OptionsCarryTheRightExtensionAndFilter()
    {
        Assert.Equal(".stl", new ExportOptions(ExportFormat.BinaryStl, false, false).Extension);
        Assert.Equal(".stl", new ExportOptions(ExportFormat.AsciiStl, false, false).Extension);
        Assert.Equal(".obj", new ExportOptions(ExportFormat.Obj, false, false).Extension);
        Assert.True(new ExportOptions(ExportFormat.Obj, false, false).IsObj);
        Assert.False(new ExportOptions(ExportFormat.AsciiStl, false, false).IsObj);
    }
}
