using System.Numerics;
using FastCraft3D.Geometry;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>
/// Cutting a mesh with a plane for the split preview.
///
/// The split itself is a boolean and gives back two closed halves; this only has to show what
/// that would do, fast enough to follow a plane being dragged. What it does promise is that
/// nothing on the wrong side of the plane survives, nothing on the right side is lost, and the
/// face the cut exposes is closed over so the piece still reads as solid.
/// </summary>
public class PlaneClipTests
{
    private static Mesh Cube(float size = 20) => Primitives.Box(size, size, size);

    private static float Furthest(Mesh mesh, Vector3 normal, float offset) =>
        mesh.Positions.Count == 0 ? 0 : mesh.Positions.Max(p => offset - Vector3.Dot(normal, p));

    /// <summary>
    /// The whole point of capping: the piece has to look like solid material that has been cut,
    /// not like a shell with the lid off. Watertight is the strong form of that - every edge
    /// meets another and they agree which way round they go - and it fails if the cap is left
    /// out, wound backwards, or triangulated wrongly.
    /// </summary>
    [Fact]
    public void TheCutFaceIsClosedOverAndTheHalfIsStillSolid()
    {
        var half = PlaneClip.Keep(Cube(), Matrix4x4.Identity, Vector3.UnitZ, 0);
        var health = half.CheckHealth();

        Assert.True(health.IsWatertight, health.Describe());
    }

    [Fact]
    public void ASlopingCutIsClosedOverToo()
    {
        var normal = Vector3.Normalize(new Vector3(1, 2, 3));

        var half = PlaneClip.Keep(Primitives.Prism(10, 20, 36), Matrix4x4.Identity, normal, 1);

        Assert.True(half.CheckHealth().IsWatertight, half.CheckHealth().Describe());
    }

    /// <summary>The face is filled at the plane and nowhere else.</summary>
    [Fact]
    public void TheCapSitsOnThePlaneItself()
    {
        var half = PlaneClip.Keep(Cube(), Matrix4x4.Identity, Vector3.UnitZ, 4);

        Assert.Equal(4f, half.ComputeBounds().Min.Z, 3);
    }

    [Fact]
    public void LeavingItOpenIsStillOnOffer()
    {
        var open = PlaneClip.Keep(Cube(), Matrix4x4.Identity, Vector3.UnitZ, 0, cap: false);

        Assert.False(open.CheckHealth().IsWatertight);
    }

    /// <summary>
    /// The one that made an imported scan come out hollow. A plane through a whole ring of the
    /// mesh's own corners leaves those corners on neither side, and the two triangles either
    /// side of each edge have to agree about that or the ring of cut edges never closes.
    /// </summary>
    [Fact]
    public void ACutStraightThroughTheMeshOwnCornersStillClosesOver()
    {
        var ball = Primitives.Sphere(10, 32, 16);
        float through = ball.Positions.First(p => p.Z is > 1f and < 9f).Z;

        var half = PlaneClip.Keep(ball, Matrix4x4.Identity, Vector3.UnitZ, through);

        Assert.True(half.CheckHealth().IsWatertight, half.CheckHealth().Describe());
    }

    /// <summary>
    /// A section with a hole in it is filled as a ring, not as a disc - and the ring only comes
    /// out at all if the edges round the cut were handed over the same way round.
    ///
    /// That is the one that let an imported scan through: which way round an edge came back
    /// depended on which corner of its triangle was over the plane, so half of them were
    /// backwards. Every triangle of a box happens to be the same way up, which is why a box
    /// closed over perfectly while a 200,000 triangle import was cut open and left that way.
    /// </summary>
    [Fact]
    public void ASectionWithAHoleInItIsFilledAsARing()
    {
        var ring = Primitives.Torus(20, 6, 48, 24);

        var half = PlaneClip.Keep(ring, Matrix4x4.Identity, Vector3.UnitZ, 1);
        var health = half.CheckHealth();

        Assert.True(health.IsWatertight, health.Describe());
    }

    /// <summary>
    /// A model with a hole of its own leaves a cut that does not come full circle. That opening
    /// is left as it is rather than filled with a guess - but everything else still gets cut.
    /// </summary>
    [Fact]
    public void AModelWithAHoleOfItsOwnIsStillCut()
    {
        var torn = new Mesh();
        var cube = Cube();

        // Every triangle but the last, so one facet of the cube is missing.
        for (int t = 0; t + 5 < cube.Indices.Count; t += 3)
        {
            torn.AddTriangle(
                cube.Positions[cube.Indices[t]],
                cube.Positions[cube.Indices[t + 1]],
                cube.Positions[cube.Indices[t + 2]]);
        }

        var kept = PlaneClip.Keep(torn, Matrix4x4.Identity, Vector3.UnitZ, 0);

        Assert.True(kept.TriangleCount > 0);
        Assert.True(Furthest(kept, Vector3.UnitZ, 0) < 1e-3f);
    }

    [Fact]
    public void NothingOnTheFarSideOfThePlaneSurvives()
    {
        var kept = PlaneClip.Keep(Cube(), Matrix4x4.Identity, Vector3.UnitZ, 0);

        Assert.True(Furthest(kept, Vector3.UnitZ, 0) < 1e-4f, "something below the plane was kept");
    }

    [Fact]
    public void APlaneClearOfTheSolidKeepsAllOfIt()
    {
        var cube = Cube();
        var kept = PlaneClip.Keep(cube, Matrix4x4.Identity, Vector3.UnitZ, -50);

        Assert.Equal(cube.TriangleCount, kept.TriangleCount);
    }

    [Fact]
    public void APlaneTheOtherSideOfTheSolidKeepsNoneOfIt()
    {
        var kept = PlaneClip.Keep(Cube(), Matrix4x4.Identity, Vector3.UnitZ, 50);

        Assert.Equal(0, kept.TriangleCount);
    }

    /// <summary>
    /// A cube cut through the middle keeps the half above the plane, so its bounds reach the
    /// plane and no further, and the sides are still their full width.
    /// </summary>
    [Fact]
    public void HalfACubeIsHalfACube()
    {
        var kept = PlaneClip.Keep(Cube(), Matrix4x4.Identity, Vector3.UnitZ, 0);
        var bounds = kept.ComputeBounds();

        Assert.Equal(0f, bounds.Min.Z, 3);
        Assert.Equal(10f, bounds.Max.Z, 3);
        Assert.Equal(-10f, bounds.Min.X, 3);
        Assert.Equal(10f, bounds.Max.X, 3);
    }

    /// <summary>Turning the plane round keeps the other half, and the two together are the whole.</summary>
    [Fact]
    public void TheTwoSidesBetweenThemHoldTheWholeSurface()
    {
        var cube = Cube();

        var above = PlaneClip.Keep(cube, Matrix4x4.Identity, Vector3.UnitZ, 3, cap: false);
        var below = PlaneClip.Keep(cube, Matrix4x4.Identity, -Vector3.UnitZ, -3, cap: false);

        Assert.Equal(0f, Area(above) + Area(below) - Area(cube), 1);

        static double Area(Mesh mesh)
        {
            double total = 0;
            for (int t = 0; t + 2 < mesh.Indices.Count; t += 3)
            {
                Vector3 a = mesh.Positions[mesh.Indices[t]];
                Vector3 b = mesh.Positions[mesh.Indices[t + 1]];
                Vector3 c = mesh.Positions[mesh.Indices[t + 2]];
                total += Vector3.Cross(b - a, c - a).Length() / 2.0;
            }
            return total;
        }
    }

    /// <summary>
    /// The cut is measured in world space, so an object standing away from the origin is cut
    /// where the plane crosses it rather than where it would if it were still at the middle.
    /// </summary>
    [Fact]
    public void TheCutIsWhereThePlaneMeetsTheObjectWhereItActuallyStands()
    {
        var lifted = Matrix4x4.CreateTranslation(0, 0, 100);

        var kept = PlaneClip.Keep(Cube(), lifted, Vector3.UnitZ, 100);
        var bounds = kept.ComputeBounds();

        Assert.Equal(100f, bounds.Min.Z, 3);
        Assert.Equal(110f, bounds.Max.Z, 3);
    }

    [Fact]
    public void ScalingAndTurningAreTakenIntoAccountToo()
    {
        var doubled = Matrix4x4.CreateScale(2f);

        var kept = PlaneClip.Keep(Cube(), doubled, Vector3.UnitZ, 0);
        var bounds = kept.ComputeBounds();

        Assert.Equal(20f, bounds.Max.Z, 3);
        Assert.Equal(-20f, bounds.Min.X, 3);
    }

    /// <summary>A plane at any angle, not just the three the axis buttons offer.</summary>
    [Fact]
    public void ASlopingPlaneCutsJustAsCleanly()
    {
        var normal = Vector3.Normalize(new Vector3(1, 2, 3));

        var kept = PlaneClip.Keep(Cube(), Matrix4x4.Identity, normal, 2);

        Assert.True(kept.TriangleCount > 0, "the plane crosses the cube, so something should be kept");
        Assert.True(Furthest(kept, normal, 2) < 1e-3f, "something beyond the plane was kept");
    }

    /// <summary>
    /// A triangle with one corner over the plane leaves a four-sided piece behind, which has to
    /// come back as two triangles rather than being dropped or left as a hole.
    /// </summary>
    [Fact]
    public void ATriangleCutAcrossComesBackWhole()
    {
        var mesh = new Mesh();
        mesh.AddTriangle(new Vector3(0, 0, -5), new Vector3(10, 0, -5), new Vector3(0, 0, 5));

        var kept = PlaneClip.Keep(mesh, Matrix4x4.Identity, -Vector3.UnitZ, 0, cap: false);

        Assert.Equal(2, kept.TriangleCount);
        Assert.True(Furthest(kept, -Vector3.UnitZ, 0) < 1e-4f);
    }
}
