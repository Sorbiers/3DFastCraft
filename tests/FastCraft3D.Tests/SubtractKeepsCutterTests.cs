using System.Numerics;
using System.Reflection;
using System.Runtime.ExceptionServices;
using FastCraft3D.ViewModels;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>
/// Subtract with "Keep what is taken away" leaves the cutter on the plate once, not twice.
///
/// The cutter was put back after the results without first being taken off, so the list showed
/// the same object twice - one entry for where it had been and one below the results.
/// </summary>
public class SubtractKeepsCutterTests
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

    private static void Subtract(MainViewModel model) =>
        ((Task)typeof(MainViewModel)
            .GetMethod("ApplySubtract", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(model, null)!).GetAwaiter().GetResult();

    [Fact]
    public void TheKeptCutterIsListedOnceBelowWhatItCut() => RunSta(() =>
    {
        var model = new MainViewModel();
        model.InsertCommand.Execute("Cube");
        model.InsertCommand.Execute("Cube");

        var target = model.Scene.Objects[0];
        var cutter = model.Scene.Objects[1];
        cutter.Position += new Vector3(10f, 0f, 0f);

        model.Scene.ClearSelection();
        target.IsSelected = true;
        cutter.IsSelected = true;
        model.SubtractKeepsCutter = true;

        Subtract(model);

        // The target was really cut, rather than nothing having happened at all.
        Assert.NotSame(target, model.Scene.Objects[0]);
        Assert.Equal(2, model.Scene.Objects.Count);
        Assert.Single(model.Scene.Objects, o => ReferenceEquals(o, cutter));
        Assert.Same(cutter, model.Scene.Objects[^1]);

        model.Undo.Undo();

        Assert.Equal(2, model.Scene.Objects.Count);
        Assert.Same(target, model.Scene.Objects[0]);
        Assert.Same(cutter, model.Scene.Objects[1]);
    });
}
