using System.Numerics;
using System.Runtime.ExceptionServices;
using FastCraft3D.Model;
using FastCraft3D.ViewModels;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>
/// Typing a size with the proportions locked.
///
/// The lock was honoured when a handle was dragged and ignored when a number was typed: the boxes
/// were bound straight to the object, and its SizeX only ever sets X. Typing a width into a locked
/// object stretched it, which is the one thing the lock exists to stop - and the fault was easy to
/// miss precisely because the same toggle worked the other way of asking.
///
/// Driven through the view model rather than through a copy of its arithmetic, because a copy is
/// exactly what would have passed while the real path was broken.
/// </summary>
public class ProportionLockTests
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

    /// <summary>A 40 x 20 x 10 block, selected, with the lock as asked for.</summary>
    private static void WithABlock(bool locked, Action<MainViewModel, SceneObject> body) => RunSta(() =>
    {
        var model = new MainViewModel();
        model.InsertCommand.Execute("Cube");

        var o = model.Scene.Objects[0];
        model.UniformScale = false;

        o.SizeX = 40f;
        o.SizeY = 20f;
        o.SizeZ = 10f;

        model.UniformScale = locked;
        body(model, o);
    });

    /// <summary>The one that was reported: the depth box, which nothing else followed.</summary>
    [Fact]
    public void TypingADepthTakesTheWidthAndHeightWithIt() => WithABlock(locked: true, (model, o) =>
    {
        model.ObjectSizeY = 40f;

        Assert.Equal(40f, o.SizeY, 3);
        Assert.Equal(80f, o.SizeX, 3);
        Assert.Equal(20f, o.SizeZ, 3);
    });

    [Fact]
    public void TypingAWidthDoesTheSame() => WithABlock(locked: true, (model, o) =>
    {
        model.ObjectSizeX = 80f;

        Assert.Equal(80f, o.SizeX, 3);
        Assert.Equal(40f, o.SizeY, 3);
        Assert.Equal(20f, o.SizeZ, 3);
    });

    [Fact]
    public void AndSoDoesTypingAHeight() => WithABlock(locked: true, (model, o) =>
    {
        model.ObjectSizeZ = 5f;

        Assert.Equal(5f, o.SizeZ, 3);
        Assert.Equal(20f, o.SizeX, 3);
        Assert.Equal(10f, o.SizeY, 3);
    });

    /// <summary>Unlocked, a box is still a box you can stretch - that is what the toggle is for.</summary>
    [Fact]
    public void UnlockedItChangesOnlyTheOneAsked() => WithABlock(locked: false, (model, o) =>
    {
        model.ObjectSizeY = 40f;

        Assert.Equal(40f, o.SizeY, 3);
        Assert.Equal(40f, o.SizeX, 3);
        Assert.Equal(10f, o.SizeZ, 3);
    });

    /// <summary>
    /// The sizes are the object's own, so a turned part scales along its own axes rather than the
    /// plate's. This is why the group path could not simply be reused for a single object.
    /// </summary>
    [Fact]
    public void ATurnedObjectScalesAlongItsOwnAxes() => WithABlock(locked: true, (model, o) =>
    {
        o.Rotation = new Vector3(0, 0, 90);

        model.ObjectSizeX = 80f;

        Assert.Equal(80f, o.SizeX, 3);
        Assert.Equal(40f, o.SizeY, 3);
        Assert.Equal(20f, o.SizeZ, 3);
    });

    /// <summary>The metre boxes are the same fields in other units, so they go the same way.</summary>
    [Fact]
    public void TheMetreBoxesObeyTheLockToo() => WithABlock(locked: true, (model, o) =>
    {
        model.ModelScale = 100f;
        model.RealD = 4f;                   // 4 m at 1:100 is 40 mm, twice the 20 mm depth

        Assert.Equal(40f, o.SizeY, 3);
        Assert.Equal(80f, o.SizeX, 3);
    });

    /// <summary>Nonsense is declined rather than applied: a size of nothing is not a size.</summary>
    [Fact]
    public void NothingIsNotASize() => WithABlock(locked: true, (model, o) =>
    {
        model.ObjectSizeY = 0f;

        Assert.Equal(20f, o.SizeY, 3);
        Assert.Equal(40f, o.SizeX, 3);
    });
}
