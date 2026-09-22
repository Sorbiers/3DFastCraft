using System.Numerics;
using FastCraft3D.Geometry;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>
/// Cutting a solid into a web of struts along the walls of a Voronoi tessellation. Everything is
/// decided on a voxel grid and extracted from it, so the two things worth pinning down are that
/// what comes out is printable and that it is still the shape it went in as.
/// </summary>
public class VoronoiTests
{
    /// <summary>A ball 40 mm across, which has no flat faces to make the answer easy.</summary>
    private static Mesh Ball() => Primitives.Sphere(20f, 32, 16);

    /// <summary>Coarse enough to run in a test, fine enough to hold a 2 mm strut.</summary>
    private static VoronoiOptions Web => new(VoronoiKind.Shell, 24, 2f, 2.5f, 0f, 64);

    [Fact]
    public void AWebComesOutWatertight()
    {
        var result = Voronoi.Build(Ball(), Web);

        Assert.Null(result.Refusal);
        Assert.True(result.Mesh.TriangleCount > 0);
        Assert.True(result.Mesh.CheckHealth().IsWatertight, result.Mesh.CheckHealth().Describe());
    }

    /// <summary>
    /// The point of it: most of the material goes. A ball of this size is 33.5 cm3 solid, and a
    /// web of two dozen cells keeps a small fraction of that.
    /// </summary>
    [Fact]
    public void AWebIsMostlyHoles()
    {
        var ball = Ball();
        var result = Voronoi.Build(ball, Web);

        double solid = ball.ComputeSignedVolume();
        double web = result.Mesh.ComputeSignedVolume();

        Assert.True(web > 0, "there should be something left");
        Assert.True(web < solid / 3.0, $"the web keeps {web / solid:P0} of the ball - that is not a web");
    }

    [Fact]
    public void AWebStaysInsideTheShapeItCameFrom()
    {
        var ball = Ball();
        var result = Voronoi.Build(ball, Web);

        var was = ball.ComputeBounds();
        var now = result.Mesh.ComputeBounds();

        // A voxel of slack: the surface is extracted on the grid, not off the original.
        float slack = result.VoxelMm * 1.5f;

        Assert.True(now.Min.X >= was.Min.X - slack && now.Max.X <= was.Max.X + slack, "it grew across X");
        Assert.True(now.Min.Y >= was.Min.Y - slack && now.Max.Y <= was.Max.Y + slack, "it grew across Y");
        Assert.True(now.Min.Z >= was.Min.Z - slack && now.Max.Z <= was.Max.Z + slack, "it grew up Z");
    }

    /// <summary>
    /// Seeds are laid out by a fixed sequence rather than drawn at random, so printing one half
    /// today and the other half next week gives two halves of the same object.
    /// </summary>
    [Fact]
    public void TheSameModelGivesTheSameWebTwice()
    {
        var first = Voronoi.Build(Ball(), Web);
        var second = Voronoi.Build(Ball(), Web);

        Assert.Equal(first.Mesh.TriangleCount, second.Mesh.TriangleCount);
        Assert.Equal(first.Mesh.Positions[0], second.Mesh.Positions[0]);
        Assert.Equal(first.Cells, second.Cells);
    }

    [Fact]
    public void MoreCellsMakeMoreStruts()
    {
        var few = Voronoi.Build(Ball(), Web with { Cells = 8 });
        var many = Voronoi.Build(Ball(), Web with { Cells = 40 });

        Assert.Null(few.Refusal);
        Assert.Null(many.Refusal);

        // More walls between more cells is more strut, so more material and more surface.
        Assert.True(many.Mesh.ComputeSignedVolume() > few.Mesh.ComputeSignedVolume(),
            "forty cells should leave more strut than eight");
    }

    [Fact]
    public void ASolidBaseLeavesTheBottomWhole()
    {
        var bare = Voronoi.Build(Ball(), Web);
        var footed = Voronoi.Build(Ball(), Web with { BaseMm = 6f });

        Assert.Null(footed.Refusal);
        Assert.True(footed.Mesh.ComputeSignedVolume() > bare.Mesh.ComputeSignedVolume(),
            "a solid base is material the bare web does not have");
        Assert.True(footed.Mesh.CheckHealth().IsWatertight);
    }

    /// <summary>A lattice keeps its face and fills the inside with struts instead of emptying it.</summary>
    [Fact]
    public void ALatticeIsHeavierThanAShellAndLighterThanTheSolid()
    {
        var ball = Ball();
        var shell = Voronoi.Build(ball, Web);
        var lattice = Voronoi.Build(ball, Web with { Kind = VoronoiKind.Lattice });

        Assert.Null(lattice.Refusal);
        Assert.True(lattice.Mesh.CheckHealth().IsWatertight, lattice.Mesh.CheckHealth().Describe());

        double solid = ball.ComputeSignedVolume();
        double foam = lattice.Mesh.ComputeSignedVolume();

        Assert.True(foam > shell.Mesh.ComputeSignedVolume(), "a skin over a foam is more than a bare web");
        Assert.True(foam < solid * 0.9, "a lattice that keeps nine tenths of the material is not a lattice");
    }

    /// <summary>
    /// A lattice is the cells' edges, not their walls, and the difference is not subtle. Walls
    /// through a volume are plates that fill it - a cell has fourteen of them - so building a
    /// lattice on walls came back three quarters solid: a block with bubbles rather than a
    /// lattice. On edges the same settings leave most of the material out.
    /// </summary>
    [Fact]
    public void ALatticeIsBuiltOnTheCellsEdgesRatherThanTheirWalls()
    {
        var result = Voronoi.Build(Ball(), Web with { Kind = VoronoiKind.Lattice, SkinMm = 0f });

        Assert.Null(result.Refusal);
        Assert.True(result.Kept < 0.35f,
            $"a lattice that keeps {result.Kept:P0} of the ball is a block with holes in it");
    }

    /// <summary>
    /// The share left is the one number that says whether the settings made a web or a block, so
    /// it comes back with the result rather than having to be measured off the mesh afterwards.
    /// </summary>
    [Fact]
    public void TheResultSaysHowMuchMaterialIsLeft()
    {
        var ball = Ball();
        var result = Voronoi.Build(ball, Web);

        Assert.InRange(result.Kept, 0.01f, 0.5f);

        // Counted on the grid, so it agrees with the mesh to within what the extraction rounds.
        double measured = result.Mesh.ComputeSignedVolume() / ball.ComputeSignedVolume();
        Assert.Equal(measured, result.Kept, 0.08);
    }

    [Fact]
    public void AFatterStrutLeavesMoreMaterialBehind()
    {
        var thin = Voronoi.Build(Ball(), Web with { Kind = VoronoiKind.Lattice, SkinMm = 0f, StrutMm = 1.5f });
        var fat = Voronoi.Build(Ball(), Web with { Kind = VoronoiKind.Lattice, SkinMm = 0f, StrutMm = 4f });

        Assert.True(fat.Kept > thin.Kept * 1.5f,
            $"{thin.Kept:P0} against {fat.Kept:P0} - the strut is not doing anything");
    }

    /// <summary>
    /// Two voxels meeting only along an edge are a shape no surface can wrap manifold, and a
    /// lattice makes them over and over: its struts cross at angles and a pair passing close
    /// leaves exactly that. It came back with nine non-manifold edges in three quarters of a
    /// million triangles, which the healer cannot mend either - there is no hole to fill.
    /// </summary>
    [Fact]
    public void ALatticeComesOutWatertightLikeEverythingElse()
    {
        var thick = new VoronoiOptions(VoronoiKind.Lattice, 60, 1.2f, 0f, 0f, 96);
        var result = Voronoi.Build(Ball(), thick);

        Assert.Null(result.Refusal);
        Assert.True(result.Mesh.CheckHealth().IsWatertight, result.Mesh.CheckHealth().Describe());
    }

    // --- The preview -------------------------------------------------------------------------

    /// <summary>
    /// The outline is the edge the solid will actually have, so the two have to agree: every
    /// point on it should sit on the model's surface, and there should be a good deal of it.
    /// </summary>
    [Fact]
    public void ThePreviewDrawsTheEdgeTheWebWillHave()
    {
        var ball = Ball();
        var outline = Voronoi.Outline(ball, Web);

        Assert.NotEmpty(outline);

        foreach (var (from, to) in outline)
        {
            // On the ball, within what a flat triangle cuts off its curve.
            Assert.InRange(from.Length(), 19f, 20.2f);
            Assert.InRange(to.Length(), 19f, 20.2f);
        }
    }

    [Fact]
    public void ThePreviewFollowsTheSameSeedsAsTheBuild()
    {
        var ball = Ball();

        // Same options, same seeds, so the preview cannot draw one web and the build make another.
        var first = Voronoi.Outline(ball, Web);
        var second = Voronoi.Outline(ball, Web);

        Assert.Equal(first.Count, second.Count);
        Assert.Equal(first[0], second[0]);

        // And a different strut moves the edge, since that is what the level is.
        Assert.NotEqual(first.Count, Voronoi.Outline(ball, Web with { StrutMm = 5f }).Count);
    }

    /// <summary>
    /// A box is twelve triangles. Contouring those alone would draw a couple of straight lines
    /// across it and call that a web, so a triangle much larger than a strut is split first.
    /// </summary>
    [Fact]
    public void ACoarseModelStillGetsAProperOutline()
    {
        var box = Primitives.Box(40, 40, 40);
        var outline = Voronoi.Outline(box, Web);

        Assert.True(outline.Count > 100, $"twelve triangles gave {outline.Count} segments");
    }

    [Fact]
    public void MoreCellsDrawMoreOutline()
    {
        var ball = Ball();

        Assert.True(Voronoi.Outline(ball, Web with { Cells = 40 }).Count
                  > Voronoi.Outline(ball, Web with { Cells = 8 }).Count);
    }

    // --- What it refuses -------------------------------------------------------------------

    [Fact]
    public void AStrutTheGridCannotHoldIsRefusedRatherThanCutBadly()
    {
        // 40 mm across at 40 voxels is a 1 mm voxel; a 0.8 mm strut falls between them.
        var result = Voronoi.Build(Ball(), Web with { StrutMm = 0.8f, Resolution = 40 });

        Assert.Equal(0, result.Mesh.TriangleCount);
        Assert.Contains("under two voxels", result.Refusal);
        Assert.Contains("Raise the detail", result.Refusal);
    }

    [Fact]
    public void AWebWithNoDepthToItIsRefused()
    {
        var result = Voronoi.Build(Ball(), Web with { SkinMm = 0f });

        Assert.Equal(0, result.Mesh.TriangleCount);
        Assert.Contains("nothing left of it", result.Refusal);
    }

    [Fact]
    public void AnEmptyMeshIsRefusedRatherThanThrowing()
    {
        var result = Voronoi.Build(new Mesh(), Web);

        Assert.Equal(0, result.Mesh.TriangleCount);
        Assert.NotNull(result.Refusal);
    }

    /// <summary>The resolution a strut asks for, which the panel shows before anyone commits to it.</summary>
    [Fact]
    public void TheDetailAStrutNeedsIsThreeVoxelsAcrossIt()
    {
        // A 1.5 mm strut on a 120 mm lamp wants a 0.5 mm voxel: 240 along the side.
        Assert.Equal(240, Voronoi.WantedResolution(120f, 1.5f));

        // And it never asks for more than the grid will take.
        Assert.Equal(VoxelRebuild.MaximumResolution, Voronoi.WantedResolution(400f, 0.8f));
    }

    [Fact]
    public void TheOptionsHoldThemselvesToWhatCanBeBuilt()
    {
        var mad = new VoronoiOptions(VoronoiKind.Shell, -5, 0.01f, -3f, -1f, 5000).Sane();

        Assert.Equal(2, mad.Cells);
        Assert.Equal(Voronoi.LeastStrutMm, mad.StrutMm);
        Assert.Equal(0f, mad.SkinMm);
        Assert.Equal(0f, mad.BaseMm);
        Assert.Equal(VoxelRebuild.MaximumResolution, mad.Resolution);
    }
}
