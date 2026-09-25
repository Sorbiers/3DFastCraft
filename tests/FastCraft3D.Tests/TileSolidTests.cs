using System.Numerics;
using FastCraft3D.Geometry;
using FastCraft3D.Geometry.Csg;
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
    /// A course running across an opening comes back as the pieces either side of it.
    ///
    /// Four ways round, because a hole can take a bite out of a piece from any side and the one
    /// that matters - a window in the middle of a wall - is the one that leaves two.
    /// </summary>
    [Fact]
    public void AnOpeningBreaksThePieceThatRunsAcrossIt()
    {
        var course = new Rect2(-30f, 0f, 30f, 5f);

        // A window well inside the run: the course survives as the piece each side of it.
        var split = TileSolid.Without(course, [new Rect2(-5f, -2f, 5f, 8f)]);
        Assert.Equal(2, split.Count);
        Assert.Equal(-30f, split.Min(r => r.MinU), 3);
        Assert.Equal(30f, split.Max(r => r.MaxU), 3);
        Assert.All(split, r => Assert.True(r.MaxU <= -5f + 1e-3f || r.MinU >= 5f - 1e-3f,
            $"{r} reaches into the opening"));

        // Swallowed whole, and untouched, and cut back at one end.
        Assert.Empty(TileSolid.Without(course, [new Rect2(-40f, -2f, 40f, 8f)]));
        Assert.Single(TileSolid.Without(course, [new Rect2(40f, 40f, 50f, 50f)]));
        Assert.Single(TileSolid.Without(course, [new Rect2(10f, -2f, 40f, 8f)]));

        // No two of the parts may overlap, whatever the hole did to it.
        var bitten = TileSolid.Without(course, [new Rect2(-5f, 2f, 5f, 8f)]);
        for (int i = 0; i < bitten.Count; i++)
            for (int j = i + 1; j < bitten.Count; j++)
                Assert.False(
                    MathF.Min(bitten[i].MaxU, bitten[j].MaxU) - MathF.Max(bitten[i].MinU, bitten[j].MinU) > 1e-6f &&
                    MathF.Min(bitten[i].MaxV, bitten[j].MaxV) - MathF.Max(bitten[i].MinV, bitten[j].MinV) > 1e-6f,
                    $"{bitten[i]} overlaps {bitten[j]}");
    }

    /// <summary>
    /// And no two of them may meet wall to wall either.
    ///
    /// A hole that bites one corner of a piece leaves an upright part beside the opening and a
    /// short part over it, and those share a face exactly. Two boxes meeting that way are the one
    /// thing the boolean cannot be asked to union, and welding turns the shared edge into one with
    /// four triangles on it - so a wall with a window came back unprintable, raised or cut, while
    /// the same siding on a plain cube was fine. The gap reads as the butt joint a fitter leaves
    /// at a reveal.
    /// </summary>
    [Fact]
    public void TwoPartsOfABittenPieceNeverShareAWall()
    {
        // A course straddling the top edge of a window, which is the case that tore.
        var strip = new Rect2(-28f, 6f, 28f, 15.4f);
        var window = new Rect2(-12.2f, -12.2f, 12.2f, 12.2f);

        Assert.Equal(3, TileSolid.Without(strip, [window]).Count);

        const float Gap = 0.6f;
        var parts = TileSolid.Without(strip, [window], Gap);

        log.WriteLine($"{parts.Count} parts: {string.Join(", ", parts)}");

        for (int i = 0; i < parts.Count; i++)
            for (int j = i + 1; j < parts.Count; j++)
            {
                Rect2 a = parts[i], b = parts[j];

                bool sideBySide =
                    MathF.Abs(a.MaxU - b.MinU) < Gap - 1e-3f || MathF.Abs(b.MaxU - a.MinU) < Gap - 1e-3f;

                bool alsoOverlapUp =
                    MathF.Min(a.MaxV, b.MaxV) - MathF.Max(a.MinV, b.MinV) > 1e-6f;

                Assert.False(sideBySide && alsoOverlapUp, $"{a} meets {b} wall to wall");
            }
    }

    /// <summary>
    /// Across and Up move the pattern. A stamp is placed somewhere on the face; a texture fills
    /// it, so the only thing those two can mean is where the courses start - which is what
    /// somebody lining a joint up with a window wants from them.
    /// </summary>
    [Fact]
    public void AcrossAndUpMoveTheLattice()
    {
        var courses = Roof();

        var where = TileSolid.Pieces(courses, 60f, 60f);
        var moved = TileSolid.Pieces(
            courses, 60f, 60f, null, false, new Vector2(0f, courses.CourseMm / 3f));

        float Lowest(List<Rect2> pieces) => pieces.Min(p => p.MinV);

        log.WriteLine($"courses start at {Lowest(where):0.###}, moved to {Lowest(moved):0.###}");

        Assert.NotEqual(Lowest(where), Lowest(moved), 3);
    }

    /// <summary>
    /// And the same on a real wall with a window cut through it, which is where it was noticed:
    /// the siding ran straight across the opening, because building the geometry knows nothing
    /// about the hole unless it is told.
    /// </summary>
    [Fact]
    public void SidingStopsAtAWindowInsteadOfRunningAcrossIt()
    {
        // A 24 mm window punched right through a 60 mm wall, the way the tool makes one.
        var wall = LocalCsg.Subtract(Primitives.Box(60f, 8f, 60f), Primitives.Box(24f, 40f, 24f));

        // Picked well above the opening, since the middle of the wall is now fresh air.
        var face = FacePatch.Find(wall, new Vector3(0f, -4f, 20f), -Vector3.UnitY)!;
        var flat = new PlanarSurface(face);

        var room = TileRoom.Of(flat);
        Assert.NotNull(room);
        Assert.NotEmpty(room!.Holes);

        var courses = SurfaceTexture.CoursesOf(
            new TextureOptions(TextureKind.Siding, 7f, 0.4f, 45f), 0.2f)!.Value;

        var over = TileSolid.Pieces(courses, 56f, 56f);
        var clear = TileSolid.Pieces(courses, 56f, 56f, room);

        log.WriteLine($"{over.Count} pieces laid over the window, {clear.Count} laid clear of it");

        Assert.True(clear.Count > over.Count,
            "a course broken at the reveal should come back as more pieces, not fewer");

        // Nothing may stand over the opening.
        var hole = room.Holes[0];

        Assert.All(clear, p => Assert.False(
            MathF.Min(p.MaxU, hole.MaxU) - MathF.Max(p.MinU, hole.MinU) > 1e-3f &&
            MathF.Min(p.MaxV, hole.MaxV) - MathF.Max(p.MinV, hole.MinV) > 1e-3f,
            $"{p} stands over the window at {hole}"));
    }

    /// <summary>
    /// A cutter runs past an edge where a raised piece keeps clear of one.
    ///
    /// Stopping short is what tore. The material between a cut and the edge it stopped at stands
    /// as a rib a fifth of a millimetre wide and as deep as the cut - seven to one on an ordinary
    /// course of siding - and the boolean cannot resolve a knife edge like that. Running past
    /// takes a hair off the reveal instead, which is what the engraver has always done.
    /// </summary>
    [Fact]
    public void ACutterOvershootsWhereARaisedPieceKeepsClear()
    {
        var wall = LocalCsg.Subtract(Primitives.Box(60f, 8f, 60f), Primitives.Box(24f, 40f, 24f));
        var flat = new PlanarSurface(FacePatch.Find(wall, new Vector3(0f, -4f, 20f), -Vector3.UnitY)!);

        var courses = SurfaceTexture.CoursesOf(
            new TextureOptions(TextureKind.Siding, 10f, 0.6f, 45f), 0.8f)!.Value;

        var raised = TileSolid.Pieces(courses, 56f, 56f, TileRoom.Of(flat, wall));
        var cut = TileSolid.Pieces(
            courses, 56f, 56f, TileRoom.Of(flat, wall, -Engraver.EdgeOvershoot), cutting: true);

        // How near the window's own edge the pieces on its left-hand side come.
        static float Nearest(List<Rect2> pieces) => pieces
            .Where(p => p.MinV < 12f && p.MaxV > -12f && p.MaxU <= 0f)
            .Max(p => p.MaxU);

        log.WriteLine($"raised stops at {Nearest(raised):0.###}, cut reaches {Nearest(cut):0.###}, "
                    + "where the window's edge is -12");

        Assert.True(Nearest(raised) <= -12f - TileRoom.ClearanceMm + 1e-3f,
            $"a raised piece comes to {Nearest(raised):0.###}, inside the window's edge at -12");

        Assert.True(Nearest(cut) >= -12f + Engraver.EdgeOvershoot - 1e-3f,
            $"a cutter stops at {Nearest(cut):0.###} and leaves a rib standing to the window at -12");
    }

    /// <summary>
    /// And wrapped round a tower, where there is no face patch to ask and the wall has to be
    /// found by asking the solid itself.
    /// </summary>
    [Fact]
    public void TilesStopAtAWindowRoundATowerToo()
    {
        const float Radius = 20f;

        // A window punched right through the tower, so it opens at both ends of the X axis - and
        // the layout's origin, which is the angle the wrap starts from, sits in one of them.
        var tower = LocalCsg.Subtract(
            Primitives.Prism(Radius, 60f, 48), Primitives.Box(60f, 12f, 12f));

        var barrel = new CylinderSurface(Vector3.Zero, Radius);
        var room = TileRoom.Of(barrel, tower);

        Assert.NotNull(room);
        Assert.True(room!.Scans, "a wrapped room has no outline, so it is walked rather than clipped");

        Assert.False(room.Supports(new Vector2(0f, 0f)), "the middle of the window reads as wall");
        Assert.True(room.Supports(new Vector2(0f, 22f)), "the wall above the window reads as fresh air");

        var courses = SurfaceTexture.CoursesOf(
            new TextureOptions(TextureKind.RoofTiles, 8f, 0.4f, 45f), 0.3f)!.Value;

        float round = 2f * MathF.PI * Radius;
        var pieces = TileSolid.Pieces(courses, round, 52f, room);

        log.WriteLine($"{pieces.Count} tiles round a {round:0.#} mm tower with a window in it");
        Assert.NotEmpty(pieces);

        Assert.All(pieces, p => Assert.False(
            p.Contains(0f, 0f), $"{p} stands over the window"));
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
