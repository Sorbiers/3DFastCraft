using FastCraft3D.Geometry;
using FastCraft3D.Model;
using FastCraft3D.Model.Commands;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>
/// What the history is allowed to hold on to.
///
/// It held everything. A mould of a scan is two parts of about twenty-six megabytes, so a long
/// session of heavy work only ever grew - nothing was ever given back. A budget rather than a step
/// count, because the steps are not the same size: this keeps thousands of ordinary edits or about
/// twenty moulds, and works out which on its own.
/// </summary>
public class UndoBudgetTests
{
    /// <summary>A step holding roughly the megabytes asked for.</summary>
    private static IUndoableCommand Heavy(int megabytes)
    {
        // 16 bytes a vertex once the indices are counted, so a million of them is about 16 MB.
        int vertices = megabytes * 1024 * 1024 / 16;

        var mesh = new Mesh();
        for (int i = 0; i < vertices; i += 3)
            mesh.AddTriangle(default, default, default);

        return new AddObjectsCommand("heavy", [new SceneObject("part", mesh)]);
    }

    private static IUndoableCommand Light() =>
        new TransformCommand("move", [], [], []);

    [Fact]
    public void ANumbersOnlyStepWeighsNothing() => Assert.Equal(0, Light().Bytes);

    [Fact]
    public void AStepHoldingGeometrySaysSo()
    {
        long bytes = Heavy(64).Bytes;

        Assert.InRange(bytes, 50L * 1024 * 1024, 80L * 1024 * 1024);
    }

    /// <summary>Small edits are not what the budget is for, so it never gets in their way.</summary>
    [Fact]
    public void OrdinaryEditsAreAllKept()
    {
        var undo = new UndoStack(new Scene());
        for (int i = 0; i < 500; i++) undo.Execute(Light());

        Assert.True(undo.CanUndo);
        Assert.Equal(0, undo.Held);
    }

    [Fact]
    public void HeavyStepsAreLetGoOfOnceTheBudgetIsSpent()
    {
        var undo = new UndoStack(new Scene());
        bool warned = false;
        undo.Trimmed += () => warned = true;

        // Well past a gigabyte.
        for (int i = 0; i < 12; i++) undo.Execute(Heavy(128));

        Assert.True(warned, "nothing said that history had been let go of");
        Assert.True(undo.Held <= UndoStack.Budget,
            $"the history is holding {undo.Held / 1048576} MB");
    }

    /// <summary>
    /// However big it is. Being unable to take back the operation you are looking at is worse
    /// than any amount of memory, so the last step is never the one that goes.
    /// </summary>
    [Fact]
    public void TheLastStepIsAlwaysUndoable()
    {
        var undo = new UndoStack(new Scene());
        undo.Execute(Heavy(2048));

        Assert.True(undo.CanUndo);
        Assert.Equal("heavy", undo.NextUndoLabel);
    }

    /// <summary>Undo and redo still walk the same steps in the same order.</summary>
    [Fact]
    public void UndoAndRedoStillGoBothWays()
    {
        var scene = new Scene();
        var undo = new UndoStack(scene);

        undo.Execute(new AddObjectsCommand("one", [new SceneObject("a", Primitives.Box(1, 1, 1))]));
        undo.Execute(new AddObjectsCommand("two", [new SceneObject("b", Primitives.Box(1, 1, 1))]));

        Assert.Equal("two", undo.NextUndoLabel);
        Assert.Equal(2, scene.Objects.Count);

        undo.Undo();
        Assert.Equal(1, scene.Objects.Count);
        Assert.Equal("one", undo.NextUndoLabel);
        Assert.Equal("two", undo.NextRedoLabel);

        undo.Redo();
        Assert.Equal(2, scene.Objects.Count);
        Assert.Equal("two", undo.NextUndoLabel);
    }

    /// <summary>Doing something new drops the redo branch, and its memory with it.</summary>
    [Fact]
    public void WhatCannotBeRedoneIsNotStillHeld()
    {
        var undo = new UndoStack(new Scene());

        undo.Execute(Heavy(64));
        undo.Undo();

        undo.Execute(Light());

        Assert.False(undo.CanRedo);
        Assert.Equal(0, undo.Held);
    }

    [Fact]
    public void ClearingLetsGoOfEverything()
    {
        var undo = new UndoStack(new Scene());
        undo.Execute(Heavy(64));

        undo.Clear();

        Assert.False(undo.CanUndo);
        Assert.Equal(0, undo.Held);
    }
}
