using System.Numerics;
using System.Runtime.ExceptionServices;
using FastCraft3D.Geometry;
using FastCraft3D.Geometry.Csg;
using FastCraft3D.Model;
using FastCraft3D.ViewModels;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>
/// Align to face and Center face to face take the wall of a hole whole, round or with flat sides:
/// a window set into its opening is put on the opening's middle, not on the middle of one wall of it.
/// </summary>
public class HoleFaceToolsTests
{
    private static void Stage(Mesh wallMesh, Action<MainViewModel, SceneObject, SceneObject> body)
    {
        ExceptionDispatchInfo? error = null;

        var thread = new Thread(() =>
        {
            try
            {
                var model = new MainViewModel();

                // A wall with a 12 by 8 opening along Y, centred on 5, 0, -3 - and a frame to put in it,
                // away from it, a little thinner than the wall and a hair smaller than the opening.
                var wall = new SceneObject("Wall", wallMesh);
                var frame = new SceneObject("Frame", Primitives.Box(11, 4, 7)) { Position = new Vector3(30, -20, 20) };
                model.Scene.Objects.Add(wall);
                model.Scene.Objects.Add(frame);
                frame.IsSelected = true;
                model.RefreshSelection();

                body(model, wall, frame);
            }
            catch (Exception ex) { error = ExceptionDispatchInfo.Capture(ex); }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        error?.Throw();
    }

    private static Mesh Wall(Vector3 openingAt) => ManifoldCsg.Subtract(
        Primitives.Box(40, 10, 30),
        MeshTransform.Transformed(Primitives.Box(12, 20, 8), Matrix4x4.CreateTranslation(openingAt)))!;

    private static readonly Vector3 OpeningAt = new(5, 0, -3);

    // A point on the wall of the opening on its +X side, off its middle, and the way the wall faces.
    private static readonly Vector3 OnTheReveal = new(OpeningAt.X + 6, 1.5f, OpeningAt.Z + 2);
    private static readonly Vector3 TheRevealFaces = new(-1, 0, 0);

    [Fact]
    public void AlignToFaceOnAnOpeningCentersTheSelectionOnItAcrossAndLeavesItsDepth() =>
        Stage(Wall(OpeningAt), (model, wall, frame) =>
        {
            model.BeginAlignFaceCommand.Execute(null);

            Assert.True(model.PickAlignFace(wall, OnTheReveal, TheRevealFaces));
            Assert.Contains("opening", model.AlignFaceTargetLabel);

            model.ApplyAlignFaceCommand.Execute(null);

            var middle = frame.WorldBounds.Center;
            Assert.Equal(OpeningAt.X, middle.X, 2);
            Assert.Equal(OpeningAt.Z, middle.Z, 2);
            Assert.Equal(-20f, middle.Y, 2); // along the opening it was left where it was
        });

    [Fact]
    public void AlignToFaceOnARoundHoleDoesTheSame()
    {
        var block = Primitives.Box(30, 30, 10);
        var drill = MeshTransform.Transformed(Primitives.Prism(3f, 20f, 48), Matrix4x4.CreateTranslation(5, 4, 0));

        Stage(ManifoldCsg.Subtract(block, drill)!, (model, wall, frame) =>
        {
            model.BeginAlignFaceCommand.Execute(null);

            Assert.True(model.PickAlignFace(wall, new Vector3(8f, 4f, 1f), new Vector3(-1, 0, 0)));
            Assert.Contains("hole", model.AlignFaceTargetLabel);

            model.ApplyAlignFaceCommand.Execute(null);

            var middle = frame.WorldBounds.Center;
            Assert.Equal(5f, middle.X, 2);
            Assert.Equal(4f, middle.Y, 2);
            Assert.Equal(20f, middle.Z, 2); // along the hole, its height is its own
        });
    }

    [Fact]
    public void AlignToFaceOnAFlatFaceIsAsItWas() => Stage(Wall(OpeningAt), (model, wall, frame) =>
    {
        model.BeginAlignFaceCommand.Execute(null);

        // The front of the wall, well clear of the opening.
        Assert.True(model.PickAlignFace(wall, new Vector3(-15, -5, 10), new Vector3(0, -1, 0)));
        Assert.DoesNotContain("opening", model.AlignFaceTargetLabel);
        Assert.Contains("a face on Wall", model.AlignFaceTargetLabel);
    });

    [Fact]
    public void CenterFaceToFaceAFrameOntoAnOpeningPutsItOnTheOpeningsAxis() =>
        Stage(Wall(OpeningAt), (model, wall, frame) =>
        {
            model.BeginCentreFaceCommand.Execute(null);

            // The front of the frame, flat, then the wall of the opening in the wall.
            Assert.True(model.PickCentreFace(frame, new Vector3(30, -22, 20), new Vector3(0, -1, 0)));
            Assert.True(model.PickCentreFace(wall, OnTheReveal, TheRevealFaces));

            Assert.Contains("opening", model.CentreFaceStatusLabel);
            Assert.True(model.CentreIsRound);

            model.ApplyCentreFaceCommand.Execute(null);

            Assert.Equal(OpeningAt.X, frame.Position.X, 2);
            Assert.Equal(OpeningAt.Z, frame.Position.Z, 2);
            Assert.Equal(-20f, frame.Position.Y, 2);
        });

    [Fact]
    public void ThePickedOpeningIsLitAsAllItsWalls() => Stage(Wall(OpeningAt), (model, wall, frame) =>
    {
        model.BeginAlignFaceCommand.Execute(null);
        model.PickAlignFace(wall, OnTheReveal, TheRevealFaces);

        Assert.NotNull(model.AlignFace);
        Assert.InRange(model.AlignFace!.Area, 399f, 401f);
    });
}
