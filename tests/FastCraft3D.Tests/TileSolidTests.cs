using System.Numerics;
using FastCraft3D.Geometry;
using FastCraft3D.Geometry.Engraving;
using Xunit;
using Xunit.Abstractions;

namespace FastCraft3D.Tests;

/// <summary>
/// Roof tiles and siding built as slabs rather than sampled as a field.
///
/// The field had two faults and both were sampling faults: a grid repeating in step with the
/// profile, and a grid line landing exactly where the profile jumps. Neither can be asked of a
/// slab, so what these check is the other thing - that the pieces are laid where a tiler would lay
/// them, and that what comes back is a solid.
/// </summary>
public class TileSolidTests(ITestOutputHelper log)
{
    /// <summary>The settings off the screenshot: a 77 mm cube at a 10 mm pitch.</summary>
    private static TileCourses Roof(float thickMm = 0.2f) =>
        SurfaceTexture.CoursesOf(new TextureOptions(TextureKind.RoofTiles, 10f, 0.2f, 45f), thickMm)!.Value;

    private static PlanarSurface Face(float side)
    {
        var wall = MeshTransform.Transformed(
            Primitives.Box(side, side, side), Matrix4x4.CreateTranslation(0, 0, side / 2f));

        return new PlanarSurface(FacePatch.Find(wall, new Vector3(0, -side / 2f, side / 2f), -Vector3.UnitY)!);
    }

    [Fact]
    public void NoTwoTilesOverlap()
    {
        var pieces = TileSolid.Pieces(Roof(), 76.756f, 76.756f);

        log.WriteLine($"{pieces.Count} pieces");
        Assert.NotEmpty(pieces);

        for (int i = 0; i < pieces.Count; i++)
            for (int j = i + 1; j < pieces.Count; j++)
            {
                var a = pieces[i];
                var b = pieces[j];

                bool over =
                    MathF.Min(a.MaxU, b.MaxU) - MathF.Max(a.MinU, b.MinU) > 1e-6f &&
                    MathF.Min(a.MaxV, b.MaxV) - MathF.Max(a.MinV, b.MinV) > 1e-6f;

                Assert.False(over, $"{a} overlaps {b} - the boolean is handed disjoint boxes");
            }
    }

    /// <summary>
    /// Alternate courses are set over by half a tile, and which way round that falls follows the
    /// course's own index rather than its place in whatever list this face happened to produce -
    /// or the size of the face decides the bond.
    /// </summary>
    [Fact]
    public void AlternateCoursesAreSetOverByHalfATile()
    {
        var courses = Roof();
        var pieces = TileSolid.Pieces(courses, 76.756f, 76.756f);

        // Group by course, and take a joint that is not against either verge.
        var rows = pieces.GroupBy(p => MathF.Round(p.MinV, 3))
                         .OrderBy(g => g.Key)
                         .Select(g => g.Select(p => p.MinU).Where(u => u > -30f && u < 30f).Min())
                         .ToList();

        Assert.True(rows.Count >= 4, "not enough whole courses on the face to judge the bond");

        for (int i = 0; i + 1 < rows.Count; i++)
        {
            float step = MathF.Abs(rows[i + 1] - rows[i]) % courses.TileMm;
            float over = MathF.Min(step, courses.TileMm - step);

            Assert.True(MathF.Abs(over - courses.TileMm / 2f) < 0.05f,
                $"courses {i} and {i + 1} are set over by {over:0.###} mm, "
                + $"where half a tile is {courses.TileMm / 2f:0.###}");
        }
    }

    /// <summary>
    /// A tile is a plate of one thickness laid at an angle, not a wedge. The ramp this replaced
    /// tapered every tile to nothing at its head, which is a knife edge no printer will lay and a
    /// joint that disappears exactly where the course above meets it.
    /// </summary>
    [Fact]
    public void ATileIsAPlateOfOneThicknessRolledUp()
    {
        var courses = Roof(0.2f);
        var whole = new Rect2(0f, 0f, courses.TileMm - courses.JointMm, courses.CourseMm - courses.JointMm);

        float rise = TileSolid.RiseOn(courses, whole);
        float expected = whole.Height * MathF.Tan(courses.SlopeDegrees * MathF.PI / 180f);

        log.WriteLine($"course {courses.CourseMm:0.###} mm, thickness {courses.ThickMm:0.##} mm, "
                    + $"rise {rise:0.###} mm, relief {TileSolid.ReliefOf(courses):0.###} mm");

        Assert.Equal(expected, rise, 4);
        Assert.True(rise > 0, "the tiles are not rolled at all");
        Assert.Equal(courses.ThickMm + rise, TileSolid.ReliefOf(courses), 4);
    }

    /// <summary>
    /// The slope runs whichever way it is asked to: up the course, so the tail laps the course
    /// below, or along the piece, so a course reads as shingles all leaning the same way.
    /// </summary>
    [Fact]
    public void TheSlopeRunsWhicheverWayItIsAskedTo()
    {
        var rolled = Roof();
        var pitched = rolled with { Slope = TileSlope.Pitch };

        // A tile is longer than it is deep, so at the same angle pitching it lifts the far end
        // over a longer run and therefore further.
        var whole = new Rect2(0f, 0f, rolled.TileMm - rolled.JointMm, rolled.CourseMm - rolled.JointMm);

        float up = TileSolid.RiseOn(rolled, whole);
        float along = TileSolid.RiseOn(pitched, whole);

        log.WriteLine($"rolled rises {up:0.###} mm over {whole.Height:0.##} mm, "
                    + $"pitched {along:0.###} mm over {whole.Width:0.##} mm");

        Assert.True(along > up, "pitching a tile that is longer than it is deep rises no further");

        // And nought degrees lays them flat, which is a plain tiled surface and a fair thing to ask for.
        Assert.Equal(0f, TileSolid.RiseOn(rolled with { SlopeDegrees = 0f }, whole), 5);
    }

    /// <summary>
    /// A piece cut at a verge keeps the plane of the ones beside it. Measured over its own extent
    /// rather than over a whole tile's, so it does not stand up to the same height over a shorter
    /// run and leave a step down the edge of the face.
    /// </summary>
    [Fact]
    public void ACutTileKeepsThePlaneOfTheOnesBesideIt()
    {
        var courses = Roof();

        var whole = new Rect2(0f, 0f, 9f, courses.CourseMm - courses.JointMm);
        var cut = whole with { MaxV = whole.MinV + whole.Height / 2f };

        float slopeOfWhole = TileSolid.RiseOn(courses, whole) / whole.Height;
        float slopeOfCut = TileSolid.RiseOn(courses, cut) / cut.Height;

        Assert.Equal(slopeOfWhole, slopeOfCut, 4);
    }

    [Fact]
    public void TheFieldComesBackAsASolid()
    {
        var built = TileSolid.Build(Face(80f), Roof(), 76.756f, 76.756f);

        log.WriteLine($"{built.TriangleCount:N0} triangles - {built.CheckHealth().Describe()}");

        Assert.True(built.TriangleCount > 0, "nothing came back");
        Assert.True(built.CheckHealth().IsWatertight, built.CheckHealth().Describe());
    }

    /// <summary>Siding is one strip the whole way across: no ends, and so nothing to stagger.</summary>
    [Fact]
    public void SidingRunsTheWholeWayAcross()
    {
        var courses = SurfaceTexture.CoursesOf(
            new TextureOptions(TextureKind.Siding, 10f, 0.4f, 45f), 0.4f)!.Value;

        var pieces = TileSolid.Pieces(courses, 60f, 60f);

        Assert.NotEmpty(pieces);
        Assert.All(pieces, p => Assert.True(p.Width > 59f,
            $"a course of siding is {p.Width:0.##} mm wide where the face is 60"));
    }

    /// <summary>
    /// Cut, the slabs are their own mirror about the face, so taking them away sinks the tiles in
    /// rather than doing nothing. Subtracting the raised solid removes the footings and no more,
    /// which is a flat recess a third of a millimetre deep with no pattern in it at all.
    /// </summary>
    [Fact]
    public void CutTilesSinkIntoTheFaceInsteadOfStandingOffIt()
    {
        var face = Face(80f);

        var raised = TileSolid.Build(face, Roof(), 76.756f, 76.756f);
        var sunk = TileSolid.Build(face, Roof(), 76.756f, 76.756f, sunk: true);

        // The face is at y = -40 and the relief runs out along -Y, so standing proud is more
        // negative and sinking in is less.
        float proud = -raised.Positions.Min(p => p.Y) - 40f;
        float into = sunk.Positions.Max(p => p.Y) + 40f;

        log.WriteLine($"raised stands {proud:0.###} mm proud; cut reaches {into:0.###} mm in");

        Assert.True(proud > 0.1f, $"the raised tiles only stand {proud:0.###} mm off the face");
        Assert.True(into > 0.1f, $"the cut tiles only reach {into:0.###} mm into the face");
        Assert.True(sunk.CheckHealth().IsWatertight, sunk.CheckHealth().Describe());
    }
}
