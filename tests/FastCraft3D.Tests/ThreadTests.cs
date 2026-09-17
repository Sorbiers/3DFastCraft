using System.Numerics;
using FastCraft3D.Geometry;
using FastCraft3D.Geometry.Csg;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>
/// Screw threads, built as their own surface rather than cut with a boolean: a rod, a nut, and the
/// solid of a threaded hole.
/// </summary>
public class ThreadTests
{
    private static ThreadOptions Metric(string name, ThreadKind kind, float length = 20f, float clearance = 0.2f,
                                        NutBody body = NutBody.Hexagon)
    {
        var size = Threads.Metric.Single(m => m.Name == name);
        return new ThreadOptions(kind, size.Diameter, size.Pitch, length, clearance, body, size.AcrossFlats, size.NutHeight);
    }

    private static void AssertSound(Mesh mesh)
    {
        var health = mesh.CheckHealth();
        Assert.True(health.IsWatertight, health.Describe());
        Assert.True(health.SignedVolume > 0, "it came out inside out");

        // A triangle folded flat still closes the count, but slicers and the boolean choke on it.
        double smallest = double.MaxValue;
        for (int i = 0; i + 2 < mesh.Indices.Count; i += 3)
        {
            Vector3 a = mesh.Positions[mesh.Indices[i]];
            double area = Vector3.Cross(mesh.Positions[mesh.Indices[i + 1]] - a, mesh.Positions[mesh.Indices[i + 2]] - a).Length() / 2;
            smallest = Math.Min(smallest, area);
        }

        Assert.True(smallest > 1e-9, $"a triangle of {smallest:0.###e0} mm2 is folded flat");
    }

    private static float Radius(Vector3 p) => new Vector2(p.X, p.Y).Length();

    [Theory]
    [InlineData("M3", 20f)]
    [InlineData("M8", 20f)]
    [InlineData("M20", 40f)]
    [InlineData("M8", 12.5f)]
    [InlineData("M6", 1.5f)]
    public void ARodComesOutAsOneSoundSolid(string size, float length) =>
        AssertSound(Threads.Build(Metric(size, ThreadKind.Rod, length)));

    [Theory]
    [InlineData("M3", NutBody.Hexagon)]
    [InlineData("M8", NutBody.Hexagon)]
    [InlineData("M20", NutBody.Hexagon)]
    [InlineData("M8", NutBody.Round)]
    public void ANutComesOutAsOneSoundSolid(string size, NutBody body) =>
        AssertSound(Threads.Build(Metric(size, ThreadKind.Nut, body: body)));

    [Theory]
    [InlineData("M3", 12f, NutBody.Hexagon)]
    [InlineData("M8", 20f, NutBody.Hexagon)]
    [InlineData("M20", 40f, NutBody.Hexagon)]
    [InlineData("M8", 16f, NutBody.Round)]
    public void ABoltComesOutAsOneSoundSolidStandingOnItsHead(string size, float length, NutBody body)
    {
        var metric = Threads.Metric.Single(m => m.Name == size);
        var options = Metric(size, ThreadKind.Bolt, length, body: body) with { HeadHeight = metric.HeadHeight };
        var bolt = Threads.Build(options);

        AssertSound(bolt);

        var bounds = bolt.ComputeBounds();
        Assert.Equal(length + metric.HeadHeight, bounds.Size.Z, 2);
        Assert.Equal(0f, bounds.Center.Z, 2);
        Assert.Equal(metric.AcrossFlats, bounds.Size.Y, 2);

        // The head is the bottom of it; above the head, nothing is wider than the thread.
        float headTop = bounds.Min.Z + metric.HeadHeight;
        Assert.True(bolt.Positions.Where(p => p.Z > headTop + 1e-3f).Max(Radius) <= options.RodOutside / 2f + 1e-3f);
    }

    /// <summary>
    /// Turned over head down by half a turn, not mirrored: its thread still climbs anticlockwise,
    /// as <see cref="TheThreadIsRightHanded"/> has it for a rod.
    /// </summary>
    [Fact]
    public void ABoltsThreadIsRightHandedToo()
    {
        var bolt = Threads.Build(Metric("M8", ThreadKind.Bolt, 20f, clearance: 0f) with { HeadHeight = 5.3f });
        var bounds = bolt.ComputeBounds();
        float middle = (bounds.Min.Z + 5.3f + bounds.Max.Z) / 2f;
        var crest = bolt.Positions
            .Where(p => MathF.Abs(p.Z - middle) < 5f && MathF.Abs(Radius(p) - 4f) < 1e-3f)
            .ToList();

        // The one nearest the middle: turned over, the first vertex made is at the top of the
        // window, and a quarter pitch above it is outside.
        var start = crest.Where(p => p.X > 3.99f && MathF.Abs(p.Y) < 1e-3f).MinBy(p => MathF.Abs(p.Z - middle));

        bool CrestAtY(float z) => crest.Any(p => p.Y > 3.99f && MathF.Abs(p.X) < 1e-3f && MathF.Abs(p.Z - z) < 1e-3f);

        Assert.True(CrestAtY(start.Z + 1.25f / 4f), "the crest does not climb anticlockwise");
        Assert.False(CrestAtY(start.Z - 1.25f / 4f), "the crest falls anticlockwise - a left-hand thread");
    }

    [Theory]
    [InlineData("M4", 10f)]
    [InlineData("M12", 25f)]
    public void AHoleCutterComesOutAsOneSoundSolid(string size, float length) =>
        AssertSound(Threads.Build(Metric(size, ThreadKind.HoleCutter, length)));

    /// <summary>
    /// Numbers no preset gives, so the profile's corners land a hair from the ends and the lead-in
    /// steps rather than neatly on them: where the vertices of neighbouring strips could disagree.
    /// </summary>
    [Theory]
    [InlineData(7.3f, 1.1f, 13.37f, 0.25f)]
    [InlineData(2f, 0.4f, 3.0001f, 0f)]
    [InlineData(30f, 3.5f, 17.4999f, 0.4f)]
    [InlineData(5f, 0.2f, 1f, 0.1f)]
    [InlineData(64f, 6f, 60.0005f, 1f)]
    public void OddSizesAndLengthsStillCloseUp(float diameter, float pitch, float length, float clearance)
    {
        var options = new ThreadOptions(ThreadKind.Rod, diameter, pitch, length, clearance,
                                        NutBody.Hexagon, diameter * 1.6f, length);

        foreach (var kind in Enum.GetValues<ThreadKind>())
            AssertSound(Threads.Build(options with { Kind = kind }));
    }

    [Fact]
    public void ARodIsTheDiameterAndLengthAskedFor()
    {
        var exact = Threads.Build(Metric("M8", ThreadKind.Rod, 20f, clearance: 0f)).ComputeBounds();

        Assert.Equal(8f, exact.Size.X, 3);
        Assert.Equal(8f, exact.Size.Y, 3);
        Assert.Equal(20f, exact.Size.Z, 3);

        // Half the clearance comes off the rod.
        var eased = Threads.Build(Metric("M8", ThreadKind.Rod, 20f, clearance: 0.2f)).ComputeBounds();
        Assert.Equal(7.9f, eased.Size.X, 3);
    }

    /// <summary>ISO 68-1: the basic profile is 5/8 of a fundamental triangle deep, 0.677 mm for M8.</summary>
    [Fact]
    public void ARodsCoreIsCutToTheIsoDepth()
    {
        var rod = Threads.Build(Metric("M8", ThreadKind.Rod, 20f, clearance: 0f));
        float core = rod.Positions.Where(p => MathF.Abs(p.Z) < 5f).Min(Radius);

        Assert.Equal(4f - 0.6766f, core, 3);
    }

    [Fact]
    public void ANutIsTheWidthAndHeightAskedFor()
    {
        var bounds = Threads.Build(Metric("M8", ThreadKind.Nut)).ComputeBounds();

        // Flats face along Y, corners along X.
        Assert.Equal(13f, bounds.Size.Y, 3);
        Assert.Equal(13f * 2f / MathF.Sqrt(3f), bounds.Size.X, 3);
        Assert.Equal(6.8f, bounds.Size.Z, 3);
    }

    [Fact]
    public void ANutTooNarrowForItsThreadIsWidenedToKeepAWall()
    {
        var sane = (Metric("M8", ThreadKind.Nut) with { AcrossFlats = 8f }).Sane();

        Assert.Equal(8f + 0.1f + 2f * ThreadOptions.MinimumWall, sane.AcrossFlats, 3);
        AssertSound(Threads.Build(sane));
    }

    [Theory]
    [InlineData("M3", 3f, 0.5f)]
    [InlineData("M4", 4f, 0.7f)]
    [InlineData("M5", 5f, 0.8f)]
    [InlineData("M6", 6f, 1f)]
    [InlineData("M8", 8f, 1.25f)]
    [InlineData("M10", 10f, 1.5f)]
    [InlineData("M12", 12f, 1.75f)]
    [InlineData("M16", 16f, 2f)]
    [InlineData("M20", 20f, 2.5f)]
    public void ThePresetsGiveTheIsoCoarsePitch(string name, float diameter, float pitch)
    {
        var size = Threads.Metric.Single(m => m.Name == name);

        Assert.Equal(diameter, size.Diameter);
        Assert.Equal(pitch, size.Pitch);
        Assert.Equal(name, (ThreadOptions.Default with { Diameter = diameter, Pitch = pitch }).SizeName);
    }

    /// <summary>
    /// A right-hand thread climbs as it turns anticlockwise seen from above, so the crest that
    /// passes +X is a quarter of a pitch higher by the time it reaches +Y. A left-hand one would be
    /// a quarter lower, and would not go into anything bought.
    /// </summary>
    [Fact]
    public void TheThreadIsRightHanded()
    {
        var rod = Threads.Build(Metric("M8", ThreadKind.Rod, 20f, clearance: 0f));
        var crest = rod.Positions
            .Where(p => MathF.Abs(p.Z) < 5f && MathF.Abs(Radius(p) - 4f) < 1e-3f)
            .ToList();

        var start = crest.First(p => p.X > 3.99f && MathF.Abs(p.Y) < 1e-3f);

        bool CrestAtY(float z) => crest.Any(p => p.Y > 3.99f && MathF.Abs(p.X) < 1e-3f && MathF.Abs(p.Z - z) < 1e-3f);

        Assert.True(CrestAtY(start.Z + 1.25f / 4f), "the crest does not climb anticlockwise");
        Assert.False(CrestAtY(start.Z - 1.25f / 4f), "the crest falls anticlockwise - a left-hand thread");
    }

    /// <summary>
    /// The point of the clearance: a rod in a nut of the same size, the two made separately, share
    /// no material. And not because they miss each other altogether - turned half a turn, the
    /// rod's crests land in the nut's and the two foul badly.
    ///
    /// The rod is a pitch longer than the nut at each end, so its thread lines up with the nut's
    /// and no face of one lies in a face of the other.
    /// </summary>
    [Fact]
    public void ARodAndANutOfTheSameSizeScrewTogether()
    {
        var nutOptions = Metric("M8", ThreadKind.Nut);
        var rod = Threads.Build(nutOptions with { Kind = ThreadKind.Rod, Length = nutOptions.NutHeight + 2f * nutOptions.Pitch });
        var nut = Threads.Build(nutOptions);

        Assert.True(nutOptions.Engages, "the rod's crests do not reach the nut's");

        // Manifold rather than the BSP engine, which takes a minute over two threads this fine.
        var shared = ManifoldCsg.Intersect(rod, nut);
        Assert.NotNull(shared);
        double overlap = Math.Abs(shared.ComputeSignedVolume());
        Assert.True(overlap < 0.01, $"the rod and nut share {overlap:0.###} mm3");

        var turned = ManifoldCsg.Intersect(MeshTransform.Transformed(rod, Matrix4x4.CreateRotationZ(MathF.PI)), nut);
        Assert.NotNull(turned);
        double fouled = Math.Abs(turned.ComputeSignedVolume());
        Assert.True(fouled > 5.0, $"half a turn out, the rod and nut share only {fouled:0.###} mm3");
    }
}
