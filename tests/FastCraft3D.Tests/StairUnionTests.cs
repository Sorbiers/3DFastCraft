using System.Numerics;
using FastCraft3D.Geometry;
using FastCraft3D.Geometry.Csg;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>
/// A flight of steps, unioned one at a time - which is what building a staircase in the app
/// amounts to, and what found the plane epsilon.
///
/// Each step meets the next on part of a face, and they all stand on the same slab. That is a
/// lot of coplanar contact in a small space, and it tore every time while the classification
/// tolerance was finer than a float can hold at these coordinates. See <see cref="CsgPlane"/>.
/// </summary>
public class StairUnionTests
{
    private static Mesh Block(float x, float y, float z, float sx, float sy, float sz) =>
        MeshTransform.Transformed(
            Primitives.Box(sx, sy, sz),
            Matrix4x4.CreateTranslation(x + sx / 2f, y + sy / 2f, z + sz / 2f));

    /// <summary>The flight from the 1:87 house: twelve steps, 2.04 mm risers, 2.9 mm treads.</summary>
    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(54f, 6f)]     // where it actually stands in the model, 90 mm out from the origin
    public void AFlightOfStepsUnionsCleanly(float x, float y)
    {
        float riser = 26.5f / 13f;
        var built = Block(x, y, 3, 14, 2.9f, riser);

        for (int n = 2; n <= 12; n++)
            built = CsgSolid.Union(built, Block(x, y + (n - 1) * 2.9f, 3, 14, 2.9f, n * riser));

        Assert.True(built.CheckHealth().IsWatertight, built.CheckHealth().Describe());
    }

    /// <summary>
    /// And standing on the slab it was cast with, which is one coplanar contact more: every step
    /// meets the one beside it and the slab underneath at the same time.
    ///
    /// Healed as the app heals it. The raw union leaves a couple of edges over at the corner
    /// where three shared planes meet, and the stitching is what closes them - which is exactly
    /// the division of labour the two are meant to have.
    /// </summary>
    [Fact]
    public void TheFlightJoinsTheSlabItStandsOn()
    {
        float riser = 26.5f / 13f;
        var built = Block(0, 0, 0, 110, 90, 3);

        for (int n = 1; n <= 12; n++)
            built = CsgSolid.Union(built, Block(54, 6 + (n - 1) * 2.9f, 3, 14, 2.9f, n * riser));

        var healed = MeshHealer.Heal(built).Mesh;
        Assert.True(healed.CheckHealth().IsWatertight, healed.CheckHealth().Describe());
    }

    /// <summary>
    /// The tolerance has to stay above what a float can resolve out where models actually sit.
    /// At 90 mm from the origin one step of a float is 7.6e-6 mm; a tolerance below that cannot
    /// tell a shared plane from two different ones.
    /// </summary>
    [Fact]
    public void TheToleranceClearsTheFloatingPointNoise()
    {
        const float far = 90f;
        float step = MathF.BitIncrement(far) - far;

        Assert.True(CsgPlane.Epsilon > step * 4,
            $"a float steps by {step} mm at {far} mm, and the epsilon is {CsgPlane.Epsilon}");

        // And well under anything a printer could lay down.
        Assert.True(CsgPlane.Epsilon < 0.01);
    }
}
