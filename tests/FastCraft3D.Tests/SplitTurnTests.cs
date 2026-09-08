using System.Numerics;
using System.Runtime.ExceptionServices;
using FastCraft3D.Geometry;
using FastCraft3D.ViewModels;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>
/// The split plane's own turn.
///
/// It keeps three angles, the way an object does, rather than working them out from which way it
/// faces. Two would describe the facing completely - a plane has no third degree of freedom -
/// but then the third ring would have nowhere to put what it was given, and there would be two
/// rings for three boxes.
/// </summary>
public class SplitTurnTests
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

    private static void Same(Vector3 expected, Vector3 actual)
    {
        float apart = Vector3.Distance(Vector3.Normalize(expected), Vector3.Normalize(actual));
        Assert.True(apart < 1e-4f, $"expected {expected}, got {actual}");
    }

    [Fact]
    public void APlaneStartsOnItsAxisWithNothingTurned() => WithModel(model =>
    {
        model.SplitAxis = Axis.Z;

        Assert.Equal(0, model.SplitRoll, 4);
        Assert.Equal(0, model.SplitPitch, 4);
        Assert.Equal(0, model.SplitYaw, 4);
        Same(Vector3.UnitZ, model.SplitNormal);
    });

    [Fact]
    public void ATypedAngleTurnsThePlane() => WithModel(model =>
    {
        model.SplitAxis = Axis.Z;
        model.SplitPitch = 90;

        Same(Vector3.UnitX, model.SplitNormal);
    });

    /// <summary>
    /// The one that made three rings worth having. Turning a plane about its own facing cannot
    /// move the cut - the same as spinning a cylinder about its axis - but the angle is still
    /// somewhere to put what the ring was given, and it comes back when asked.
    /// </summary>
    [Fact]
    public void TurningAboutItsOwnFacingIsRememberedEvenThoughTheCutDoesNotMove() => WithModel(model =>
    {
        model.SplitAxis = Axis.Z;

        model.TurnSplitPlane(Axis.Z, 30);

        Assert.Equal(30, model.SplitYaw, 3);
        Same(Vector3.UnitZ, model.SplitNormal);
    });

    /// <summary>
    /// A second drag goes where its ring says. Adding the amount to one of the three angles is
    /// the obvious thing and is wrong: they are applied in order, so only the last lines up with
    /// the world and the other two turn the plane about its own axes.
    /// </summary>
    [Fact]
    public void TwoTurnsEndWhereTheRingsSaidTheyWould() => WithModel(model =>
    {
        model.SplitAxis = Axis.Z;

        model.TurnSplitPlane(Axis.X, 30);
        model.TurnSplitPlane(Axis.Y, 40);

        var byHand = Vector3.Transform(Vector3.UnitZ,
            Matrix4x4.CreateRotationX(30f * MathF.PI / 180f)
            * Matrix4x4.CreateRotationY(40f * MathF.PI / 180f));

        Same(byHand, model.SplitNormal);
    });

    [Fact]
    public void ChoosingAnAxisPutsThePlaneBackOnItSquare() => WithModel(model =>
    {
        model.SplitAxis = Axis.Z;
        model.SplitPitch = 35;
        model.SplitRoll = 12;

        model.SplitAxis = Axis.X;

        Assert.Equal(0, model.SplitRoll, 4);
        Assert.Equal(0, model.SplitPitch, 4);
        Assert.Equal(0, model.SplitYaw, 4);
        Same(Vector3.UnitX, model.SplitNormal);
    });

    /// <summary>
    /// Turning only turns it. The offset is measured along the facing, so a new facing on its
    /// own would move the plane bodily as well - off the solid it is meant to be cutting.
    /// </summary>
    [Fact]
    public void TurningLeavesThePlaneWhereItWas() => WithModel(model =>
    {
        model.InsertCommand.Execute("Cube");
        model.SplitAxis = Axis.Z;
        model.SplitOffset = 14f;

        // The point the plane turns about: the one on it nearest what it is cutting.
        var centre = model.Scene.Objects[0].WorldBounds.Center;
        var pivot = centre - model.SplitNormal * (Vector3.Dot(model.SplitNormal, centre) - model.SplitOffset);

        model.TurnSplitPlane(Axis.X, 25);

        Assert.Equal(Vector3.Dot(model.SplitNormal, pivot), model.SplitOffset, 3);
    });

    /// <summary>Dragged in steps, as the ring reports it, and the steps add up.</summary>
    [Fact]
    public void ADragArrivingInStepsEndsWhereOneStepWouldHave() => WithModel(model =>
    {
        model.SplitAxis = Axis.Z;

        for (int i = 0; i < 6; i++) model.TurnSplitPlane(Axis.Y, 5);

        Assert.Equal(30, model.SplitPitch, 3);
    });
}
