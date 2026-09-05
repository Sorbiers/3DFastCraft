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
            Assert.Contains("grooves", model.EngraveSummary);
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

            model.EngraveDepth = 0.15f;

            Assert.Equal(0.15f, model.EngraveDepth, 4);
            Assert.Contains("0.15 mm deep", model.EngraveSummary);
            Assert.True(model.HasEngraveAdvice);
            Assert.Contains("0.2 mm layers", model.EngraveAdvice);
        });
    }

    [Fact]
    public void ThePreviewIsEmptyUntilAFaceIsPickedAndFilledAfterwards()
    {
        RunSta(() =>
        {
            var (model, cube) = WithACube();
            model.BeginEngraveCommand.Execute(null);

            Assert.True(model.EngravePreview.IsEmpty);

            model.PickEngraveFace(cube, TopOf(cube), Vector3.UnitZ);

            Assert.False(model.EngravePreview.IsEmpty);
            Assert.NotEmpty(model.EngravePreview.Rectangles);
        });
    }

    /// <summary>The preview has to redraw as the settings change, or it is showing a lie.</summary>
    [Fact]
    public void ChangingASettingRedrawsThePreview()
    {
        RunSta(() =>
        {
            var (model, cube) = WithACube();
            model.BeginEngraveCommand.Execute(null);
            model.PickEngraveFace(cube, TopOf(cube), Vector3.UnitZ);

            int redraws = 0;
            model.EngraveFaceChanged += () => redraws++;
            int before = model.EngravePreview.Count;

            model.EngraveSize = 4f;

            Assert.True(redraws > 0, "the viewport was never told to redraw");
            Assert.True(model.EngravePreview.Count > before, "a finer pattern should mean more grooves");
        });
    }

    /// <summary>
    /// Sliding the pattern is what lines a corner up, so it has to reach the preview. The count
    /// is deliberately not asserted: shifting a pattern can carry a line off the edge of the
    /// face, and on a small face that is the usual outcome rather than a fault.
    /// </summary>
    [Fact]
    public void ShiftingThePatternMovesThePreview()
    {
        RunSta(() =>
        {
            var (model, cube) = WithACube();
            model.BeginEngraveCommand.Execute(null);
            model.PickEngraveFace(cube, TopOf(cube), Vector3.UnitZ);
            model.EngravePattern = Geometry.Engraving.PatternKind.Stripes;
            model.EngraveSize = 4f;

            var before = model.EngravePreview.Rectangles.Select(r => r.MinV).ToList();
            model.EngraveOffsetV = 1.5f;
            var after = model.EngravePreview.Rectangles.Select(r => r.MinV).ToList();

            Assert.Equal(1.5f, model.EngraveOffsetV, 4);
            Assert.NotEmpty(after);
            Assert.NotEqual(before, after);
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

    /// <summary>Wood previews as curves, so the preview has to carry them as well.</summary>
    [Fact]
    public void WoodPreviewsAsRibbons()
    {
        RunSta(() =>
        {
            var (model, cube) = WithACube();
            model.BeginEngraveCommand.Execute(null);
            model.PickEngraveFace(cube, TopOf(cube), Vector3.UnitZ);

            model.EngravePattern = Geometry.Engraving.PatternKind.Wood;
            model.EngraveSize = 3f;

            Assert.Empty(model.EngravePreview.Rectangles);
            Assert.NotEmpty(model.EngravePreview.Ribbons);
        });
    }

    /// <summary>Brick courses are always level, so the direction control does not apply to it.</summary>
    [Fact]
    public void OnlyStripesAndBoardsOfferADirection()
    {
        RunSta(() =>
        {
            var model = new MainViewModel();

            model.EngravePattern = Geometry.Engraving.PatternKind.Brick;
            Assert.False(model.EngraveDirectionApplies);

            model.EngravePattern = Geometry.Engraving.PatternKind.Stripes;
            Assert.True(model.EngraveDirectionApplies);

            model.EngravePattern = Geometry.Engraving.PatternKind.Wood;
            Assert.True(model.EngraveDirectionApplies);
        });
    }
}
