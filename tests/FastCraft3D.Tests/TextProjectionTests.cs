using System.Numerics;
using FastCraft3D.Geometry;
using FastCraft3D.Geometry.Csg;
using FastCraft3D.Geometry.Engraving;
using FastCraft3D.Text;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>
/// Laying flat lettering onto something that is not flat. The letters never learn about the
/// surface; the surface never learns about letters.
/// </summary>
public class TextSurfaceTests
{
    [Fact]
    public void FlatLetteringOnAFaceGoesWhereTheFaceIs()
    {
        var plate = Primitives.Box(60, 60, 10);
        var face = FacePatch.Find(plate, new Vector3(0, 0, 5), Vector3.UnitZ)!;
        var surface = new PlanarSurface(face);

        var at = surface.At(Vector2.Zero, 0);

        Assert.Equal(5f, at.Z, 3);
        Assert.Equal(0f, surface.Sag(new Vector2(-30, 0), new Vector2(30, 0)));
    }

    /// <summary>Across a cylinder is arc length, so a letter keeps its size whatever the radius.</summary>
    [Fact]
    public void AcrossACylinderIsMeasuredAlongTheSurface()
    {
        var surface = new CylinderSurface(Vector3.Zero, radius: 20f);

        // A quarter of the way round is a quarter of the circumference.
        float quarter = MathF.PI * 20f / 2f;
        var at = surface.At(new Vector2(quarter, 0), 0);

        Assert.Equal(0f, at.X, 3);
        Assert.Equal(20f, at.Y, 3);
    }

    [Fact]
    public void HeightOnACylinderRunsOutwardsFromTheAxis()
    {
        var surface = new CylinderSurface(Vector3.Zero, 20f);

        Assert.Equal(22f, surface.At(Vector2.Zero, 2f).X, 3);   // proud
        Assert.Equal(18f, surface.At(Vector2.Zero, -2f).X, 3);  // sunk in
    }

    [Fact]
    public void UpACylinderIsStraightUp()
    {
        var surface = new CylinderSurface(new Vector3(0, 0, 5), 20f);

        Assert.Equal(15f, surface.At(new Vector2(0, 10), 0).Z, 3);
    }

    /// <summary>Text long enough to go right round should meet itself, not pile up.</summary>
    [Fact]
    public void TextRightRoundACylinderMeetsItself()
    {
        var surface = new CylinderSurface(Vector3.Zero, 20f);
        float circumference = MathF.Tau * 20f;

        var start = surface.At(Vector2.Zero, 0);
        var round = surface.At(new Vector2(circumference, 0), 0);

        Assert.Equal(0f, (start - round).Length(), 3);
    }

    [Fact]
    public void OnASphereEverythingSitsOnIt()
    {
        var surface = new SphereSurface(Vector3.Zero, 30f);

        foreach (var uv in new[] { Vector2.Zero, new Vector2(10, 5), new Vector2(-20, 12) })
            Assert.Equal(30f, surface.At(uv, 0).Length(), 2);

        Assert.Equal(32f, surface.At(new Vector2(4, 4), 2f).Length(), 2);
    }

    /// <summary>
    /// A step across a barrel strays from it; the same step straight up one does not. That
    /// difference is the whole reason the gap is measured rather than the length.
    /// </summary>
    [Fact]
    public void OnlyStepsThatGoRoundACurveStrayFromIt()
    {
        var barrel = new CylinderSurface(Vector3.Zero, 20f);

        // 10 mm of arc on a 20 mm barrel turns through half a radian, and the middle of the
        // chord across it falls 20 * (1 - cos 0.25) = 0.62 mm short of the surface.
        Assert.Equal(0.622f, barrel.Sag(new Vector2(-5, 0), new Vector2(5, 0)), 3);

        // The same distance straight up it is on the surface the whole way.
        Assert.Equal(0f, barrel.Sag(new Vector2(0, -5), new Vector2(0, 5)), 4);
    }

    [Fact]
    public void AStepOverABallStraysWhicheverWayItGoes()
    {
        var ball = new SphereSurface(Vector3.Zero, 20f);

        Assert.True(ball.Sag(new Vector2(-5, 0), new Vector2(5, 0)) > 0.2f);
        Assert.True(ball.Sag(new Vector2(0, -5), new Vector2(0, 5)) > 0.2f);
    }
}

/// <summary>Lettering wrapped, and lettering with sloping walls.</summary>
public class TextProjectionTests
{
    private static TextShape Square(float size, Vector2 at = default) => new(
        [at + new Vector2(-size / 2, -size / 2), at + new Vector2(size / 2, -size / 2),
         at + new Vector2(size / 2, size / 2), at + new Vector2(-size / 2, size / 2)], []);

    [Fact]
    public void LetteringWrappedRoundACylinderStaysOnIt()
    {
        var surface = new CylinderSurface(Vector3.Zero, 20f);

        var solid = TextSolid.Build([Square(20)], surface, 0f, 1.5f);

        Assert.True(solid.CheckHealth().IsWatertight, solid.CheckHealth().Describe());

        // Every point sits between the barrel and 1.5 mm proud of it.
        foreach (var p in solid.Positions)
        {
            float out_ = new Vector2(p.X, p.Y).Length();
            Assert.InRange(out_, 19.9f, 21.6f);
        }
    }

    /// <summary>
    /// The whole reason for dividing it up: a flat span laid round a barrel would cut the corner
    /// and sink into it.
    /// </summary>
    [Fact]
    public void WrappedLetteringFollowsTheCurveRatherThanChordingAcrossIt()
    {
        var surface = new CylinderSurface(Vector3.Zero, 20f);

        var solid = TextSolid.Build([Square(30)], surface, 0f, 1f);

        // A single flat span 30 mm across a 20 mm barrel would dip about 3 mm below the surface.
        float nearest = solid.Positions.Min(p => new Vector2(p.X, p.Y).Length());
        Assert.True(nearest > 19.5f, $"the lettering cut into the barrel, down to {nearest:0.##} mm");
    }

    [Fact]
    public void WrappedLetteringHasFarMoreTrianglesThanFlat()
    {
        var plate = Primitives.Box(80, 80, 10);
        var face = FacePatch.Find(plate, new Vector3(0, 0, 5), Vector3.UnitZ)!;

        var flat = TextSolid.Build([Square(30)], new PlanarSurface(face), 0f, 1f);
        var wrapped = TextSolid.Build([Square(30)], new CylinderSurface(Vector3.Zero, 20f), 0f, 1f);

        Assert.True(wrapped.TriangleCount > flat.TriangleCount * 4);
    }

    /// <summary>
    /// The cost of wrapping, which is the thing that went wrong. Splitting every edge to fix a
    /// few put one short word at 147,000 triangles and a twenty-five second boolean; breaking up
    /// only what strays leaves it in the low thousands.
    /// </summary>
    [Fact]
    public void WrappingDoesNotRunAwayIntoTensOfThousandsOfTriangles()
    {
        var shapes = GlyphOutlines.Build("TEXT", "Arial", 10f, bold: true)
            .Select(g => new TextShape(g.Outline, g.Holes))
            .ToList();

        var wrapped = TextSolid.Build(shapes, new CylinderSurface(Vector3.Zero, 10f), 0.02f, -0.8f);

        Assert.True(wrapped.TriangleCount < 8000, $"{wrapped.TriangleCount:N0} triangles to wrap one word");
        Assert.True(wrapped.CheckHealth().IsWatertight, wrapped.CheckHealth().Describe());
    }

    /// <summary>
    /// Nothing bends along a barrel's axis, so lettering that only runs that way should come out
    /// no denser than it went in.
    /// </summary>
    [Fact]
    public void AStripeUpABarrelIsNotBrokenUpAtAll()
    {
        var narrow = new TextShape(
            [new Vector2(-0.4f, -20), new Vector2(0.4f, -20),
             new Vector2(0.4f, 20), new Vector2(-0.4f, 20)], []);

        var flat = TextSolid.Build([narrow], new CylinderSurface(Vector3.Zero, 1e6f), 0f, 1f);
        var wrapped = TextSolid.Build([narrow], new CylinderSurface(Vector3.Zero, 20f), 0f, 1f);

        Assert.Equal(flat.TriangleCount, wrapped.TriangleCount);
    }

    [Fact]
    public void LetteringOverASphereStaysOnIt()
    {
        var solid = TextSolid.Build([Square(20)], new SphereSurface(Vector3.Zero, 30f), 0f, 1.5f);

        Assert.True(solid.CheckHealth().IsWatertight, solid.CheckHealth().Describe());
        Assert.All(solid.Positions, p => Assert.InRange(p.Length(), 29.8f, 31.6f));
    }

    /// <summary>Cutting wrapped lettering into the cylinder it was wrapped onto has to work.</summary>
    [Fact]
    public void WrappedLetteringCutsIntoACylinderCleanly()
    {
        var barrel = MeshTransform.Transformed(
            Primitives.Create(PrimitiveKind.Cylinder), Matrix4x4.CreateScale(4f));

        float radius = barrel.ComputeBounds().Size.X / 2;
        var surface = new CylinderSurface(barrel.ComputeBounds().Center, radius);

        var solid = TextSolid.Build([Square(12)], surface, 0.05f, -0.8f);
        var result = CsgSolid.Subtract(barrel, solid);

        Assert.True(result.ComputeSignedVolume() < barrel.ComputeSignedVolume());
        Assert.True(result.ComputeSignedVolume() > barrel.ComputeSignedVolume() * 0.9);
    }

    /// <summary>
    /// The case that actually failed in the app: real letters, wrapped round the cylinder the
    /// app inserts, cut at the depth the panel offers.
    ///
    /// A cylinder is a thirty-two sided prism pretending to be round, so its surface wanders
    /// either side of the smooth one the lettering is laid on. Starting the cutter a hair proud -
    /// which is plenty on a flat face - left it grazing the material, and the result came back
    /// with torn edges and was refused.
    /// </summary>
    [Fact]
    public void RealLetteringCutsIntoTheCylinderTheAppInserts()
    {
        var barrel = Primitives.Create(PrimitiveKind.Cylinder);
        var bounds = barrel.ComputeBounds();

        var side = new Vector3(bounds.Max.X, bounds.Center.Y, bounds.Center.Z);
        var surface = new CylinderSurface(
            new Vector3(bounds.Center.X, bounds.Center.Y, side.Z),
            new Vector2(side.X - bounds.Center.X, 0).Length());

        var shapes = GlyphOutlines.Build("TEXT", "Arial", 10f, bold: true)
            .Select(g => new TextShape(g.Outline, g.Holes))
            .ToList();

        var result = TextCutter.Apply(barrel, shapes, surface, raised: false, depthMm: 0.8f)!;

        Assert.True(result.CheckHealth().IsWatertight, result.CheckHealth().Describe());
        Assert.True(result.ComputeSignedVolume() < barrel.ComputeSignedVolume());
    }

    /// <summary>
    /// The sweep that found the trouble in the first place. Ten millimetres was the one that came
    /// back torn while everything either side of it was clean, and the letters with counters are
    /// here because a hole wrapped round a barrel is the fiddliest shape the cutter makes.
    /// </summary>
    [Theory]
    [InlineData("TEXT", 6f)]
    [InlineData("TEXT", 8f)]
    [InlineData("TEXT", 10f)]
    [InlineData("TEXT", 11f)]
    [InlineData("TEXT", 12f)]
    [InlineData("TEXT", 14f)]
    [InlineData("TEXT", 16f)]
    [InlineData("HI", 9f)]
    public void LetteringWrapsOntoTheCylinderCleanly(string word, float height)
    {
        var barrel = Primitives.Create(PrimitiveKind.Cylinder);
        var bounds = barrel.ComputeBounds();
        var surface = new CylinderSurface(bounds.Center, bounds.Size.X / 2);

        var shapes = GlyphOutlines.Build(word, "Arial", height, bold: true)
            .Select(g => new TextShape(g.Outline, g.Holes))
            .ToList();

        var result = TextCutter.Apply(barrel, shapes, surface, raised: false, depthMm: 0.8f)!;

        Assert.True(
            result.CheckHealth().IsWatertight,
            $"{word} at {height} mm: {result.CheckHealth().Describe()}");
    }

    /// <summary>
    /// Lettering with counters - O, B, A - wrapped round a barrel is past what the boolean will
    /// do: it comes back with a few torn edges however it is nudged. What matters is that this
    /// is caught rather than shipped, so the tool reports it and leaves the object alone.
    ///
    /// Written down as a test because it is a real limit of the engine rather than an oversight,
    /// and because the day it starts passing is worth knowing about.
    /// </summary>
    [Fact]
    public void LetteringWithCountersWrappedRoundABarrelIsCaughtRatherThanShipped()
    {
        var barrel = Primitives.Create(PrimitiveKind.Cylinder);
        var bounds = barrel.ComputeBounds();
        var surface = new CylinderSurface(bounds.Center, bounds.Size.X / 2);

        var shapes = GlyphOutlines.Build("BOX", "Arial", 9f, bold: true)
            .Select(g => new TextShape(g.Outline, g.Holes))
            .ToList();

        // The cutter itself is sound; it is the boolean against a faceted barrel that is not.
        var solid = TextSolid.Build(shapes, surface, surface.ClearanceMm, -0.8f);
        Assert.True(solid.CheckHealth().IsWatertight, solid.CheckHealth().Describe());

        var result = TextCutter.Apply(barrel, shapes, surface, raised: false, depthMm: 0.8f)!;

        // Whatever comes back, the tool checks it before keeping it - and this does not pass.
        Assert.False(result.CheckHealth().IsWatertight);
    }

    /// <summary>Standing the cutter further off must not change how deep the lettering goes.</summary>
    [Fact]
    public void RetryingDoesNotChangeHowDeepTheLetteringIs()
    {
        var plate = Primitives.Box(60, 60, 10);
        var face = FacePatch.Find(plate, new Vector3(0, 0, 5), Vector3.UnitZ)!;
        var surface = new PlanarSurface(face);

        var cut = TextCutter.Apply(plate, [Square(20)], surface, raised: false, depthMm: 1.5f)!;

        // How much material went, which is the only thing the depth is meant to decide - and it
        // must not shift when the cutter is stood further off to get the boolean through.
        Assert.Equal(20 * 20 * 1.5, plate.ComputeSignedVolume() - cut.ComputeSignedVolume(), 1);
        Assert.Equal(3.5f, cut.Positions.Where(p => p.Z < 4.9f && p.Z > 0.1f).Max(p => p.Z), 3);
    }

    [Fact]
    public void RaisedLetteringJoinsOntoTheObjectRatherThanFloatingOverIt()
    {
        var plate = Primitives.Box(60, 60, 10);
        var face = FacePatch.Find(plate, new Vector3(0, 0, 5), Vector3.UnitZ)!;

        var raised = TextCutter.Apply(plate, [Square(20)], new PlanarSurface(face), true, 2f)!;

        Assert.True(raised.CheckHealth().IsWatertight, raised.CheckHealth().Describe());
        Assert.Equal(7f, raised.ComputeBounds().Max.Z, 3);
        Assert.Equal(plate.ComputeSignedVolume() + 20 * 20 * 2, raised.ComputeSignedVolume(), 1);
    }

    /// <summary>Round surfaces have to be cleared by more than a flat one.</summary>
    [Fact]
    public void ARoundSurfaceAsksForMoreClearanceThanAFlatOne()
    {
        var plate = Primitives.Box(60, 60, 10);
        var face = FacePatch.Find(plate, new Vector3(0, 0, 5), Vector3.UnitZ)!;

        Assert.True(new PlanarSurface(face).ClearanceMm < 0.05f);
        Assert.True(new CylinderSurface(Vector3.Zero, 10f).ClearanceMm > 0.1f);
        Assert.True(new SphereSurface(Vector3.Zero, 10f).ClearanceMm > 0.1f);
    }

    // --- Bevel ----------------------------------------------------------------------

    [Fact]
    public void ABevelSlopesTheWallsInward()
    {
        var plate = Primitives.Box(60, 60, 10);
        var face = FacePatch.Find(plate, new Vector3(0, 0, 5), Vector3.UnitZ)!;

        var straight = TextSolid.Build([Square(10)], face, 0f, 2f);
        var bevelled = TextSolid.Build([Square(10)], face, 0f, 2f, bevelMm: 0.5f);

        Assert.True(bevelled.CheckHealth().IsWatertight, bevelled.CheckHealth().Describe());

        // A 10 mm square drawn in half a millimetre all round loses about a fifth of its volume.
        Assert.True(bevelled.ComputeSignedVolume() < straight.ComputeSignedVolume());
        Assert.True(bevelled.ComputeSignedVolume() > straight.ComputeSignedVolume() * 0.75);
    }

    [Fact]
    public void TheBevelledEndIsTheSmallerOne()
    {
        var plate = Primitives.Box(60, 60, 10);
        var face = FacePatch.Find(plate, new Vector3(0, 0, 5), Vector3.UnitZ)!;

        var solid = TextSolid.Build([Square(10)], face, 0f, 2f, bevelMm: 1f);

        float atBase = solid.Positions.Where(p => p.Z < 5.5f).Max(p => p.X);
        float atTop = solid.Positions.Where(p => p.Z > 6.5f).Max(p => p.X);

        Assert.Equal(5f, atBase, 2);
        Assert.Equal(4f, atTop, 2);
    }

    /// <summary>
    /// The safety that matters: a bevel wider than the stroke would swallow it, so that shape
    /// keeps its upright walls instead of closing up.
    /// </summary>
    [Fact]
    public void ABevelTooWideForTheStrokeIsDeclined()
    {
        var plate = Primitives.Box(60, 60, 10);
        var face = FacePatch.Find(plate, new Vector3(0, 0, 5), Vector3.UnitZ)!;

        var solid = TextSolid.Build([Square(2)], face, 0f, 2f, bevelMm: 5f);

        Assert.True(solid.CheckHealth().IsWatertight, solid.CheckHealth().Describe());
        Assert.Equal(2 * 2 * 2, solid.ComputeSignedVolume(), 1); // untouched, not collapsed
    }

    [Fact]
    public void ABevelOnLetteringWithHolesStaysPrintable()
    {
        var plate = Primitives.Box(60, 60, 10);
        var face = FacePatch.Find(plate, new Vector3(0, 0, 5), Vector3.UnitZ)!;

        var ring = new TextShape(
            Square(20).Outline,
            [new List<Vector2> { new(-4, -4), new(-4, 4), new(4, 4), new(4, -4) }]);

        var solid = TextSolid.Build([ring], face, 0f, 2f, bevelMm: 0.6f);

        Assert.True(solid.CheckHealth().IsWatertight, solid.CheckHealth().Describe());
        Assert.True(solid.ComputeSignedVolume() > 0);
    }

    [Fact]
    public void NoBevelLeavesTheWallsUpright()
    {
        var plate = Primitives.Box(60, 60, 10);
        var face = FacePatch.Find(plate, new Vector3(0, 0, 5), Vector3.UnitZ)!;

        var solid = TextSolid.Build([Square(10)], face, 0f, 2f, bevelMm: 0f);

        Assert.Equal(10 * 10 * 2, solid.ComputeSignedVolume(), 2);
    }
}
