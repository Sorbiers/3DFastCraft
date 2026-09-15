using System.Numerics;
using FastCraft3D.Geometry;
using Xunit;

namespace FastCraft3D.Tests;

public class RestingFacesTests
{
    [Fact]
    public void TheHullOfACubeIsTwelveTriangles()
    {
        Assert.Equal(12, ConvexHull.Build(Primitives.Box(20, 20, 20).Positions).Count);
    }

    [Fact]
    public void NoPointLiesOutsideTheHull()
    {
        var random = new Random(7);
        var points = Enumerable.Range(0, 3000)
            .Select(_ => new Vector3(random.NextSingle() * 50, random.NextSingle() * 30, random.NextSingle() * 20))
            .ToList();

        var hull = ConvexHull.Build(points);
        Assert.NotEmpty(hull);

        foreach (var (a, b, c) in hull)
        {
            var normal = Vector3.Normalize(Vector3.Cross(points[b] - points[a], points[c] - points[a]));
            float offset = Vector3.Dot(normal, points[a]);
            Assert.All(points, p => Assert.True(Vector3.Dot(normal, p) - offset < 1e-3f));
        }
    }

    [Fact]
    public void PointsInOnePlaneHaveNoHull()
    {
        var flat = Enumerable.Range(0, 50).Select(i => new Vector3(i % 7, i % 5, 0)).ToList();
        Assert.Empty(ConvexHull.Build(flat));
    }

    [Fact]
    public void ACubeCanRestOnAnyOfItsSixFaces()
    {
        var faces = RestingFaces.Find(Primitives.Box(20, 20, 20));

        Assert.Equal(6, faces.Count);
        Assert.All(faces, f => Assert.Equal(400f, f.Area, 1));
    }

    /// <summary>A cup stands upside down on its rim, where the mesh has no face at all.</summary>
    [Fact]
    public void AnOpenTopIsAFaceToStandOn()
    {
        var cup = LocalCsgCup();
        var faces = RestingFaces.Find(cup);

        Assert.Contains(faces, f => f.Normal.Z > 0.99f);
        Assert.Contains(faces, f => f.Normal.Z < -0.99f);
    }

    /// <summary>A leaning tower on a narrow face would topple, so that face is not offered.</summary>
    [Fact]
    public void AFaceTheWeightDoesNotFallOverIsNotOffered()
    {
        // A flat slab with a small square foot far off to one side: the foot's underside is on the
        // hull, but the slab's weight is nowhere near it.
        var slab = MeshTransform.Transformed(Primitives.Box(60, 20, 4), Matrix4x4.CreateTranslation(0, 0, 10));
        var foot = MeshTransform.Transformed(Primitives.Box(4, 4, 8), Matrix4x4.CreateTranslation(28, 0, 4));
        var shape = Mesh.Combine([slab, foot]);

        var faces = RestingFaces.Find(shape);

        Assert.DoesNotContain(faces, f => f.Normal.Z < -0.99f && f.Area < 20f);
    }

    [Fact]
    public void TheFaceUnderALookFromAboveIsTheTop()
    {
        var faces = RestingFaces.Find(Primitives.Box(20, 20, 20));

        int under = RestingFaces.Under(faces, new Vector3(1, 1, 100), -Vector3.UnitZ);

        Assert.True(under >= 0);
        Assert.True(faces[under].Normal.Z > 0.99f);
        Assert.Equal(-1, RestingFaces.Under(faces, new Vector3(100, 100, 100), -Vector3.UnitZ));
    }

    private static Mesh LocalCsgCup()
    {
        var outer = Primitives.Box(30, 30, 30);
        var inner = MeshTransform.Transformed(Primitives.Box(24, 24, 30), Matrix4x4.CreateTranslation(0, 0, 5));
        return FastCraft3D.Geometry.Csg.LocalCsg.Subtract(outer, inner);
    }
}