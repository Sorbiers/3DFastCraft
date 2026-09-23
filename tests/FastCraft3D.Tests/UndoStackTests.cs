using FastCraft3D.Geometry;
using FastCraft3D.Model;
using FastCraft3D.Model.Commands;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>
/// The hooks Export session and Record are built on: a read-only view of the steps taken, and
/// the moments just before and just after a new one commits.
/// </summary>
public class UndoStackTests
{
    private static IUndoableCommand Light(string label) => new TransformCommand(label, [], [], []);

    [Fact]
    public void HistoryListsEveryStepDoneSoFarOldestFirst()
    {
        var undo = new UndoStack(new Scene());
        undo.Execute(Light("first"));
        undo.Execute(Light("second"));

        Assert.Equal(["first", "second"], undo.History.Select(c => c.Label));
    }

    /// <summary>Undo and redo revisit a step already announced once - History says what got here, not what is on screen right now.</summary>
    [Fact]
    public void HistoryShrinksOnUndoAndGrowsBackOnRedo()
    {
        var undo = new UndoStack(new Scene());
        undo.Execute(Light("first"));
        undo.Execute(Light("second"));

        undo.Undo();
        Assert.Equal(["first"], undo.History.Select(c => c.Label));

        undo.Redo();
        Assert.Equal(["first", "second"], undo.History.Select(c => c.Label));
    }

    [Fact]
    public void ExecutingFiresBeforeTheStepChangesTheScene()
    {
        var scene = new Scene();
        var undo = new UndoStack(scene);
        int countWhenExecuting = -1;

        undo.Executing += () => countWhenExecuting = scene.Objects.Count;
        undo.Execute(new AddObjectsCommand("add", [new SceneObject("a", Primitives.Box(1, 1, 1))]));

        Assert.Equal(0, countWhenExecuting); // the object had not been added yet
        Assert.Equal(1, scene.Objects.Count); // but has by the time Execute returns
    }

    [Fact]
    public void ExecutedFiresAfterWithTheStepThatJustRan()
    {
        var undo = new UndoStack(new Scene());
        IUndoableCommand? seen = null;
        undo.Executed += command => seen = command;

        var step = Light("move it");
        undo.Execute(step);

        Assert.Same(step, seen);
    }

    /// <summary>Revisiting a step is not a new one - Executing and Executed are for Execute alone.</summary>
    [Fact]
    public void UndoAndRedoDoNotRaiseExecutingOrExecuted()
    {
        var undo = new UndoStack(new Scene());
        undo.Execute(Light("first"));

        int executingCount = 0, executedCount = 0;
        undo.Executing += () => executingCount++;
        undo.Executed += _ => executedCount++;

        undo.Undo();
        undo.Redo();

        Assert.Equal(0, executingCount);
        Assert.Equal(0, executedCount);
    }
}
