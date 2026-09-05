using FastCraft3D.Geometry;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>
/// The white outline drawn around a selected object. It has to trace the shape, not the
/// tessellation, or a sphere becomes a ball of wireframe.
/// </summary>
public class FeatureEdgeTests
{
    [Fact]
    public void ACubeOutlinesAsItsTwelveEdges()
    {
        var edges = FeatureEdges.Build(Primitives.Box(20, 20, 20));

        // The diagonal splitting each square face is not a feature: its two triangles are
        // coplanar, so only the twelve real edges survive.
        Assert.Equal(12, edges.Count);
    }

    [Fact]
    public void ASmoothSphereHasNoInternalEdges()
    {
        var edges = FeatureEdges.Build(Primitives.Sphere(10, 32, 16));

        // A 32-segment sphere turns about 11 degrees per step, under the 25 degree threshold,
        // so nothing on it counts as a crease.
        Assert.Empty(edges);
    }

    [Fact]
    public void ACylinderOutlinesItsTwoRimsOnly()
    {
        var edges = FeatureEdges.Build(Primitives.Prism(10, 20, 32));

        // The two circular rims, and nothing from the smooth wall or the flat caps.
        Assert.Equal(64, edges.Count);
    }

    [Fact]
    public void AWedgeKeepsItsSharpEdges()
    {
        var edges = FeatureEdges.Build(Primitives.Wedge(20, 20, 20));

        // A triangular prism has nine edges.
        Assert.Equal(9, edges.Count);
    }

    [Fact]
    public void AnEmptyMeshOutlinesToNothing()
    {
        Assert.Empty(FeatureEdges.Build(new Mesh()));
    }

    /// <summary>An open surface has boundary edges, which are always part of the outline.</summary>
    [Fact]
    public void BoundaryEdgesAreAlwaysIncluded()
    {
        var sheet = new Mesh();
        sheet.AddTriangle(new(0, 0, 0), new(10, 0, 0), new(10, 10, 0));
        sheet.AddTriangle(new(0, 0, 0), new(10, 10, 0), new(0, 10, 0));

        var edges = FeatureEdges.Build(sheet.Welded());

        // Four sides of the square. The shared diagonal is flat and is left out.
        Assert.Equal(4, edges.Count);
    }

    [Fact]
    public void ALooserThresholdFindsMoreEdges()
    {
        var sphere = Primitives.Sphere(10, 16, 8);

        int fine = FeatureEdges.Build(sphere, creaseAngleDegrees: 5f).Count;
        int coarse = FeatureEdges.Build(sphere, creaseAngleDegrees: 60f).Count;

        Assert.True(fine > coarse, "a smaller crease angle should classify more edges as sharp");
    }
}
