using System.Numerics;
using FastCraft3D.Geometry;
using FastCraft3D.Geometry.Csg;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>
/// Rebuild face lays one flat face again from its outline: the points inside it go, nothing on
/// the outline moves, and the rest of the mesh is left exactly as it was.
/// </summary>
public class FaceRebuildTests
{
    private static FacePatch Top(Mesh mesh) =>
        FacePatch.Find(mesh, new Vector3(0.3f, 0.2f, mesh.ComputeBounds().Max.Z), Vector3.UnitZ)
        ?? throw new InvalidOperationException("no top face");

    private static HashSet<int> Inside(FacePatch face)
    {
        var outline = face.Boundary.SelectMany(e => new[] { e.A, e.B }).ToHashSet();
        return face.Triangles.SelectMany(t => new[] { t, t + 1, t + 2 })
            .Select(i => face.Mesh.Indices[i]).Where(v => !outline.Contains(v)).ToHashSet();
    }

    /// <summary>Every triangle as its three corners, so two meshes can be compared whatever their numbering.</summary>
    private static List<(Vector3, Vector3, Vector3)> Corners(Mesh mesh, IEnumerable<int> triangles) =>
        triangles.Select(t => (mesh.Positions[mesh.Indices[t]], mesh.Positions[mesh.Indices[t + 1]], mesh.Positions[mesh.Indices[t + 2]]))
            .ToList();

    private static IEnumerable<int> AllTriangles(Mesh mesh) => Enumerable.Range(0, mesh.TriangleCount).Select(i => i * 3);

    private static void Sound(Mesh before, Mesh after)
    {
        var health = after.CheckHealth();
        Assert.True(health.IsWatertight, health.Describe());
        Assert.Equal(before.ComputeSignedVolume(), after.ComputeSignedVolume(), 3);
        Assert.Equal(before.ComputeBounds().Min, after.ComputeBounds().Min);
        Assert.Equal(before.ComputeBounds().Max, after.ComputeBounds().Max);

        // Nothing moved and nothing new appeared: every point is one the mesh already had.
        var had = before.Positions.ToHashSet();
        Assert.All(after.Positions, p => Assert.Contains(p, had));
    }

    [Fact]
    public void TheTopOfASubdividedBoxLosesEveryPointInsideItAndKeepsItsOutline()
    {
        var mesh = MeshSubdivision.Subdivide(Primitives.Box(20, 20, 20), 3);
        var face = Top(mesh);
        int outline = face.Boundary.Count;
        Assert.NotEmpty(Inside(face));

        var result = FaceRebuild.Rebuild(mesh, face.Triangles);

        Assert.True(result.Changed, result.Refusal);
        Assert.Equal(outline - 2, result.TrianglesAfter);
        Assert.Equal(Inside(face).Count, result.PointsRemoved);
        Sound(mesh, result.Mesh);

        var again = Top(result.Mesh);
        Assert.Empty(Inside(again));
        Assert.Equal(outline, again.Boundary.Count);
        Assert.Equal(face.Area, again.Area, 2);
    }

    [Fact]
    public void EveryTriangleOutsideTheFaceIsLeftExactlyAsItWas()
    {
        var mesh = MeshSubdivision.Subdivide(Primitives.Box(20, 20, 20), 2);
        var face = Top(mesh);
        var inFace = face.Triangles.ToHashSet();

        var result = FaceRebuild.Rebuild(mesh, face.Triangles);
        Assert.True(result.Changed, result.Refusal);

        var others = Corners(mesh, AllTriangles(mesh).Where(t => !inFace.Contains(t)));
        var now = Corners(result.Mesh, AllTriangles(result.Mesh));
        Assert.All(others, t => Assert.Contains(t, now));
        Assert.Equal(others.Count + result.TrianglesAfter, result.Mesh.TriangleCount);
    }

    [Fact]
    public void AFaceWithAHoleThroughItKeepsTheHole()
    {
        var mesh = CsgSolid.Subtract(MeshSubdivision.Subdivide(Primitives.Box(20, 20, 20), 2), Primitives.Prism(4, 40, 24));
        var face = FacePatch.Find(mesh, new Vector3(8, 8, 10), Vector3.UnitZ)!;
        Assert.NotNull(face);

        var result = FaceRebuild.Rebuild(mesh, face.Triangles);

        Assert.Null(result.Refusal);
        Sound(mesh, result.Mesh);

        // Outline points plus two for the hole, less two.
        Assert.Equal(face.Boundary.Count, result.TrianglesAfter);
        var again = FacePatch.Find(result.Mesh, new Vector3(8, 8, 10), Vector3.UnitZ)!;
        Assert.Equal(face.Area, again.Area, 2);
    }

    [Fact]
    public void ACurvedSurfaceIsLeftAlone()
    {
        var mesh = Primitives.Sphere(10, 32, 16);
        var face = FacePatch.Find(mesh, new Vector3(10, 0, 0), Vector3.UnitX)!;

        var result = FaceRebuild.Rebuild(mesh, face.Triangles);

        Assert.False(result.Changed);
        Assert.Same(mesh, result.Mesh);
    }

    [Fact]
    public void AFaceAlreadyLaidFromItsOutlineIsNotChangedAgain()
    {
        var mesh = MeshSubdivision.Subdivide(Primitives.Box(20, 20, 20), 2);
        var once = FaceRebuild.Rebuild(mesh, Top(mesh).Triangles).Mesh;

        var twice = FaceRebuild.Rebuild(once, Top(once).Triangles);

        Assert.False(twice.Changed);
        Assert.Same(once, twice.Mesh);
    }

    [Fact]
    public void TheSameFaceAlwaysComesOutTheSame()
    {
        var mesh = MeshSubdivision.Subdivide(Primitives.Box(20, 20, 20), 2);

        var a = FaceRebuild.Rebuild(mesh, Top(mesh).Triangles).Mesh;
        var b = FaceRebuild.Rebuild(mesh, Top(mesh).Triangles).Mesh;

        Assert.Equal(a.Positions, b.Positions);
        Assert.Equal(a.Indices, b.Indices);
    }

    [Fact]
    public void AStepOfAHundredthOfAMillimetreIsNotFlattened()
    {
        // Two boxes side by side, one a hundredth higher: Surface info takes both tops as one
        // face, and laying them as one would shave the step off.
        var low = Primitives.Box(10, 10, 10);
        var high = MeshTransform.Transformed(Primitives.Box(10, 10, 10.02f), Matrix4x4.CreateTranslation(10, 0, 0.01f));
        var mesh = CsgSolid.Union(MeshSubdivision.Subdivide(low, 2), MeshSubdivision.Subdivide(high, 2));
        var face = FacePatch.Find(mesh, new Vector3(0.3f, 0.2f, 5), Vector3.UnitZ)!;

        var result = FaceRebuild.Rebuild(mesh, face.Triangles);

        if (result.Changed) Sound(mesh, result.Mesh);
        Assert.Equal(mesh.ComputeBounds().Max.Z, result.Mesh.ComputeBounds().Max.Z);
        Assert.Contains(result.Mesh.Positions, p => MathF.Abs(p.Z - 5f) < 1e-4f);
    }

    /// <summary>
    /// Every flat face of every primitive, whole and split at several heights, laid again: none
    /// may open the mesh, move a point or change the volume, and none may come out with more
    /// triangles than it had.
    /// </summary>
    [Fact]
    public void NoFlatFaceOfAnyPrimitiveOrSplitHalfIsEverMadeWorse()
    {
        // Not the sphere or the torus: every face of theirs is one facet, which the curved
        // surface test already covers, and finding a thousand one-facet faces one by one is what
        // made this take minutes.
        foreach (var kind in Enum.GetValues<PrimitiveKind>().Where(k => k is not PrimitiveKind.Sphere and not PrimitiveKind.Torus))
        {
            var whole = MeshSubdivision.Subdivide(Primitives.Create(kind), 1);
            var shapes = new List<Mesh> { whole };

            foreach (float height in new[] { -4.3f, 0.7f, 5.1f })
            {
                var (front, back) = PlaneSplit.Split(whole, Axis.Z, height, SplitKeep.Both);
                if (front is not null) shapes.Add(front);
                if (back is not null) shapes.Add(back);
            }

            foreach (var mesh in shapes)
            {
                var seen = new HashSet<int>();
                foreach (int t in AllTriangles(mesh))
                {
                    if (seen.Contains(t)) continue;
                    var face = FacePatch.FromTriangle(mesh, t);
                    if (face is null) continue;
                    seen.UnionWith(face.Triangles);

                    var result = FaceRebuild.Rebuild(mesh, face.Triangles);
                    if (!result.Changed) continue;

                    Assert.True(result.TrianglesAfter <= result.TrianglesBefore, $"{kind}: {result.TrianglesBefore} became {result.TrianglesAfter}");
                    Assert.Equal(mesh.CheckHealth().IsWatertight, result.Mesh.CheckHealth().IsWatertight);
                    double was = mesh.ComputeSignedVolume(), now = result.Mesh.ComputeSignedVolume();
                    Assert.True(Math.Abs(now - was) <= 1e-6 * Math.Abs(was) + 1e-4, $"{kind}: volume {was} became {now}");
                    var had = mesh.Positions.ToHashSet();
                    Assert.All(result.Mesh.Positions, p => Assert.Contains(p, had));
                }
            }
        }
    }
}
