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
        { "boards", new SurfaceProfiles.Boarding(9f, 1f, 0.5f, 0.4f) },
        { "fine boards", new SurfaceProfiles.Boarding(4f, 0.6f, 0.4f, 0.4f) },
        { "long boards", new SurfaceProfiles.Boarding(12f, 1.4f, 0.6f, 0.4f, 40f) }
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
    /// A grain is the one profile that has to be sampled evenly, and it is held to what a nozzle
    /// will lay rather than to how fine the maths could go.
    ///
    /// This used to compare it against siding and a roof, which cost almost nothing because a ramp
    /// is two lines however long it is. Those two are slabs now and have no samples at all, so
    /// what is left to check is that the grain is sampled to the nozzle and stays on the right
    /// side of what a face is worth.
    /// </summary>
    [Fact]
    public void AGrainCostsWhatItMustAndNoMore()
    {
        var boards = ReliefField.Cost(new SurfaceProfiles.Boarding(9f, 1f, 0.5f, 0.4f), 120f, 80f);
        var coarse = ReliefField.Cost(new SurfaceProfiles.Boarding(9f, 1f, 0.5f, 1.2f), 120f, 80f);

        log.WriteLine($"on a 120 x 80 face: boards {boards.Triangles:N0}, " +
                      $"at a coarse nozzle {coarse.Triangles:N0} triangles");

        Assert.True(boards.CanBuild, boards.Refusal);
        Assert.True(boards.Triangles > 10_000, $"{boards.Triangles} triangles is no grain at all");

        // A coarser nozzle is what the preview asks for, and it has to be markedly cheaper or
        // there was no point asking.
        Assert.True(coarse.Triangles < boards.Triangles / 2,
            $"a coarse grain cost {coarse.Triangles:N0} against {boards.Triangles:N0}");
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
