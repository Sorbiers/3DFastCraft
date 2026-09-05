using System.Numerics;
using FastCraft3D.Geometry;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>
/// Breaking up only the edges that need it. The whole risk of doing that rather than splitting
/// everything is that the surface comes apart along an edge one side split and the other did
/// not, so that is what most of these check.
/// </summary>
public class MeshSubdivisionTests
{
    private static bool LongInX(Vector3 a, Vector3 b) => MathF.Abs(b.X - a.X) > 5f;

    [Fact]
    public void SplittingSomeEdgesLeavesTheSolidClosed()
    {
        var box = Primitives.Box(40, 6, 6);

        var split = MeshSubdivision.SplitEdgesWhere(box, LongInX);

        Assert.True(split.TriangleCount > box.TriangleCount);
        Assert.True(split.CheckHealth().IsWatertight, split.CheckHealth().Describe());
    }

    [Fact]
    public void SplittingChangesNothingAboutTheShape()
    {
        var box = Primitives.Box(40, 6, 6);

        var split = MeshSubdivision.SplitEdgesWhere(box, LongInX);

        Assert.Equal(box.ComputeSignedVolume(), split.ComputeSignedVolume(), 3);
        Assert.Equal(box.ComputeBounds().Size, split.ComputeBounds().Size);
    }

    [Fact]
    public void EdgesThatAreShortEnoughAreLeftAlone()
    {
        var box = Primitives.Box(40, 6, 6);

        var split = MeshSubdivision.SplitEdgesWhere(box, LongInX);

        foreach (var (a, b) in Edges(split))
            Assert.True(MathF.Abs(b.X - a.X) <= 5.001f, $"a {MathF.Abs(b.X - a.X):0.##} mm step survived");
    }

    /// <summary>The saving: what does not stray is not touched, however big the mesh is.</summary>
    [Fact]
    public void NothingIsSplitWhenNothingNeedsIt()
    {
        var sphere = Primitives.Create(PrimitiveKind.Sphere);

        var split = MeshSubdivision.SplitEdgesWhere(sphere, (_, _) => false);

        Assert.Equal(sphere.TriangleCount, split.TriangleCount);
    }

    /// <summary>
    /// A criterion that can never be satisfied - every edge, however small - has to stop rather
    /// than run away.
    /// </summary>
    [Fact]
    public void AnImpossibleDemandStopsAtTheBudget()
    {
        var box = Primitives.Box(20, 20, 20);

        var split = MeshSubdivision.SplitEdgesWhere(box, (_, _) => true, maxTriangles: 2000);

        Assert.True(split.TriangleCount < 12_000, $"{split.TriangleCount:N0} triangles is a runaway");
        Assert.True(split.CheckHealth().IsWatertight, split.CheckHealth().Describe());
    }

    [Fact]
    public void TheRoundLimitIsHonoured()
    {
        var box = Primitives.Box(20, 20, 20);

        var once = MeshSubdivision.SplitEdgesWhere(box, (_, _) => true, maxRounds: 1);

        Assert.Equal(box.TriangleCount * 4, once.TriangleCount);
    }

    private static IEnumerable<(Vector3 A, Vector3 B)> Edges(Mesh mesh)
    {
        for (int t = 0; t + 2 < mesh.Indices.Count; t += 3)
            for (int k = 0; k < 3; k++)
                yield return (
                    mesh.Positions[mesh.Indices[t + k]],
                    mesh.Positions[mesh.Indices[t + (k + 1) % 3]]);
    }
}
