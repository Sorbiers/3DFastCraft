using System.Numerics;

namespace FastCraft3D.Model.Commands;

public interface IUndoableCommand
{
    string Label { get; }
    void Apply(Scene scene);
    void Revert(Scene scene);

    /// <summary>
    /// Roughly how much memory this step is holding on to, for the history's budget.
    ///
    /// Nought for the ones that only remember numbers - a move, a colour. The ones that hold
    /// geometry say so: a mould of a three hundred thousand triangle scan comes out as two parts
    /// of about twenty-six megabytes, measured, and nothing was ever letting them go.
    ///
    /// Deliberately rough, and deliberately over rather than under: a mesh held by both the scene
    /// and a step is counted twice, which errs towards keeping less history rather than more.
    /// </summary>
    long Bytes => 0;
}

/// <summary>What a set of objects costs to keep, near enough for a budget.</summary>
internal static class Weight
{
    public static long Of(IEnumerable<SceneObject> objects) => objects.Sum(o =>
        (long)o.Mesh.Positions.Count * 12 + (long)o.Mesh.Indices.Count * 4);
}

/// <summary>
/// Undo/redo for every mutation in the app.
///
/// Wired in from the start rather than retrofitted: once operations mutate state directly it
/// becomes very hard to reconstruct the inverse, especially for booleans that replace meshes
/// wholesale.
/// </summary>
public sealed class UndoStack
{
    /// <summary>
    /// How much geometry the history may hold before the oldest steps are let go.
    ///
    /// A budget rather than a count, because the steps are not the same size: a move remembers
    /// nothing but numbers and a mould remembers fifty megabytes. This is thousands of ordinary
    /// edits or about twenty moulds, and it works out which without being told. What it replaces
    /// is no limit at all, which on a long session of heavy work only ever went one way.
    /// </summary>
    public const long Budget = 1L << 30;

    private readonly List<IUndoableCommand> done = new();
    private readonly Stack<IUndoableCommand> undone = new();
    private readonly Scene scene;
    private long held;

    public UndoStack(Scene scene) => this.scene = scene;

    public event Action? Changed;

    /// <summary>
    /// Raised when a step has been let go to stay inside the budget - which is the moment undo
    /// stops being able to get you all the way back, and so the moment to say so.
    /// </summary>
    public event Action? Trimmed;

    public bool CanUndo => done.Count > 0;
    public bool CanRedo => undone.Count > 0;
    public string? NextUndoLabel => done.Count > 0 ? done[^1].Label : null;
    public string? NextRedoLabel => undone.Count > 0 ? undone.Peek().Label : null;

    /// <summary>How much the history is holding, for anything that wants to show it.</summary>
    public long Held => held;

    public void Execute(IUndoableCommand command)
    {
        command.Apply(scene);

        done.Add(command);
        held += command.Bytes;

        foreach (var dropped in undone) held -= dropped.Bytes;
        undone.Clear();

        Fit();
        Changed?.Invoke();
    }

    public void Undo()
    {
        if (done.Count == 0) return;

        var command = done[^1];
        done.RemoveAt(done.Count - 1);

        command.Revert(scene);
        undone.Push(command);
        Changed?.Invoke();
    }

    public void Redo()
    {
        if (undone.Count == 0) return;

        var command = undone.Pop();
        command.Apply(scene);
        done.Add(command);
        Changed?.Invoke();
    }

    public void Clear()
    {
        done.Clear();
        undone.Clear();
        held = 0;
        Changed?.Invoke();
    }

    /// <summary>
    /// Lets the oldest steps go until the history is back inside its budget.
    ///
    /// Never the last one. Whatever was just done has to be undoable however big it is - being
    /// unable to take back the operation you are looking at is worse than any amount of memory.
    /// </summary>
    private void Fit()
    {
        bool lost = false;

        while (done.Count > 1 && held > Budget)
        {
            held -= done[0].Bytes;
            done.RemoveAt(0);
            lost = true;
        }

        if (lost) Trimmed?.Invoke();
    }
}

public sealed class AddObjectsCommand(string label, IReadOnlyList<SceneObject> objects) : IUndoableCommand
{
    public string Label { get; } = label;

    public long Bytes => Weight.Of(objects);

    public void Apply(Scene scene)
    {
        scene.ClearSelection();
        foreach (var o in objects)
        {
            scene.Objects.Add(o);
            o.IsSelected = true;
        }
    }

    public void Revert(Scene scene)
    {
        foreach (var o in objects)
            scene.Objects.Remove(o);
    }
}

public sealed class DeleteObjectsCommand(IReadOnlyList<SceneObject> objects) : IUndoableCommand
{
    private readonly List<(SceneObject Object, int Index)> removed = new();

    public string Label => objects.Count == 1 ? "Delete object" : $"Delete {objects.Count} objects";

    public long Bytes => Weight.Of(objects);

    public void Apply(Scene scene)
    {
        // Every index is resolved against the untouched list first. Looking them up while
        // removing would read positions from an already-shifted list, and undo would then
        // reinsert the objects in the wrong order.
        removed.Clear();
        foreach (var o in objects)
        {
            int index = scene.Objects.IndexOf(o);
            if (index >= 0) removed.Add((o, index));
        }

        foreach (var (o, _) in removed.OrderByDescending(r => r.Index))
            scene.Objects.Remove(o);
    }

    public void Revert(Scene scene)
    {
        // Ascending, so each insertion lands at a position the earlier ones have already made room for.
        foreach (var (o, index) in removed.OrderBy(r => r.Index))
            scene.Objects.Insert(Math.Min(index, scene.Objects.Count), o);
    }
}

/// <summary>
/// Swaps one set of objects for another. Booleans and cuts consume their inputs and produce
/// new geometry, so expressing them as a replacement makes undo a single reversible step.
/// </summary>
public sealed class ReplaceObjectsCommand(
    string label,
    IReadOnlyList<SceneObject> removed,
    IReadOnlyList<SceneObject> added) : IUndoableCommand
{
    private readonly List<int> removedIndices = new();

    public string Label { get; } = label;

    public long Bytes => Weight.Of(removed) + Weight.Of(added);

    public void Apply(Scene scene)
    {
        // As in DeleteObjectsCommand: resolve all positions against the untouched list, then
        // remove from the back, so undo can restore the original ordering.
        removedIndices.Clear();
        var located = new List<(SceneObject Object, int Index)>();
        foreach (var o in removed)
        {
            int index = scene.Objects.IndexOf(o);
            if (index >= 0) located.Add((o, index));
        }

        foreach (var (o, _) in located.OrderByDescending(r => r.Index))
            scene.Objects.Remove(o);

        removedIndices.AddRange(located.Select(r => r.Index));

        scene.ClearSelection();
        foreach (var o in added)
        {
            scene.Objects.Add(o);
            o.IsSelected = true;
        }
    }

    public void Revert(Scene scene)
    {
        foreach (var o in added)
            scene.Objects.Remove(o);

        var pairs = removed.Zip(removedIndices, (o, i) => (Object: o, Index: i)).OrderBy(p => p.Index);
        scene.ClearSelection();
        foreach (var (o, index) in pairs)
        {
            scene.Objects.Insert(Math.Min(index, scene.Objects.Count), o);
            o.IsSelected = true;
        }
    }
}

/// <summary>
/// Several commands that have to undo as one.
///
/// Written for the circular repeat, which both moves the originals onto the circle and adds the
/// copies: two commands, but one thing happened, and one Ctrl+Z has to take all of it back. Undone
/// in reverse, because the later steps were built on the state the earlier ones left.
/// </summary>
public sealed class CompoundCommand(string label, IReadOnlyList<IUndoableCommand> steps) : IUndoableCommand
{
    public string Label { get; } = label;

    public long Bytes => steps.Sum(step => step.Bytes);

    public void Apply(Scene scene)
    {
        foreach (var step in steps) step.Apply(scene);
    }

    public void Revert(Scene scene)
    {
        for (int i = steps.Count - 1; i >= 0; i--) steps[i].Revert(scene);
    }
}

public readonly record struct TransformState(Vector3 Position, Vector3 Rotation, Vector3 Scale)
{
    public static TransformState Capture(SceneObject o) => new(o.Position, o.Rotation, o.Scale);

    public void ApplyTo(SceneObject o)
    {
        o.Position = Position;
        o.Rotation = Rotation;
        o.Scale = Scale;
    }
}

/// <summary>
/// A position / rotation / size change. Callers capture the "before" state when an edit starts
/// and push a single command when it ends, so one drag or one typed value is one undo step
/// rather than a hundred.
/// </summary>
public sealed class TransformCommand(
    string label,
    IReadOnlyList<SceneObject> objects,
    IReadOnlyList<TransformState> before,
    IReadOnlyList<TransformState> after) : IUndoableCommand
{
    public string Label { get; } = label;

    public void Apply(Scene scene)
    {
        for (int i = 0; i < objects.Count; i++)
            after[i].ApplyTo(objects[i]);
    }

    public void Revert(Scene scene)
    {
        for (int i = 0; i < objects.Count; i++)
            before[i].ApplyTo(objects[i]);
    }

    /// <summary>Null when nothing actually moved, so no-op edits do not clutter the undo stack.</summary>
    public static TransformCommand? CreateIfChanged(
        string label, IReadOnlyList<SceneObject> objects, IReadOnlyList<TransformState> before)
    {
        var after = objects.Select(TransformState.Capture).ToList();
        bool changed = before.Where((state, i) => !state.Equals(after[i])).Any();
        return changed ? new TransformCommand(label, objects, before, after) : null;
    }
}

/// <summary>
/// Painting a selection. Separate from <see cref="TransformCommand"/> because colour is not part
/// of the transform, and because repainting the whole selection has to undo as one step rather
/// than one step per object.
/// </summary>
public sealed class ColourCommand(
    string label,
    IReadOnlyList<SceneObject> objects,
    IReadOnlyList<Vector3> before,
    Vector3 after) : IUndoableCommand
{
    public string Label { get; } = label;

    public void Apply(Scene scene)
    {
        foreach (var o in objects) o.Colour = after;
    }

    public void Revert(Scene scene)
    {
        for (int i = 0; i < objects.Count; i++)
            objects[i].Colour = before[i];
    }

    /// <summary>Null when every object already has that colour, so re-picking it is not an undo step.</summary>
    public static ColourCommand? CreateIfChanged(
        string label, IReadOnlyList<SceneObject> objects, Vector3 colour)
    {
        if (objects.Count == 0) return null;

        var before = objects.Select(o => o.Colour).ToList();
        return before.All(c => c == colour) ? null : new ColourCommand(label, objects, before, colour);
    }
}
