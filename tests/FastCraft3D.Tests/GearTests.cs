using System.Numerics;
using FastCraft3D.Geometry;
using FastCraft3D.Geometry.Csg;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>Gears, ring gears and racks: closed solids, the sizes asked for, and teeth that mesh.</summary>
public class GearTests
{
    private static GearOptions Plain => new() { Bore = BoreShape.None };

    private static Mesh Only(GearOptions options)
    {
        var result = Gears.Build(options);
        Assert.Null(result.Refusal);
        return Assert.Single(result.Parts).Mesh;
    }

    private static void Closed(Mesh mesh)
    {
        var health = mesh.CheckHealth();
        Assert.True(health.IsWatertight, health.Describe());
        Assert.True(mesh.ComputeSignedVolume() > 0, "the solid is inside out");
    }

    /// <summary>
    /// The middle of the solid by volume. A gear is the same all the way round, so this is on its
    /// axis - where the middle of its box is not, once it has an odd number of teeth.
    /// </summary>
    private static Vector3 Centroid(Mesh mesh)
    {
        double volume = 0;
        var sum = Vector3.Zero;
        for (int t = 0; t + 2 < mesh.Indices.Count; t += 3)
        {
            var a = mesh.Positions[mesh.Indices[t]];
            var b = mesh.Positions[mesh.Indices[t + 1]];
            var c = mesh.Positions[mesh.Indices[t + 2]];
            float v = Vector3.Dot(a, Vector3.Cross(b, c)) / 6f;
            volume += v;
            sum += v * (a + b + c) / 4f;
        }

        return sum / (float)volume;
    }

    private static float Reach(Mesh mesh) => mesh.Positions.Max(p => new Vector2(p.X, p.Y).Length());

    /// <summary>How much of one part is inside the other, by Manifold: the BSP takes minutes over two gears' worth of teeth.</summary>
    private static double Overlap(Mesh a, Mesh b)
    {
        var both = ManifoldCsg.Intersect(a, b);
        Assert.NotNull(both);
        return both.TriangleCount == 0 ? 0 : Math.Abs(both.ComputeSignedVolume());
    }

    [Fact]
    public void ASpurGearIsClosedAndTheSizeItsModuleSays()
    {
        var gear = Only(Plain with { Module = 2f, Teeth = 24, Thickness = 6f });

        Closed(gear);
        Assert.Equal(2f * 26f / 2f, Reach(gear), 1);
        Assert.Equal(0f, gear.ComputeBounds().Min.Z, 3);
        Assert.Equal(6f, gear.ComputeBounds().Max.Z, 3);

        // Solid down to the roots: nothing of it is nearer the middle than the root circle.
        float root = 2f * (24f - 2.5f) / 2f;
        Assert.True(gear.Positions.Where(p => p.Z > 0.01f && p.Z < 5.99f).All(p => new Vector2(p.X, p.Y).Length() > root - 0.2f));
    }

    /// <summary>
    /// Rolling the rack gives the involute where there is one: above the base circle the tooth is
    /// as thick as the formula says, to a few thousandths of a millimetre.
    /// </summary>
    [Theory]
    [InlineData(0.2)]
    [InlineData(0.5)]
    [InlineData(0.8)]
    public void RollingTheRackGivesTheInvoluteAboveTheBaseCircle(double fromBaseToTip)
    {
        const double module = 2.0, backlash = 0.1;
        const int teeth = 40;
        double pressure = 20.0 * Math.PI / 180.0;
        double r = module * teeth / 2.0, baseRadius = r * Math.Cos(pressure), tip = r + module;
        double radius = baseRadius + (tip - baseRadius) * fromBaseToTip;

        double at = Math.Acos(baseRadius / radius);
        double formula = Math.PI / (2 * teeth) - backlash / (4 * r)
                       + (Math.Tan(pressure) - pressure) - (Math.Tan(at) - at);
        double rolled = Gears.ToothHalfAngle(module, teeth, pressure, backlash, radius);

        Assert.True(Math.Abs(rolled - formula) * radius < 0.005,
            $"at {radius:0.###} mm the tooth is {rolled * radius:0.####} half-width, the involute {formula * radius:0.####}");
    }

    /// <summary>A small pinion's tooth is thinner just above its root than further up: the undercut a rack leaves.</summary>
    [Fact]
    public void ASmallPinionIsUndercutAtItsRoots()
    {
        const double module = 1.0;
        const int teeth = 9;
        double pressure = 20.0 * Math.PI / 180.0;
        double r = module * teeth / 2.0;

        double low = Gears.ToothHalfAngle(module, teeth, pressure, 0, r - 1.0 * module) * (r - 1.0 * module);
        double higher = Gears.ToothHalfAngle(module, teeth, pressure, 0, r - 0.6 * module) * (r - 0.6 * module);

        Assert.True(low < higher, $"half-width {low:0.###} near the root, {higher:0.###} above it");
        Closed(Only(Plain with { Module = 1f, Teeth = 9 }));
    }

    [Theory]
    [InlineData(ToothForm.Helical)]
    [InlineData(ToothForm.Herringbone)]
    public void HelicalAndHerringboneGearsAreClosed(ToothForm form)
    {
        var gear = Only(Plain with { Form = form, HelixAngle = 25f, Teeth = 30, Thickness = 12f });

        Closed(gear);
        Assert.Equal(1.5f * 32f / 2f, Reach(gear), 1);
    }

    /// <summary>A herringbone winds back to where it started, so its top face is its bottom face over again.</summary>
    [Fact]
    public void AHerringboneGearEndsTurnedAsItBegan()
    {
        var gear = Only(Plain with { Form = ToothForm.Herringbone, HelixAngle = 30f, Teeth = 20, Thickness = 10f });

        var bottom = gear.Positions.Where(p => p.Z < 1e-3f).Select(p => (MathF.Round(p.X, 2), MathF.Round(p.Y, 2))).ToHashSet();
        var top = gear.Positions.Where(p => p.Z > 10f - 1e-3f).Select(p => (MathF.Round(p.X, 2), MathF.Round(p.Y, 2))).ToHashSet();

        Assert.True(top.SetEquals(bottom));
    }

    [Theory]
    [InlineData(BoreShape.Round)]
    [InlineData(BoreShape.DShaft)]
    [InlineData(BoreShape.Hex)]
    public void EveryBoreGoesStraightThroughAndLeavesTheGearClosed(BoreShape bore)
    {
        var solid = Only(Plain with { Teeth = 30 });
        var bored = Only(Plain with { Teeth = 30, Bore = bore, BoreSize = 6f, BoreFlat = 5f });

        Closed(bored);

        double removed = solid.ComputeSignedVolume() - bored.ComputeSignedVolume();
        double expected = bore switch
        {
            BoreShape.Round => Math.PI * 9.0 * 8.0,
            BoreShape.Hex => 3.0 * Math.Sqrt(3.0) / 2.0 * Math.Pow(6.0 / Math.Sqrt(3.0), 2) * 8.0,
            _ => (Math.PI * 9.0 - SegmentArea(3.0, 2.0)) * 8.0
        };
        Assert.Equal(expected, removed, expected * 0.02);

        // A helical gear's bore stays straight: the shaft has to go through it.
        var helical = Only(Plain with { Teeth = 30, Bore = bore, BoreSize = 6f, BoreFlat = 5f, Form = ToothForm.Helical, HelixAngle = 30f });
        Closed(helical);
        Assert.Equal(bored.ComputeSignedVolume(), helical.ComputeSignedVolume(), bored.ComputeSignedVolume() * 0.01);
    }

    /// <summary>The area of a circle cut off beyond a chord this far from the middle.</summary>
    private static double SegmentArea(double radius, double distance) =>
        radius * radius * Math.Acos(distance / radius) - distance * Math.Sqrt(radius * radius - distance * distance);

    [Fact]
    public void AHubStandsOnTheGearAndTakesASetScrew()
    {
        var options = Plain with { Teeth = 30, Bore = BoreShape.Round, BoreSize = 5f, HubDiameter = 14f, HubHeight = 7f };
        var plain = Only(options);
        var drilled = Gears.Build(options with { SetScrew = 3f });

        Closed(plain);
        Assert.Equal(15f, plain.ComputeBounds().Max.Z, 3);

        var gear = Assert.Single(drilled.Parts).Mesh;
        Closed(gear);
        Assert.True(gear.ComputeSignedVolume() < plain.ComputeSignedVolume() - 10, "no hole for the set screw");
    }

    [Fact]
    public void ASetScrewWithoutAHubIsLeftOutAndSaidSo()
    {
        var result = Gears.Build(Plain with { SetScrew = 3f });

        Assert.Single(result.Parts);
        Assert.Contains(result.Notes, n => n.Contains("hub"));
    }

    [Fact]
    public void ABoreTooWideForTheRootsIsRefused()
    {
        var result = Gears.Build(new GearOptions { Teeth = 12, Module = 1f, Bore = BoreShape.Round, BoreSize = 9f });

        Assert.Empty(result.Parts);
        Assert.NotNull(result.Refusal);
    }

    [Fact]
    public void AChamferDrawsTheBottomOfTheTeethIn()
    {
        var gear = Only(Plain with { Teeth = 20, Chamfer = 0.5f });

        Closed(gear);
        float bottom = gear.Positions.Where(p => p.Z < 1e-3f).Max(p => new Vector2(p.X, p.Y).Length());
        float above = gear.Positions.Where(p => MathF.Abs(p.Z - 0.5f) < 1e-3f).Max(p => new Vector2(p.X, p.Y).Length());
        Assert.Equal(above - 0.5f, bottom, 2);
    }

    [Fact]
    public void ARingGearIsClosedWithItsTeethInside()
    {
        var ring = Only(new GearOptions { Kind = GearKind.Ring, Teeth = 60, Module = 1.5f, Rim = 3f });

        Closed(ring);
        Assert.Equal(1.5f * (60f + 2.5f) / 2f + 3f, Reach(ring), 1);
        float inside = ring.Positions.Min(p => new Vector2(p.X, p.Y).Length());
        Assert.Equal(1.5f * 58f / 2f, inside, 1);
    }

    [Fact]
    public void ARackIsClosedAndAToothPitchLongPerTooth()
    {
        var rack = Only(new GearOptions { Kind = GearKind.Rack, Teeth = 10, Module = 2f, Rim = 4f });

        Closed(rack);
        var size = rack.ComputeBounds().Size;
        Assert.Equal(10f * MathF.PI * 2f, size.X, 2);
        Assert.Equal(4f + 2.25f * 2f, size.Y, 2);
    }

    // --- Pairs -------------------------------------------------------------------------

    [Theory]
    [InlineData(ToothForm.Straight, 20, 40)]
    [InlineData(ToothForm.Straight, 9, 31)]
    [InlineData(ToothForm.Helical, 18, 30)]
    [InlineData(ToothForm.Herringbone, 16, 24)]
    public void TwoGearsComeOutInMeshWithoutTouching(ToothForm form, int teeth, int partner)
    {
        var result = Gears.Build(Plain with { Form = form, Teeth = teeth, PartnerTeeth = partner, Module = 1.5f, Thickness = 6f });

        Assert.Equal(2, result.Parts.Count);
        var (a, b) = (result.Parts[0].Mesh, result.Parts[1].Mesh);
        Closed(a);
        Closed(b);

        var centreA = Centroid(a);
        var centreB = Centroid(b);
        Assert.Equal(1.5f * (teeth + partner) / 2f, new Vector2(centreB.X - centreA.X, centreB.Y - centreA.Y).Length(), 1);

        // In mesh: their outlines reach into each other's roots...
        Assert.True(Reach(a) + Reach(b) > 1.5f * (teeth + partner) / 2f + 1.5f);

        // ...and yet nothing of one is inside the other.
        Assert.True(Overlap(a, b) < 0.01, $"the gears overlap by {Overlap(a, b):0.####} mm³");

        // Without the backlash taken off they would still only just touch - so the gap is the backlash, not luck.
        var tight = Gears.Build(Plain with { Form = form, Teeth = teeth, PartnerTeeth = partner, Module = 1.5f, Thickness = 6f, Backlash = 0f });
        Assert.True(Overlap(tight.Parts[0].Mesh, tight.Parts[1].Mesh) < 0.05);
    }

    [Theory]
    [InlineData(ToothForm.Straight)]
    [InlineData(ToothForm.Helical)]
    public void AGearInsideARingComesOutInMeshWithoutTouching(ToothForm form)
    {
        var result = Gears.Build(new GearOptions
        {
            Kind = GearKind.Ring, Form = form, Teeth = 60, PartnerTeeth = 20, Module = 1.5f, Thickness = 6f, Bore = BoreShape.None
        });

        Assert.Equal(2, result.Parts.Count);
        Closed(result.Parts[0].Mesh);
        Closed(result.Parts[1].Mesh);
        Assert.True(Overlap(result.Parts[0].Mesh, result.Parts[1].Mesh) < 0.01);
    }

    [Theory]
    [InlineData(ToothForm.Straight)]
    [InlineData(ToothForm.Helical)]
    public void AGearOnARackComesOutInMeshWithoutTouching(ToothForm form)
    {
        var result = Gears.Build(new GearOptions
        {
            Kind = GearKind.Rack, Form = form, Teeth = 12, PartnerTeeth = 16, Module = 1.5f, Thickness = 6f, Bore = BoreShape.None
        });

        Assert.Equal(2, result.Parts.Count);
        var (rack, gear) = (result.Parts[0].Mesh, result.Parts[1].Mesh);
        Closed(rack);
        Closed(gear);
        Assert.True(gear.ComputeBounds().Min.Y < rack.ComputeBounds().Max.Y - 1f, "the gear is not down among the rack's teeth");
        Assert.True(Overlap(rack, gear) < 0.01);
    }

    [Fact]
    public void APinionTooSmallForTheBoreStillComesPlainAndSaysSo()
    {
        var result = Gears.Build(new GearOptions { Teeth = 40, PartnerTeeth = 8, Module = 1f, Bore = BoreShape.Round, BoreSize = 5f });

        Assert.Equal(2, result.Parts.Count);
        Assert.Contains(result.Notes, n => n.Contains("too small for the bore"));
    }
}
