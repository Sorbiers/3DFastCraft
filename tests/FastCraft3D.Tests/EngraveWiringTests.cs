using System.Numerics;
using System.Runtime.ExceptionServices;
using FastCraft3D.Model;
using FastCraft3D.ViewModels;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>
/// The path between the click and the geometry: entering the mode, picking a face, and what the
/// panel is allowed to do at each point. The click itself is the one link these cannot reach -
/// it lands on a Direct3D viewport, which no automation can drive.
/// </summary>
public class EngraveWiringTests
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

    private static (MainViewModel Model, SceneObject Cube) WithACube()
    {
        var model = new MainViewModel();
        model.InsertCommand.Execute("Cube");
        return (model, model.Scene.Objects[0]);
    }

    private static Vector3 TopOf(SceneObject o) => new(o.PositionX, o.PositionY, o.WorldBounds.Max.Z);

    [Fact]
    public void EngravingNeedsExactlyOneObject()
    {
        RunSta(() =>
        {
            var model = new MainViewModel();
            Assert.False(model.BeginEngraveCommand.CanExecute(null));

            model.InsertCommand.Execute("Cube");
            Assert.True(model.BeginEngraveCommand.CanExecute(null));

            model.InsertCommand.Execute("Cylinder");
            model.SelectAllCommand.Execute(null);
            Assert.False(model.BeginEngraveCommand.CanExecute(null));
        });
    }

    [Fact]
    public void NothingCanBeEngravedUntilAFaceIsPicked()
    {
        RunSta(() =>
        {
            var (model, cube) = WithACube();

            model.BeginEngraveCommand.Execute(null);

            Assert.True(model.IsEngraveMode);
            Assert.False(model.HasEngraveFace);
            Assert.False(model.ApplyEngraveCommand.CanExecute(null));
            Assert.Contains("Click the face", model.EngraveSummary);

            model.PickEngraveFace(cube, TopOf(cube), Vector3.UnitZ);

            Assert.True(model.HasEngraveFace);
            Assert.True(model.ApplyEngraveCommand.CanExecute(null));
        });
    }

    [Fact]
    public void PickingAFaceDescribesItAndRaisesTheHighlight()
    {
        RunSta(() =>
        {
            var (model, cube) = WithACube();
            int highlights = 0;
            model.EngraveFaceChanged += () => highlights++;

            model.BeginEngraveCommand.Execute(null);
            bool picked = model.PickEngraveFace(cube, TopOf(cube), Vector3.UnitZ);

            Assert.True(picked);
            Assert.NotNull(model.EngraveFace);
            Assert.Equal(Vector3.UnitZ, model.EngraveFace!.Normal);
            Assert.Contains("stud", model.EngraveSummary);
            Assert.Equal(FastCraft3D.Geometry.Engraving.PatternKind.Studs, model.EngravePattern);
            Assert.Equal(2, highlights); // once on entering the mode, once on the pick
        });
    }

    [Fact]
    public void AClickThatMissesAFaceChangesNothing()
    {
        RunSta(() =>
        {
            var (model, cube) = WithACube();
            model.BeginEngraveCommand.Execute(null);

            bool picked = model.PickEngraveFace(cube, new Vector3(0, 0, 900), Vector3.UnitZ);

            Assert.False(picked);
            Assert.False(model.HasEngraveFace);
            Assert.Contains("flat", model.Status);
        });
    }

    /// <summary>Picking outside the mode must not quietly arm the tool.</summary>
    [Fact]
    public void AClickIsIgnoredWhenTheModeIsOff()
    {
        RunSta(() =>
        {
            var (model, cube) = WithACube();

            Assert.False(model.PickEngraveFace(cube, TopOf(cube), Vector3.UnitZ));
            Assert.False(model.HasEngraveFace);
        });
    }

    [Fact]
    public void CancellingForgetsTheFace()
    {
        RunSta(() =>
        {
            var (model, cube) = WithACube();
            model.BeginEngraveCommand.Execute(null);
            model.PickEngraveFace(cube, TopOf(cube), Vector3.UnitZ);

            model.CancelEngraveCommand.Execute(null);

            Assert.False(model.IsEngraveMode);
            Assert.False(model.HasEngraveFace);
            Assert.Null(model.EngraveFace);
        });
    }

    /// <summary>Both modes take over the click, so starting one has to end the other.</summary>
    [Fact]
    public void SplittingAndEngravingCannotBothBeOn()
    {
        RunSta(() =>
        {
            var (model, _) = WithACube();

            model.BeginEngraveCommand.Execute(null);
            Assert.True(model.IsEngraveMode);

            model.BeginSplitCommand.Execute(null);
            Assert.True(model.IsSplitMode);
            Assert.False(model.IsEngraveMode);

            model.BeginEngraveCommand.Execute(null);
            Assert.True(model.IsEngraveMode);
            Assert.False(model.IsSplitMode);
        });
    }

    [Fact]
    public void TheSettingsSurviveBeingChangedAndAreReflectedInTheSummary()
    {
        RunSta(() =>
        {
            var (model, cube) = WithACube();
            model.BeginEngraveCommand.Execute(null);
            model.PickEngraveFace(cube, TopOf(cube), Vector3.UnitZ);

            model.EngravePattern = Geometry.Engraving.PatternKind.StudUnderside;
            model.EngraveDepth = 2f;

            Assert.Equal(2f, model.EngraveDepth, 4);
            Assert.Contains("hollowed 2 mm deep", model.EngraveSummary);
        });
    }

    /// <summary>
    /// A record struct's parameterless form is its zero value, not its declared defaults - so
    /// the panel has to be handed real settings or it opens showing nothing but zeros.
    /// </summary>
    [Fact]
    public void ThePanelOpensOnSensibleSettings()
    {
        RunSta(() =>
        {
            var model = new MainViewModel();

            Assert.Equal(20f, model.EngraveSize, 3);
            Assert.Equal(1.2f, model.EngraveGrooveWidth, 3);
            Assert.Equal(0.6f, model.EngraveDepth, 3);
            Assert.Equal(0f, model.EngraveOffsetU, 3);
        });
    }
}
