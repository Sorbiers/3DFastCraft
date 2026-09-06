using System.Numerics;
using FastCraft3D.Geometry;
using FastCraft3D.Geometry.Csg;
using FastCraft3D.Geometry.Engraving;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>
/// Standing a pattern off a face instead of cutting it in.
///
/// The reason it is worth having is not only that it looks different. The joints of a cut pattern
/// are one connected web - the perpends run into the courses either side - and a web is what the
/// boolean struggles with. Raised, the bricks are separate pieces with mortar between them.
/// </summary>
public class RaisedPatternTests
{
    private static readonly EngraveOptions Brick = EngraveOptions.Default with
    {
        Kind = PatternKind.Brick, Size = 5f, GrooveWidth = 0.4f, Depth = 0.6f, Raised = true
    };

    private static FacePatch FaceOf(Mesh mesh, Vector3 normal)
    {
        var bounds = mesh.ComputeBounds();
        var at = new Vector3(
            MathF.Abs(normal.X) > 0.5f ? (normal.X > 0 ? bounds.Max.X : bounds.Min.X) : bounds.Center.X,
            MathF.Abs(normal.Y) > 0.5f ? (normal.Y > 0 ? bounds.Max.Y : bounds.Min.Y) : bounds.Center.Y,
            bounds.Center.Z);

        return FacePatch.Find(mesh, at, normal)!;
    }

    // --- The pattern itself -------------------------------------------------------------

    [Fact]
    public void TheRaisedPiecesNeverTouchOneAnother()
    {
        var pieces = GroovePattern.Raised(Brick, new Rect2(0, 0, 60, 40)).Rectangles;

        Assert.True(pieces.Count > 40, $"only {pieces.Count} bricks on a 60 x 40 wall");

        for (int i = 0; i < pieces.Count; i++)
            for (int j = i + 1; j < pieces.Count; j++)
            {
                var a = pieces[i];
                var b = pieces[j];

                bool overlap = a.MinU < b.MaxU && b.MinU < a.MaxU
                            && a.MinV < b.MaxV && b.MinV < a.MaxV;

                Assert.False(overlap, $"brick {i} runs into brick {j}");
            }
    }

    /// <summary>A brick is the size asked for; the mortar between them is the line width.</summary>
    [Fact]
    public void ABrickIsTheSizeAskedFor()
    {
        var pieces = GroovePattern.Raised(Brick, new Rect2(0, 0, 60, 40)).Rectangles;
        var one = pieces[pieces.Count / 2];

        Assert.Equal(5f, one.MaxU - one.MinU, 3);
        Assert.Equal(5f / 3f, one.MaxV - one.MinV, 3);
    }

    /// <summary>
    /// Grain raises the lines themselves rather than what lies between them, because the lines
    /// are what the pattern is made of - and unlike a cut one, a raised line has to keep itself
    /// and its whole width on the face.
    /// </summary>
    [Fact]
    public void GrainRaisesTheLinesThemselves()
    {
        var wood = Brick with { Kind = PatternKind.Wood, Size = 4f, GrooveWidth = 0.5f };
        var raised = GroovePattern.Raised(wood, new Rect2(0, 0, 60, 40));

        Assert.Empty(raised.Rectangles);
        Assert.NotEmpty(raised.Ribbons);

        foreach (var line in raised.Ribbons)
        {
            float half = line.Width * 0.5f;

            foreach (var point in line.Points)
            {
                Assert.InRange(point.X, half - 1e-3f, 60f - half + 1e-3f);
                Assert.InRange(point.Y, half - 1e-3f, 40f - half + 1e-3f);
            }
        }
    }

    /// <summary>
    /// Grain on all four walls of a cube, which is where it used to give out - it took the
    /// boolean, and by the second or third wall the accumulated splitting tore it open. It goes
    /// through the vertical decomposition now and never touches the boolean at all.
    /// </summary>
    [Theory]
    [InlineData(47.13f)]
    [InlineData(58.5f)]
    [InlineData(60f)]
    public void AllFourWallsOfACubeCanCarryGrain(float side)
    {
        var built = Primitives.Box(side, side, side);
        var wood = Brick with { Kind = PatternKind.Wood, Size = 4f, GrooveWidth = 0.5f };

        foreach (var n in new[] { -Vector3.UnitY, Vector3.UnitY, Vector3.UnitX, -Vector3.UnitX })
        {
            var result = Engraver.Engrave(built, FaceOf(built, n), wood);

            Assert.True(result.IsPrintable, $"{n}: {result.Health.Describe()}");
            built = result.Mesh;
        }
    }

    /// <summary>Raised grain still has to come out printable, boolean or no boolean.</summary>
    [Fact]
    public void GrainCanBeRaisedOnAWall()
    {
        var wall = Primitives.Box(60, 8, 40);
        var wood = Brick with { Kind = PatternKind.Wood, Size = 4f, GrooveWidth = 0.5f };

        var result = Engraver.Engrave(wall, FaceOf(wall, -Vector3.UnitY), wood);

        Assert.True(result.IsPrintable, result.Health.Describe());
        Assert.True(result.Mesh.ComputeSignedVolume() > wall.ComputeSignedVolume(), "nothing was added");
    }

    // --- On a face -----------------------------------------------------------------------

    [Fact]
    public void RaisingAddsMaterialRatherThanTakingItAway()
    {
        var wall = Primitives.Box(60, 8, 40);
        var before = wall.ComputeSignedVolume();

        var result = Engraver.Engrave(wall, FaceOf(wall, -Vector3.UnitY), Brick);

        Assert.True(result.IsPrintable, result.Health.Describe());
        Assert.True(result.Mesh.ComputeSignedVolume() > before, "nothing was added");
    }

    /// <summary>
    /// The bricks stop just short of the edge. One reaching exactly to the corner has its side in
    /// the plane of the face next door, and once that face is patterned too its bricks arrive to
    /// meet it exactly - which is the one thing to avoid.
    /// </summary>
    [Fact]
    public void NothingIsRaisedRightUpToTheEdge()
    {
        var wall = Primitives.Box(60, 8, 40);
        var face = FaceOf(wall, -Vector3.UnitY);
        var area = Engraver.RaisedArea(face);

        Assert.Equal(face.Min.X + Engraver.RaisedInset, area.MinU, 4);
        Assert.Equal(face.Max.Y - Engraver.RaisedInset, area.MaxV, 4);

        foreach (var piece in Engraver.Pattern(face, Brick).Rectangles)
        {
            Assert.True(piece.MinU >= face.Min.X + Engraver.RaisedInset - 1e-4f);
            Assert.True(piece.MaxU <= face.Max.X - Engraver.RaisedInset + 1e-4f);
        }
    }

    /// <summary>Sunk a hair into the face, or the two would share a plane.</summary>
    [Fact]
    public void ThePiecesAreFootedInsideTheFace()
    {
        var wall = Primitives.Box(60, 8, 40);
        var face = FaceOf(wall, -Vector3.UnitY);

        var solid = GrooveSolid.Raised(Engraver.Pattern(face, Brick).Rectangles, face, 0.6f);

        // The face is at y = -4 and its outward direction is -Y, so the footings sit above it.
        Assert.Equal(-4f - 0.6f, solid.ComputeBounds().Min.Y, 3);
        Assert.Equal(-4f + GrooveSolid.Sink, solid.ComputeBounds().Max.Y, 3);
    }

    /// <summary>
    /// The case that matters: four walls of a box, one after another, at awkward dimensions. The
    /// fourth is where a pattern has three others to meet, and where cutting comes unstuck.
    /// </summary>
    [Theory]
    [InlineData(58.49f, 36.41f, 35.07f)]
    [InlineData(61.3f, 33.7f, 41.9f)]
    [InlineData(72.5f, 24.25f, 38.75f)]
    public void AllFourWallsOfABoxCanBeRaised(float x, float y, float z)
    {
        var built = Primitives.Box(x, y, z);

        foreach (var n in new[] { -Vector3.UnitY, Vector3.UnitY, Vector3.UnitX, -Vector3.UnitX })
        {
            var result = Engraver.Engrave(built, FaceOf(built, n), Brick);

            Assert.True(result.IsPrintable, $"{n}: {result.Health.Describe()}");
            built = result.Mesh;
        }
    }

    /// <summary>
    /// A box with a square cross-section, which used to be the one that would not go.
    ///
    /// Three walls always went on and the fourth tore, at some sizes and not others, because
    /// each union split geometry all over the model and the fourth pattern had three walls'
    /// worth of mismatched edges to land on. Retiling the face touches nothing else, so the
    /// fourth wall is no different from the first.
    /// </summary>
    [Theory]
    [InlineData(40f)]
    [InlineData(47.13f)]
    [InlineData(58.5f)]
    [InlineData(60f)]
    [InlineData(75f)]
    public void AllFourWallsOfACubeCanBeRaised(float side)
    {
        var built = Primitives.Box(side, side, side);
        var fine = Brick with { GrooveWidth = 0.6f };

        foreach (var n in new[] { -Vector3.UnitY, Vector3.UnitY, Vector3.UnitX, -Vector3.UnitX })
        {
            var result = Engraver.Engrave(built, FaceOf(built, n), fine);

            Assert.True(result.IsPrintable, $"{n}: {result.Health.Describe()}");
            built = result.Mesh;
        }
    }

    [Fact]
    public void StripesRaiseIntoBoards()
    {
        var wall = Primitives.Box(60, 8, 40);
        var boards = Brick with { Kind = PatternKind.Stripes, Size = 6f, GrooveWidth = 0.5f };

        var result = Engraver.Engrave(wall, FaceOf(wall, -Vector3.UnitY), boards);

        Assert.True(result.IsPrintable, result.Health.Describe());
        Assert.True(result.Mesh.ComputeSignedVolume() > wall.ComputeSignedVolume());
    }

    [Fact]
    public void TheSummarySaysWhichWayRoundItIs()
    {
        var wall = Primitives.Box(60, 8, 40);
        var state = new FastCraft3D.ViewModels.EngraveState();
        state.Pick(wall, FaceOf(wall, -Vector3.UnitY));

        state.Options = Brick;
        Assert.Contains("proud", state.Describe());

        state.Options = Brick with { Raised = false };
        Assert.Contains("deep", state.Describe());
    }
}
