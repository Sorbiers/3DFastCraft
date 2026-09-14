using System.Numerics;
using FastCraft3D.Geometry;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>
/// Taper narrows or widens a solid towards the top; Bend curves it over. Both stay solids.
/// </summary>
public class TaperBendTests
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

    private static Mesh Column() =>
        MeshTransform.Transformed(Primitives.Box(20, 20, 60), Matrix4x4.CreateTranslation(0, 0, 30));

    [Theory]
    [InlineData(0.5f)]
    [InlineData(0.05f)]
    [InlineData(2.5f)]
    public void ATaperedColumnIsClosedWithTheBottomKeptAndTheTopScaled(float top)
    {
        var tapered = MeshDeform.Taper(Column(), top);

        Assert.True(tapered.CheckHealth().IsWatertight, tapered.CheckHealth().Describe());

        var bottomRing = tapered.Positions.Where(p => p.Z < 0.01f).ToList();
        var topRing = tapered.Positions.Where(p => p.Z > 59.99f).ToList();
        Assert.Equal(20f, bottomRing.Max(p => p.X) - bottomRing.Min(p => p.X), 0.01f);
        Assert.Equal(20f * top, topRing.Max(p => p.X) - topRing.Min(p => p.X), 0.01f);

        // A square tapering linearly is a frustum; its volume is known exactly.
        double a = 400, b = 400 * top * top;
        double frustum = 60.0 / 3.0 * (a + b + Math.Sqrt(a * b));
        Assert.Equal(frustum, Volume(tapered), frustum * 0.01);
    }

    [Fact]
    public void NoTaperLeavesTheSolidAlone()
    {
        var column = Column();
        Assert.Same(column, MeshDeform.Taper(column, 1f));
    }

    [Theory]
    [InlineData(45f, Axis.X)]
    [InlineData(90f, Axis.X)]
    [InlineData(-90f, Axis.Y)]
    public void ABentColumnIsClosedAndKeepsItsVolume(float degrees, Axis towards)
    {
        var column = Column();
        var bent = MeshDeform.Bend(column, degrees, towards);

        Assert.True(bent.CheckHealth().IsWatertight, bent.CheckHealth().Describe());

        // The middle keeps its length, the outside stretches as much as the inside squeezes.
        Assert.Equal(Volume(column), Volume(bent), Volume(column) * 0.01);
    }

    [Fact]
    public void BentAQuarterTurnTheTopEndsUpLyingOnItsSide()
    {
        var bent = MeshDeform.Bend(Column(), 90f, Axis.X);

        // The centre line is 60 mm long and bends round a quarter circle of radius 60 / (pi / 2).
        // The top face comes round to stand upright at that distance along X, facing +X, its outer
        // edge - the side away from the bend - that far up plus half the column's width.
        float radius = 60f / (MathF.PI / 2f);

        var bounds = bent.ComputeBounds();
        Assert.Equal(radius, bounds.Max.X, 0.1f);
        Assert.Equal(radius + 10f, bounds.Max.Z, 0.1f);
        Assert.Contains(bent.Positions, p => Vector3.Distance(p, new Vector3(radius, 10f, radius + 10f)) < 0.05f);

        // The bottom has not moved.
        Assert.Contains(bent.Positions, p => Vector3.Distance(p, new Vector3(10, 10, 0)) < 1e-3f);
    }

    [Fact]
    public void ABendTooTightForTheThicknessIsRefusedRatherThanFolded()
    {
        var squat = MeshTransform.Transformed(Primitives.Box(40, 40, 10), Matrix4x4.CreateTranslation(0, 0, 5));

        Assert.True(MeshDeform.Folds(squat, 90f, Axis.X));
        Assert.Same(squat, MeshDeform.Bend(squat, 90f, Axis.X));

        float largest = MeshDeform.LargestBend(squat, Axis.X);
        Assert.False(MeshDeform.Folds(squat, largest, Axis.X));
        Assert.True(MeshDeform.Bend(squat, largest, Axis.X).CheckHealth().IsWatertight);
    }
}
