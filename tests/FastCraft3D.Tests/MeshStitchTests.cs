using System.Numerics;
using FastCraft3D.Geometry;
using FastCraft3D.Geometry.Csg;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>
/// Edges that meet a vertex partway along instead of at a corner. A boolean produces these
/// constantly, and until they were dealt with the model was reported as broken and Repair could
/// do nothing about it - there is no hole to fill and nothing facing the wrong way.
/// </summary>
public class MeshStitchTests
{
    /// <summary>
    /// A square made of three triangles: one covering the left half, and two covering the right,
    /// meeting at a point halfway up the middle. That middle vertex sits along the left
    /// triangle's long edge, and nothing shares it.
    /// </summary>
    private static Mesh WithATJunction()
    {
        var mesh = new Mesh();

        var a = new Vector3(0, 0, 0);
        var b = new Vector3(0, 10, 0);
        var c = new Vector3(10, 10, 0);
        var d = new Vector3(10, 0, 0);
        var middle = new Vector3(5, 5, 0);       // on the diagonal a-c

        mesh.AddTriangle(a, c, b);               // the long edge a-c
        mesh.AddTriangle(a, d, middle);          // the two short ones, a-middle and middle-c
        mesh.AddTriangle(middle, d, c);

        return mesh.Welded();
    }

    [Fact]
    public void AVertexSittingAlongAnEdgeIsFound()
    {
        Assert.True(MeshStitch.Count(WithATJunction()) > 0);
    }

    [Fact]
    public void StitchingMakesBothSidesShareTheirCorners()
    {
        var stitched = MeshStitch.CloseTJunctions(WithATJunction());

        Assert.Equal(0, MeshStitch.Count(stitched));
        Assert.Equal(4, stitched.TriangleCount);
    }

    /// <summary>The corners were on the edge already, so nothing about the shape can change.</summary>
    [Fact]
    public void StitchingMovesNothing()
    {
        var before = WithATJunction();
        var after = MeshStitch.CloseTJunctions(before);

        Assert.Equal(before.ComputeBounds().Min, after.ComputeBounds().Min);
        Assert.Equal(before.ComputeBounds().Max, after.ComputeBounds().Max);

        foreach (var p in after.Positions)
            Assert.Contains(before.Positions, q => (p - q).Length() < 1e-5f);
    }

    [Fact]
    public void ASoundMeshIsLeftExactlyAsItWas()
    {
        var box = Primitives.Box(20, 20, 20);

        Assert.Equal(0, MeshStitch.Count(box));
        Assert.Equal(box.TriangleCount, MeshStitch.CloseTJunctions(box).TriangleCount);
    }

    [Fact]
    public void NothingToStitchOnAnEmptyMesh()
    {
        Assert.Equal(0, MeshStitch.Count(new Mesh()));
        Assert.Equal(0, MeshStitch.CloseTJunctions(new Mesh()).TriangleCount);
    }
}

/// <summary>
/// The case that sent people to Repair: taking one rounded box out of another, or merging them.
/// About one in five came back torn, and no amount of repairing would mend it.
/// </summary>
public class RoundedBooleanTests
{
    private static Mesh Tray(float outerRadius, float innerRadius, float inset, float lift) =>
        MeshTransform.Transformed(
            RoundedPrimitives.RoundedBox(50 - inset * 2, 50 - inset * 2, 18, innerRadius, RoundEdges.All),
            Matrix4x4.CreateTranslation(0, 0, lift));

    [Theory]
    [InlineData(BooleanOp.Union, 3f, 3f, 3f, 4f)]
    [InlineData(BooleanOp.Subtract, 3f, 3f, 3f, 4f)]
    [InlineData(BooleanOp.Union, 5f, 1f, 2f, 0f)]
    [InlineData(BooleanOp.Subtract, 2f, 5f, 6f, -2f)]
    [InlineData(BooleanOp.Intersect, 3f, 2f, 4f, 4f)]
    public void RoundedBoxesComeOutPrintable(
        BooleanOp op, float outerRadius, float innerRadius, float inset, float lift)
    {
        var outer = RoundedPrimitives.RoundedBox(50, 50, 20, outerRadius, RoundEdges.All);

        var raw = CsgSolid.Apply(outer, Tray(outerRadius, innerRadius, inset, lift), op);
        var healed = MeshHealer.Heal(raw).Mesh;

        Assert.True(healed.CheckHealth().IsWatertight, healed.CheckHealth().Describe());
    }

    /// <summary>Mending it must not quietly reshape it.</summary>
    [Fact]
    public void MendingKeepsTheShape()
    {
        var outer = RoundedPrimitives.RoundedBox(50, 50, 20, 3f, RoundEdges.All);
        var raw = CsgSolid.Apply(outer, Tray(3f, 3f, 3f, 4f), BooleanOp.Union);

        Assert.False(raw.CheckHealth().IsWatertight, "this pair used to come out torn");

        var healed = MeshHealer.Heal(raw).Mesh;

        Assert.Equal(raw.ComputeSignedVolume(), healed.ComputeSignedVolume(), 3);
        Assert.Equal(raw.ComputeBounds().Size, healed.ComputeBounds().Size);
    }
}
