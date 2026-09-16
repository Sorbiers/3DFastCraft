using System.Numerics;
using FastCraft3D.Geometry;
using FastCraft3D.Geometry.Csg;
using FastCraft3D.Geometry.Engraving;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>
/// Brick studs as a connector between two halves, the fit test plates, and the filament defaults.
/// </summary>
public class BrickConnectorTests
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

    private static ConnectorOptions Bricks => ConnectorOptions.Default with { Style = ConnectorStyle.Bricks };

    [Fact]
    public void FilamentPrintersStartATenthTight()
    {
        Assert.Equal(-0.1f, EngraveOptions.Default.StudFit);
        Assert.Equal(-0.1f, ConnectorOptions.Default.BrickFit);
    }

    [Theory]
    [InlineData(40f, 40f)]
    [InlineData(30f, 55f)]
    public void ASplitBlockGetsStudsBelowAndSocketsAboveThatDoNotCollide(float width, float depth)
    {
        var block = MeshTransform.Transformed(Primitives.Box(width, depth, 30f), Matrix4x4.CreateTranslation(0, 0, 15f));
        var (top, bottom) = PlaneSplit.Split(block, Axis.Z, 15f, SplitKeep.Both);

        var joined = Connectors.JoinBricks(top!, bottom!, Vector3.UnitZ, Bricks);

        Assert.NotNull(joined);
        var (front, back, studs) = joined.Value;
        Assert.True(studs > 0, "no stud fitted on the cut");
        Assert.True(front.CheckHealth().IsWatertight, front.CheckHealth().Describe());
        Assert.True(back.CheckHealth().IsWatertight, back.CheckHealth().Describe());

        // The studs stand up out of the lower half; the upper half is hollowed to take them.
        Assert.Equal(15f + BrickStuds.StudHeight, back.ComputeBounds().Max.Z, 0.01f);
        Assert.True(Volume(front) < Volume(top!) - 1.0, "the upper half was not hollowed");

        // Put back together, nothing of one half is inside the other: every stud has its socket,
        // and no tube lands on a stud. The same grid on both faces is what makes this so.
        var overlap = ManifoldCsg.Intersect(front, back);
        Assert.NotNull(overlap);
        Assert.True(Math.Abs(Volume(overlap)) < 0.05, $"the halves overlap by {Volume(overlap):0.###} mm3");
    }

    /// <summary>
    /// What the viewport marks on the cut while the settings are chosen: the same places the tool
    /// will use, read off the section without cutting anything.
    /// </summary>
    [Fact]
    public void ThePreviewMarksWhereTheConnectorsWillGo()
    {
        var block = MeshTransform.Transformed(Primitives.Box(40f, 40f, 40f), Matrix4x4.CreateTranslation(0, 0, 20f));

        var pins = Connectors.Preview(block, Vector3.UnitZ, 20f, ConnectorOptions.Default);
        Assert.Equal(ConnectorOptions.Default.Count, pins.Count);
        Assert.All(pins, mark => Assert.Equal(20f, mark.At.Z, 0.01f));
        Assert.All(pins, mark => Assert.Equal(ConnectorOptions.Default.Radius + ConnectorOptions.Default.Clearance, mark.Radius, 0.01f));

        // Studs stand on the same grid the tool lays out, so the marks and the studs agree.
        var marks = Connectors.Preview(block, Vector3.UnitZ, 20f, Bricks);
        var (_, _, studs) = Connectors.JoinBricks(
            PlaneSplit.Split(block, Axis.Z, 20f, SplitKeep.Both).Front!,
            PlaneSplit.Split(block, Axis.Z, 20f, SplitKeep.Both).Back!,
            Vector3.UnitZ, Bricks)!.Value;

        Assert.Equal(studs, marks.Count);
        Assert.All(marks, mark => Assert.Equal(BrickStuds.StudRadius(Bricks.BrickFit), mark.Radius, 0.01f));
    }

    /// <summary>
    /// Connect objects marks its joint too. Each part is read a little inside its own face rather
    /// than on the plane between them: a section taken exactly on a face comes back as the whole of
    /// it or nothing at all, and nothing is what it came back as.
    /// </summary>
    [Fact]
    public void ThePreviewMarksAJointBetweenTwoParts()
    {
        var below = MeshTransform.Transformed(Primitives.Box(80f, 80f, 30f), Matrix4x4.CreateTranslation(0, 0, 15f));
        var above = MeshTransform.Transformed(Primitives.Box(80f, 80f, 30f), Matrix4x4.CreateTranslation(0, 0, 45f));
        var roof = MeshTransform.Transformed(Primitives.Wedge(80f, 80f, 25f), Matrix4x4.CreateTranslation(0, 0, 42.5f));

        var flat = Connectors.SharedFace(above.ComputeBounds(), below.ComputeBounds());
        var sloping = Connectors.SharedFace(roof.ComputeBounds(), below.ComputeBounds());
        Assert.NotNull(flat);
        Assert.NotNull(sloping);

        var onFlat = Connectors.Preview(above, below, flat, Bricks);
        var onSlope = Connectors.Preview(roof, below, sloping, Bricks);

        Assert.NotEmpty(onFlat);
        Assert.All(onFlat, mark => Assert.Equal(30f, mark.At.Z, 0.01f));

        // The roof takes fewer, and none out at the thin end where its socket would break through.
        Assert.True(onSlope.Count < onFlat.Count, $"{onSlope.Count} marks under a roof, {onFlat.Count} under a block");
        Assert.True(onSlope.Max(mark => mark.At.X) < onFlat.Max(mark => mark.At.X));
    }

    /// <summary>
    /// A part that thins out above the cut - a roof over a wall - takes studs only where there is
    /// still material a socket and a roof deep. Placed on the cut face alone, the pockets came out
    /// through the slope and the studs showed through the roof with them.
    /// </summary>
    [Fact]
    public void NoStudGoesWhereTheSocketWouldBreakThroughASlope()
    {
        var slab = MeshTransform.Transformed(Primitives.Box(80f, 80f, 10f), Matrix4x4.CreateTranslation(0, 0, 5f));
        // Twenty millimetres thick at x = -40 and nothing at x = +40, so it thins by a millimetre
        // every four along: below x = 28 there is less than a socket and a roof.
        var roof = MeshTransform.Transformed(Primitives.Wedge(80f, 80f, 20f), Matrix4x4.CreateTranslation(0, 0, 20f));

        var joined = Connectors.JoinBricks(roof, slab, Vector3.UnitZ, Bricks);

        Assert.NotNull(joined);
        var (front, back, studs) = joined.Value;
        Assert.True(studs > 0, "no stud fitted under the thick end");

        // Where the studs stand: nothing above the cut belongs to the slab but them.
        float furthest = back.Positions.Where(p => p.Z > 10.5f).Max(p => p.X);
        Assert.True(furthest < 24f, $"a stud reaches x = {furthest:0.#}, where the roof is too thin for its socket");

        // And the pocket stops with them. A millimetre into the roof, the slope past the last stud
        // is still solid; cut to the whole face instead, the cavity came out through it in a row of
        // holes with the tubes inside showing through.
        var (_, rings) = PlaneClip.KeepOpen(front, Matrix4x4.Identity, Vector3.UnitZ, 11f);
        var shapes = Polygon2.Nest(rings.Select(ring => ring.Select(p => new Vector2(p.X, p.Y)).ToList()).ToList());
        var beyond = new Vector2(30f, 0f);

        Assert.True(
            shapes.Any(s => Polygon2.Contains(s.Outline, beyond) && !s.Holes.Any(h => Polygon2.Contains(h, beyond))),
            "the pocket broke out through the slope");

        var overlap = ManifoldCsg.Intersect(front, back);
        Assert.NotNull(overlap);
        Assert.True(Math.Abs(Volume(overlap)) < 0.05, $"the halves overlap by {Volume(overlap):0.###} mm3");

        // And nothing of the roof ends up outside the shape it started as. A tube stands in the
        // hollow and is as deep as it: one placed where the roof is thinner came out through the
        // slope, a row of round slivers along it.
        var out_ = ManifoldCsg.Subtract(front, roof);
        Assert.NotNull(out_);
        Assert.True(Math.Abs(Volume(out_)) < 0.05,
                    $"{Math.Abs(Volume(out_)):0.##} mm3 of the roof came out through its own surface");
    }

    /// <summary>
    /// A wall one stud wide gets a brick's own hollow - 8 mm less a wall each side - centred on the
    /// studs. Cut to the wall's own outline instead, a 10 mm wall came out hollowed 7.1 mm wide for
    /// a 4.65 mm stud, which located the halves and gripped nothing.
    /// </summary>
    [Fact]
    public void TheSocketInAWallOneStudWideIsAsWideAsABricksHollow()
    {
        var slab = MeshTransform.Transformed(Primitives.Box(80f, 80f, 10f), Matrix4x4.CreateTranslation(0, 0, 5f));
        var wall = LocalCsg.Subtract(
            MeshTransform.Transformed(Primitives.Box(80f, 80f, 40f), Matrix4x4.CreateTranslation(0, 0, 30f)),
            MeshTransform.Transformed(Primitives.Box(60f, 60f, 42f), Matrix4x4.CreateTranslation(0, 0, 30f)));

        var joined = Connectors.JoinBricks(wall, slab, Vector3.UnitZ, Bricks);

        Assert.NotNull(joined);
        var (front, back, studs) = joined.Value;
        Assert.True(studs > 0, "no stud fitted on a 10 mm wall");

        // The hollow follows the middle of the wall - 70 mm a side - a brick's hollow wide and a
        // socket deep. Hollowing the whole 10 mm wall instead would take half as much again.
        double middle = 4 * (80 - 10);
        double groove = BrickStuds.Pitch - 2 * BrickStuds.Wall(Bricks.BrickFit);
        double socket = BrickStuds.StudHeight + BrickStuds.SocketClearance;
        double taken = Volume(wall) - Volume(front);

        Assert.InRange(taken, middle * groove * socket * 0.8, middle * groove * socket * 1.2);

        var overlap = ManifoldCsg.Intersect(front, back);
        Assert.NotNull(overlap);
        Assert.True(Math.Abs(Volume(overlap)) < 0.05, $"the halves overlap by {Volume(overlap):0.###} mm3");
    }

    /// <summary>
    /// A wall a little over two studs and two brick walls wide - 7.8 mm - is the narrowest that
    /// takes a stud, and it has to: it is what a hollow model cut above its floor leaves.
    /// </summary>
    [Fact]
    public void AWallJustWideEnoughStillTakesStudsAndTheirSockets()
    {
        var slab = MeshTransform.Transformed(Primitives.Box(80f, 80f, 10f), Matrix4x4.CreateTranslation(0, 0, 5f));
        var wall = LocalCsg.Subtract(
            MeshTransform.Transformed(Primitives.Box(80f, 80f, 40f), Matrix4x4.CreateTranslation(0, 0, 30f)),
            MeshTransform.Transformed(Primitives.Box(64.4f, 64.4f, 42f), Matrix4x4.CreateTranslation(0, 0, 30f)));

        var joined = Connectors.JoinBricks(wall, slab, Vector3.UnitZ, Bricks);

        Assert.NotNull(joined);
        var (front, back, studs) = joined.Value;
        Assert.True(studs > 0, "no stud fitted on a 7.8 mm wall");
        Assert.True(Volume(front) < Volume(wall) - 1.0, "the wall was not hollowed for the studs");

        var overlap = ManifoldCsg.Intersect(front, back);
        Assert.NotNull(overlap);
        Assert.True(Math.Abs(Volume(overlap)) < 0.05, $"the halves overlap by {Volume(overlap):0.###} mm3");
    }

    /// <summary>
    /// The two halves of a cut do not always have the same face: a hollow model cut just above its
    /// floor leaves a wide ledge below and a thin wall above. Studs placed on the ledge alone met
    /// solid wall rather than a socket, and held the halves apart.
    /// </summary>
    [Fact]
    public void NoStudGoesWhereTheUpperHalfIsOnlyAThinWall()
    {
        var slab = MeshTransform.Transformed(Primitives.Box(80f, 80f, 10f), Matrix4x4.CreateTranslation(0, 0, 5f));
        var wall = LocalCsg.Subtract(
            MeshTransform.Transformed(Primitives.Box(80f, 80f, 40f), Matrix4x4.CreateTranslation(0, 0, 30f)),
            MeshTransform.Transformed(Primitives.Box(77f, 77f, 42f), Matrix4x4.CreateTranslation(0, 0, 30f)));

        var joined = Connectors.JoinBricks(wall, slab, Vector3.UnitZ, Bricks);

        Assert.NotNull(joined);
        Assert.Equal(0, joined.Value.Studs);
    }

    [Fact]
    public void ACutTooNarrowForAStudPlacesNone()
    {
        var bar = MeshTransform.Transformed(Primitives.Box(6f, 40f, 20f), Matrix4x4.CreateTranslation(0, 0, 10f));
        var (top, bottom) = PlaneSplit.Split(bar, Axis.Z, 10f, SplitKeep.Both);

        var joined = Connectors.JoinBricks(top!, bottom!, Vector3.UnitZ, Bricks);

        Assert.NotNull(joined);
        Assert.Equal(0, joined.Value.Studs);
    }

    [Fact]
    public void EachFitTestPlateIsClosedWithFourStudsAndItsOwnNotches()
    {
        var plates = BrickStuds.CouponFits.Select((fit, i) => BrickStuds.FitCoupon(fit, i + 1)).ToList();

        Assert.All(plates, p =>
        {
            Assert.NotNull(p);
            Assert.True(p.CheckHealth().IsWatertight, p.CheckHealth().Describe());
            Assert.Equal(BrickStuds.PlateHeight + BrickStuds.StudHeight, p.ComputeBounds().Max.Z, 0.01f);
        });

        // Tighter fits are fatter everywhere that grips, so they hold more plastic - less the notches,
        // which take away more the looser the plate.
        var volumes = plates.Select(p => Volume(p!)).ToList();
        Assert.True(volumes[0] < volumes[^1], $"{volumes[0]:0.#} at -0.2, {volumes[^1]:0.#} at +0.1");
    }
}
