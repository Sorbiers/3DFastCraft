using System.Numerics;
using FastCraft3D.Geometry;
using FastCraft3D.Geometry.Csg;
using FastCraft3D.Geometry.Engraving;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>
/// Lettering goes through Manifold first, which cuts cleanly where the BSP engine tears.
///
/// An outline heart - an outline with a hole in it - wrapped on the default cylinder tore in every
/// placement tried through the BSP engine, cut or raised.
/// </summary>
public class ManifoldCsgTests
{
    private static double Volume(Mesh mesh)
    {
        double v = 0;
        for (int i = 0; i < mesh.Indices.Count; i += 3)
        {
            var a = mesh.Positions[mesh.Indices[i]];
            var b = mesh.Positions[mesh.Indices[i + 1]];
            var c = mesh.Positions[mesh.Indices[i + 2]];
            v += Vector3.Dot(a, Vector3.Cross(b, c)) / 6.0;
        }

        return v;
    }

    /// <summary>A heart 10 mm tall, with a smaller heart taken out of the middle of it.</summary>
    private static TextShape OutlineHeart()
    {
        List<Vector2> Heart(float scale, float lift) => Enumerable.Range(0, 96).Select(i =>
        {
            float t = i * MathF.Tau / 96;
            float x = 16f * MathF.Pow(MathF.Sin(t), 3);
            float y = 13f * MathF.Cos(t) - 5f * MathF.Cos(2 * t) - 2f * MathF.Cos(3 * t) - MathF.Cos(4 * t);
            return new Vector2(x, y + lift) * scale;
        }).ToList();

        const float mm = 10f / 30f;
        return new TextShape(Heart(mm, 0f), [Heart(mm * 0.7f, 1f)]);
    }

    [Fact]
    public void TheNativeLibraryLoadsAndSubtractsToTheRightVolume()
    {
        var block = Primitives.Box(20, 20, 20);
        var tool = MeshTransform.Transformed(Primitives.Box(20, 20, 20), Matrix4x4.CreateTranslation(10, 0, 0));

        var cut = ManifoldCsg.Subtract(block, tool);

        Assert.NotNull(cut);
        Assert.False(ManifoldCsg.Unavailable);
        Assert.True(cut.CheckHealth().IsWatertight);
        Assert.Equal(4000, Volume(cut), 1);
    }

    [Fact]
    public void AnOpenMeshIsDeclinedRatherThanThrown()
    {
        var open = Primitives.Box(20, 20, 20);
        var torn = new Mesh(open.Positions.ToList(), open.Indices.Take(open.Indices.Count - 3).ToList());

        Assert.Null(ManifoldCsg.Subtract(torn, Primitives.Box(5, 5, 5)));
    }

    [Theory]
    [InlineData(-1.9f, 0f, 0f, false)]
    [InlineData(-1.6f, -8.24f, 1.78f, false)]
    [InlineData(-1.3f, 3f, -2f, false)]
    [InlineData(2.5f, -4f, 1.78f, false)]
    [InlineData(-1.6f, 0f, 0f, true)]
    [InlineData(2.5f, 3f, -2f, true)]
    public void AnOutlineHeartWrapsCleanlyOntoTheDefaultCylinder(float angle, float across, float up, bool raised)
    {
        var cylinder = Primitives.Create(PrimitiveKind.Cylinder);
        var bounds = cylinder.ComputeBounds();
        var axis = new Vector2(bounds.Center.X, bounds.Center.Y);
        var profile = SurfaceProfile.Build(cylinder, axis)!;

        float radius = profile.RadiusAt(angle, bounds.Center.Z)!.Value;
        var surface = new CylinderSurface(new Vector3(axis.X, axis.Y, bounds.Center.Z), radius, angle, profile);
        var heart = new SurfacePlacement(new Vector2(across, up), 0).Apply([OutlineHeart()]);

        var result = TextCutter.Apply(cylinder, heart, surface, raised, depthMm: 0.8f);

        Assert.NotNull(result);
        Assert.True(result.CheckHealth().IsWatertight, result.CheckHealth().Describe());

        // Cut in or stood proud, not left alone: the volume moves the right way.
        double before = Volume(cylinder), after = Volume(result);
        Assert.True(raised ? after > before + 5 : after < before - 5, $"volume {before:0.0} -> {after:0.0}");
    }
}
