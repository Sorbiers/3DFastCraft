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

    // --- Bevel, worm, ratchet, and a gear cut away ------------------------------------

    /// <summary>How far the solid reaches from its axis within a few degrees of a bearing.</summary>
    private static float ReachAt(Mesh mesh, float degrees, float window = 3f)
    {
        float at = degrees * MathF.PI / 180f;
        float best = 0f;

        foreach (var p in mesh.Positions)
        {
            float away = MathF.Abs((float)Math.IEEERemainder(MathF.Atan2(p.Y, p.X) - at, Math.Tau)) * 180f / MathF.PI;
            if (away <= window) best = MathF.Max(best, new Vector2(p.X, p.Y).Length());
        }

        return best;
    }

    [Fact]
    public void ABevelIsClosedAndTapersTowardsItsApex()
    {
        var mesh = Only(Plain with { Kind = GearKind.Bevel, Module = 2f, Teeth = 20, Thickness = 8f, ConeAngle = 45f });
        Closed(mesh);

        var box = mesh.ComputeBounds();

        // 20 teeth of module 2: 40 mm across the pitch circle, 44 over the tips at the back.
        Assert.Equal(44f, box.Size.X, 0.3f);

        // An 8 mm face on a 45 degree cone stands 8 * cos 45 tall.
        Assert.Equal(5.66f, box.Size.Z, 0.05f);
    }

    [Fact]
    public void ABevelsFaceIsHeldToAThirdOfItsCone()
    {
        var result = Gears.Build(Plain with
        {
            Kind = GearKind.Bevel, Module = 2f, Teeth = 20, Thickness = 20f, ConeAngle = 45f
        });

        Assert.Null(result.Refusal);
        var mesh = Assert.Single(result.Parts).Mesh;

        // The cone is 40 / 2 / sin 45 = 28.28 mm long, so the face is cut to 9.43 mm and the
        // gear stands 9.43 * cos 45 tall rather than the 20 asked for.
        Assert.Equal(6.67f, mesh.ComputeBounds().Size.Z, 0.05f);
        Assert.Contains(result.Notes, n => n.Contains("third of its cone"));
    }

    [Fact]
    public void ABevelPairTakesItsConesFromItsTeeth()
    {
        var result = Gears.Build(Plain with
        {
            Kind = GearKind.Bevel, Module = 2f, Teeth = 20, PartnerTeeth = 20, Thickness = 6f
        });

        Assert.Null(result.Refusal);
        Assert.Equal(2, result.Parts.Count);
        foreach (var part in result.Parts) Closed(part.Mesh);

        // Equal teeth: two 45 degree cones, which is a right angle between the shafts.
        Assert.Contains(result.Notes, n => n.Contains("45"));
    }

    [Fact]
    public void AWormIsClosedAndAsThickAsItsThreadMakesIt()
    {
        var result = Gears.Build(Plain with { Kind = GearKind.Worm, Module = 2f, Thickness = 24f });

        Assert.Null(result.Refusal);
        var worm = Assert.Single(result.Parts).Mesh;
        Closed(worm);

        var box = worm.ComputeBounds();

        // Ten times the module across the pitch, and a module above that on each side.
        Assert.Equal(24f, box.Size.X, 0.4f);
        Assert.Equal(24f, box.Size.Z, 0.01f);
    }

    [Fact]
    public void AWormComesWithItsWheelWhenOneIsAskedFor()
    {
        var result = Gears.Build(Plain with
        {
            Kind = GearKind.Worm, Module = 1.5f, Thickness = 20f, PartnerTeeth = 30
        });

        Assert.Null(result.Refusal);
        Assert.Equal(2, result.Parts.Count);
        foreach (var part in result.Parts) Closed(part.Mesh);

        Assert.Contains(result.Notes, n => n.Contains("30 to 1"));
    }

    [Fact]
    public void ARatchetIsClosedAndAsWideAsItsTips()
    {
        var mesh = Only(Plain with { Kind = GearKind.Ratchet, Module = 2f, Teeth = 16, Thickness = 5f });
        Closed(mesh);

        var box = mesh.ComputeBounds();
        Assert.Equal(32f, box.Size.X, 0.4f);
        Assert.Equal(5f, box.Size.Z, 0.001f);
    }

    [Fact]
    public void ARatchetsTeethAllLeanTheSameWay()
    {
        var wheel = Only(Plain with { Kind = GearKind.Ratchet, Module = 2f, Teeth = 12, Thickness = 4f });

        // A tooth is a long ramp up to a tip and a steep face back down to the next root, so
        // round the wheel the corners alternate: a long gap, then a short one. Teeth that were
        // symmetrical - a star, not a ratchet - would leave every gap the same.
        var angles = wheel.Positions
            .Where(p => p.Z < 0.001f)
            .Select(p => (MathF.Atan2(p.Y, p.X) * 180f / MathF.PI + 360f) % 360f)
            .OrderBy(a => a)
            .ToList();

        var gaps = new List<float>();
        for (int i = 0; i < angles.Count; i++)
        {
            float gap = (angles[(i + 1) % angles.Count] - angles[i] + 360f) % 360f;
            if (gap > 0.01f) gaps.Add(gap);
        }

        Assert.Equal(24, gaps.Count);
        Assert.Equal(12, gaps.Count(g => MathF.Abs(g - 4f) < 0.1f));    // the catching faces
        Assert.Equal(12, gaps.Count(g => MathF.Abs(g - 26f) < 0.1f));   // the ramps
    }

    [Fact]
    public void ARatchetCanBeMadeWithItsPawl()
    {
        var result = Gears.Build(Plain with
        {
            Kind = GearKind.Ratchet, Module = 2f, Teeth = 16, Thickness = 5f,
            WithPawl = true, Bore = BoreShape.Round, BoreSize = 4f
        });

        Assert.Null(result.Refusal);
        Assert.Equal(2, result.Parts.Count);
        foreach (var part in result.Parts) Closed(part.Mesh);

        Assert.Contains(result.Notes, n => n.Contains("pivot"));
    }

    [Fact]
    public void AGearCutAwayKeepsItsTeethOnOneSectorOnly()
    {
        var whole = Only(Plain with { Module = 2f, Teeth = 20, Thickness = 6f });
        var part = Only(Plain with { Module = 2f, Teeth = 20, Thickness = 6f, KeptTeeth = 5 });

        Closed(part);

        // Teeth 0 to 4 are kept, so their middles lie at 0, 18, 36, 54 and 72 degrees. A tooth
        // reaches the tip circle at 22 mm; the bare rim is left at the roots, 17.5 mm.
        Assert.Equal(22f, ReachAt(part, 36f), 0.2f);
        Assert.Equal(17.5f, ReachAt(part, 180f), 0.2f);

        // The whole one has teeth in both places.
        Assert.Equal(22f, ReachAt(whole, 180f), 0.2f);

        Assert.True(part.ComputeSignedVolume() < whole.ComputeSignedVolume(),
            "cutting teeth away should leave less of it");
    }

    [Fact]
    public void AGearCutAwaySaysWhatItLeftAndWhy()
    {
        var result = Gears.Build(Plain with { Module = 2f, Teeth = 20, Thickness = 6f, KeptTeeth = 5 });

        Assert.Null(result.Refusal);
        Assert.Contains(result.Notes, n => n.Contains("5 of 20"));
        Assert.Contains(result.Notes, n => n.Contains("90") && n.Contains("degree"));
        Assert.Contains(result.Notes, n => n.Contains("locking arc"));
    }

    [Fact]
    public void KeepingEveryToothLeavesAWholeGear()
    {
        var whole = Only(Plain with { Module = 2f, Teeth = 20, Thickness = 6f });
        var kept = Only(Plain with { Module = 2f, Teeth = 20, Thickness = 6f, KeptTeeth = 20 });

        Assert.Equal(whole.TriangleCount, kept.TriangleCount);
        Assert.Equal(whole.ComputeSignedVolume(), kept.ComputeSignedVolume(), 0.01f);
    }

    [Fact]
    public void ABevelPairIsMadeSideBySideToPrint()
    {
        var result = Gears.Build(Plain with
        {
            Kind = GearKind.Bevel, Module = 1.5f, Teeth = 20, PartnerTeeth = 40, Thickness = 8f
        });

        Assert.Equal(2, result.Parts.Count);

        var first = result.Parts[0].Mesh.ComputeBounds();
        var second = result.Parts[1].Mesh.ComputeBounds();

        Assert.True(second.Min.X > first.Max.X, $"they sit on top of each other: {first.Max.X} then {second.Min.X}");
        Assert.Equal(0f, first.Min.Z, 0.001f);
        Assert.Equal(0f, second.Min.Z, 0.001f);
    }

    [Fact]
    public void ABevelPairStandsAtARightAngleWhenShownTogether()
    {
        var result = Gears.Build(Plain with
        {
            Kind = GearKind.Bevel, Module = 1.5f, Teeth = 20, PartnerTeeth = 40, Thickness = 8f
        });

        var mate = result.Parts[1];
        Assert.NotNull(mate.InMesh);

        var flat = mate.Mesh.ComputeBounds();
        var stood = MeshTransform.Transformed(mate.Mesh, mate.InMesh!.Value).ComputeBounds();

        // Lying down it is wide and shallow; turned into mesh it is up on its edge, so what was
        // across it is now its height.
        Assert.Equal(flat.Size.X, stood.Size.Z, 0.2f);
        Assert.Equal(flat.Size.Z, stood.Size.X, 0.2f);

        // And it has come back to meet the first, which is at the origin.
        Assert.True(stood.Min.X < 0f, "the mate should reach back over the first gear's axis");
    }

    [Fact]
    public void ABevelsHeightIsItsFaceLeaningAtItsOwnConeAngle()
    {
        var result = Gears.Build(Plain with
        {
            Kind = GearKind.Bevel, Module = 1.5f, Teeth = 20, PartnerTeeth = 40, Thickness = 8f
        });

        // 20 and 40 teeth: cones of 26.57 and 63.43 degrees, one cone distance of 33.54 mm, so
        // both get the whole 8 mm face - and the flatter cone of the two stands the shallower.
        float pinion = result.Parts[0].Mesh.ComputeBounds().Size.Z;
        float wheel = result.Parts[1].Mesh.ComputeBounds().Size.Z;

        Assert.Equal(8f * MathF.Cos(26.565f * MathF.PI / 180f), pinion, 0.05f);
        Assert.Equal(8f * MathF.Cos(63.435f * MathF.PI / 180f), wheel, 0.05f);
    }

    private static GearOptions WormDrive => Plain with
    {
        Kind = GearKind.Worm, Module = 1.5f, Thickness = 20f, PartnerTeeth = 30
    };

    [Fact]
    public void AWormDriveIsMadeSideBySideToPrint()
    {
        var result = Gears.Build(WormDrive);

        Assert.Null(result.Refusal);
        Assert.Equal(2, result.Parts.Count);

        var worm = result.Parts[0].Mesh.ComputeBounds();
        var wheel = result.Parts[1].Mesh.ComputeBounds();

        Assert.True(wheel.Min.X > worm.Max.X, $"they sit on top of each other: {worm.Max.X} then {wheel.Min.X}");

        // The worm stands on its end, which is how a thread prints; the wheel lies flat.
        Assert.Equal(20f, worm.Size.Z, 0.01f);
        Assert.Equal(0f, worm.Min.Z, 0.01f);
        Assert.Equal(0f, wheel.Min.Z, 0.01f);
    }

    [Fact]
    public void AWormLiesAcrossItsWheelWhenTheDriveIsShownTogether()
    {
        var result = Gears.Build(WormDrive);

        var worm = result.Parts[0];
        var wheel = result.Parts[1];
        Assert.NotNull(worm.InMesh);
        Assert.NotNull(wheel.InMesh);

        var laid = MeshTransform.Transformed(worm.Mesh, worm.InMesh!.Value).ComputeBounds();

        // On its side: its length now runs along Y and its diameter stands up. Ten times the
        // module across the pitch, and a module of thread on each side of that.
        Assert.Equal(20f, laid.Size.Y, 0.01f);
        Assert.Equal(18f, laid.Size.Z, 0.1f);

        // 15 mm across the worm's pitch and 45 across the wheel's: 30 mm between the shafts,
        // with the worm at the wheel's half height - the wheel's, not its own, which is what
        // having one Thickness for both of them used to get wrong.
        Assert.Equal(30f, laid.Center.X, 0.1f);
        Assert.Equal(9.95f / 2f, laid.Center.Z, 0.1f);

        // And the wheel comes back to the middle to meet it.
        var back = MeshTransform.Transformed(wheel.Mesh, wheel.InMesh!.Value).ComputeBounds();
        Assert.Equal(0f, back.Center.X, 0.01f);
    }

    [Fact]
    public void AWormAndItsWheelDoNotRunIntoOneAnother()
    {
        var result = Gears.Build(WormDrive);

        var worm = MeshTransform.Transformed(result.Parts[0].Mesh, result.Parts[0].InMesh!.Value);
        var wheel = MeshTransform.Transformed(result.Parts[1].Mesh, result.Parts[1].InMesh!.Value);

        // Nothing of the worm reaches inside the wheel's roots, and nothing of the wheel reaches
        // inside the worm's. Shown driven into one another, a pair looks broken.
        float roots = 1.5f * 30 / 2f - 1.25f * 1.5f;
        float nearest = worm.Positions.Min(p => new Vector2(p.X, p.Y).Length());
        Assert.True(nearest > roots - 1.5f, $"the worm reaches {nearest} mm in, past the wheel's roots at {roots}");

        float wormRoot = 15f / 2f - 1.25f * 1.5f;
        float deepest = wheel.Positions
            .Where(p => MathF.Abs(p.Y) < 8f)
            .Min(p => new Vector2(p.X - 30f, p.Z - 10f).Length());
        Assert.True(deepest > wormRoot - 0.1f, $"the wheel reaches {deepest} mm from the worm's axis, inside its root at {wormRoot}");
    }

    [Fact]
    public void AWormWheelIsAsWideAsTheWormAsksForAndNotAsLongAsTheWormIs()
    {
        // A worm four times as long drives the same wheel: its length only gives the shafts room.
        var shortWorm = Gears.Build(WormDrive with { Thickness = 20f });
        var longWorm = Gears.Build(WormDrive with { Thickness = 80f });

        float narrow = shortWorm.Parts[1].Mesh.ComputeBounds().Size.Z;
        float wide = longWorm.Parts[1].Mesh.ComputeBounds().Size.Z;

        Assert.Equal(narrow, wide, 0.001f);

        // Two modules for every root of the diameter quotient and one: 2 * 1.5 * sqrt(11).
        Assert.Equal(9.95f, narrow, 0.05f);
    }

    [Fact]
    public void AWormWheelTakesTheWidthItIsGiven()
    {
        var result = Gears.Build(WormDrive with { WheelWidth = 6f });

        Assert.Equal(6f, result.Parts[1].Mesh.ComputeBounds().Size.Z, 0.001f);
    }

    [Fact]
    public void AWormIsBoredForItsShaftAndWillTakeACollar()
    {
        var result = Gears.Build(new GearOptions
        {
            Kind = GearKind.Worm, Module = 1.5f, Thickness = 30f,
            Bore = BoreShape.Round, BoreSize = 5f, HubDiameter = 12f, HubHeight = 6f
        });

        Assert.Null(result.Refusal);
        var worm = Assert.Single(result.Parts).Mesh;
        Closed(worm);

        var box = worm.ComputeBounds();

        // The collar stands on the far end of the thread, and the bore goes through the lot.
        Assert.Equal(36f, box.Size.Z, 0.01f);
        Assert.True(worm.Positions.Any(p => new Vector2(p.X, p.Y).Length() < 2.6f),
            "nothing of it is near the axis, so there is no bore");
        Assert.DoesNotContain(result.Notes, n => n.Contains("left off") || n.Contains("left out"));
    }

    [Fact]
    public void AWormWhoseBoreWouldLeaveNoWallIsRefused()
    {
        var result = Gears.Build(new GearOptions
        {
            Kind = GearKind.Worm, Module = 1.5f, Thickness = 20f,
            Bore = BoreShape.Round, BoreSize = 12f
        });

        // 15 mm across the pitch leaves roots at 5.6 mm: a 12 mm hole eats them.
        Assert.Empty(result.Parts);
        Assert.NotNull(result.Refusal);
        Assert.Contains("roots of the worm", result.Refusal);
    }

    private static GearOptions Ratchet => new()
    {
        Kind = GearKind.Ratchet, Module = 2f, Teeth = 16, Thickness = 5f, WithPawl = true,
        Bore = BoreShape.Round, BoreSize = 4f
    };

    [Fact]
    public void ARatchetAndItsPawlAreMadeSideBySideToPrint()
    {
        var result = Gears.Build(Ratchet);

        Assert.Null(result.Refusal);
        Assert.Equal(2, result.Parts.Count);

        var wheel = result.Parts[0].Mesh.ComputeBounds();
        var pawl = result.Parts[1].Mesh.ComputeBounds();

        Assert.True(pawl.Min.X > wheel.Max.X, $"they sit on top of each other: {wheel.Max.X} then {pawl.Min.X}");
        Assert.Equal(0f, pawl.Min.Z, 0.001f);
        Assert.Equal(wheel.Size.Z, pawl.Size.Z, 0.001f);
    }

    [Fact]
    public void ThePawlSitsInAToothWhenTheTwoAreShownTogether()
    {
        var result = Gears.Build(Ratchet);

        var pawl = result.Parts[1];
        Assert.NotNull(pawl.InMesh);

        var shown = MeshTransform.Transformed(pawl.Mesh, pawl.InMesh!.Value);
        var wheel = result.Parts[0].Mesh;

        float tip = 2f * 16 / 2f, root = tip - 2f;

        // Its point reaches into the teeth without cutting into the wheel's roots.
        float nearest = shown.Positions.Min(p => new Vector2(p.X, p.Y).Length());
        Assert.True(nearest < tip, $"the pawl stops {nearest} mm out, short of the teeth at {tip}");
        Assert.True(nearest > root - 0.01f, $"the pawl reaches {nearest} mm in, past the roots at {root}");

        // And they lie in the same plane, as a ratchet and its pawl have to.
        Assert.Equal(wheel.ComputeBounds().Min.Z, shown.ComputeBounds().Min.Z, 0.01f);
        Assert.Equal(wheel.ComputeBounds().Max.Z, shown.ComputeBounds().Max.Z, 0.01f);
    }

    [Fact]
    public void ThePawlsArmLiesAlongTheWayTheWheelPushesIt()
    {
        var result = Gears.Build(Ratchet);
        var shown = MeshTransform.Transformed(result.Parts[1].Mesh, result.Parts[1].InMesh!.Value);

        // The point is the nearest part of it to the wheel's centre and the pivot the furthest,
        // and the arm between them is at a right angle to the radius through the point: that is
        // what puts the tooth's push down the arm rather than under the point.
        var point = shown.Positions.MinBy(p => new Vector2(p.X, p.Y).LengthSquared());
        var contact = new Vector2(point.X, point.Y);

        float pivotFromCentre = shown.Positions.Max(p => new Vector2(p.X, p.Y).Length());
        float armAndReach = MathF.Sqrt(pivotFromCentre * pivotFromCentre - contact.LengthSquared());

        Assert.True(armAndReach > 0.8f * (pivotFromCentre - contact.Length()),
            "the arm should run across the wheel, not straight out from its centre");
    }
}
