using System.Diagnostics;
using System.Numerics;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using FastCraft3D.Geometry;
using FastCraft3D.Model;
using FastCraft3D.Render;
using FastCraft3D.View;
using FastCraft3D.ViewModels;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>
/// Tools run from their panel to the finished object, as a click on the panel's button runs them:
/// what the panel leaves behind when it closes is what they have to work with.
///
/// In one collection with the other tests that open a panel, since all of them put the panel
/// somewhere through the one static host.
/// </summary>
[Collection("Tool panels")]
public class ToolFlowTests
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

    /// <summary>
    /// Opens the next panel through the host, lets <paramref name="whileOpen"/> do what a user would
    /// with the preview, then presses the named button - all from inside the panel's own wait.
    /// </summary>
    private static void AnswerPanel(MainViewModel model, string button, Action whileOpen) =>
        AnswerPanel(model, button, _ => whileOpen());

    private static void AnswerPanel(MainViewModel model, string button, Action<ToolPanel> whileOpen)
    {
        ToolPanel.Host = panel =>
        {
            model.ShowPanel(panel);
            if (panel is null) return;

            panel.Dispatcher.BeginInvoke(new Action(() =>
            {
                whileOpen(panel);
                var pressed = (Button)panel.FindName(button)!;
                pressed.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, pressed));
            }), DispatcherPriority.Background);
        };
    }

    /// <summary>Runs the dispatcher until something is so, for the part of a tool that finishes after an await.</summary>
    private static void PumpUntil(Func<bool> done, int milliseconds = 20000)
    {
        var watch = Stopwatch.StartNew();
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(TimeSpan.FromMilliseconds(20), DispatcherPriority.Background,
            (_, _) => { if (done() || watch.ElapsedMilliseconds > milliseconds) frame.Continue = false; },
            Dispatcher.CurrentDispatcher);
        timer.Start();
        Dispatcher.PushFrame(frame);
        timer.Stop();
    }

    [Fact]
    public void CutTakesTheHoleOutOfTheSelectedPart() => WithModel(model =>
    {
        var cube = new SceneObject("Cube", Primitives.Box(20, 20, 20)) { Position = new Vector3(0, 0, 10) };
        model.Scene.Objects.Add(cube);
        cube.IsSelected = true;
        model.RefreshSelection();
        double before = cube.ToWorldMesh().ComputeSignedVolume();

        AnswerPanel(model, "AcceptButton", () => { });
        model.InsertHoleCommand.Execute(null);
        PumpUntil(() => !model.Scene.Objects.Contains(cube) && !model.IsBusy);

        var cut = Assert.Single(model.Scene.Objects);
        Assert.NotSame(cube, cut);
        var mesh = cut.ToWorldMesh();
        Assert.True(mesh.CheckHealth().IsWatertight);
        Assert.True(mesh.ComputeSignedVolume() < before - 50, "no hole was cut");
        Assert.False(model.PanelHandles);
    });

    [Fact]
    public void AHoleGoesWhereTheCutterWasMoved() => WithModel(model =>
    {
        var block = new SceneObject("Block", Primitives.Box(60, 20, 20)) { Position = new Vector3(0, 0, 10) };
        model.Scene.Objects.Add(block);
        block.IsSelected = true;
        model.RefreshSelection();

        // Moved along X by the handles, while the panel is open.
        AnswerPanel(model, "AcceptButton", () =>
        {
            Assert.True(model.PanelHandles);
            var preview = Assert.Single(model.Scene.Selection);
            preview.Position += new Vector3(20, 0, 0);
        });
        model.InsertHoleCommand.Execute(null);
        PumpUntil(() => !model.Scene.Objects.Contains(block) && !model.IsBusy);

        var cut = Assert.Single(model.Scene.Objects).ToWorldMesh();

        // Where the hole is, there is nothing on the top face: no vertex of the top face sits
        // nearer the hole's middle than the hole's own edge, at X = 20.
        var holeRim = cut.Positions.Where(p => MathF.Abs(p.Z - 20f) < 1e-3f && MathF.Abs(p.Y) < 5f && p.X > 10f && p.X < 30f).ToList();
        Assert.NotEmpty(holeRim);
        Assert.True(holeRim.Average(p => p.X) is > 19f and < 21f, $"the hole's rim is round X = {holeRim.Average(p => p.X):0.#}");
    });

    [Fact]
    public void ACutterIsAddedWhereItWasPut() => WithModel(model =>
    {
        AnswerPanel(model, "AddButton", () =>
        {
            var preview = Assert.Single(model.Scene.Selection);
            preview.Position += new Vector3(15, -5, 0);
        });
        model.InsertHoleCommand.Execute(null);
        PumpUntil(() => model.Scene.Objects.Count == 1 && !model.HasOpenPanel);

        var cutter = Assert.Single(model.Scene.Objects);
        Assert.Equal(15f, cutter.WorldBounds.Center.X, 1);
        Assert.Equal(-5f, cutter.WorldBounds.Center.Y, 1);
    });

    [Fact]
    public void AddLeavesThePartWholeAndPutsTheCutterWhereItStood() => WithModel(model =>
    {
        var cube = new SceneObject("Cube", Primitives.Box(20, 20, 20)) { Position = new Vector3(0, 0, 10) };
        model.Scene.Objects.Add(cube);
        cube.IsSelected = true;
        model.RefreshSelection();

        AnswerPanel(model, "AddButton", () => { });
        model.InsertHoleCommand.Execute(null);
        PumpUntil(() => model.Scene.Objects.Count == 2 && !model.HasOpenPanel);

        Assert.Equal(2, model.Scene.Objects.Count);
        Assert.Contains(cube, model.Scene.Objects);
        var cutter = model.Scene.Objects.Single(o => o != cube);

        // All the way through a part 20 deep: from the top a hair past the bottom, not the
        // part's diagonal again below it.
        Assert.Equal(20f + HoleCutter.Overshoot, cutter.WorldBounds.Max.Z, 1);
        Assert.InRange(cutter.WorldBounds.Min.Z, -1.5f, 0f);
    });

    [Fact]
    public void ABoltCircleCutsEveryHoleAtOnce() => WithModel(model =>
    {
        var plate = new SceneObject("Plate", Primitives.Box(60, 60, 10)) { Position = new Vector3(0, 0, 5) };
        model.Scene.Objects.Add(plate);
        plate.IsSelected = true;
        model.RefreshSelection();

        AnswerPanel(model, "AcceptButton", panel =>
        {
            ((RadioButton)panel.FindName("CircleBox")!).IsChecked = true;
            ((TextBox)panel.FindName("CircleDiameterBox")!).Text = "30";
            ((TextBox)panel.FindName("CountBox")!).Text = "4";
        });
        model.InsertHoleCommand.Execute(null);
        PumpUntil(() => !model.Scene.Objects.Contains(plate) && !model.IsBusy, 60000);

        var cut = Assert.Single(model.Scene.Objects).ToWorldMesh();
        Assert.True(cut.CheckHealth().IsWatertight);

        // A plain top face has only its corners; each hole puts a rim round its own middle.
        foreach (var middle in new[] { new Vector2(15, 0), new Vector2(0, 15), new Vector2(-15, 0), new Vector2(0, -15) })
            Assert.Contains(cut.Positions, p => MathF.Abs(p.Z - 10f) < 1e-3f && Vector2.Distance(new Vector2(p.X, p.Y), middle) < 5f);
        Assert.DoesNotContain(cut.Positions, p => MathF.Abs(p.Z - 10f) < 1e-3f && new Vector2(p.X, p.Y).Length() < 5f);
    });

    [Fact]
    public void AThreadIsMadeWhereItsPreviewWasMovedAndTurned() => WithModel(model =>
    {
        AnswerPanel(model, "AddButton", () =>
        {
            var preview = Assert.Single(model.Scene.Selection);
            preview.Position += new Vector3(-30, 10, 0);
            preview.Rotation = new Vector3(90, 0, 0);
        });
        model.InsertThreadCommand.Execute(null);
        PumpUntil(() => model.Scene.Objects.Count == 1 && !model.HasOpenPanel);

        var thread = Assert.Single(model.Scene.Objects);
        Assert.Equal(-30f, thread.Position.X, 1);
        Assert.Equal(10f, thread.Position.Y, 1);
        Assert.Equal(90f, thread.Rotation.X, 1);
    });

    [Fact]
    public void AThreadedHoleIsCutIntoTheSelectedPart() => WithModel(model =>
    {
        var cube = new SceneObject("Cube", Primitives.Box(20, 20, 20)) { Position = new Vector3(0, 0, 10) };
        model.Scene.Objects.Add(cube);
        cube.IsSelected = true;
        model.RefreshSelection();
        double before = cube.ToWorldMesh().ComputeSignedVolume();

        AnswerPanel(model, "CutButton", panel =>
        {
            var cut = (Button)panel.FindName("CutButton")!;
            Assert.NotEqual(Visibility.Visible, cut.Visibility);

            ((ComboBox)panel.FindName("KindBox")!).SelectedIndex = (int)ThreadKind.HoleCutter;
            Assert.Equal(Visibility.Visible, cut.Visibility);

            // Sunk into the middle of the top, its mouth just proud of it.
            var cutter = Assert.Single(model.Scene.Selection);
            Assert.Equal(20f + HoleCutter.Overshoot, cutter.WorldBounds.Max.Z, 2);
            Assert.Equal(0f, cutter.WorldBounds.Center.X, 2);
        });
        model.InsertThreadCommand.Execute(null);
        PumpUntil(() => !model.Scene.Objects.Contains(cube) && !model.IsBusy, 60000);

        var drilled = Assert.Single(model.Scene.Objects).ToWorldMesh();
        Assert.True(drilled.CheckHealth().IsWatertight);
        Assert.True(drilled.ComputeSignedVolume() < before - 300, "no threaded hole was cut");
    });

    [Fact]
    public void APreviewCannotBeResizedAndTypingIntoItsBoxesLeavesNoUndoStep() => WithModel(model =>
    {
        AnswerPanel(model, "AddButton", () =>
        {
            Assert.True(model.ShowManipulatorBar);
            Assert.False(model.ResizeOffered);

            model.GizmoMode = GizmoMode.Scale;
            Assert.NotEqual(GizmoMode.Scale, model.GizmoMode);
        });
        model.InsertHoleCommand.Execute(null);
        PumpUntil(() => model.Scene.Objects.Count == 1 && !model.HasOpenPanel);

        Assert.True(model.ResizeOffered);
    });
}
