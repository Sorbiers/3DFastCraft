using System.Numerics;
using FastCraft3D.Geometry;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>
/// Sliding one shape at another until they touch, measured on the triangles rather than on the
/// bounding box. Most of these are cases where the box is wrong.
/// </summary>
public class MeshSweepTests
{
    private static Mesh At(Mesh mesh, float x, float y = 0, float z = 0) =>
        MeshTransform.Transformed(mesh, Matrix4x4.CreateTranslation(x, y, z));

    private static Mesh Turned(Mesh mesh, float degrees, Vector3 axis) =>
        MeshTransform.Transformed(
            mesh, Matrix4x4.CreateFromAxisAngle(axis, degrees * MathF.PI / 180f));

    private static float Travel(Mesh mover, Mesh blocker, Axis axis, int direction) =>
        MeshSweep.Distance([mover], [blocker], axis, direction);

    /// <summary>
    /// A square facing down at the given height, with one near-vertical sliver hanging off its
    /// edge - the shape of the triangles a boolean leaves down the side of a cylinder.
    /// </summary>
    private static Mesh SquareWithASliver(float height, float top)
    {
        var mesh = new Mesh();

        mesh.AddTriangle(new(-10, -10, height), new(10, 10, height), new(10, -10, height));
        mesh.AddTriangle(new(-10, -10, height), new(-10, 10, height), new(10, 10, height));

        // A wall a hair off vertical: forty millimetres tall, a millionth wide flattened.
        mesh.AddTriangle(new(10, -10, height), new(10, 10, top), new(10.000001f, 10, top));

        return mesh;
    }

    [Fact]
    public void ASliverDownASideIsNotMistakenForTheSurfaceBelowIt()
    {
        var mover = SquareWithASliver(30f, 70f);
        var floor = new Mesh();
        floor.AddTriangle(new(-20, -20, 0), new(20, -20, 0), new(20, 20, 0));
        floor.AddTriangle(new(-20, -20, 0), new(20, 20, 0), new(-20, 20, 0));

        // Thirty millimetres to fall, and the sliver is no part of the answer: its plane is too
        // steep to be evaluated, and before it was clamped it put the drop at nothing.
        Assert.Equal(30f - MeshSweep.ClearanceMm, Travel(mover, floor, Axis.Z, -1), 3);
    }

    // --- The cases a box gets right too -------------------------------------------------

    [Fact]
    public void TwoBoxesMeetFaceToFace()
    {
        var mover = Primitives.Box(10, 10, 10);
        var blocker = At(Primitives.Box(10, 10, 10), 30);

        Assert.Equal(20f - MeshSweep.ClearanceMm, Travel(mover, blocker, Axis.X, 1), 4);
    }

    [Fact]
    public void NothingInTheWayIsNoLimitAtAll()
    {
        var mover = Primitives.Box(10, 10, 10);
        var beside = At(Primitives.Box(10, 10, 10), 0, 40);

        Assert.True(float.IsPositiveInfinity(Travel(mover, beside, Axis.X, 1)));
    }

    [Fact]
    public void SomethingBehindIsNotRunIntoByGoingForwards()
    {
        var mover = Primitives.Box(10, 10, 10);
        var behind = At(Primitives.Box(10, 10, 10), -30);

        Assert.True(float.IsPositiveInfinity(Travel(mover, behind, Axis.X, 1)));
        Assert.Equal(20f - MeshSweep.ClearanceMm, Travel(mover, behind, Axis.X, -1), 4);
    }

    [Fact]
    public void PartsAlreadyThroughEachOtherAreLeftFreeToMove()
    {
        var mover = Primitives.Box(10, 10, 10);
        var overlapping = At(Primitives.Box(10, 10, 10), 4);

        Assert.True(float.IsPositiveInfinity(Travel(mover, overlapping, Axis.X, 1)));
        Assert.True(float.IsPositiveInfinity(Travel(mover, overlapping, Axis.X, -1)));
    }

    [Fact]
    public void TheNearestOfSeveralIsTheOneThatStopsIt()
    {
        var mover = Primitives.Box(10, 10, 10);
        var near = At(Primitives.Box(10, 10, 10), 30);
        var far = At(Primitives.Box(10, 10, 10), 60);

        Assert.Equal(20f - MeshSweep.ClearanceMm, MeshSweep.Distance([mover], [far, near], Axis.X, 1), 4);
    }

    // --- The cases a box gets wrong -----------------------------------------------------

    /// <summary>
    /// The one that prompted this. A cone's box is its base, so a box sweep stops a ball a whole
    /// base radius short of the sloping side it was being brought up against.
    /// </summary>
    [Fact]
    public void ABallMeetsTheSlopeOfAConeRatherThanItsBox()
    {
        // A cone 20 across and 20 tall, tip up, and a ball of the same size beside it.
        var cone = At(Primitives.Create(PrimitiveKind.Cone), 40);
        var ball = Primitives.Create(PrimitiveKind.Sphere);

        float exact = Travel(ball, cone, Axis.X, 1);
        float box = CollisionSweep.Limit(
            [ball.ComputeBounds()], [cone.ComputeBounds()], Axis.X, 1000f);

        // The box has them meeting at the base radius; the real surfaces are further apart,
        // because at the height of the ball's equator the cone has narrowed to half its base.
        Assert.True(exact > box + 3f, $"exact {exact:0.##} mm vs box {box:0.##} mm");

        // And the ball really does end up touching the cone, not inside it.
        var moved = At(ball, exact);
        Assert.False(Overlap(moved, cone), "the ball ended up inside the cone");
        Assert.True(Overlap(At(ball, exact + 0.5f), cone), "it stopped more than half a millimetre short");
    }

    /// <summary>
    /// Rotation is where the box is furthest out on ordinary shapes, so it has to come out
    /// exactly right. A 20 mm box turned 45 degrees reaches 14.14 mm to its corner.
    /// </summary>
    [Fact]
    public void ATurnedBoxIsMetExactlyAtItsCorner()
    {
        var mover = Primitives.Box(10, 10, 10);
        var turned = At(Turned(Primitives.Box(20, 20, 20), 45, Vector3.UnitZ), 40);

        Assert.Equal(40f - 14.142f - 5f, Travel(mover, turned, Axis.X, 1), 2);
    }

    /// <summary>
    /// Two balls side by side but at different heights touch a good deal further apart than
    /// their boxes do, because a box has no idea the ball narrows as it rises.
    /// </summary>
    [Fact]
    public void TwoBallsAtDifferentHeightsMeetWhereTheyActuallyTouch()
    {
        var mover = Primitives.Create(PrimitiveKind.Sphere);            // radius 10 at the origin
        var above = At(Primitives.Create(PrimitiveKind.Sphere), 40, 0, 12);

        float exact = Travel(mover, above, Axis.X, 1);
        float box = CollisionSweep.Limit(
            [mover.ComputeBounds()], [above.ComputeBounds()], Axis.X, 1000f);

        // Centres 20 apart when they touch, so with 12 of that vertical the horizontal is
        // sqrt(400 - 144) = 16, and the mover starts 40 away. The box stops it at 20.
        Assert.Equal(40f - 16f, exact, 0);
        Assert.Equal(20f, box, 1);
    }

    [Fact]
    public void ItWorksTheSameOnEveryAxis()
    {
        var mover = Primitives.Box(10, 10, 10);

        foreach (var axis in new[] { Axis.X, Axis.Y, Axis.Z })
        {
            var offset = axis switch
            {
                Axis.X => new Vector3(30, 0, 0),
                Axis.Y => new Vector3(0, 30, 0),
                _ => new Vector3(0, 0, 30)
            };

            var blocker = MeshTransform.Transformed(
                Primitives.Box(10, 10, 10), Matrix4x4.CreateTranslation(offset));

            Assert.Equal(20f - MeshSweep.ClearanceMm, MeshSweep.Distance([mover], [blocker], axis, 1), 4);
        }
    }

    // --- The contract the caller relies on ----------------------------------------------

    [Fact]
    public void ADragIsNeverSentBackwardsOrFurtherThanAsked()
    {
        var mover = Primitives.Box(10, 10, 10);
        var blocker = At(Primitives.Box(10, 10, 10), 30);

        Assert.Equal(5f, MeshSweep.Limit([mover], [blocker], Axis.X, 5f), 3);
        Assert.Equal(20f - MeshSweep.ClearanceMm, MeshSweep.Limit([mover], [blocker], Axis.X, 100f), 4);
        Assert.Equal(0f, MeshSweep.Limit([mover], [blocker], Axis.X, 0f), 3);
    }

    [Fact]
    public void TouchingPartsPermitNoMovementTowardsEachOther()
    {
        var mover = Primitives.Box(10, 10, 10);
        var touching = At(Primitives.Box(10, 10, 10), 10);

        Assert.Equal(0f, Travel(mover, touching, Axis.X, 1), 3);
    }

    /// <summary>Faces are left a hair apart, because a boolean handles two coplanar ones badly.</summary>
    [Fact]
    public void ContactLeavesAHairOfClearance()
    {
        var mover = Primitives.Box(10, 10, 10);
        var blocker = At(Primitives.Box(10, 10, 10), 30);

        var landed = At(mover, Travel(mover, blocker, Axis.X, 1));

        float gap = blocker.ComputeBounds().Min.X - landed.ComputeBounds().Max.X;
        Assert.True(gap > 0, $"the faces ended up {gap:0.#####} mm apart");
        Assert.True(gap < 0.01f, $"{gap:0.#####} mm is more than a hair");
    }

    [Fact]
    public void AnEmptySceneNeverBlocksAnything()
    {
        var mover = Primitives.Box(10, 10, 10);

        Assert.True(float.IsPositiveInfinity(MeshSweep.Distance([mover], [], Axis.X, 1)));
        Assert.True(float.IsPositiveInfinity(MeshSweep.Distance([], [mover], Axis.X, 1)));
    }

    /// <summary>
    /// A shape dense enough to stall the drag falls back to its box. Stopping a fraction short
    /// is a far smaller nuisance than a pause every time the mouse goes down.
    /// </summary>
    [Fact]
    public void SomethingTooDenseToFlattenFallsBackToItsBox()
    {
        var ball = Primitives.Create(PrimitiveKind.Sphere);

        var cone = At(Primitives.Create(PrimitiveKind.Cone), 40);
        var denseCone = At(
            MeshSubdivision.Subdivide(
                MeshSubdivision.Subdivide(Primitives.Create(PrimitiveKind.Cone), 4), 2), 40);

        Assert.True(denseCone.TriangleCount > MeshSweep.TriangleBudget);

        float exact = Travel(ball, cone, Axis.X, 1);
        float coarse = Travel(ball, denseCone, Axis.X, 1);
        float box = CollisionSweep.Limit(
            [ball.ComputeBounds()], [cone.ComputeBounds()], Axis.X, 1000f);

        Assert.True(exact > box + 3f, "the sparse cone should be measured exactly");
        Assert.Equal(box, coarse, 2);
    }

    /// <summary>
    /// Something dense but nowhere near the mover costs nothing and does not spoil the answer
    /// for whatever is genuinely in front.
    /// </summary>
    [Fact]
    public void SomethingDenseOffToOneSideDoesNotSpoilTheExactAnswer()
    {
        var ball = Primitives.Create(PrimitiveKind.Sphere);
        var cone = At(Primitives.Create(PrimitiveKind.Cone), 40);
        var elsewhere = At(MeshSubdivision.Subdivide(Primitives.Create(PrimitiveKind.Sphere), 4), 0, 400);

        float alone = MeshSweep.Distance([ball], [cone], Axis.X, 1);
        float crowded = MeshSweep.Distance([ball], [cone, elsewhere], Axis.X, 1);

        Assert.Equal(alone, crowded, 3);
    }

    // --- Helpers ------------------------------------------------------------------------

    /// <summary>Whether two solids share any volume, used to check where a sweep left things.</summary>
    private static bool Overlap(Mesh a, Mesh b)
    {
        var both = FastCraft3D.Geometry.Csg.CsgSolid.Intersect(a, b);
        return both.TriangleCount > 0 && Math.Abs(both.ComputeSignedVolume()) > 1e-3;
    }

    // --- Any direction, not only the three axes -----------------------------------------

    [Fact]
    public void ASweepDownAnAxisIsTheSameWhicheverWayItIsAskedFor()
    {
        var mover = Primitives.Box(10, 10, 10);
        var blocker = At(Primitives.Box(10, 10, 10), 30);

        Assert.Equal(
            MeshSweep.Distance([mover], [blocker], Axis.X, 1),
            MeshSweep.Distance([mover], [blocker], Vector3.UnitX), 4);
    }

    [Fact]
    public void ACornerIsMetOnTheDiagonalRunIntoIt()
    {
        var mover = Primitives.Box(10, 10, 10);
        var blocker = At(Primitives.Box(10, 10, 10), 30, 30);

        // Both boxes are 10 across, so 20 mm of gap on each axis: the corners meet after
        // 20 * root 2 along the diagonal between them.
        float diagonal = MeshSweep.Distance([mover], [blocker], new Vector3(1, 1, 0));

        Assert.Equal(20f * MathF.Sqrt(2f) - MeshSweep.ClearanceMm, diagonal, 3);
    }

    [Fact]
    public void GoingPastSomethingOnTheDiagonalIsNotBlockedByIt()
    {
        var mover = Primitives.Box(10, 10, 10);

        // Dead ahead on X, so a move that way is stopped 30 mm out. Set off at 45 degrees and
        // the mover passes beside it instead, with nothing to stop it at all.
        var blocker = At(Primitives.Box(10, 10, 10), 40);

        Assert.Equal(30f - MeshSweep.ClearanceMm, MeshSweep.Distance([mover], [blocker], Axis.X, 1), 3);
        Assert.True(float.IsPositiveInfinity(MeshSweep.Distance([mover], [blocker], new Vector3(1, 1, 0))));
    }

    [Fact]
    public void ADiagonalSweepUpwardsMeetsWhatIsAboveAndAcross()
    {
        var mover = Primitives.Box(10, 10, 10);
        var above = At(Primitives.Box(10, 10, 10), 20, 0, 20);

        float slope = MeshSweep.Distance([mover], [above], new Vector3(1, 0, 1));

        Assert.Equal(10f * MathF.Sqrt(2f) - MeshSweep.ClearanceMm, slope, 3);
    }

    [Fact]
    public void SomethingAlreadyTouchingLeavesNowhereToGo()
    {
        var mover = Primitives.Box(10, 10, 10);
        var touching = At(Primitives.Box(10, 10, 10), 10);

        Assert.Equal(0f, MeshSweep.Distance([mover], [touching], new Vector3(2, 0, 0)), 3);
    }

    [Fact]
    public void ACanComesDownOnItsBowlAndNotThroughIt()
    {
        // The shape of the complaint: a round part hanging over a wider one, dropped straight
        // down. It should stop on the bowl's rim, not carry on to the plate.
        var can = At(Primitives.Prism(40, 132, 64), 0, 0, 100);
        var bowl = Primitives.Prism(50, 20, 6);

        float down = MeshSweep.Distance([can], [bowl], -Vector3.UnitZ);

        // The can's bottom starts at 100 - 66 = 34; the bowl's top is at 10.
        Assert.Equal(24f - MeshSweep.ClearanceMm, down, 3);
    }
}
