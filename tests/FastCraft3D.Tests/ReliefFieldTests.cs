using System.Numerics;
using FastCraft3D.Geometry;
using FastCraft3D.Geometry.Csg;
using FastCraft3D.Geometry.Engraving;
using Xunit;
using Xunit.Abstractions;

namespace FastCraft3D.Tests;

/// <summary>
/// The textures that have a shape rather than an outline.
///
/// Two things matter and neither is how they look. The solid has to come back closed, because it
/// is unioned onto the model and a torn one takes the model with it. And the sample count has to
/// follow what the profile actually needs: a ramp is two lines however tall the wall is, and a
/// profile that forgets that costs as many triangles as a grain does for a shape that has none.
/// </summary>
public class ReliefFieldTests(ITestOutputHelper log)
{
    private static FacePatch FaceOf(Mesh wall, Vector3 at, Vector3 way) => FacePatch.Find(wall, at, way)!;

    private static Mesh Wall(float side) =>
        MeshTransform.Transformed(Primitives.Box(side, side, side), Matrix4x4.CreateTranslation(0, 0, side / 2f));

    private static (Mesh Wall, PlanarSurface Face) Upright(float side)
    {
        var wall = Wall(side);
        return (wall, new PlanarSurface(FaceOf(wall, new Vector3(0, -side / 2f, side / 2f), -Vector3.UnitY)));
    }

    public static TheoryData<string, IRelief> Profiles => new()
    {
        { "siding", new SurfaceProfiles.Siding(6f, 1.2f) },
        { "roof", new SurfaceProfiles.Pantile(8f, 5f, 1.4f, 0.6f) },
        { "boards", new SurfaceProfiles.Boarding(9f, 1f, 0.5f, 0.4f) }
    };

    [Theory]
    [MemberData(nameof(Profiles))]
    public void TheSolidComesBackClosed(string what, IRelief relief)
    {
        var (_, face) = Upright(60f);
        var built = ReliefField.Build(face, relief, 58f, 58f);

        var health = built.CheckHealth();
        log.WriteLine($"{what}: {built.TriangleCount:N0} triangles, {health.Describe()}");

        Assert.True(built.TriangleCount > 0, $"{what} produced nothing");
        Assert.True(health.IsWatertight, $"{what} came back {health.Describe()}");
    }

    [Theory]
    [MemberData(nameof(Profiles))]
    public void ItStandsProudOfTheFaceAndNeverBehindIt(string what, IRelief relief)
    {
        var (_, face) = Upright(60f);
        var built = ReliefField.Build(face, relief, 58f, 58f);

        // The face is at y = -30 and the relief stands out along -Y, so nothing may be deeper into
        // the wall than the sink, and something must stand clear of it.
        float deepest = built.Positions.Max(p => p.Y);
        float highest = -built.Positions.Min(p => p.Y);

        Assert.True(deepest <= -30f + ReliefField.SinkMm + 1e-3f,
            $"{what} reaches {deepest:0.##} mm into the wall");
        Assert.True(highest > 30f + 0.3f, $"{what} stands only {highest - 30f:0.##} mm proud");
    }

    /// <summary>
    /// A ramp is two lines. Siding is nothing but ramps, so a wall of it costs a few hundred
    /// triangles however big the wall is - and laid on an even grid instead it cost as many as a
    /// grain does, for a shape with no detail in it at all.
    /// </summary>
    [Fact]
    public void ASlopeCostsNothingAndAGrainCostsWhatItMust()
    {
        var siding = ReliefField.Cost(new SurfaceProfiles.Siding(6f, 1.2f), 120f, 80f);
        var roof = ReliefField.Cost(new SurfaceProfiles.Pantile(8f, 5f, 1.4f, 0.6f), 120f, 80f);
        var boards = ReliefField.Cost(new SurfaceProfiles.Boarding(9f, 1f, 0.5f, 0.4f), 120f, 80f);

        log.WriteLine($"on a 120 x 80 face: siding {siding.Triangles:N0}, roof {roof.Triangles:N0}, " +
                      $"boards {boards.Triangles:N0} triangles");

        Assert.Equal(2, siding.Across);
        Assert.True(siding.Triangles < 400, $"{siding.Triangles} triangles for a wall of ramps");
        Assert.True(roof.Triangles < 60_000, $"{roof.Triangles} triangles for a roof");

        // The grain is the one that has to be sampled, and it should be the dearest by a long way.
        Assert.True(boards.Triangles > roof.Triangles, "boarding came out cheaper than a plain roof");
    }

    /// <summary>
    /// The profile is what the samples say it is. A lap that slopes out going down and steps back
    /// at the foot of the course is the whole of what makes siding read as siding.
    /// </summary>
    [Fact]
    public void SidingSlopesOutGoingDownAndStepsBackAtEachCourse()
    {
        var siding = new SurfaceProfiles.Siding(courseMm: 6f, depthMm: 1.2f);

        float head = siding.Height(new Vector2(0, 3f));
        float middle = siding.Height(new Vector2(0, 0f));
        float foot = siding.Height(new Vector2(0, -2.99f));
        float below = siding.Height(new Vector2(0, -3.01f));

        Assert.True(head < 0.05f, $"the head of a course stands {head:0.##} mm proud");
        Assert.True(middle > head && foot > middle, "the course does not slope out going down");
        Assert.True(foot > 1.1f, $"the foot of a course reaches only {foot:0.##} mm");
        Assert.True(below < 0.05f, $"the course below starts {below:0.##} mm proud - there is no step");
    }

    /// <summary>A tile is a rectangle with a joint each side, and the joints do not line up.</summary>
    [Fact]
    public void RoofTilesAreStaggeredAndJointedRightThrough()
    {
        var roof = new SurfaceProfiles.Pantile(tileMm: 8f, courseMm: 5f, depthMm: 1.4f, jointMm: 0.6f);

        // A joint sits at every whole tile in an unstaggered course, and half a tile over in the
        // course below it.
        Assert.True(roof.Height(new Vector2(0f, 2f)) < 0.01f, "no joint where one was laid");
        Assert.True(roof.Height(new Vector2(4f, 2f)) > 0.1f, "the middle of a tile is jointed");

        Assert.True(roof.Height(new Vector2(4f, -3f)) < 0.01f, "the course below is not staggered");
        Assert.True(roof.Height(new Vector2(0f, -3f)) > 0.1f, "the course below is jointed in line");
    }

    /// <summary>
    /// Grain has to be printable: ridges no finer than a nozzle is wide, and a relief deep enough
    /// that a printer lays something different where they are.
    /// </summary>
    [Fact]
    public void GrainIsCoarseEnoughForAFourTenthsNozzle()
    {
        var boards = new SurfaceProfiles.Boarding(boardMm: 9f, depthMm: 1f, jointMm: 0.5f, nozzleMm: 0.4f);

        // Walked along one board, across the grain, which is the direction it changes fastest.
        var heights = new List<float>();
        for (float y = -3.5f; y < -0.5f; y += 0.05f) heights.Add(boards.Height(new Vector2(0f, y)));

        float low = heights.Min(), high = heights.Max();
        Assert.True(high - low > 0.05f, $"the grain is only {high - low:0.###} mm deep - it will not show");
        Assert.True(high - low < 0.5f, $"the grain is {high - low:0.##} mm deep - that is not grain");

        // How often it turns over: a ridge and a trough must be at least a nozzle apart.
        int turns = 0;
        for (int i = 1; i + 1 < heights.Count; i++)
            if ((heights[i] - heights[i - 1]) * (heights[i + 1] - heights[i]) < 0) turns++;

        float perMm = turns / 3f;
        Assert.True(perMm < 1f / 0.4f, $"the grain turns over {perMm:0.#} times a millimetre");
    }

    /// <summary>
    /// And the whole way through: unioned onto the wall it came from, the result is still printable.
    /// </summary>
    [Theory]
    [MemberData(nameof(Profiles))]
    public void ItGoesOntoTheWallAndComesBackPrintable(string what, IRelief relief)
    {
        var (wall, face) = Upright(40f);
        var built = ReliefField.Build(face, relief, 38f, 38f);

        var joined = LocalCsg.Union(wall, built);

        log.WriteLine($"{what}: {joined.TriangleCount:N0} triangles after the union");

        Assert.True(joined.CheckHealth().IsWatertight, $"{what} came back {joined.CheckHealth().Describe()}");
        Assert.True(joined.ComputeSignedVolume() > wall.ComputeSignedVolume(), $"{what} added nothing");
    }
}
