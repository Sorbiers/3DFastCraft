using System.Numerics;
using FastCraft3D.Geometry;
using FastCraft3D.Geometry.Engraving;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>
/// Finding the flat face under the cursor. A box face is two triangles and a boolean result's
/// face can be dozens, but the person clicking sees one surface either way.
/// </summary>
public class FacePatchTests
{
    private static Mesh Box(float size = 20) => Primitives.Box(size, size, size);

    [Fact]
    public void ABoxFaceIsBothOfItsTriangles()
    {
        var mesh = Box();

        var face = FacePatch.Find(mesh, new Vector3(0, 0, 10), Vector3.UnitZ);

        Assert.NotNull(face);
        Assert.Equal(2, face.Triangles.Count);
        Assert.Equal(400f, face.Area, 2);            // 20 x 20
        Assert.Equal(new Vector2(20, 20), face.Size);
    }

    [Fact]
    public void TheFaceStopsAtTheEdgeRatherThanWrappingRoundTheBox()
    {
        var mesh = Box();

        var face = FacePatch.Find(mesh, new Vector3(0, 0, 10), Vector3.UnitZ);

        // Every vertex of the patch must sit on the top; wrapping would drag in z = -10.
        foreach (int t in face!.Triangles)
            for (int k = 0; k < 3; k++)
                Assert.Equal(10f, mesh.Positions[mesh.Indices[t + k]].Z, 4);
    }

    [Fact]
    public void EachSideOfABoxIsItsOwnFace()
    {
        var mesh = Box();

        var top = FacePatch.Find(mesh, new Vector3(0, 0, 10), Vector3.UnitZ);
        var side = FacePatch.Find(mesh, new Vector3(10, 0, 0), Vector3.UnitX);

        Assert.Equal(Vector3.UnitZ, top!.Normal);
        Assert.Equal(Vector3.UnitX, side!.Normal);
        Assert.Equal(2, side.Triangles.Count);
    }

    /// <summary>
    /// The normal matters: the near and far walls of a thin plate are both within tolerance of a
    /// click, and picking the wrong one would engrave the back.
    /// </summary>
    [Fact]
    public void AThinPlateEngravesTheSideThatWasClicked()
    {
        var mesh = Primitives.Box(40, 40, 0.6f);

        var front = FacePatch.Find(mesh, new Vector3(0, 0, 0.3f), Vector3.UnitZ);
        var back = FacePatch.Find(mesh, new Vector3(0, 0, -0.3f), -Vector3.UnitZ);

        Assert.Equal(Vector3.UnitZ, front!.Normal);
        Assert.Equal(-Vector3.UnitZ, back!.Normal);
    }

    /// <summary>A curved surface is not a face: the patch must not walk around the barrel.</summary>
    [Fact]
    public void ACylinderSideIsOneFacetNotTheWholeBarrel()
    {
        var mesh = Primitives.Create(PrimitiveKind.Cylinder);
        var bounds = mesh.ComputeBounds();
        float radius = bounds.Size.X / 2;

        var face = FacePatch.Find(mesh, new Vector3(radius, 0, 0), Vector3.UnitX);

        Assert.NotNull(face);
        Assert.True(face.Triangles.Count <= 4, $"walked onto {face.Triangles.Count} triangles");
    }

    [Fact]
    public void TheFlatEndOfACylinderIsAWholeFace()
    {
        var mesh = Primitives.Create(PrimitiveKind.Cylinder);
        float top = mesh.ComputeBounds().Max.Z;

        var face = FacePatch.Find(mesh, new Vector3(0, 0, top), Vector3.UnitZ);

        Assert.NotNull(face);
        Assert.True(face.Triangles.Count > 8, "the cap should be a fan, not a single triangle");
        Assert.Equal(Vector3.UnitZ, face.Normal);
    }

    /// <summary>
    /// Courses have to come out level. The in-plane axes are chosen for that, not taken from
    /// whatever basis the cross products happened to produce.
    /// </summary>
    [Fact]
    public void URunsHorizontallyOnAnyUprightFace()
    {
        foreach (var normal in new[] { Vector3.UnitX, -Vector3.UnitX, Vector3.UnitY, -Vector3.UnitY })
        {
            var (u, v) = FacePatch.PlaneAxes(normal);

            Assert.Equal(0f, u.Z, 5);              // U is level
            Assert.Equal(1f, MathF.Abs(v.Z), 5);   // so V climbs the wall
            Assert.Equal(0f, (normal - Vector3.Cross(u, v)).Length(), 4);
        }
    }

    [Fact]
    public void ALevelFaceStillGetsAUsableFrame()
    {
        var (u, v) = FacePatch.PlaneAxes(Vector3.UnitZ);

        Assert.Equal(1f, u.Length(), 5);
        Assert.Equal(1f, v.Length(), 5);
        Assert.Equal(0f, (Vector3.UnitZ - Vector3.Cross(u, v)).Length(), 4);
    }

    [Fact]
    public void TheBoundaryOfABoxFaceIsItsFourEdges()
    {
        var face = FacePatch.Find(Box(), new Vector3(0, 0, 10), Vector3.UnitZ);

        // Two triangles sharing a diagonal: four outer edges, and the diagonal is not one.
        Assert.Equal(4, face!.Boundary.Count);
    }

    [Fact]
    public void AClickThatMissesFindsNothing()
    {
        Assert.Null(FacePatch.Find(Box(), new Vector3(0, 0, 500), Vector3.UnitZ));
        Assert.Null(FacePatch.Find(new Mesh(), Vector3.Zero, Vector3.UnitZ));
    }
}

/// <summary>
/// The cutter. Grooves are handed over as rectangles that may overlap, and they have to come
/// back as one solid - concatenated boxes would leave the walls of every overlap inside the
/// result.
/// </summary>
public class GrooveSolidTests
{
    private static FacePatch TopOf(Mesh mesh) =>
        FacePatch.Find(mesh, new Vector3(0, 0, mesh.ComputeBounds().Max.Z), Vector3.UnitZ)!;

    private static FacePatch Face => TopOf(Primitives.Box(100, 100, 10));

    private static double VolumeOf(Mesh mesh) => mesh.ComputeSignedVolume();

    [Fact]
    public void OneRectangleBecomesABoxOfTheRightSize()
    {
        var solid = GrooveSolid.Build([new Rect2(0, 0, 10, 4)], Face, depth: 0.5f);

        Assert.True(solid.CheckHealth().IsWatertight);

        // The cutter stands proud of the surface, so it is deeper than the cut it makes.
        Assert.Equal(10 * 4 * (0.5 + GrooveSolid.Lift), VolumeOf(solid), 3);
    }

    [Fact]
    public void TheCutterIsWoundOutwardNotInsideOut()
    {
        var solid = GrooveSolid.Build([new Rect2(0, 0, 10, 4)], Face, depth: 0.5f);

        Assert.True(VolumeOf(solid) > 0, "negative volume means the winding is inverted");
    }

    /// <summary>
    /// The point of the whole cell grid: two rectangles crossing must produce the volume of
    /// their union, with no wall left standing where they overlap.
    /// </summary>
    [Fact]
    public void CrossingRectanglesMergeIntoOneSolid()
    {
        var across = new Rect2(0, 4, 20, 6);   // 20 x 2
        var down = new Rect2(9, 0, 11, 10);    // 2 x 10, overlapping 2 x 2

        var solid = GrooveSolid.Build([across, down], Face, depth: 1f);

        double expected = (20 * 2 + 2 * 10 - 2 * 2) * (1 + GrooveSolid.Lift);
        Assert.Equal(expected, VolumeOf(solid), 3);
        Assert.True(solid.CheckHealth().IsWatertight);
    }

    [Fact]
    public void IdenticalRectanglesDoNotDoubleUp()
    {
        var rectangle = new Rect2(0, 0, 8, 3);

        var solid = GrooveSolid.Build([rectangle, rectangle, rectangle], Face, depth: 0.4f);

        Assert.Equal(8 * 3 * (0.4 + GrooveSolid.Lift), VolumeOf(solid), 3);
        Assert.True(solid.CheckHealth().IsWatertight);
    }

    [Fact]
    public void SeparateRectanglesStaySeparate()
    {
        var solid = GrooveSolid.Build(
            [new Rect2(0, 0, 5, 5), new Rect2(20, 20, 25, 25)], Face, depth: 1f);

        Assert.Equal(2, MeshComponents.Count(solid));
        Assert.Equal(2 * 25 * (1 + GrooveSolid.Lift), VolumeOf(solid), 3);
    }

    /// <summary>A whole brick wall's worth of grooves, still one clean solid.</summary>
    [Fact]
    public void AFullPatternStaysWatertight()
    {
        var face = Face;
        var options = new EngraveOptions(PatternKind.Brick, Size: 20, GrooveWidth: 1.5f, Depth: 0.6f);
        var grooves = GroovePattern.Build(options, Engraver.PatternArea(face)).Rectangles;

        var solid = GrooveSolid.Build(grooves, face, options.Depth);

        Assert.True(grooves.Count > 50, $"only {grooves.Count} grooves");
        Assert.True(solid.CheckHealth().IsWatertight, solid.CheckHealth().Describe());
        Assert.True(VolumeOf(solid) > 0);
    }

    [Fact]
    public void NothingInMeansNothingOut()
    {
        Assert.Equal(0, GrooveSolid.Build([], Face, 1f).TriangleCount);
        Assert.Equal(0, GrooveSolid.Build([new Rect2(0, 0, 0, 0)], Face, 1f).TriangleCount);
        Assert.Equal(0, GrooveSolid.Build([new Rect2(0, 0, 5, 5)], Face, depth: 0).TriangleCount);
    }
}
