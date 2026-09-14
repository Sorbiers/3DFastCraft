using System.Numerics;
using FastCraft3D.Geometry;
using FastCraft3D.Geometry.Engraving;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>
/// Wrapped lettering follows the object's own surface, not a circle through the click.
///
/// A heart cut 1 mm into a barrel stretched to an oval tore at some clicks and not others: the
/// circle it was wrapped on sat outside the surface at one end of the heart and inside it at the
/// other, and the cutter crossed the surface at a shallow angle at both.
/// </summary>
public class SurfaceProfileTests
{
    private static List<Vector2> Circle(float r, int n, bool anticlockwise) =>
        Enumerable.Range(0, n).Select(i =>
        {
            float a = (anticlockwise ? 1 : -1) * i * MathF.Tau / n;
            return new Vector2(r * MathF.Cos(a), r * MathF.Sin(a));
        }).ToList();

    private static Mesh OvalBarrel(float stretch = 1.3f) =>
        MeshTransform.Transformed(Primitives.Prism(20f, 40f, 32), Matrix4x4.CreateScale(stretch, 1f, 1f));

    [Fact]
    public void TheProfileReadsTheRealSurfaceOfAStretchedBarrel()
    {
        var barrel = OvalBarrel();
        var bounds = barrel.ComputeBounds();
        var profile = SurfaceProfile.Build(barrel, new Vector2(bounds.Center.X, bounds.Center.Y))!;

        for (int i = 0; i < 24; i++)
        {
            float angle = i * MathF.Tau / 24 + 0.05f;
            float cos = MathF.Cos(angle), sin = MathF.Sin(angle);

            // The ellipse the 32 flats stand in for; a flat is at most 0.13 mm inside it.
            float ellipse = 1f / MathF.Sqrt(cos * cos / (26f * 26f) + sin * sin / (20f * 20f));
            float read = profile.RadiusAt(angle, bounds.Center.Z)!.Value;

            Assert.InRange(read, ellipse - 0.3f, ellipse + 0.05f);
        }
    }

    [Fact]
    public void NothingIsReadAboveOrBelowItExceptTheNearestSection()
    {
        var barrel = OvalBarrel();
        var bounds = barrel.ComputeBounds();
        var profile = SurfaceProfile.Build(barrel, new Vector2(bounds.Center.X, bounds.Center.Y))!;

        // Lettering running off the top keeps following the last section rather than jumping.
        float top = profile.RadiusAt(0.3f, bounds.Max.Z + 5f)!.Value;
        float middle = profile.RadiusAt(0.3f, bounds.Center.Z)!.Value;
        Assert.Equal(middle, top, 0.05f);
    }

    [Theory]
    [InlineData(2.22f, 1.3f)]
    [InlineData(3.70f, 1.3f)]
    [InlineData(0.37f, 1.3f)]
    [InlineData(1.11f, 1.3f)]
    [InlineData(4.81f, 1.3f)]
    [InlineData(0.37f, 1f)]
    [InlineData(2.22f, 1f)]
    [InlineData(1.11f, 1.6f)]
    public void AHeartCutsCleanlyIntoAnOvalBarrelWhereverItIsClicked(float angle, float stretch)
    {
        var barrel = OvalBarrel(stretch);
        var bounds = barrel.ComputeBounds();
        var axis = new Vector2(bounds.Center.X, bounds.Center.Y);
        var profile = SurfaceProfile.Build(barrel, axis)!;

        // A ring stands in for the heart: what matters is an outline with a hole in it.
        var ring = new TextShape(Circle(6f, 48, true), [Circle(4f, 48, false)]);

        float radius = profile.RadiusAt(angle, bounds.Center.Z)!.Value;
        var surface = new CylinderSurface(new Vector3(axis.X, axis.Y, bounds.Center.Z), radius, angle, profile);

        var cut = TextCutter.Apply(barrel, [ring], surface, raised: false, depthMm: 1f);

        Assert.NotNull(cut);
        Assert.True(cut.CheckHealth().IsWatertight, cut.CheckHealth().Describe());
    }

    [Fact]
    public void TheCutIsAsDeepAtBothEndsOfTheLettering()
    {
        var barrel = OvalBarrel();
        var bounds = barrel.ComputeBounds();
        var axis = new Vector2(bounds.Center.X, bounds.Center.Y);
        var profile = SurfaceProfile.Build(barrel, axis)!;

        const float angle = 3.70f;
        float radius = profile.RadiusAt(angle, bounds.Center.Z)!.Value;
        var surface = new CylinderSurface(new Vector3(axis.X, axis.Y, bounds.Center.Z), radius, angle, profile);

        // Six millimetres either side of the click, the floor is still a millimetre under the surface.
        foreach (float across in new[] { -6f, 6f })
        {
            var floor = surface.At(new Vector2(across, 0), -1f);
            var onSurface = surface.At(new Vector2(across, 0), 0f);
            float depth = (onSurface - floor).Length();

            Assert.Equal(1f, depth, 0.01f);

            float real = profile.RadiusAt(angle + across / radius, bounds.Center.Z)!.Value;
            Assert.Equal(real, new Vector2(onSurface.X - axis.X, onSurface.Y - axis.Y).Length(), 0.01f);
        }
    }
}
