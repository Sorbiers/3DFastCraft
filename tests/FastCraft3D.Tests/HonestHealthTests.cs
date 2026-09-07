using System.Numerics;
using FastCraft3D.Geometry;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>
/// What the health check is allowed to claim.
///
/// The house build turned up a group of touching walls reported as "watertight - ready to print"
/// while it was not a solid at all, and every boolean after it inherited the damage.
/// </summary>
public class HonestHealthTests
{
    private static Mesh Block(float x, float y, float z, float sx, float sy, float sz) =>
        MeshTransform.Transformed(
            Primitives.Box(sx, sy, sz),
            Matrix4x4.CreateTranslation(x + sx / 2f, y + sy / 2f, z + sz / 2f));

    /// <summary>
    /// Two shells meeting on a face are not one solid, whatever the edge count says. Unwelded,
    /// each copy of the shared face keeps its own vertices, so every edge still belongs to
    /// exactly two triangles - and the mesh used to pass.
    /// </summary>
    [Fact]
    public void ConcatenatingTouchingPartsIsNotWatertight()
    {
        var pair = Mesh.Combine([Block(0, 0, 0, 10, 10, 10), Block(10, 0, 0, 10, 10, 10)]);

        Assert.False(pair.CheckHealth().IsWatertight,
            "two shells sharing a face have that face inside them, with material both sides");
    }

    /// <summary>Parts that do not touch are two separate solids, and that is fine.</summary>
    [Fact]
    public void ConcatenatingPartsThatDoNotTouchIsStillSound()
    {
        var pair = Mesh.Combine([Block(0, 0, 0, 10, 10, 10), Block(20, 0, 0, 10, 10, 10)]);

        Assert.True(pair.CheckHealth().IsWatertight, pair.CheckHealth().Describe());
    }

    /// <summary>And the ordinary case is unchanged.</summary>
    [Fact]
    public void APlainSolidIsStillPrintable()
    {
        Assert.True(Primitives.Box(10, 20, 30).CheckHealth().IsWatertight);
        Assert.True(Primitives.Sphere(10, 24, 16).CheckHealth().IsWatertight);
    }
}
