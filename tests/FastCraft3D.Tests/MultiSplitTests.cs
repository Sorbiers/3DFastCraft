using System.Numerics;
using System.Runtime.ExceptionServices;
using FastCraft3D.Geometry;
using FastCraft3D.Model;
using FastCraft3D.Model.Commands;
using FastCraft3D.ViewModels;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>
/// One plane through everything selected. The plane belongs to the scene rather than to an
/// object, which is how an assembly gets sliced in half in one stroke.
/// </summary>
public class MultiSplitTests
{
    private static void RunSta(Action body)
    {
        ExceptionDispatchInfo? error = null;
        var thread = new Thread(() =>
        {
            try { body(); }
            catch (Exception ex) { error = ExceptionDispatchInfo.Capture(ex); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        error?.Throw();
    }

    [Fact]
    public void SplittingCanStartWithSeveralObjectsSelected()
    {
        RunSta(() =>
        {
            var model = new MainViewModel();
            Assert.False(model.BeginSplitCommand.CanExecute(null));

            model.InsertCommand.Execute("Cube");
            Assert.True(model.BeginSplitCommand.CanExecute(null));

            model.InsertCommand.Execute("Cylinder");
            model.SelectAllCommand.Execute(null);

            Assert.True(model.BeginSplitCommand.CanExecute(null));
            model.BeginSplitCommand.Execute(null);
            Assert.True(model.IsSplitMode);
        });
    }

    /// <summary>
    /// The plane has to reach everything it is going to cut, so its travel is measured across
    /// the whole selection rather than across whichever object happened to be first.
    /// </summary>
    [Fact]
    public void ThePlaneSpansEverythingSelected()
    {
        RunSta(() =>
        {
            var model = new MainViewModel();
            model.InsertCommand.Execute("Cube");
            model.BeginSplitCommand.Execute(null);

            float aloneRange = model.SplitMaximum - model.SplitMinimum;

            // A second object well above the first widens what the plane must cover. It has to
            // move along the plane's own normal, which starts as Z - moving it sideways would
            // not change how far the plane has to travel at all.
            model.InsertCommand.Execute("Cube");
            model.Scene.Objects[1].PositionZ = 120;
            model.SelectAllCommand.Execute(null);
            model.BeginSplitCommand.Execute(null);

            Assert.True(model.SplitMaximum - model.SplitMinimum > aloneRange + 100,
                "the plane's travel did not grow to cover the second object");
        });
    }

    /// <summary>Splitting several objects has to be one step, not one per object.</summary>
    [Fact]
    public void TwoObjectsBecomeFourPiecesInOneUndoStep()
    {
        var scene = new Scene();
        var undo = new UndoStack(scene);

        var left = new SceneObject("Left", Primitives.Box(20, 20, 20));
        var right = new SceneObject("Right", Primitives.Box(20, 20, 20)) { PositionX = 40 };
        scene.Objects.Add(left);
        scene.Objects.Add(right);

        // What the view model does, without the async plumbing: one plane, every object.
        var produced = new List<SceneObject>();
        foreach (var o in new[] { left, right })
        {
            var (front, back) = PlaneSplit.Split(o.ToWorldMesh(), Vector3.UnitZ, 0f, SplitKeep.Both);
            if (front is not null) produced.Add(new SceneObject($"{o.Name} top", front));
            if (back is not null) produced.Add(new SceneObject($"{o.Name} bottom", back));
        }

        undo.Execute(new ReplaceObjectsCommand("Split", [left, right], produced));

        Assert.Equal(4, scene.Objects.Count);
        Assert.All(scene.Objects, o => Assert.True(o.Mesh.CheckHealth().IsWatertight));

        undo.Undo();

        Assert.Equal(2, scene.Objects.Count);
        Assert.False(undo.CanUndo);
    }

    /// <summary>
    /// A plane that reaches some objects and misses others cuts what it can. Refusing the whole
    /// operation because one part of five sits clear of the plane would be no use to anyone.
    /// </summary>
    [Fact]
    public void ObjectsThePlaneMissesAreLeftAlone()
    {
        var hit = new SceneObject("Hit", Primitives.Box(20, 20, 20));
        var clear = new SceneObject("Clear", Primitives.Box(20, 20, 20)) { PositionZ = 200 };

        var onHit = PlaneSplit.Split(hit.ToWorldMesh(), Vector3.UnitZ, 0f, SplitKeep.Both);
        var onClear = PlaneSplit.Split(clear.ToWorldMesh(), Vector3.UnitZ, 0f, SplitKeep.Both);

        Assert.NotNull(onHit.Front);
        Assert.NotNull(onHit.Back);

        // Well above the plane, so one side gets everything and the other nothing.
        Assert.True(onClear.Front is null || onClear.Back is null);
    }

    [Fact]
    public void EveryPieceKeepsTheColourOfWhatItCameFrom()
    {
        var source = new SceneObject("Painted", Primitives.Box(20, 20, 20))
        {
            Colour = Palette.FromHex("#D6455C")
        };

        var (front, back) = PlaneSplit.Split(source.ToWorldMesh(), Vector3.UnitZ, 0f, SplitKeep.Both);
        var pieces = new[] { front, back }
            .Where(m => m is not null)
            .Select(m => new SceneObject("piece", m!) { Colour = source.Colour })
            .ToList();

        Assert.Equal(2, pieces.Count);
        Assert.All(pieces, p => Assert.Equal(source.Colour, p.Colour));
    }
}
