using System.Numerics;
using FastCraft3D.Geometry;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>
/// Twist turns a solid about its upright axis by more the higher up it goes, and stays a solid.
/// </summary>
public class MeshTwistTests
{
    private static double Volume(Mesh mesh)
    {
        double v = 0;
        for (int i = 0; i < mesh.Indices.Count; i += 3)
        {
            var a = mesh.Positions[mesh.Indices[i]];
            var b = mesh.Positions[mesh.Indices[i + 1]];
            var c = mesh.Positions[mesh.Indices[i + 2]];
            v += Vector3.Dot(a, Vector3.Cross(b, c)) / 6.0;
        }

        return v;
    }

    private static Mesh Column() =>
        MeshTransform.Transformed(Primitives.Box(20, 20, 60), Matrix4x4.CreateTranslation(0, 0, 30));

    [Theory]
    [InlineData(45f)]
    [InlineData(90f)]
    [InlineData(-180f)]
    [InlineData(360f)]
    public void ATwistedColumnIsStillClosedAndHoldsAsMuch(float degrees)
    {
        var column = Column();
        var twisted = MeshDeform.Twist(column, degrees);

        Assert.True(twisted.CheckHealth().IsWatertight, twisted.CheckHealth().Describe());

        // Every level is the same square, turned: the volume can only move by what the straight
        // edges cut off the curves, which the tolerance keeps small.
        Assert.Equal(Volume(column), Volume(twisted), Volume(column) * 0.01);
    }

    [Fact]
    public void TheBottomStaysAndTheTopTurnsByTheWholeAngle()
    {
        var twisted = MeshDeform.Twist(Column(), 45f);

        // The top corner at (10, 10) comes round an eighth of a turn anticlockwise, onto the Y
        // axis. An eighth rather than a quarter: a square turned a quarter lands its corners on
        // each other, and would pass untwisted.
        float corner = new Vector2(10, 10).Length();
        Assert.Contains(twisted.Positions, p => p.Z > 59.99f && Vector3.Distance(p, new Vector3(0, corner, 60)) < 1e-3f);
        Assert.DoesNotContain(twisted.Positions, p => p.Z > 59.99f && Vector3.Distance(p, new Vector3(10, 10, 60)) < 1e-3f);

        Assert.Contains(twisted.Positions, p => Vector3.Distance(p, new Vector3(10, 10, 0)) < 1e-3f);
    }

    [Fact]
    public void TheSidesAreBrokenUpToFollowTheTurnRatherThanLeaningFlat()
    {
        var gentle = MeshDeform.Twist(Column(), 10f);
        var steep = MeshDeform.Twist(Column(), 360f);

        Assert.True(steep.TriangleCount > gentle.TriangleCount * 4,
            $"{gentle.TriangleCount} at 10 degrees, {steep.TriangleCount} at 360");

        // Halfway up, a corner has come round half the turn: it lies on the circle through the
        // corners, not on the straight line between the bottom and top ones.
        float corner = new Vector2(10, 10).Length();
        var middle = steep.Positions.Where(p => MathF.Abs(p.Z - 30f) < 1f).ToList();
        Assert.NotEmpty(middle);
        Assert.True(middle.Max(p => new Vector2(p.X, p.Y).Length()) > corner - 0.1f);
    }

    [Fact]
    public void NoTurnLeavesTheSolidAlone()
    {
        var column = Column();
        Assert.Same(column, MeshDeform.Twist(column, 0f));
    }

    [Fact]
    public void ACylinderTurnedTwiceOverIsStillClosed()
    {
        var cylinder = MeshTransform.Transformed(Primitives.Prism(15f, 50f, 64), Matrix4x4.CreateTranslation(40, -20, 25));
        var twisted = MeshDeform.Twist(cylinder, 720f);

        Assert.True(twisted.CheckHealth().IsWatertight, twisted.CheckHealth().Describe());
        Assert.Equal(Volume(cylinder), Volume(twisted), Volume(cylinder) * 0.01);

        // Turned about its own axis, not the plate's origin: it has not moved.
        var before = cylinder.ComputeBounds();
        var after = twisted.ComputeBounds();
        Assert.True(Vector3.Distance(before.Center, after.Center) < 0.2f);
    }
}
