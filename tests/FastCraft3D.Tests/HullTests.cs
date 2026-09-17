using System.Numerics;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using FastCraft3D.Geometry;
using FastCraft3D.Model;
using FastCraft3D.Model.Commands;
using FastCraft3D.Render;
using FastCraft3D.ViewModels;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>Hull, which wraps a selection in one skin; and handles on a tool's preview, which leave no undo step.</summary>
public class HullTests
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

    private static SceneObject Cylinder(float x) =>
        new("Cylinder", Primitives.Prism(5f, 10f, 64)) { Position = new Vector3(x, 0, 5) };

    [Fact]
    public void TwoCylindersWrapIntoASlot()
    {
        RunSta(() =>
        {
            var model = new MainViewModel();
            var left = Cylinder(-10);
            var right = Cylinder(10);
            model.Scene.Objects.Add(left);
            model.Scene.Objects.Add(right);
            left.IsSelected = right.IsSelected = true;
            model.RefreshSelection();

            model.HullCommand.Execute(null);

            var hull = Assert.Single(model.Scene.Objects);
            var mesh = hull.ToWorldMesh();
            Assert.True(mesh.CheckHealth().IsWatertight);

            // A 64-sided circle's area, and a 20 by 10 rectangle between the two, 10 mm tall.
            double slot = (64.0 / 2.0 * 25.0 * Math.Sin(2 * Math.PI / 64) + 20.0 * 10.0) * 10.0;
            Assert.Equal(slot, mesh.ComputeSignedVolume(), slot * 0.01);
            Assert.Equal(new Vector3(30, 10, 10), hull.WorldBounds.Size);

            model.UndoCommand.Execute(null);
            Assert.Equal(2, model.Scene.Objects.Count);
        });
    }

    [Fact]
    public void AFlatSelectionIsNotWrapped()
    {
        RunSta(() =>
        {
            var model = new MainViewModel();
            var flat = new SceneObject("Flat", new Mesh([new(0, 0, 0), new(10, 0, 0), new(0, 10, 0)], [0, 1, 2]));
            model.Scene.Objects.Add(flat);
            flat.IsSelected = true;
            model.RefreshSelection();

            model.HullCommand.Execute(null);

            Assert.Same(flat, Assert.Single(model.Scene.Objects));
        });
    }

    private sealed class FrontProjector : IScreenProjector
    {
        public bool TryProject(Vector3 world, out Point screen)
        {
            screen = new Point(500 + world.X * 4, 400 - world.Z * 4);
            return true;
        }

        public Vector3 ViewDirection => Vector3.UnitY;
        public bool IsReady => true;
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void ADragOnAPreviewLeavesNoUndoStep(bool records, bool expectUndo)
    {
        RunSta(() =>
        {
            var scene = new Scene();
            var cube = new SceneObject("Preview", Primitives.Box(20, 20, 20)) { IsSelected = true };
            scene.Objects.Add(cube);
            var undo = new UndoStack(scene);
            var canvas = new Canvas();
            var gizmo = new GizmoController(canvas, new FrontProjector(), scene, undo) { Mode = GizmoMode.Move, RecordsUndo = records };
            gizmo.Rebuild();

            gizmo.TryBeginDrag(new Point(0, 0), (FrameworkElement)canvas.Children[0]);
            gizmo.ContinueDrag(new Point(40, 0));
            gizmo.EndDrag();

            Assert.Equal(10f, cube.Position.X, 3);
            Assert.Equal(expectUndo, undo.CanUndo);
        });
    }
}
