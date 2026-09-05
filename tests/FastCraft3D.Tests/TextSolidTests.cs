using System.Numerics;
using System.Runtime.ExceptionServices;
using FastCraft3D.Geometry;
using FastCraft3D.Geometry.Csg;
using FastCraft3D.Geometry.Engraving;
using FastCraft3D.Text;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>
/// Lettering: outlines extruded into a solid, then cut into a face or raised off it.
/// </summary>
public class TextSolidTests
{
    private static FacePatch TopOf(Mesh mesh) =>
        FacePatch.Find(mesh, new Vector3(0, 0, mesh.ComputeBounds().Max.Z), Vector3.UnitZ)!;

    private static TextShape Square(float size) => new(
        [new(-size / 2, -size / 2), new(size / 2, -size / 2),
         new(size / 2, size / 2), new(-size / 2, size / 2)], []);

    private static TextShape Ring(float outer, float inner) => new(
        [new(-outer / 2, -outer / 2), new(outer / 2, -outer / 2),
         new(outer / 2, outer / 2), new(-outer / 2, outer / 2)],
        [new List<Vector2>
        {
            new(-inner / 2, -inner / 2), new(-inner / 2, inner / 2),
            new(inner / 2, inner / 2), new(inner / 2, -inner / 2)
        }]);

    [Fact]
    public void AShapeExtrudesToASolidOfTheRightSize()
    {
        var face = TopOf(Primitives.Box(60, 60, 10));

        var solid = TextSolid.Build([Square(10)], face, 0f, 2f);

        Assert.True(solid.CheckHealth().IsWatertight, solid.CheckHealth().Describe());
        Assert.Equal(10 * 10 * 2, solid.ComputeSignedVolume(), 2);
    }

    /// <summary>The counter of an O has to be a hole all the way through the solid.</summary>
    [Fact]
    public void AHoleGoesRightThroughTheSolid()
    {
        var face = TopOf(Primitives.Box(60, 60, 10));

        var solid = TextSolid.Build([Ring(10, 4)], face, 0f, 2f);

        Assert.True(solid.CheckHealth().IsWatertight, solid.CheckHealth().Describe());
        Assert.Equal((10 * 10 - 4 * 4) * 2, solid.ComputeSignedVolume(), 2);
    }

    [Fact]
    public void SeveralShapesBecomeSeveralPieces()
    {
        var face = TopOf(Primitives.Box(60, 60, 10));
        var left = new TextShape(
            Square(6).Outline.Select(p => p - new Vector2(10, 0)).ToList(), []);
        var right = new TextShape(
            Square(6).Outline.Select(p => p + new Vector2(10, 0)).ToList(), []);

        var solid = TextSolid.Build([left, right], face, 0f, 2f);

        Assert.Equal(2, MeshComponents.Count(solid));
        Assert.Equal(2 * 6 * 6 * 2, solid.ComputeSignedVolume(), 2);
    }

    [Fact]
    public void ItIsWoundOutwardNotInsideOut()
    {
        var solid = TextSolid.Build([Square(8)], TopOf(Primitives.Box(60, 60, 10)), 0f, 2f);

        Assert.True(solid.ComputeSignedVolume() > 0);
    }

    /// <summary>Cutting the lettering into a face leaves a printable part.</summary>
    [Fact]
    public void EngravingWithItLeavesAWatertightPart()
    {
        var plate = Primitives.Box(60, 60, 10);
        var face = TopOf(plate);

        var solid = TextSolid.Build([Ring(20, 8)], face, 0.02f, -0.8f);
        var result = CsgSolid.Subtract(plate, solid);

        var health = result.CheckHealth();
        Assert.True(health.IsWatertight, health.Describe());
        Assert.True(result.ComputeSignedVolume() < plate.ComputeSignedVolume());
    }

    /// <summary>And raising it leaves one too, with more material rather than less.</summary>
    [Fact]
    public void RaisingItAddsMaterial()
    {
        var plate = Primitives.Box(60, 60, 10);
        var face = TopOf(plate);

        var solid = TextSolid.Build([Ring(20, 8)], face, -0.02f, 1.5f);
        var result = CsgSolid.Union(plate, solid);

        var health = result.CheckHealth();
        Assert.True(health.IsWatertight, health.Describe());
        Assert.True(result.ComputeSignedVolume() > plate.ComputeSignedVolume());
    }

    [Fact]
    public void NothingInMeansNothingOut()
    {
        var face = TopOf(Primitives.Box(60, 60, 10));

        Assert.Equal(0, TextSolid.Build([], face, 0f, 2f).TriangleCount);
        Assert.Equal(0, TextSolid.Build([Square(10)], face, 1f, 1f).TriangleCount);
    }
}

/// <summary>
/// Reading outlines out of a real font. These touch the operating system's font handling, so
/// they check shape rather than exact coordinates - a different machine has different fonts.
/// </summary>
public class GlyphOutlineTests
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

    [Fact]
    public void ALetterComesBackAsAnOutline()
    {
        RunSta(() =>
        {
            var glyphs = GlyphOutlines.Build("I", "Arial", 10f, bold: false);

            Assert.NotEmpty(glyphs);
            Assert.True(glyphs[0].Outline.Count >= 4);
        });
    }

    /// <summary>The reason the whole triangulator exists.</summary>
    [Fact]
    public void AnOHasAHoleInIt()
    {
        RunSta(() =>
        {
            var glyphs = GlyphOutlines.Build("O", "Arial", 10f, bold: false);

            Assert.Single(glyphs);
            Assert.Single(glyphs[0].Holes);
        });
    }

    [Fact]
    public void EachLetterIsItsOwnShape()
    {
        RunSta(() =>
        {
            var glyphs = GlyphOutlines.Build("III", "Arial", 10f, bold: false);

            Assert.Equal(3, glyphs.Count);
        });
    }

    [Fact]
    public void TheTextComesOutTheHeightAskedFor()
    {
        RunSta(() =>
        {
            var glyphs = GlyphOutlines.Build("HEIGHT", "Arial", 12f, bold: false);
            var all = glyphs.SelectMany(g => g.Outline).ToList();

            float height = all.Max(p => p.Y) - all.Min(p => p.Y);
            Assert.Equal(12f, height, 1);
        });
    }

    /// <summary>Centred on the origin, so it lands where it is placed rather than off to a side.</summary>
    [Fact]
    public void TheTextIsCentred()
    {
        RunSta(() =>
        {
            var all = GlyphOutlines.Build("CENTRE", "Arial", 10f, false)
                .SelectMany(g => g.Outline).ToList();

            Assert.Equal(0f, (all.Max(p => p.X) + all.Min(p => p.X)) / 2, 1);
            Assert.Equal(0f, (all.Max(p => p.Y) + all.Min(p => p.Y)) / 2, 1);
        });
    }

    [Fact]
    public void EmptyTextProducesNothing()
    {
        RunSta(() =>
        {
            Assert.Empty(GlyphOutlines.Build("", "Arial", 10f, false));
            Assert.Empty(GlyphOutlines.Build("   ", "Arial", 10f, false));
            Assert.Empty(GlyphOutlines.Build("A", "Arial", 0f, false));
        });
    }

    /// <summary>A word, extruded and cut into a plate, has to leave something printable.</summary>
    [Fact]
    public void ARealWordEngravesCleanly()
    {
        RunSta(() =>
        {
            var plate = Primitives.Box(80, 30, 6);
            var face = FacePatch.Find(plate, new Vector3(0, 0, 3), Vector3.UnitZ)!;

            var shapes = GlyphOutlines.Build("BOX", "Arial", 12f, bold: true)
                .Select(g => new TextShape(g.Outline, g.Holes))
                .ToList();

            Assert.Equal(3, shapes.Count);

            var solid = TextSolid.Build(shapes, face, 0.02f, -0.6f);
            Assert.True(solid.CheckHealth().IsWatertight, solid.CheckHealth().Describe());

            var result = CsgSolid.Subtract(plate, solid);
            Assert.True(result.CheckHealth().IsWatertight, result.CheckHealth().Describe());
            Assert.True(result.ComputeSignedVolume() < plate.ComputeSignedVolume());
        });
    }
}

/// <summary>The path between the click and the lettering.</summary>
public class EmbossWiringTests
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

    private static (FastCraft3D.ViewModels.MainViewModel Model, FastCraft3D.Model.SceneObject Cube) WithACube()
    {
        var model = new FastCraft3D.ViewModels.MainViewModel();
        model.InsertCommand.Execute("Cube");
        return (model, model.Scene.Objects[0]);
    }

    private static Vector3 TopOf(FastCraft3D.Model.SceneObject o) =>
        new(o.PositionX, o.PositionY, o.WorldBounds.Max.Z);

    [Fact]
    public void NothingCanBeLetteredUntilAFaceIsPicked()
    {
        RunSta(() =>
        {
            var (model, cube) = WithACube();
            model.BeginEmbossCommand.Execute(null);

            Assert.True(model.IsEmbossMode);
            Assert.False(model.HasEmbossFace);
            Assert.False(model.ApplyEmbossCommand.CanExecute(null));
            Assert.Contains("Click the face", model.EmbossSummary);

            model.PickEmbossFace(cube, TopOf(cube), Vector3.UnitZ);

            Assert.True(model.HasEmbossFace);
            Assert.True(model.ApplyEmbossCommand.CanExecute(null));
        });
    }

    [Fact]
    public void ThePreviewFollowsWhatIsTyped()
    {
        RunSta(() =>
        {
            var (model, cube) = WithACube();
            model.BeginEmbossCommand.Execute(null);
            Assert.Null(model.EmbossPreview());

            model.PickEmbossFace(cube, TopOf(cube), Vector3.UnitZ);
            Assert.NotNull(model.EmbossPreview());

            model.EmbossText = "";
            Assert.Null(model.EmbossPreview());
        });
    }

    [Fact]
    public void TheSummaryReportsWhatWillHappen()
    {
        RunSta(() =>
        {
            var (model, cube) = WithACube();
            model.BeginEmbossCommand.Execute(null);
            model.PickEmbossFace(cube, TopOf(cube), Vector3.UnitZ);

            model.EmbossText = "AB";
            model.EmbossDepth = 1.5f;
            Assert.Contains("cut 1.5 mm deep", model.EmbossSummary);

            model.EmbossRaised = true;
            Assert.Contains("raised 1.5 mm", model.EmbossSummary);
        });
    }

    /// <summary>Every mode claims the click, so starting one has to end the others.</summary>
    [Fact]
    public void LetteringEndsTheOtherModes()
    {
        RunSta(() =>
        {
            var (model, _) = WithACube();

            model.BeginEngraveCommand.Execute(null);
            model.BeginEmbossCommand.Execute(null);
            Assert.False(model.IsEngraveMode);
            Assert.True(model.IsEmbossMode);

            model.BeginMeasureCommand.Execute(null);
            Assert.True(model.IsMeasureMode);
        });
    }

    [Fact]
    public void CancellingForgetsTheFace()
    {
        RunSta(() =>
        {
            var (model, cube) = WithACube();
            model.BeginEmbossCommand.Execute(null);
            model.PickEmbossFace(cube, TopOf(cube), Vector3.UnitZ);

            model.CancelEmbossCommand.Execute(null);

            Assert.False(model.IsEmbossMode);
            Assert.False(model.HasEmbossFace);
            Assert.Null(model.EmbossFace);
        });
    }

    [Fact]
    public void SettingsAreHeldInSensibleRanges()
    {
        RunSta(() =>
        {
            var (model, _) = WithACube();

            model.EmbossHeight = -5f;
            Assert.True(model.EmbossHeight >= 1f);

            model.EmbossDepth = 0f;
            Assert.True(model.EmbossDepth > 0f);

            model.EmbossFont = "  ";
            Assert.Equal("Arial", model.EmbossFont);
        });
    }
}
