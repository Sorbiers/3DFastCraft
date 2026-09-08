using System.Numerics;
using FastCraft3D.Geometry;
using FastCraft3D.Geometry.Csg;
using FastCraft3D.Geometry.Moulding;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>
/// Working out how a mould should come apart.
///
/// The question is not about faces, it is about lines of sight through material: a mould half
/// lifts off if nothing it has to clear sits over the top of something else. So the whole study
/// is a scan of the occupancy grid, and these are the shapes whose answers are already known.
/// </summary>
public class MouldAnalysisTests
{
    private const int Coarse = 40;

    private static MouldStudy Study(Mesh mesh) => MouldAnalysis.Study(mesh, Coarse);

    private static PullReport For(MouldStudy study, Axis axis) =>
        study.Pulls.Single(p => p.Axis == axis);

    private static Mesh Moved(Mesh mesh, float x, float y, float z) =>
        MeshTransform.Transformed(mesh, Matrix4x4.CreateTranslation(x, y, z));

    /// <summary>Nothing overhangs anything, so it comes apart whichever way you pull it.</summary>
    [Fact]
    public void ABoxHasNoUndercutsOnAnyAxis()
    {
        var study = Study(Primitives.Box(30, 20, 10));

        Assert.All(study.Pulls, p => Assert.True(p.LiftsStraightOut, $"{p.Axis} reported {p.Trapped}"));
        Assert.Single(study.Cuts);
    }

    /// <summary>And it splits through the middle, which is where its section is widest.</summary>
    [Fact]
    public void ASphereSplitsAtItsEquator()
    {
        var study = Study(Primitives.Sphere(15, 48, 24));

        Assert.All(study.Pulls, p => Assert.True(p.LiftsStraightOut));
        Assert.Equal(0f, study.Cuts[0].At, 2f);          // within a voxel or two of the middle
        Assert.Equal(2, study.Parts);
    }

    /// <summary>
    /// The one the whole approach exists for. A ring has to be pulled apart along its hole: across
    /// it, every line of sight leaves the metal and comes back on the far limb, and no flat mould
    /// in two pieces will let go of it.
    /// </summary>
    [Fact]
    public void ARingIsPulledAlongItsHoleAndNotAcrossIt()
    {
        // Primitives.Torus lies in XY, so its hole runs along Z.
        var study = Study(Primitives.Torus(18, 5, 48, 24));

        Assert.Equal(Axis.Z, study.Best);
        Assert.True(For(study, Axis.Z).LiftsStraightOut);

        Assert.False(For(study, Axis.X).LiftsStraightOut);
        Assert.False(For(study, Axis.Y).LiftsStraightOut);

        // Found the good axis, so one cut is enough.
        Assert.Single(study.Cuts);
    }

    /// <summary>
    /// A hole straight through is only a problem from the sides. Pulled along the hole it is a
    /// plain prismatic feature and the mould forms it with a core standing off each half.
    /// </summary>
    [Fact]
    public void ATunnelIsPulledAlongTheTunnel()
    {
        var block = Primitives.Box(40, 30, 30);
        var bore = Moved(Primitives.Prism(6, 60, 32), 0, 0, 0);

        // Prism stands along Z, so turn it to run along X.
        bore = MeshTransform.Transformed(bore, Matrix4x4.CreateRotationY(MathF.PI / 2f));

        var study = Study(CsgSolid.Subtract(block, bore));

        Assert.Equal(Axis.X, study.Best);
        Assert.True(For(study, Axis.X).LiftsStraightOut);
        Assert.False(For(study, Axis.Z).LiftsStraightOut);
    }

    /// <summary>
    /// A cup is not an undercut. The inside opens upward, so the core comes out with the top half
    /// - which is why the test has to be about lines of sight rather than about overhanging faces.
    /// </summary>
    [Fact]
    public void AnOpenCupComesApartAlongItsOpening()
    {
        var body = Primitives.Box(30, 30, 30);
        var hollow = Moved(Primitives.Box(22, 22, 24), 0, 0, 6);

        var study = Study(CsgSolid.Subtract(body, hollow));

        Assert.True(For(study, Axis.Z).LiftsStraightOut);
    }

    /// <summary>
    /// A closed shell is trapped whichever way it is pulled, and saying so is the useful answer.
    /// One flat cut cannot reach an enclosed void, so a second is recommended.
    /// </summary>
    [Fact]
    public void SomethingTrappedEveryWayAsksForMoreThanOneCut()
    {
        var shell = CsgSolid.Subtract(Primitives.Box(30, 30, 30), Primitives.Box(18, 18, 18));

        var study = Study(shell);

        Assert.All(study.Pulls, p => Assert.False(p.LiftsStraightOut));
        Assert.True(study.Cuts.Count > 1, $"only {study.Cuts.Count} cut(s) for a closed shell");
        Assert.True(study.Parts >= 4);
    }

    /// <summary>The cut goes where the section is widest, which on a cone is its base.</summary>
    [Fact]
    public void AConeIsCutAtItsBase()
    {
        var cone = Primitives.Cone(15, 40, 48);
        var bottom = cone.ComputeBounds().Min.Z;

        var study = Study(cone);

        Assert.Equal(Axis.Z, study.Best);
        Assert.Equal(bottom, study.Cuts[0].At, 3f);
    }

    /// <summary>
    /// A ring's top is one level stretch the whole way round, and the middle of that stretch is
    /// the hole - where there is no model. The pour hole has to land on the metal: put over the
    /// gap it stands in mid-air, misses the cavity, and leaves a shaft to nowhere.
    /// </summary>
    [Fact]
    public void ThePourHoleOnARingLandsOnTheRingAndNotInTheHole()
    {
        var study = Study(Primitives.Torus(18, 5, 48, 24));

        float fromAxis = new Vector2(study.Sprue.X, study.Sprue.Y).Length();

        Assert.InRange(fromAxis, 13f, 23f);
    }

    /// <summary>Silicone is poured in at the top, so the hole has to meet the model's high point.</summary>
    [Fact]
    public void TheSprueGoesToTheHighestPoint()
    {
        var tower = Moved(Primitives.Box(10, 10, 40), 0, 0, 20);
        var study = Study(tower);

        Assert.Equal(40f, study.Sprue.Z, 3f);
    }

    /// <summary>
    /// A second peak is where a bubble sits. The taller one takes the pour hole and the other
    /// gets a vent, or the silicone rises past it and seals the air in.
    /// </summary>
    [Fact]
    public void ALowerSecondPeakAsksForAVent()
    {
        var tall = Moved(Primitives.Box(12, 12, 40), -20, 0, 20);
        var short_ = Moved(Primitives.Box(12, 12, 26), 20, 0, 13);
        var bar = Moved(Primitives.Box(52, 12, 6), 0, 0, 3);

        var pair = CsgSolid.Union(CsgSolid.Union(tall, short_), bar);
        var study = Study(pair);

        Assert.Equal(40f, study.Sprue.Z, 3f);
        Assert.Equal(-20f, study.Sprue.X, 3f);

        var vent = Assert.Single(study.Vents);
        Assert.Equal(26f, vent.Z, 3f);
        Assert.Equal(20f, vent.X, 3f);
    }

    /// <summary>The summary is what the dialog shows, so it has to say the useful part.</summary>
    [Fact]
    public void TheSummarySaysWhereToCutAndIntoHowMany()
    {
        var study = Study(Primitives.Sphere(15, 48, 24));

        Assert.Contains("Two parts", study.Summary);
        Assert.Contains("lifts straight out", study.Summary);
    }

    /// <summary>It reads a grid the size of a rebuild, so it has to be possible to give up on it.</summary>
    [Fact]
    public void TheStudyCanBeAborted()
    {
        var source = new CancellationTokenSource();
        source.Cancel();

        Assert.Throws<OperationCanceledException>(
            () => MouldAnalysis.Study(Primitives.Sphere(15, 32, 16), Coarse, source.Token));
    }
}
