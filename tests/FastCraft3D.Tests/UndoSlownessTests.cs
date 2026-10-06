using System.Numerics;
using FastCraft3D.Geometry;
using FastCraft3D.Model;
using FastCraft3D.Model.Commands;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>
/// What the next undo or redo will carry, which is how the window knows to say it is working
/// before one that takes a while.
/// </summary>
public class UndoSlownessTests
{
    private static (Scene Scene, UndoStack Undo) Fresh()
    {
        var scene = new Scene();
        return (scene, new UndoStack(scene));
    }

    [Fact]
    public void AMoveCarriesNoGeometrySoUndoingItIsQuick()
    {
        var (scene, undo) = Fresh();
        var part = new SceneObject("Part", Primitives.Box(10, 10, 10));
        undo.Execute(new AddObjectsCommand("Add", [part]));

        var before = new[] { TransformState.Capture(part) };
        part.Position = new Vector3(30, 0, 0);
        undo.Execute(TransformCommand.CreateIfChanged("Move", [part], before)!);

        Assert.Equal(0, undo.NextUndoBytes);
    }

    [Fact]
    public void UndoingAnAdditionOfADensePartCarriesItsGeometryAndRedoingItDoesToo()
    {
        var (scene, undo) = Fresh();
        var dense = new SceneObject("Dense", Primitives.Sphere(50, 300, 200));
        undo.Execute(new AddObjectsCommand("Add", [dense]));

        long carried = undo.NextUndoBytes;
        Assert.True(carried > 1_000_000, $"{carried} bytes for a part of {dense.Mesh.TriangleCount} triangles");

        undo.Undo();

        Assert.Equal(0, undo.NextUndoBytes);
        Assert.Equal(carried, undo.NextRedoBytes);
    }
}
