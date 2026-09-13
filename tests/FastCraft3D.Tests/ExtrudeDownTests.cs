using System.Numerics;
using FastCraft3D.Geometry;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>A model cut at a height and built straight down to a flat base on the plate.</summary>
public class ExtrudeDownTests
{
    /// <summary>
    /// Volume by the divergence theorem, signed. Positive only when every face points outward,
    /// which is the check that walls and base are wound the way the model is.
    /// </summary>
    private static float SignedVolume(Mesh mesh)
    {
        double total = 0;
        for (int t = 0; t + 2 < mesh.Indices.Count; t += 3)
        {
            Vector3 a = mesh.Positions[mesh.Indices[t]];
            Vector3 b = mesh.Positions[mesh.Indices[t + 1]];
            Vector3 c = mesh.Positions[mesh.Indices[t + 2]];
            total += Vector3.Dot(a, Vector3.Cross(b, c));
        }

        return (float)(total / 6.0);
    }

    [Fact]
    public void ABoxOffThePlateStandsOnItAfterward()
    {
        // 20 mm cube, lifted so it spans 10 to 30.
        var lifted = MeshTransform.Transformed(Primitives.Box(20, 20, 20), Matrix4x4.CreateTranslation(0, 0, 20));

        var result = ExtrudeDown.Apply(lifted, 15f);

        Assert.NotNull(result);
        Assert.True(result.CheckHealth().IsWatertight);

        var bounds = result.ComputeBounds();
        Assert.Equal(0f, bounds.Min.Z, 3);
        Assert.Equal(30f, bounds.Max.Z, 3);

        // 20 x 20 all the way from the plate to the top, and facing outward.
        Assert.Equal(20f * 20f * 30f, SignedVolume(result), 0);
    }

    /// <summary>A section with a hole in it keeps the hole all the way down: a hollow neck stays hollow.</summary>
    [Fact]
    public void ARingCutThroughKeepsItsHoleInTheBase()
    {
        var ring = MeshTransform.Transformed(Primitives.Torus(20, 7, 64, 32), Matrix4x4.CreateTranslation(0, 0, 12));

        var result = ExtrudeDown.Apply(ring, 12f);

        Assert.NotNull(result);
        Assert.True(result.CheckHealth().IsWatertight);
        Assert.True(SignedVolume(result) > 0f);

        // Nothing of the base covers the middle of the ring.
        for (int t = 0; t + 2 < result.Indices.Count; t += 3)
        {
            Vector3 a = result.Positions[result.Indices[t]];
            Vector3 b = result.Positions[result.Indices[t + 1]];
            Vector3 c = result.Positions[result.Indices[t + 2]];
            if (a.Z != 0f || b.Z != 0f || c.Z != 0f) continue;

            var centre = (a + b + c) / 3f;
            Assert.True(new Vector2(centre.X, centre.Y).Length() > 12f, "the base fills the hole");
        }
    }

    [Theory]
    [InlineData(30f)]  // at the top
    [InlineData(40f)]  // above it
    [InlineData(5f)]   // below the lowest point, so the cut misses
    [InlineData(0f)]   // on the plate: no walls to build
    public void AHeightThatDoesNotCrossTheModelIsRefused(float height)
    {
        var lifted = MeshTransform.Transformed(Primitives.Box(20, 20, 20), Matrix4x4.CreateTranslation(0, 0, 20));
        Assert.Null(ExtrudeDown.Apply(lifted, height));
    }
}
