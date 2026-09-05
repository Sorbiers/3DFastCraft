using System.Numerics;
using FastCraft3D.Geometry;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>
/// Smoothing has one job and one hazard: take the faceting off, without quietly eating the
/// model. Plain averaging does the first and fails the second, which is why this uses Taubin's
/// two-step.
/// </summary>
public class MeshSmoothingTests
{
    private static Mesh Sphere() => Primitives.Create(PrimitiveKind.Sphere);

    [Fact]
    public void ItKeepsTheModelWatertight()
    {
        var smoothed = MeshSmoothing.Smooth(Sphere(), passes: 8);

        Assert.True(smoothed.CheckHealth().IsWatertight, smoothed.CheckHealth().Describe());
    }

    [Fact]
    public void NothingIsAddedOrRemoved()
    {
        var sphere = Sphere();

        var smoothed = MeshSmoothing.Smooth(sphere, passes: 4);

        Assert.Equal(sphere.TriangleCount, smoothed.TriangleCount);
        Assert.Equal(sphere.VertexCount, smoothed.VertexCount);
    }

    /// <summary>
    /// The whole reason for the second pass. Plain averaging pulls the surface inward every
    /// time, and after a dozen passes the model is visibly smaller.
    /// </summary>
    [Fact]
    public void TheShapeDoesNotShrinkAway()
    {
        var sphere = Sphere();
        double before = sphere.ComputeSignedVolume();

        var smoothed = MeshSmoothing.Smooth(sphere, passes: 12);
        double after = smoothed.ComputeSignedVolume();

        Assert.True(after > before * 0.9,
            $"volume fell from {before:N1} to {after:N1} - it is shrinking");
        Assert.True(after < before * 1.1, "and it should not be growing either");
    }

    /// <summary>Repeating it must stay stable rather than wandering off.</summary>
    [Fact]
    public void ManyPassesStaySteady()
    {
        var sphere = Sphere();
        var smoothed = MeshSmoothing.Smooth(sphere, passes: 60);

        Assert.True(smoothed.CheckHealth().IsWatertight);
        Assert.True(smoothed.ComputeSignedVolume() > sphere.ComputeSignedVolume() * 0.75);
        Assert.All(smoothed.Positions, p => Assert.True(float.IsFinite(p.X + p.Y + p.Z)));
    }

    /// <summary>
    /// What smoothing is actually for: taking noise out of a surface, not taking the facets off
    /// a tidy one. A sphere whose vertices already lie on a sphere has nothing to remove - its
    /// faceting is the tessellation, and no amount of averaging changes how many triangles
    /// there are. Roughen it first and the difference is plain.
    /// </summary>
    [Fact]
    public void ItTakesTheRoughnessOutOfASurface()
    {
        var rough = Roughened(Sphere(), 0.6f);

        var smoothed = MeshSmoothing.Smooth(rough, passes: 6);

        Assert.True(Creases(smoothed) < Creases(rough) * 0.7,
            $"creasing only fell from {Creases(rough):N1} to {Creases(smoothed):N1}");
    }

    /// <summary>And it should put the surface back roughly where it belonged.</summary>
    [Fact]
    public void ItMovesTheSurfaceBackTowardsWhereItShouldBe()
    {
        var sphere = Sphere();
        var rough = Roughened(sphere, 0.6f);

        var smoothed = MeshSmoothing.Smooth(rough, passes: 8);

        Assert.True(DistanceFrom(smoothed, sphere) < DistanceFrom(rough, sphere));
    }

    private static double DistanceFrom(Mesh mesh, Mesh reference) =>
        mesh.Positions.Select((p, i) => (double)(p - reference.Positions[i]).Length()).Average();

    /// <summary>Shakes the vertices about by a repeatable amount.</summary>
    private static Mesh Roughened(Mesh mesh, float amount)
    {
        var points = new List<Vector3>(mesh.VertexCount);

        for (int i = 0; i < mesh.VertexCount; i++)
        {
            // A cheap repeatable hash, so the same mesh is roughened the same way every run.
            uint h = (uint)(i * 2654435761u);
            float Wobble(int shift) => ((h >> shift) & 255) / 255f - 0.5f;

            points.Add(mesh.Positions[i] + new Vector3(Wobble(0), Wobble(8), Wobble(16)) * amount);
        }

        return new Mesh(points, mesh.Indices);
    }

    /// <summary>Total angle between neighbouring faces - how faceted the surface is.</summary>
    private static double Creases(Mesh mesh)
    {
        var normals = new Dictionary<(int, int), Vector3>();
        double total = 0;

        for (int t = 0; t + 2 < mesh.Indices.Count; t += 3)
        {
            Vector3 a = mesh.Positions[mesh.Indices[t]];
            var normal = Vector3.Cross(
                mesh.Positions[mesh.Indices[t + 1]] - a,
                mesh.Positions[mesh.Indices[t + 2]] - a);

            if (normal.LengthSquared() < 1e-18f) continue;
            normal = Vector3.Normalize(normal);

            for (int k = 0; k < 3; k++)
            {
                int x = mesh.Indices[t + k], y = mesh.Indices[t + (k + 1) % 3];
                var key = x < y ? (x, y) : (y, x);

                if (normals.TryGetValue(key, out var other))
                    total += Math.Acos(Math.Clamp(Vector3.Dot(normal, other), -1f, 1f));
                else
                    normals[key] = normal;
            }
        }

        return total;
    }

    /// <summary>An open mesh must keep its rim, or the hole changes shape as the surface relaxes.</summary>
    [Fact]
    public void TheRimOfAnOpenMeshIsHeldStill()
    {
        var box = Primitives.Box(20, 20, 20);
        var open = new Mesh(box.Positions, box.Indices.Take(box.Indices.Count - 3).ToList());

        var rim = OpenEdgeVertices(open);
        Assert.NotEmpty(rim);

        var smoothed = MeshSmoothing.Smooth(open, passes: 10);

        foreach (int v in rim)
            Assert.Equal(0f, (open.Positions[v] - smoothed.Positions[v]).Length(), 5);
    }

    private static HashSet<int> OpenEdgeVertices(Mesh mesh)
    {
        var counts = new Dictionary<(int, int), int>();
        for (int t = 0; t + 2 < mesh.Indices.Count; t += 3)
            for (int k = 0; k < 3; k++)
            {
                int a = mesh.Indices[t + k], b = mesh.Indices[t + (k + 1) % 3];
                var key = a < b ? (a, b) : (b, a);
                counts[key] = counts.GetValueOrDefault(key) + 1;
            }

        var rim = new HashSet<int>();
        foreach (var ((a, b), count) in counts)
        {
            if (count != 1) continue;
            rim.Add(a);
            rim.Add(b);
        }

        return rim;
    }

    [Fact]
    public void AskingForNoSmoothingDoesNothing()
    {
        var sphere = Sphere();

        Assert.Same(sphere, MeshSmoothing.Smooth(sphere, passes: 0));
        Assert.Same(sphere, MeshSmoothing.Smooth(sphere, passes: -3));
    }

    [Fact]
    public void AnEmptyMeshIsHandedStraightBack()
    {
        var empty = new Mesh();

        Assert.Same(empty, MeshSmoothing.Smooth(empty));
    }

    /// <summary>
    /// A cube collapses, and that is honest rather than a fault: with eight vertices there is no
    /// low frequency for the outward pass to hold on to, so the shape is entirely the thing being
    /// filtered out. It stays a valid solid, and the panel says so before anyone wonders - which
    /// is why rounding a box's corners is Round edges' job, not this one.
    /// </summary>
    [Fact]
    public void ACubeCollapsesButStaysValid()
    {
        var cube = Primitives.Box(20, 20, 20);

        var smoothed = MeshSmoothing.Smooth(cube, passes: 5);

        Assert.True(smoothed.CheckHealth().IsWatertight);
        Assert.True(smoothed.ComputeSignedVolume() > 0, "it must not turn inside out");
        Assert.True(smoothed.ComputeSignedVolume() < cube.ComputeSignedVolume());
    }
}
