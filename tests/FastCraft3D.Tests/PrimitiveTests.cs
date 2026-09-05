using System.Numerics;
using FastCraft3D.Geometry;
using Xunit;

namespace FastCraft3D.Tests;

public class PrimitiveTests
{
    public static TheoryData<PrimitiveKind> AllKinds()
    {
        var data = new TheoryData<PrimitiveKind>();
        foreach (PrimitiveKind kind in Enum.GetValues<PrimitiveKind>())
            data.Add(kind);
        return data;
    }

    [Theory]
    [MemberData(nameof(AllKinds))]
    public void EveryPrimitiveIsWatertight(PrimitiveKind kind)
    {
        var health = Primitives.Create(kind).CheckHealth();

        Assert.True(health.IsWatertight,
            $"{kind} is not watertight: {health.Describe()}");
    }

    /// <summary>
    /// Positive signed volume means the winding is outward-facing. A negative result here
    /// would export an inside-out STL that slicers either reject or print hollow.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllKinds))]
    public void EveryPrimitiveFacesOutwards(PrimitiveKind kind)
    {
        var health = Primitives.Create(kind).CheckHealth();

        Assert.False(health.IsInsideOut,
            $"{kind} has inward-facing normals (signed volume {health.SignedVolume:0.##})");
    }

    [Theory]
    [MemberData(nameof(AllKinds))]
    public void EveryPrimitiveFitsItsNominalSize(PrimitiveKind kind)
    {
        var bounds = Primitives.Create(kind, 20f).ComputeBounds();

        Assert.False(bounds.IsEmpty);
        Assert.True(bounds.Size.X <= 20.001f && bounds.Size.Y <= 20.001f && bounds.Size.Z <= 20.001f,
            $"{kind} exceeds its 20 mm nominal size: {bounds}");
        // Centred on the origin so scaling and rotation behave predictably.
        Assert.True(bounds.Center.Length() < 0.001f, $"{kind} is not centred: {bounds.Center}");
    }

    [Fact]
    public void CubeHasExactAnalyticVolume()
    {
        double volume = Primitives.Box(20, 20, 20).ComputeSignedVolume();

        Assert.Equal(8000.0, volume, 3);
    }

    [Fact]
    public void PyramidHasExactAnalyticVolume()
    {
        // A pyramid is one third of its bounding prism.
        double volume = Primitives.Pyramid(20, 20).ComputeSignedVolume();

        Assert.Equal(20 * 20 * 20 / 3.0, volume, 3);
    }

    [Fact]
    public void WedgeIsHalfItsBoundingBox()
    {
        double volume = Primitives.Wedge(20, 20, 20).ComputeSignedVolume();

        Assert.Equal(4000.0, volume, 3);
    }

    [Fact]
    public void TetrahedronIsOneThirdOfItsCube()
    {
        // A regular tetrahedron inscribed in a cube of edge a has volume a^3 / 3.
        double volume = Primitives.Tetrahedron(20).ComputeSignedVolume();

        Assert.Equal(8000 / 3.0, volume, 3);
    }

    [Fact]
    public void CylinderApproximatesItsAnalyticVolume()
    {
        // A 32-gon prism inscribed in r=10 is slightly smaller than the true cylinder.
        double volume = Primitives.Prism(10, 20, 32).ComputeSignedVolume();
        double ideal = Math.PI * 100 * 20;

        Assert.InRange(volume, ideal * 0.99, ideal);
    }

    [Fact]
    public void SphereApproximatesItsAnalyticVolume()
    {
        double volume = Primitives.Sphere(10, 32, 16).ComputeSignedVolume();
        double ideal = 4.0 / 3.0 * Math.PI * 1000;

        Assert.InRange(volume, ideal * 0.97, ideal);
    }

    [Fact]
    public void SpherePoleTrianglesAreWeldedAway()
    {
        var sphere = Primitives.Sphere(10, 32, 16);

        // Degenerate triangles at the collapsed pole rings must not survive the weld,
        // or CSG inherits planes with no usable normal.
        for (int i = 0; i + 2 < sphere.Indices.Count; i += 3)
        {
            Vector3 a = sphere.Positions[sphere.Indices[i]];
            Vector3 b = sphere.Positions[sphere.Indices[i + 1]];
            Vector3 c = sphere.Positions[sphere.Indices[i + 2]];
            Assert.True(Vector3.Cross(b - a, c - a).Length() > 1e-9f, "degenerate triangle survived");
        }
    }
}
