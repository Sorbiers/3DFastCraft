using System.Numerics;
using System.Runtime.ExceptionServices;
using FastCraft3D.Geometry;
using FastCraft3D.Geometry.Csg;
using FastCraft3D.View;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>Screw holes and insert pockets, and the faces that will need support.</summary>
public class HoleAndOverhangTests
{
    private static void Closed(Mesh mesh)
    {
        var health = mesh.CheckHealth();
        Assert.True(health.IsWatertight, health.Describe());
        Assert.True(mesh.ComputeSignedVolume() > 0, "inside out");
    }

    private static float Reach(Mesh mesh, float atZ) =>
        mesh.Positions.Where(p => MathF.Abs(p.Z - atZ) < 1e-3f).Max(p => new Vector2(p.X, p.Y).Length());

    [Fact]
    public void APlainHoleIsTheClearanceWideAndAsDeepAsAsked()
    {
        var cutter = HoleCutter.Build(new HoleOptions { Size = "M4", Head = HoleHead.Plain, Depth = 12f, ExtraClearance = 0.2f }, 12f);

        Assert.NotNull(cutter);
        Closed(cutter);
        Assert.Equal(-12f, cutter.ComputeBounds().Min.Z, 3);
        Assert.Equal(HoleCutter.Overshoot, cutter.ComputeBounds().Max.Z, 3);
        Assert.Equal((4.5f + 0.2f) / 2f, Reach(cutter, -12f), 3);
    }

    [Fact]
    public void ACountersinkIsTheHeadWideAtTheSurfaceAndNarrowsAtNinetyDegrees()
    {
        var cutter = HoleCutter.Build(new HoleOptions { Size = "M3", Head = HoleHead.Countersunk, ExtraClearance = 0f }, 10f)!;

        Closed(cutter);
        float head = 6.72f / 2f, shaft = 3.4f / 2f;
        Assert.Equal(head, Reach(cutter, 0f), 3);
        Assert.Equal(shaft, Reach(cutter, -(head - shaft)), 3);
    }

    [Fact]
    public void ACounterboreSinksACapHeadBelowTheSurface()
    {
        var cutter = HoleCutter.Build(new HoleOptions { Size = "M5", Head = HoleHead.Counterbored, ExtraClearance = 0f }, 15f)!;

        Closed(cutter);
        Assert.Equal((8.5f + 1f) / 2f, Reach(cutter, -5.5f), 3);
    }

    [Fact]
    public void ANutPocketIsAHexagonAtTheFarEnd()
    {
        var cutter = HoleCutter.Build(new HoleOptions { Size = "M3", Head = HoleHead.Plain, NutPocket = true, ExtraClearance = 0f }, 10f);

        Assert.NotNull(cutter);
        Closed(cutter);

        // Across the corners of a 5.5 mm nut, at the bottom; the plain shaft above it.
        var bottom = cutter.ComputeBounds();
        Assert.Equal(-10f - HoleCutter.Overshoot, bottom.Min.Z, 2);
        float corners = cutter.Positions.Where(p => p.Z < -9f).Max(p => new Vector2(p.X, p.Y).Length());
        Assert.Equal(5.5f / MathF.Sqrt(3f), corners, 2);
    }

    [Fact]
    public void AnInsertPocketIsTheInsertsHoleAndALittleDeeperThanItIsLong()
    {
        var options = new HoleOptions { Kind = HoleKind.Insert, Size = "M3", ExtraClearance = 1f };
        var cutter = HoleCutter.Build(options, 50f)!;

        Closed(cutter);
        Assert.Equal(-(5.7f + 1f), cutter.ComputeBounds().Min.Z, 3);
        Assert.Equal(4f / 2f, Reach(cutter, cutter.ComputeBounds().Min.Z), 3);
    }

    [Fact]
    public void AHoleTooShallowForItsHeadIsMadeDeepEnough()
    {
        float depth = HoleCutter.DepthOf(new HoleOptions { Size = "M8", Head = HoleHead.Counterbored, NutPocket = true }, 2f);

        Assert.True(depth >= 8f + 1.5f + 6.8f, $"{depth} mm is not room for the head and the nut");
    }

    [Fact]
    public void ACutterTakesACleanHoleOutOfABlock()
    {
        var block = MeshTransform.Transformed(Primitives.Box(30, 30, 10), Matrix4x4.CreateTranslation(0, 0, 5));
        var cutter = MeshTransform.Transformed(
            HoleCutter.Build(new HoleOptions { Size = "M3", Head = HoleHead.Countersunk }, 11f)!,
            Matrix4x4.CreateTranslation(0, 0, 10));

        var drilled = LocalCsg.Subtract(block, cutter);

        Closed(drilled);
        Assert.True(drilled.ComputeSignedVolume() < block.ComputeSignedVolume() - 100, "hardly anything was taken out");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheHolePanelOpensForAPartOrForACutter(bool intoAPart) => RunSta(() =>
    {
        int previews = 0;
        var dialog = new HoleDialog(new HoleOptions(), 0f, 0f, intoAPart ? "Block" : null, (options, _, _, _) =>
        {
            if (options is not null) previews++;
            return true;
        });

        Assert.Equal(1, previews);
        Assert.Equal(intoAPart, dialog.Through);
        dialog.Close();
    });

    private static void RunSta(Action body)
    {
        ExceptionDispatchInfo? error = null;
        var thread = new Thread(() =>
        {
            try { body(); }
            catch (Exception ex) { error = ExceptionDispatchInfo.Capture(ex); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        error?.Throw();
    }

    // --- Overhangs ---------------------------------------------------------------------

    private static Mesh Box(Matrix4x4 place) => MeshTransform.Transformed(Primitives.Box(20, 20, 20), place);

    [Fact]
    public void ABoxOnTheBedHasNoOverhangs()
    {
        var onTheBed = Box(Matrix4x4.CreateTranslation(0, 0, 10));

        Assert.Equal(0, Overhangs.Faces(onTheBed, 45f).TriangleCount);
    }

    [Fact]
    public void AFloatingBoxOverhangsWithItsWholeUnderside()
    {
        var floating = Box(Matrix4x4.CreateTranslation(0, 0, 30));

        Assert.Equal(2, Overhangs.Faces(floating, 45f).TriangleCount);
        Assert.Equal(400.0, Overhangs.Area(floating, 45f), 1);
    }

    /// <summary>
    /// Tipped 30 degrees about X and lifted clear, its underside leans 60 degrees from upright and
    /// one side 30: at 45 only the underside counts, at 25 the side does too.
    /// </summary>
    [Fact]
    public void AFaceCountsOnlyWhenItLeansFurtherThanTheAngle()
    {
        var tipped = Box(Matrix4x4.CreateRotationX(MathF.PI / 6f) * Matrix4x4.CreateTranslation(0, 0, 50));

        Assert.Equal(400.0, Overhangs.Area(tipped, 45f), 1);
        Assert.Equal(800.0, Overhangs.Area(tipped, 25f), 1);
    }
}
