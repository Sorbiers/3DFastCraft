using System.Diagnostics;
using System.Numerics;
using System.Runtime.ExceptionServices;
using FastCraft3D.Geometry;
using FastCraft3D.Geometry.Csg;
using FastCraft3D.Geometry.Engraving;
using FastCraft3D.ViewModels;
using Xunit;
using Xunit.Abstractions;

namespace FastCraft3D.Tests;

/// <summary>
/// Rubble, castle walling, bark and wood grain: the fields that look like what they are named
/// after, and go round a barrel as one ring with no seam.
///
/// What is checked is what decides whether the tool works - the field comes back closed, the
/// panel's figure is what is built, it unions onto a face or a barrel and stays printable - and,
/// as far as a number can say it, whether it still looks like itself: stones of different sizes,
/// courses of different heights, grain running the way it was asked to.
/// </summary>
public class StoneTextureTests(ITestOutputHelper log)
{
    private static readonly TextureKind[] Kinds =
        [TextureKind.Rubble, TextureKind.Castle, TextureKind.Bark, TextureKind.Grain];

    public static TheoryData<TextureKind> EachKind
    {
        get
        {
            var data = new TheoryData<TextureKind>();
            foreach (var kind in Kinds) data.Add(kind);
            return data;
        }
    }

    /// <summary>Both ways for the textures that turn, the one way for the rest.</summary>
    private static bool[] Ways(TextureKind kind) =>
        (kind is TextureKind.Bark or TextureKind.Grain) ? [false, true] : [false];

    private static PlanarSurface Face(float side)
    {
        var box = MeshTransform.Transformed(
            Primitives.Box(side, side, side), Matrix4x4.CreateTranslation(0, 0, side / 2f));

        return new PlanarSurface(FacePatch.Find(box, new Vector3(0, -side / 2f, side / 2f), -Vector3.UnitY)!);
    }

    private static IRelief Profile(TextureKind kind, float pitch, float groove, float depth,
                                   float aspect = 0f, bool turned = false, float around = 0f) =>
        SurfaceTexture.ProfileOf(new TextureOptions(kind, pitch, groove, 45f, turned, aspect), depth, 0.4f, around)!;

    [Theory]
    [MemberData(nameof(EachKind))]
    public void EachIsAShapedTextureThatGoesRoundABarrel(TextureKind kind)
    {
        var o = TextureOptions.Default with { Kind = kind };

        Assert.True(o.IsProfiled);
        Assert.True(o.Rings);
        Assert.False(o.IsLaid);
        Assert.Empty(SurfaceTexture.Over(40f, 40f, o, seamless: false));
        Assert.Null(SurfaceTexture.CoursesOf(o, 1f));
        Assert.NotNull(SurfaceTexture.ProfileOf(o, 1f));

        // Walls and bark are pieces with joints, which arrive raised and have proportions; grain
        // is lines, which can be cut. Grain and bark run either way; a wall's courses are level.
        Assert.Equal(kind != TextureKind.Grain, o.IsMasonry);
        Assert.Equal(kind is TextureKind.Grain or TextureKind.Bark, o.Turns);
    }

    /// <summary>
    /// Over every setting worth trying and two sizes of face: something closed comes back, the
    /// panel's figure is exactly what was built, and it looks like a texture - it reaches the
    /// depth asked for, never past it, and has joints or grooves without being all joint.
    /// </summary>
    [Fact]
    public void EverySettingBuildsAClosedFieldThatLooksLikeItself()
    {
        float[] pitches = [3f, 8f, 15f, 30f];
        float[] grooves = [0.4f, 1f, 2f];
        float[] depths = [0.4f, 1f, 2.5f];
        float[] aspects = [0f, 1f, 3f];
        (float A, float U)[] faces = [(15f, 15f), (60f, 40f)];

        var surfaces = faces.ToDictionary(f => f, f => Face(MathF.Max(f.A, f.U) + 4f));
        var complaints = new List<string>();
        int cases = 0;
        long worst = 0;

        foreach (var kind in Kinds)
            foreach (bool turned in Ways(kind))
                foreach (float pitch in pitches)
                    foreach (float groove in grooves)
                        foreach (float depth in depths)
                            foreach (float aspect in aspects)
                                foreach (var face in faces)
                                {
                                    cases++;
                                    string what = $"{kind,-6} turned={turned,-5} pitch={pitch,-3} groove={groove,-3} " +
                                                  $"depth={depth,-3} aspect={aspect,-3} face={face.A}x{face.U}";

                                    var relief = Profile(kind, pitch, groove, depth, aspect, turned);
                                    var cost = ReliefField.Cost(relief, face.A, face.U);
                                    if (!cost.CanBuild)
                                    {
                                        complaints.Add($"{what}: refused - {cost.Refusal}");
                                        continue;
                                    }

                                    var built = ReliefField.Build(surfaces[face], relief, face.A, face.U);
                                    worst = Math.Max(worst, built.TriangleCount);

                                    string why = Judge(relief, built, cost, depth, pitch, face.A, face.U);
                                    if (why.Length > 0) complaints.Add($"{what}: {why}");
                                }

        foreach (var line in complaints.Take(25)) log.WriteLine(line);
        foreach (var group in complaints.GroupBy(c => c[..6] + c[(c.LastIndexOf(':') + 1)..].Split(' ', 4)[1]))
            log.WriteLine($"  {group.Count(),4} x {group.Key}");
        log.WriteLine($"=== {complaints.Count} of {cases} bad; the heaviest was {worst:N0} triangles");

        Assert.Empty(complaints);
    }

    private static string Judge(IRelief relief, Mesh built, ReliefField.ReliefCost cost,
                                float depth, float pitch, float wide, float tall)
    {
        if (!built.CheckHealth().IsWatertight) return $"came back {built.CheckHealth().Describe()}";
        if (cost.Triangles != built.TriangleCount)
            return $"the panel said {cost.Triangles:N0} triangles and {built.TriangleCount:N0} were built";

        var heights = Samples(relief, wide, tall).ToList();
        float highest = heights.Max(), lowest = heights.Min();
        float low = heights.Count(h => h < 0.4f * depth) / (float)heights.Count;

        // A face smaller than a couple of pieces may land wholly on one stone, or wholly in a
        // joint's shoulder, so it is only asked to show something.
        bool whole = 2f * pitch <= MathF.Min(wide, tall);

        if (lowest < 0f) return $"dips {lowest:0.###} mm below the face";
        if (highest > depth + 1e-4f) return $"stands {highest:0.###} mm, past the {depth} asked for";
        if (highest < (whole ? 0.6f : 0.3f) * depth) return $"reaches only {highest:0.###} of {depth} mm";
        if (whole && low < 0.005f) return "has no joints or grooves to speak of";
        if (low > 0.85f) return $"is {low:P0} joint - no texture left";

        return "";
    }

    /// <summary>The heights on the field's own sample lines, which are the corners of the mesh.</summary>
    private static IEnumerable<float> Samples(IRelief relief, float wide, float tall)
    {
        foreach (float u in relief.Across(wide).Where(u => MathF.Abs(u) <= wide / 2f))
            foreach (float v in relief.Up(tall).Where(v => MathF.Abs(v) <= tall / 2f))
                yield return relief.Height(new Vector2(u, v));
    }

    /// <summary>
    /// Told the way round, a field is the same one way round later: the stone that runs off one
    /// end is the stone that comes back on at the other, so the ring closes with nothing to see.
    /// </summary>
    [Theory]
    [MemberData(nameof(EachKind))]
    public void ItRepeatsExactlyRoundABarrel(TextureKind kind)
    {
        var random = new Random(5);
        float worst = 0f;

        foreach (bool turned in Ways(kind))
            foreach (float radius in new[] { 6f, 12f, 30f })
            {
                float around = MathF.Tau * radius;
                var relief = Profile(kind, kind == TextureKind.Grain ? 2.5f : 9f, 0.8f, 1.2f, 0f, turned, around);

                for (int k = 0; k < 400; k++)
                {
                    var at = new Vector2((random.NextSingle() - 0.5f) * around, (random.NextSingle() - 0.5f) * 40f);
                    float here = relief.Height(at);
                    float roundAgain = relief.Height(at + new Vector2(around, 0f));

                    worst = MathF.Max(worst, MathF.Abs(here - roundAgain));
                }
            }

        log.WriteLine($"{kind}: off by at most {worst:0.######} mm one way round later");
        Assert.True(worst < 1e-3f, $"{kind} is {worst:0.###} mm different one way round later");
    }

    /// <summary>
    /// Built as a ring it is closed with no ends at all - one quad more in every row and no end
    /// walls - and it costs exactly what the panel said, less than a flat sheet of the same size,
    /// which has to wall off both ends.
    /// </summary>
    [Theory]
    [MemberData(nameof(EachKind))]
    public void ARingIsClosedWithNoEnds(TextureKind kind)
    {
        float around = MathF.Tau * 12f;
        var relief = Profile(kind, kind == TextureKind.Grain ? 2.5f : 9f, 0.8f, 1.2f, around: around);
        var surface = new CylinderSurface(new Vector3(0, 0, 12f), 12f);

        var ring = ReliefField.Build(surface, relief, around, 20f, round: true);
        var sheet = ReliefField.Build(surface, relief, around, 20f);
        var cost = ReliefField.Cost(relief, around, 20f, round: true);

        log.WriteLine($"{kind}: ring {ring.TriangleCount:N0} triangles, sheet {sheet.TriangleCount:N0}");

        Assert.True(ring.CheckHealth().IsWatertight, ring.CheckHealth().Describe());
        Assert.Equal(cost.Triangles, ring.TriangleCount);
        Assert.Equal(cost.Across * cost.Up * 4L, ring.TriangleCount);
        Assert.True(ring.TriangleCount < sheet.TriangleCount);
    }

    /// <summary>
    /// Onto a barrel as a ring: the default 32-sided cylinder, a finer one, and a big coarse one
    /// whose flats stand well clear of the circle - from a corner of the barrel, the middle of a
    /// flat, and somewhere in between. Raised, and grain cut as well.
    /// </summary>
    [Theory]
    [InlineData(32, 10f)]
    [InlineData(96, 12f)]
    [InlineData(32, 30f)]
    public void ARingGoesOntoABarrelAndStaysPrintable(int sides, float radius)
    {
        var barrel = Primitives.Create(PrimitiveKind.Cylinder, 2f * radius, sides);
        var bounds = barrel.ComputeBounds();
        var axis = new Vector2(bounds.Center.X, bounds.Center.Y);
        var profile = SurfaceProfile.Build(barrel, axis)!;
        var bad = new List<string>();

        log.WriteLine($"the barrel strays {profile.ProudMm:0.###} mm proud of its profile and {profile.ShortMm:0.###} short");

        foreach (var kind in Kinds)
            foreach (bool raised in kind == TextureKind.Grain ? new[] { true, false } : new[] { true })
                foreach (float start in new[] { 0f, 0.013f, MathF.PI / sides })
                {
                    float reach = profile.RadiusAt(start, bounds.Center.Z)!.Value;
                    float around = MathF.Tau * reach;
                    var surface = new CylinderSurface(new Vector3(axis.X, axis.Y, bounds.Center.Z), reach, start, profile);

                    var relief = Profile(kind, kind == TextureKind.Grain ? 2.5f : 7f, 0.8f, 1f, around: around);
                    var ring = ReliefField.Build(surface, relief, around, bounds.Size.Z - 1f, sunk: !raised, round: true);

                    var clock = Stopwatch.StartNew();
                    var joined = raised ? LocalCsg.Union(barrel, ring) : LocalCsg.Subtract(barrel, ring);
                    clock.Stop();

                    var health = joined.CheckHealth();
                    double before = barrel.ComputeSignedVolume(), after = joined.ComputeSignedVolume();
                    string what = $"{kind} {(raised ? "raised" : "cut")} from {start:0.###} rad";

                    log.WriteLine($"{what}: {ring.TriangleCount:N0} + {barrel.TriangleCount:N0} -> " +
                                  $"{joined.TriangleCount:N0} triangles in {clock.ElapsedMilliseconds} ms, {health.Describe()}");

                    if (!health.IsWatertight) bad.Add($"{what}: {health.Describe()}");
                    else if (raised ? after <= before : after >= before) bad.Add($"{what}: the volume did not move");
                    else if (clock.Elapsed.TotalSeconds > 10) bad.Add($"{what}: took {clock.Elapsed.TotalSeconds:0.#} s");
                }

        Assert.Empty(bad);
    }

    /// <summary>
    /// Onto a face the way Apply lays it on: kept to the face, then unioned. Every one of them,
    /// at a pitch somebody would use, quickly and closed.
    /// </summary>
    [Theory]
    [InlineData(TextureKind.Rubble, 10f)]
    [InlineData(TextureKind.Castle, 14f)]
    [InlineData(TextureKind.Bark, 5f)]
    [InlineData(TextureKind.Grain, 2.5f)]
    public void ItGoesOntoAFaceAndStaysPrintable(TextureKind kind, float pitch)
    {
        var box = MeshTransform.Transformed(Primitives.Box(60f, 60f, 60f), Matrix4x4.CreateTranslation(0, 0, 30f));
        var face = new PlanarSurface(FacePatch.Find(box, new Vector3(0, -30f, 30f), -Vector3.UnitY)!);

        var clock = Stopwatch.StartNew();
        var field = ReliefField.Build(face, Profile(kind, pitch, 0.8f, 1.2f), 58f, 58f);
        var kept = TextCutter.OnTheFace(field, face);
        var joined = LocalCsg.Union(box, kept);
        clock.Stop();

        log.WriteLine($"{kind}: {field.TriangleCount:N0} triangles of texture, {joined.TriangleCount:N0} in all, " +
                      $"{clock.ElapsedMilliseconds} ms");

        Assert.True(joined.CheckHealth().IsWatertight, joined.CheckHealth().Describe());
        Assert.True(joined.ComputeSignedVolume() > box.ComputeSignedVolume());
        Assert.True(clock.Elapsed.TotalSeconds < 10, $"took {clock.Elapsed.TotalSeconds:0.#} s");
    }

    /// <summary>
    /// On a face a field is a patch, and the handles place it as they place a flat texture: slid,
    /// turned, and where it is pushed past the edge, kept to the face and still put on closed.
    /// </summary>
    [Theory]
    [MemberData(nameof(EachKind))]
    public void APatchGoesWhereTheHandlesPutIt(TextureKind kind)
    {
        var box = MeshTransform.Transformed(Primitives.Box(60f, 60f, 60f), Matrix4x4.CreateTranslation(0, 0, 30f));
        var face = new PlanarSurface(FacePatch.Find(box, new Vector3(0, -30f, 30f), -Vector3.UnitY)!);
        var placement = new SurfacePlacement(new Vector2(20f, -5f), 30f);

        var relief = Profile(kind, kind == TextureKind.Grain ? 2.5f : 6f, 0.8f, 1f);
        var field = ReliefField.Build(new PlacedSurface(face, placement), relief, 30f, 20f);

        // Measured back in the face's own layout, along the patch and across it.
        var along = new Vector2(MathF.Cos(MathF.PI / 6f), MathF.Sin(MathF.PI / 6f));
        var across = new Vector2(-along.Y, along.X);
        var laid = field.Positions.Select(p => face.Face.ToUv(p) - face.Middle - placement.OffsetMm).ToList();

        float halfLong = laid.Max(p => MathF.Abs(Vector2.Dot(p, along)));
        float halfTall = laid.Max(p => MathF.Abs(Vector2.Dot(p, across)));
        log.WriteLine($"{kind}: {halfLong:0.###} x {halfTall:0.###} either side of where it was put");

        Assert.InRange(halfLong, 15f - 1e-2f, 15f + 1e-2f);
        Assert.InRange(halfTall, 10f - 1e-2f, 10f + 1e-2f);

        // Turned and slid that far it runs off the side of the face, and is cut back to it.
        var kept = TextCutter.OnTheFace(field, face);
        var joined = LocalCsg.Union(box, kept);

        Assert.True(kept.ComputeBounds().Max.X <= 30f + 1e-3f, "it was not kept to the face");
        Assert.True(joined.CheckHealth().IsWatertight, joined.CheckHealth().Describe());
    }

    /// <summary>
    /// A ring cannot move - it is the whole way round - but its stones slide round it and up it,
    /// and slid the whole way round they are back where they started.
    /// </summary>
    [Theory]
    [MemberData(nameof(EachKind))]
    public void ARingSlidesRoundAndUpTheBarrel(TextureKind kind)
    {
        float around = MathF.Tau * 12f;
        var relief = Profile(kind, kind == TextureKind.Grain ? 2.5f : 9f, 0.8f, 1.2f, around: around);
        var surface = new CylinderSurface(new Vector3(0, 0, 12f), 12f);

        var still = ReliefField.Build(surface, relief, around, 20f, round: true);
        var slid = ReliefField.Build(surface, new SlidRelief(relief, new Vector2(7.3f, 2.1f)), around, 20f, round: true);
        var roundAgain = ReliefField.Build(surface, new SlidRelief(relief, new Vector2(around, 0f)), around, 20f, round: true);

        float moved = still.Positions.Zip(slid.Positions, Vector3.Distance).Max();
        float back = still.Positions.Zip(roundAgain.Positions, Vector3.Distance).Max();
        log.WriteLine($"{kind}: slid moves the surface up to {moved:0.###} mm; once round, {back:0.######} mm");

        Assert.True(slid.CheckHealth().IsWatertight, slid.CheckHealth().Describe());
        Assert.True(moved > 0.3f, "sliding it changed nothing");
        Assert.True(back < 1e-3f, $"slid the whole way round it is {back:0.###} mm out");
    }

    /// <summary>
    /// A rubble wall is stones of every size, not a pavement of equal ones: measured as the
    /// stones themselves, found by flooding each one out to its joints.
    /// </summary>
    [Fact]
    public void RubbleStonesComeInEverySize()
    {
        var areas = Stones(Profile(TextureKind.Rubble, 10f, 1f, 1.5f), 100f, 70f);

        float mean = areas.Average(), spread = MathF.Sqrt(areas.Average(a => (a - mean) * (a - mean))) / mean;
        log.WriteLine($"{areas.Count} stones, {areas.Min():0} to {areas.Max():0} mm2, spread {spread:P0}");

        Assert.True(areas.Count > 30, $"only {areas.Count} whole stones");
        Assert.True(areas.Max() > 3f * areas.Min(), "the stones are all much the same size");
        Assert.True(spread > 0.3f, $"the stones vary by only {spread:P0}");
    }

    /// <summary>
    /// Castle walling is courses of different heights and stones of different lengths - brick,
    /// with the numbers varied, which is all that keeps it from reading as brick.
    /// </summary>
    [Fact]
    public void CastleCoursesAndStonesVary()
    {
        var relief = Profile(TextureKind.Castle, 14f, 1f, 1.5f);

        var tall = Runs(relief, along: false);
        var longs = Runs(relief, along: true);

        log.WriteLine($"stones {tall.Min():0.0} to {tall.Max():0.0} mm tall, {longs.Min():0.0} to {longs.Max():0.0} mm long");

        Assert.True(tall.Max() > 1.3f * tall.Min(), "every course is the same height");
        Assert.True(longs.Max() > 1.6f * longs.Min(), "every stone is the same length");

        // And courses, not rubble: stones are longer than they are tall.
        Assert.True(longs.Average() > 1.5f * tall.Average(), "the stones are as tall as they are long");
    }

    /// <summary>
    /// Grain runs along the face and bark up it, and turned they run the other way: the height
    /// changes faster across the run than along it.
    /// </summary>
    [Theory]
    [InlineData(TextureKind.Grain, false, true)]
    [InlineData(TextureKind.Grain, true, false)]
    [InlineData(TextureKind.Bark, false, false)]
    [InlineData(TextureKind.Bark, true, true)]
    public void ItRunsTheWayItWasAsked(TextureKind kind, bool turned, bool runsAcross)
    {
        var relief = Profile(kind, kind == TextureKind.Grain ? 3f : 5f, 0.8f, 1.2f, turned: turned);

        double alongX = 0, alongY = 0;
        const float Step = 0.1f;
        for (float x = -30f; x < 30f; x += 0.37f)
            for (float y = -30f; y < 30f; y += 0.41f)
            {
                float here = relief.Height(new Vector2(x, y));
                alongX += MathF.Abs(relief.Height(new Vector2(x + Step, y)) - here);
                alongY += MathF.Abs(relief.Height(new Vector2(x, y + Step)) - here);
            }

        log.WriteLine($"{kind} turned={turned}: changes {alongX:0} along x, {alongY:0} along y");

        // Running across, it changes as it goes up; running up, as it goes across.
        if (runsAcross) Assert.True(alongY > 1.5 * alongX, "it does not run across");
        else Assert.True(alongX > 1.5 * alongY, "it does not run up");
    }

    /// <summary>
    /// The walls round a box, found from a face picked on one of them: four corners, the way round,
    /// and a strip that goes round each corner on the mitre: the two walls' offsets joined at their
    /// meeting point.
    /// </summary>
    [Fact]
    public void TheWallsRoundABoxUnrollIntoOneStripThatTurnsEachCornerOnTheMitre()
    {
        var box = MeshTransform.Transformed(Primitives.Box(40, 30, 20), Matrix4x4.CreateTranslation(0, 0, 10));
        var face = FacePatch.Find(box, new Vector3(20, 0, 10), Vector3.UnitX)!;
        var loop = WallLoop.Around(box, face, new Vector3(20, 0, 10), out var why);

        Assert.True(loop is not null, why);
        Assert.Equal(4, loop!.Count);
        Assert.Equal(140f, loop.Perimeter, 3);
        Assert.Equal(0f, loop.Low, 3);
        Assert.Equal(20f, loop.High, 3);

        // The click is the middle of the +X wall, a quarter of the way round from its end.
        var run = WallRun.Round(loop, box, out why);
        Assert.True(run is { Closed: true }, why);
        var surface = new WallsSurface(run!);
        Assert.Equal(new Vector3(20, 0, 10), surface.At(Vector2.Zero, 0), new Vec3Comparer(1e-3f));
        Assert.Equal(new Vector3(21, 0, 10), surface.At(Vector2.Zero, 1), new Vec3Comparer(1e-3f));

        // Two fold lines a hair either side of each of the four corners.
        var folds = surface.FoldsAcross(-70f, 70f);
        Assert.Equal(8, folds.Count);
        foreach (float corner in folds.Chunk(2).Select(pair => (pair[0] + pair[1]) / 2f))
        {
            var before = surface.At(new Vector2(corner - 0.01f, 0), 1.5f);
            var at = surface.At(new Vector2(corner, 0), 1.5f);
            var after = surface.At(new Vector2(corner + 0.01f, 0), 1.5f);

            // A hair either side it is 1.5 mm off one wall or the other, and at the corner itself it
            // is on the mitre, 1.5 mm off both: the two offset lines, joined at their meeting point.
            foreach (var near in new[] { before, after })
                Assert.True(MathF.Abs(MathF.Abs(near.X) - 21.5f) < 1e-3f || MathF.Abs(MathF.Abs(near.Y) - 16.5f) < 1e-3f);
            Assert.Equal(21.5f, MathF.Abs(at.X), 3);
            Assert.Equal(16.5f, MathF.Abs(at.Y), 3);
        }
    }

    /// <summary>
    /// Two walls picked are a strip with two ends: as long as the two walls, turning the corner
    /// between them on the mitre, and square to each end wall at its far edge rather than leaning
    /// out past the walls that were left alone.
    /// </summary>
    [Fact]
    public void TwoWallsOfABoxAreAStripThatTurnsTheirCornerAndEndsSquare()
    {
        var box = MeshTransform.Transformed(Primitives.Box(40, 30, 20), Matrix4x4.CreateTranslation(0, 0, 10));
        var face = FacePatch.Find(box, new Vector3(20, 0, 10), Vector3.UnitX)!;
        var loop = WallLoop.Around(box, face, new Vector3(20, 0, 10), out var why)!;

        int east = loop.WallNear(new Vector2(20, 0), Vector2.UnitX, out _);
        int north = loop.WallNear(new Vector2(0, 15), Vector2.UnitY, out _);
        var run = WallRun.Of(loop, box, east, out why)!.With(box, north, out why);

        Assert.True(run is { Count: 2, Closed: false }, why);
        Assert.Equal(70f, run!.Length, 3);
        Assert.Equal(0f, run.Low, 3);
        Assert.Equal(20f, run.High, 3);

        // The +X wall runs from y = -15 to +15, then the +Y wall from x = +20 back to -20: the one
        // corner is 30 mm along a 70 mm strip, 5 mm short of its middle.
        var surface = new WallsSurface(run);
        var folds = surface.FoldsAcross(-35f, 35f);
        Assert.Equal(2, folds.Count);
        Assert.Equal(-5f, (folds[0] + folds[1]) / 2f, 3);
        Assert.Equal(new Vector3(21.5f, 16.5f, 10), surface.At(new Vector2(-5f, 0), 1.5f), new Vec3Comparer(1e-3f));

        // Square at both ends, flush with the edges of the walls left bare.
        Assert.Equal(new Vector3(21.5f, -15f, 10), surface.At(new Vector2(-35f, 0), 1.5f), new Vec3Comparer(1e-3f));
        Assert.Equal(new Vector3(-20f, 16.5f, 10), surface.At(new Vector2(35f, 0), 1.5f), new Vec3Comparer(1e-3f));

        // Taking in the far wall takes the one between with it; an end can be let go, the middle cannot.
        int west = loop.WallNear(new Vector2(-20, 0), -Vector2.UnitX, out _);
        int south = loop.WallNear(new Vector2(0, -15), -Vector2.UnitY, out _);
        var three = WallRun.Of(loop, box, east, out _)!.With(box, west, out _)!;
        Assert.Equal(3, three.Count);
        Assert.True(three.Holds(north) ^ three.Holds(south));
        Assert.Null(three.Without(box, three.Holds(north) ? north : south, out why));
        Assert.Equal(2, three.Without(box, west, out _)!.Count);
        Assert.True(three.With(box, three.Holds(north) ? south : north, out _)!.Closed);
    }

    private sealed class Vec3Comparer(float tolerance) : IEqualityComparer<Vector3>
    {
        public bool Equals(Vector3 a, Vector3 b) => Vector3.Distance(a, b) <= tolerance;
        public int GetHashCode(Vector3 v) => 0;
    }

    /// <summary>A part that is not a closed upright shape cannot have its walls gone round, and says so.</summary>
    [Fact]
    public void AFaceOnTopOfAPartHasNoWallsToGoRound()
    {
        var box = MeshTransform.Transformed(Primitives.Box(40, 30, 20), Matrix4x4.CreateTranslation(0, 0, 10));
        var top = FacePatch.Find(box, new Vector3(0, 0, 20), Vector3.UnitZ)!;

        Assert.Null(WallLoop.Around(box, top, new Vector3(0, 0, 20), out var why));
        Assert.Contains("not an upright wall", why);
    }

    /// <summary>The panel offers all four, and a wall or bark arrives raised.</summary>
    [Fact]
    public void ThePanelOffersThemAndAWallArrivesRaised() => WithModel(model =>
    {
        foreach (var kind in Kinds)
        {
            Assert.Contains(kind, model.EmbossTextures);

            model.EmbossTexture = TextureKind.None;
            model.EmbossRaised = false;
            model.EmbossTexture = kind;

            Assert.True(model.UsesProfile);
            Assert.Equal(kind != TextureKind.Grain, model.EmbossRaised);
            Assert.Equal(kind != TextureKind.Grain, model.TextureHasCourses);
            Assert.Equal(kind is TextureKind.Grain or TextureKind.Bark, model.TextureRuns);
            Assert.DoesNotContain(TextProjection.Spherical, model.EmbossProjections);

            // On a face it is a patch that turns; round a barrel a ring that only slides.
            model.EmbossProjection = TextProjection.Planar;
            Assert.False(model.TextureSlides);
            Assert.True(model.EmbossTurns);

            model.EmbossProjection = TextProjection.Cylindrical;
            Assert.True(model.TextureSlides);
            Assert.False(model.EmbossTurns);
        }

        // Boarding is not a ring, so wrapped it is still a patch that turns.
        model.EmbossTexture = TextureKind.Planks;
        Assert.False(model.TextureSlides);
        Assert.True(model.EmbossTurns);
    });

    private static void WithModel(Action<MainViewModel> body)
    {
        ExceptionDispatchInfo? error = null;
        var thread = new Thread(() =>
        {
            try { body(new MainViewModel()); }
            catch (Exception ex) { error = ExceptionDispatchInfo.Capture(ex); }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        error?.Throw();
    }

    /// <summary>The area of every stone that lies wholly inside the patch, in square millimetres.</summary>
    private static List<float> Stones(IRelief relief, float wide, float tall)
    {
        const float Cell = 0.2f;
        int w = (int)(wide / Cell), h = (int)(tall / Cell);
        var stone = new bool[w, h];

        for (int i = 0; i < w; i++)
            for (int j = 0; j < h; j++)
                stone[i, j] = relief.Height(new Vector2(-wide / 2f + (i + 0.5f) * Cell, -tall / 2f + (j + 0.5f) * Cell)) > 0f;

        var seen = new bool[w, h];
        var areas = new List<float>();
        var queue = new Queue<(int, int)>();

        for (int i = 0; i < w; i++)
            for (int j = 0; j < h; j++)
            {
                if (!stone[i, j] || seen[i, j]) continue;

                int count = 0;
                bool edge = false;
                seen[i, j] = true;
                queue.Enqueue((i, j));

                while (queue.Count > 0)
                {
                    var (a, b) = queue.Dequeue();
                    count++;
                    if (a == 0 || b == 0 || a == w - 1 || b == h - 1) edge = true;

                    foreach (var (c, d) in new[] { (a + 1, b), (a - 1, b), (a, b + 1), (a, b - 1) })
                        if (c >= 0 && d >= 0 && c < w && d < h && stone[c, d] && !seen[c, d])
                        {
                            seen[c, d] = true;
                            queue.Enqueue((c, d));
                        }
                }

                if (!edge) areas.Add(count * Cell * Cell);
            }

        return areas;
    }

    /// <summary>
    /// How long each run of stone is along lines across the face, or up it, from one joint to the
    /// next - the lengths of the stones, or the heights of the courses.
    /// </summary>
    private static List<float> Runs(IRelief relief, bool along)
    {
        const float Cell = 0.1f;
        var runs = new List<float>();

        for (float line = -40f; line <= 40f; line += 3.7f)
        {
            float start = float.NaN;
            for (float t = -50f; t <= 50f; t += Cell)
            {
                var at = along ? new Vector2(t, line) : new Vector2(line, t);
                bool inStone = relief.Height(at) > 0f;

                if (inStone && float.IsNaN(start)) start = t;
                else if (!inStone && !float.IsNaN(start))
                {
                    if (start > -50f + Cell) runs.Add(t - start);
                    start = float.NaN;
                }
            }
        }

        return runs;
    }
}
