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
