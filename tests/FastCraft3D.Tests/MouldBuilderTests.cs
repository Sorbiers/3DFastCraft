using FastCraft3D.Geometry;
using FastCraft3D.Geometry.Moulding;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>
/// Building the mould itself: a block, the model taken out of it, a hole to pour through, and flat
/// cuts. What these check is that the plain arrangement came out right - the pieces, the cavity,
/// the keys and the hole - and that what did not come out right is said so rather than handed over.
/// </summary>
public class MouldBuilderTests
{
    private static Mesh Ball() => Primitives.Sphere(10, 32, 16);

    private static MouldResult Mould(Mesh model, MouldOptions? options = null) =>
        MouldBuilder.Build(model, MouldAnalysis.Study(model, 40), options ?? MouldOptions.Default);

    private static double Volume(Mesh mesh) => Math.Abs(mesh.ComputeSignedVolume());

    private static double Block(Mesh model)
    {
        var span = model.ComputeBounds().Size
                 + new System.Numerics.Vector3(MouldOptions.Default.Wall * 2f);

        return span.X * span.Y * span.Z;
    }

    [Fact]
    public void OneCutGivesTwoParts() => Assert.Equal(2, Mould(Ball()).Parts.Count);

    [Fact]
    public void ALightModelIsCutExactly() => Assert.Contains("cut exactly", Mould(Ball()).Summary);

    /// <summary>
    /// Weight no longer decides the route.
    ///
    /// It used to: the cavity was the block with the model subtracted from it, the boolean is
    /// quadratic, and a scan ran for twenty minutes and twenty gigabytes before it had to be
    /// killed. The cavity is now a block with the model inside it turned inside out - two shells,
    /// no arithmetic - so nothing about it grows with the model. A ring of sixteen thousand
    /// triangles is well past the twenty thousand the old limit would have allowed once the
    /// block is counted, and it is cut exactly, and quickly.
    /// </summary>
    [Fact]
    public void AModelPastTheOldLimitIsCutExactly()
    {
        var ring = Primitives.Torus(20, 7, 128, 64);
        var mould = MouldBuilder.Build(ring, MouldAnalysis.Study(ring, 48), MouldOptions.Default);

        Assert.Contains("cut exactly", mould.Summary);
        Assert.All(mould.Parts, p => Assert.True(p.Watertight, $"{p.Name} is torn"));
    }

    /// <summary>
    /// What is sampled instead is a model that will not close.
    ///
    /// The cavity is the model turned inside out within the block, and a surface with a hole in it
    /// has no inside for the void to be. The grid does not care - it asks whether points are in
    /// the material and builds a surface from the answers - so a torn scan still gets a mould, at
    /// the cost of the detail the sampling drops. Which route was taken is in the summary, because
    /// the two do not give the same thing.
    /// </summary>
    [Fact]
    public void AModelThatWillNotCloseIsSampledInstead()
    {
        var whole = Primitives.Sphere(20, 32, 16);

        // The same ball with one triangle missing.
        var torn = new Mesh();
        for (int t = 3; t + 2 < whole.Indices.Count; t += 3)
            torn.AddTriangle(whole.Positions[whole.Indices[t]],
                whole.Positions[whole.Indices[t + 1]], whole.Positions[whole.Indices[t + 2]]);

        Assert.False(torn.CheckHealth().IsWatertight, "the model should not be closed");

        var mould = MouldBuilder.Build(
            torn, MouldAnalysis.Study(torn, 48), MouldOptions.Default with { Resolution = 128 });

        Assert.Contains("sampled to", mould.Summary);
    }

    [Fact]
    public void BothHalvesWouldPrint()
    {
        Assert.All(Mould(Ball()).Parts, p => Assert.True(p.Watertight, $"{p.Name} is torn"));
    }

    /// <summary>
    /// What was taken out of the block is the model, give or take the pour hole. If this drifts,
    /// the cavity is not the shape anybody asked to cast.
    /// </summary>
    [Fact]
    public void WhatIsMissingFromTheBlockIsTheModel()
    {
        var model = Ball();
        var mould = Mould(model);

        double removed = Block(model) - mould.Parts.Sum(p => Volume(p.Mesh));

        // At least the model, and not much more: the rest is the pour hole and the difference
        // between a dome on one face and the slightly wider socket in the other.
        Assert.InRange(removed, Volume(model), Volume(model) * 1.6);
    }

    /// <summary>
    /// The exact version of the same thing. With nothing drilled and no keys, the pieces put back
    /// together are the block less the model and nothing else - which a boolean can say to within
    /// a fraction of a per cent, and which is the whole reason the cavity is cut this way.
    /// </summary>
    [Fact]
    public void TheCavityIsExactlyTheModel()
    {
        var model = Ball();
        var mould = Mould(model, MouldOptions.Default with
        {
            SprueRadius = 0f, AddVents = false, KeyRadius = 0f
        });

        double removed = Block(model) - mould.Parts.Sum(p => Volume(p.Mesh));

        Assert.Equal(Volume(model), removed, Volume(model) * 0.01);
    }

    /// <summary>The faces key into each other, and only one way round: one gains, the other loses.</summary>
    [Fact]
    public void OneFaceCarriesTheDomesAndTheOtherTheSockets()
    {
        // Each half against its own keyless self rather than against the other half, so what is
        // measured is the keys and not the difference between the halves.
        var bare = MouldOptions.Default with { SprueRadius = 0f, AddVents = false };

        var plain = Mould(Ball(), bare with { KeyRadius = 0f });
        var keyed = Mould(Ball(), bare);

        double first = Volume(keyed.Parts[0].Mesh) - Volume(plain.Parts[0].Mesh);
        double second = Volume(keyed.Parts[1].Mesh) - Volume(plain.Parts[1].Mesh);

        Assert.True(Math.Max(first, second) > 300, $"neither face gained: {first:0}, {second:0}");
        Assert.True(Math.Min(first, second) < -300, $"neither face lost: {first:0}, {second:0}");
    }

    /// <summary>
    /// The faces that seal have to be flat, and a plane split gives exactly that - every vertex of
    /// the cut face on one plane, not most of them near it. Building the block on a voxel grid
    /// instead left seventy per cent of the face planar and rolled the border off by a fifth of a
    /// millimetre, which is a seam to weep through.
    /// </summary>
    [Fact]
    public void ThePartingFaceIsFlat()
    {
        var model = Ball();
        var study = MouldAnalysis.Study(model, 40) with { Cuts = [new MouldCut(Axis.Z, 0f)] };

        var mould = MouldBuilder.Build(model, study, MouldOptions.Default with
        {
            KeyRadius = 0f, SprueRadius = 0f, AddVents = false
        });

        foreach (var part in mould.Parts)
        {
            var box = part.Mesh.ComputeBounds();

            // Of the piece's two ends, the one at the cut is the face that has to seal.
            float face = MathF.Abs(box.Max.Z) < MathF.Abs(box.Min.Z) ? box.Max.Z : box.Min.Z;
            Assert.Equal(0f, face, 1e-3f);

            var onFace = part.Mesh.Positions.Where(p => MathF.Abs(p.Z - face) < 0.25f).ToList();
            int flat = onFace.Count(p => MathF.Abs(p.Z - face) < 1e-4f);

            // All but a handful: within a quarter of a millimetre of the cut there are also a few
            // vertices of the cavity wall, which are near the plane without being on it.
            Assert.True(onFace.Count > 20, $"{part.Name} has only {onFace.Count} face vertices");
            Assert.True(flat > onFace.Count - 8,
                $"{onFace.Count - flat} of {part.Name}'s {onFace.Count} face vertices are off the plane");
        }
    }

    [Fact]
    public void TwoCutsGiveFourParts()
    {
        var mould = FourPart();

        Assert.Equal(4, mould.Parts.Count);
        Assert.Equal(4, mould.Parts.Select(p => p.Name).Distinct().Count());
    }

    /// <summary>
    /// Four pieces with a pour hole is where the engine starts to give out, and the honest thing
    /// is to name the pieces that came back torn rather than hand over four and hope.
    ///
    /// The second cut has to pass through the bore, and a cylinder breaking out through a curved
    /// cavity wall is the case this engine handles worst. Rearranging the build - the bores joining
    /// the model rather than being drilled out of the cavity, the cuts before the keys, the keys a
    /// hair off their plane - took it from three torn in four down to at most one. A torn piece
    /// goes through Rebuild on the Edit tab and comes back printable.
    /// </summary>
    [Fact]
    public void ATornPieceIsNamedRatherThanQuietlyHandedOver()
    {
        var mould = FourPart();
        int torn = mould.Parts.Count(p => !p.Watertight);

        Assert.True(torn <= 1, $"{torn} of four pieces are torn");

        if (torn == 0) Assert.Contains("all watertight", mould.Summary);
        else Assert.Contains("Rebuild", mould.Summary);
    }

    private static MouldResult FourPart()
    {
        var model = Ball();
        var study = MouldAnalysis.Study(model, 40) with
        {
            Cuts = [new MouldCut(Axis.Z, 0f), new MouldCut(Axis.X, 4f)]
        };

        return MouldBuilder.Build(model, study, MouldOptions.Default);
    }

    /// <summary>The pour hole has to reach the cavity, or there is no way to get silicone in.</summary>
    [Fact]
    public void ThePourHoleTakesMaterialOutOfTheTop()
    {
        var model = Ball();

        var withHole = Mould(model);
        var without = Mould(model, MouldOptions.Default with { SprueRadius = 0f, AddVents = false });

        double drilled = without.Parts.Sum(p => Volume(p.Mesh))
                       - withHole.Parts.Sum(p => Volume(p.Mesh));

        // A 10 mm hole through the 8 mm of wall above the ball, at least.
        Assert.True(drilled > Math.PI * 25 * 8 * 0.7, $"the pour hole only removed {drilled:0} mm3");
    }

    [Fact]
    public void NothingIsMadeFromAnEmptyMesh()
    {
        var mould = MouldBuilder.Build(
            new Mesh(), new MouldStudy([], [], default, [], ""), MouldOptions.Default);

        Assert.Empty(mould.Parts);
    }

    /// <summary>It is a run of booleans on a block, so it has to be possible to give up on it.</summary>
    [Fact]
    public void TheBuildCanBeAborted()
    {
        var model = Ball();
        var study = MouldAnalysis.Study(model, 40);

        var source = new CancellationTokenSource();
        source.Cancel();

        Assert.Throws<OperationCanceledException>(
            () => MouldBuilder.Build(model, study, MouldOptions.Default, source.Token));
    }
}
