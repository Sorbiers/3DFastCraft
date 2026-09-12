using System.Numerics;
using System.Runtime.ExceptionServices;
using FastCraft3D.Geometry;
using FastCraft3D.Model;
using FastCraft3D.ViewModels;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>
/// The boxes along the bottom bar and down the side read off whatever is selected, so they have
/// to change when the selection does.
///
/// They did not. Setting Selected announced the sizes and the readings at scale, and nothing
/// announced the positions - so two objects a plate apart both showed the first one's X, and a
/// value typed into a box that had not caught up moved the new object by the old one's numbers.
///
/// Both tests here assert the announcement rather than the value. The getters were never wrong:
/// they read the selection live, so anything that asks gets the right answer - which is exactly
/// why this survived. A box already on screen only asks again when it is told to.
/// </summary>
public class SelectionReadoutTests
{
    private static void WithModel(Action<MainViewModel> body)
    {
        ExceptionDispatchInfo? error = null;

        var thread = new Thread(() =>
        {
            try { body(new MainViewModel()); }
            catch (Exception ex) { error = ExceptionDispatchInfo.Capture(ex); }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        error?.Throw();
    }

    private static SceneObject At(string name, float x) =>
        new(name, Primitives.Box(10, 10, 10)) { Position = new Vector3(x, 0, 0) };

    private static void Pick(MainViewModel model, SceneObject wanted)
    {
        foreach (var o in model.Scene.Objects) o.IsSelected = ReferenceEquals(o, wanted);
        model.RefreshSelection();
    }

    private static List<string> Listen(MainViewModel model)
    {
        var heard = new List<string>();
        model.PropertyChanged += (_, e) => heard.Add(e.PropertyName ?? "");
        return heard;
    }

    [Fact]
    public void PickingAnotherObjectAnnouncesThePositionBoxes()
    {
        WithModel(model =>
        {
            var first = At("First", 20f);
            var second = At("Second", -35f);
            model.Scene.Objects.Add(first);
            model.Scene.Objects.Add(second);
            Pick(model, first);

            var heard = Listen(model);
            Pick(model, second);

            Assert.Contains(nameof(MainViewModel.ObjectPositionX), heard);
            Assert.Contains(nameof(MainViewModel.ObjectPositionY), heard);
            Assert.Contains(nameof(MainViewModel.ObjectPositionZ), heard);
            Assert.Equal(-35f, model.ObjectPositionX, 3);
        });
    }

    /// <summary>
    /// The way it was found: a copy is set aside from its original, so the two read differently -
    /// and clicking between them showed one X for both.
    /// </summary>
    [Fact]
    public void ADuplicateAndItsOriginalDoNotShareAReadout()
    {
        WithModel(model =>
        {
            var original = At("Block", 0f);
            model.Scene.Objects.Add(original);
            Pick(model, original);

            model.DuplicateCommand.Execute(null);

            var copy = Assert.Single(model.Scene.Objects.Where(o => !ReferenceEquals(o, original)));
            Assert.NotEqual(original.PositionX, copy.PositionX);

            var heard = Listen(model);
            Pick(model, copy);

            Assert.Contains(nameof(MainViewModel.ObjectPositionX), heard);
            Assert.Equal(copy.PositionX, model.ObjectPositionX, 3);
        });
    }
}
