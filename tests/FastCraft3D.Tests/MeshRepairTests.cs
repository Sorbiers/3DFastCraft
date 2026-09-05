using System.Numerics;
using FastCraft3D.Geometry;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>Regression cover for the two defects that made early CSG output non-manifold.</summary>
public class MeshRepairTests
{
    /// <summary>
    /// Welding used to quantise each position into a grid cell and merge only exact key
    /// matches. Two vertices a nanometre apart but straddling a cell boundary hashed
    /// differently and survived as duplicates, leaving zero-length edges that read as holes.
    /// Sweeping across many boundaries catches any return to key-equality matching.
    /// </summary>
    [Fact]
    public void WeldMergesNearDuplicatesAcrossCellBoundaries()
    {
        const float tolerance = 1e-4f;

        for (int i = 1; i <= 200; i++)
        {
            float onBoundary = i * tolerance;
            var mesh = new Mesh();
            mesh.AddTriangle(new Vector3(onBoundary, 0, 0), new Vector3(10, 0, 0), new Vector3(0, 10, 0));
            mesh.AddTriangle(new Vector3(onBoundary - tolerance / 1000f, 0, 0), new Vector3(0, 10, 0), new Vector3(0, 0, 10));

            var welded = mesh.Welded(tolerance);

            Assert.Equal(4, welded.VertexCount);
        }
    }

    [Fact]
    public void WeldKeepsVerticesFurtherApartThanTolerance()
    {
        var mesh = new Mesh();
        mesh.AddTriangle(new Vector3(0, 0, 0), new Vector3(10, 0, 0), new Vector3(0, 10, 0));
        mesh.AddTriangle(new Vector3(0.5f, 0, 0), new Vector3(0, 10, 0), new Vector3(0, 0, 10));

        var welded = mesh.Welded(1e-4f);

        // Six raw vertices, but (0,10,0) is genuinely shared, so five survive.
        Assert.Equal(5, welded.VertexCount);
    }

    /// <summary>A zero-thickness fin adds two extra users to each of its edges.</summary>
    [Fact]
    public void CoincidentTrianglePairsAreCancelled()
    {
        var tetrahedron = Primitives.Tetrahedron(10);
        var withFin = new Mesh(tetrahedron.Positions, tetrahedron.Indices);
        int a = withFin.Indices[0], b = withFin.Indices[1], c = withFin.Indices[2];
        withFin.Indices.AddRange([a, b, c, a, c, b]);

        Assert.False(withFin.CheckHealth().IsWatertight);

        var repaired = MeshRepair.Repair(withFin);

        Assert.True(repaired.CheckHealth().IsWatertight, repaired.CheckHealth().Describe());
        Assert.Equal(tetrahedron.TriangleCount, repaired.TriangleCount);
    }

    /// <summary>
    /// A T-junction: the long edge of one triangle meets the midpoint of its neighbour.
    /// The surface has no gap, but the long edge has a single user.
    /// </summary>
    [Fact]
    public void TJunctionsAreStitched()
    {
        var mesh = new Mesh();
        var corner0 = new Vector3(0, 0, 0);
        var corner1 = new Vector3(10, 0, 0);
        var midpoint = new Vector3(5, 0, 0);
        var above = new Vector3(5, 5, 0);
        var below = new Vector3(5, -5, 0);

        mesh.AddTriangle(corner0, corner1, above);          // spans the full edge
        mesh.AddTriangle(corner0, below, midpoint);         // stops at the midpoint
        mesh.AddTriangle(midpoint, below, corner1);

        Assert.True(mesh.Welded().CheckHealth().BoundaryEdges > 0);

        var repaired = MeshRepair.Repair(mesh.Welded());

        // The long edge must now be split at the midpoint, so it is shared by two triangles.
        var users = 0;
        int c0 = repaired.Positions.FindIndex(p => Vector3.Distance(p, corner0) < 1e-5f);
        int mid = repaired.Positions.FindIndex(p => Vector3.Distance(p, midpoint) < 1e-5f);
        for (int i = 0; i + 2 < repaired.Indices.Count; i += 3)
        {
            int[] t = [repaired.Indices[i], repaired.Indices[i + 1], repaired.Indices[i + 2]];
            if (t.Contains(c0) && t.Contains(mid)) users++;
        }

        Assert.True(mid >= 0, "midpoint vertex is missing");
        Assert.Equal(2, users);
    }
}
