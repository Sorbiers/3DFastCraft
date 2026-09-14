using System.Numerics;
using FastCraft3D.Geometry;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>
/// Insert, Custom: a shape with its size, segments and roundness chosen before it is made.
/// </summary>
public class CustomShapeTests
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

    public static TheoryData<PrimitiveKind, float> Shapes => new()
    {
        { PrimitiveKind.Cube, 0f },
        { PrimitiveKind.Cube, 3f },
        { PrimitiveKind.Cylinder, 0f },
        { PrimitiveKind.Cylinder, 2f },
        { PrimitiveKind.Cone, 0f },
        { PrimitiveKind.Sphere, 0f },
        { PrimitiveKind.Torus, 0f }
    };

    [Theory]
    [MemberData(nameof(Shapes))]
    public void EveryShapeComesOutClosedFacingOutAndTheSizeAskedFor(PrimitiveKind kind, float roundness)
    {
        float height = kind == PrimitiveKind.Torus ? 8f : 30f;
        var shape = new CustomShape(kind, 40f, 24f, height, 64, 32, roundness);

        var mesh = shape.Build();
        var bounds = mesh.ComputeBounds();

        Assert.True(mesh.CheckHealth().IsWatertight, mesh.CheckHealth().Describe());
        Assert.True(Volume(mesh) > 0, "the faces point inwards");

        // A multiple of four segments puts a corner at every quarter turn, so round shapes reach
        // their full width and depth exactly.
        Assert.Equal(40f, bounds.Size.X, 0.01f);
        Assert.Equal(24f, bounds.Size.Y, 0.01f);
        Assert.Equal(height, bounds.Size.Z, 0.01f);
        Assert.Equal(Vector3.Zero, bounds.Center, new Vector3EqualityComparer(0.01f));
    }

    [Theory]
    [InlineData(PrimitiveKind.Cylinder)]
    [InlineData(PrimitiveKind.Cone)]
    [InlineData(PrimitiveKind.Sphere)]
    [InlineData(PrimitiveKind.Torus)]
    public void MoreSegmentsMakeMoreTriangles(PrimitiveKind kind)
    {
        var coarse = new CustomShape(kind, 20f, 20f, kind == PrimitiveKind.Torus ? 5f : 20f, 16, 8, 0f).Build();
        var fine = new CustomShape(kind, 20f, 20f, kind == PrimitiveKind.Torus ? 5f : 20f, 128, 8, 0f).Build();

        Assert.True(fine.TriangleCount > coarse.TriangleCount * 4,
            $"{coarse.TriangleCount} at 16 segments, {fine.TriangleCount} at 128");
    }

    [Fact]
    public void ASphereTakesItsRingsFromTheirOwnBox()
    {
        var few = new CustomShape(PrimitiveKind.Sphere, 20f, 20f, 20f, 64, 8, 0f).Build();
        var many = new CustomShape(PrimitiveKind.Sphere, 20f, 20f, 20f, 64, 48, 0f).Build();

        Assert.True(many.TriangleCount > few.TriangleCount * 3);
    }

    [Fact]
    public void ATorusTooThickForItsWidthIsHeldToOneThatStillHasAHole()
    {
        var shape = new CustomShape(PrimitiveKind.Torus, 20f, 20f, 15f, 64, 32, 0f);

        var sane = shape.Sane();
        var mesh = shape.Build();

        Assert.True(sane.Height < 10f);
        Assert.True(mesh.CheckHealth().IsWatertight, mesh.CheckHealth().Describe());
    }

    [Fact]
    public void RoundnessIsHeldToWhatTheSizeAllowsAndIgnoredWhereItMeansNothing()
    {
        Assert.Equal(5f, new CustomShape(PrimitiveKind.Cube, 10f, 10f, 10f, 64, 32, 9f).Sane().Roundness, 0.01f);
        Assert.Equal(0f, new CustomShape(PrimitiveKind.Sphere, 10f, 10f, 10f, 64, 32, 3f).Sane().Roundness);
    }

    [Fact]
    public void NumbersOutsideTheRangeAreBroughtIn()
    {
        var sane = new CustomShape(PrimitiveKind.Cylinder, 0f, -5f, 10f, 1, 1000, 0f).Sane();

        Assert.Equal(CustomShape.MinimumSize, sane.Width);
        Assert.Equal(CustomShape.MinimumSize, sane.Depth);
        Assert.Equal(CustomShape.MinimumSegments, sane.Segments);
        Assert.Equal(CustomShape.MaximumRings, sane.Rings);
    }

    private sealed class Vector3EqualityComparer(float tolerance) : IEqualityComparer<Vector3>
    {
        public bool Equals(Vector3 a, Vector3 b) => Vector3.Distance(a, b) <= tolerance;
        public int GetHashCode(Vector3 v) => 0;
    }
}
