using System.Numerics;
using System.Runtime.ExceptionServices;
using FastCraft3D.Geometry;
using FastCraft3D.Model;
using FastCraft3D.ViewModels;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>Rebuild face, from Surface info: the face read is laid again, as one undo step, on the same object.</summary>
public class RebuildFaceFlowTests
{
    private static void Stage(Action<MainViewModel, SceneObject> body)
    {
        ExceptionDispatchInfo? error = null;

        var thread = new Thread(() =>
        {
            try
            {
                var model = new MainViewModel();
                var box = new SceneObject("Box", MeshSubdivision.Subdivide(Primitives.Box(20, 20, 20), 2))
                {
                    Position = new Vector3(5, 0, 10),
                    Colour = new Vector3(0.8f, 0.2f, 0.2f),
                    Filament = 3
                };
                model.Scene.Objects.Add(box);

                model.BeginSurfaceInfoCommand.Execute(null);
                body(model, box);
            }
            catch (Exception ex) { error = ExceptionDispatchInfo.Capture(ex); }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        error?.Throw();
    }

    private static string Fact(MainViewModel model, string label) => model.SurfaceFacts.Single(f => f.Label == label).Value;

    [Fact]
    public void TheFaceReadIsLaidAgainOnTheSameObjectAndOneUndoPutsItBack() => Stage((model, box) =>
    {
        var original = box.Mesh;
        Assert.True(model.PickSurfaceInfo(box, new Vector3(5.3f, 0.2f, 20), Vector3.UnitZ));
        Assert.Contains("points inside", Fact(model, "Triangles"));

        model.RebuildFaceCommand.Execute(null);

        Assert.Contains(box, model.Scene.Objects);
        Assert.True(box.Mesh.TriangleCount < original.TriangleCount);
        Assert.True(box.Mesh.CheckHealth().IsWatertight);
        Assert.Equal(new Vector3(5, 0, 10), box.Position);
        Assert.Equal(3, box.Filament);
        Assert.DoesNotContain("points inside", Fact(model, "Triangles"));
        Assert.StartsWith("Rebuilt the face", model.Status);

        model.UndoCommand.Execute(null);

        Assert.Same(original, box.Mesh);
    });

    [Fact]
    public void AFaceOnACurvedSurfaceIsLeftAsItWas() => Stage((model, _) =>
    {
        var ball = new SceneObject("Ball", Primitives.Sphere(10, 32, 16)) { Position = new Vector3(40, 0, 10) };
        model.Scene.Objects.Add(ball);
        var original = ball.Mesh;

        Assert.True(model.PickSurfaceInfo(ball, new Vector3(50, 0, 10), Vector3.UnitX));
        model.RebuildFaceCommand.Execute(null);

        Assert.Same(original, ball.Mesh);
        Assert.StartsWith("Nothing to rebuild", model.Status);
    });
}
