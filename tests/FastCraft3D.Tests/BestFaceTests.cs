using System.Numerics;
using FastCraft3D.Geometry;
using FastCraft3D.Geometry.Csg;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>
/// Which way up a part should be printed.
///
/// The shapes are the ones anybody would check by eye: a block with a peg on it belongs on its
/// base, an L goes flat on its side rather than standing on end, and a cup stands the right way
/// up. Each is built in the pose the answer has to argue with, so a tool that simply agreed with
/// however the part arrived would fail.
/// </summary>
public class BestFaceTests
{
    private static List<StandingChoice> Rank(Mesh world, FaceChoice favour = FaceChoice.LessSupport) =>
        BestFace.Rank(world, RestingFaces.Find(world), Overhangs.DefaultAngle, favour);

    private static Mesh At(Mesh mesh, float x, float y, float z) =>
        MeshTransform.Transformed(mesh, Matrix4x4.CreateTranslation(x, y, z));

    private static bool Faces(Vector3 normal, Vector3 way) =>
        Vector3.Dot(Vector3.Normalize(normal), way) > 0.99f;

    /// <summary>A 40 x 40 x 10 block with a 6 mm peg standing on it, itself standing on its base.</summary>
    private static Mesh BlockWithAPeg() =>
        LocalCsg.Union(At(Primitives.Box(40, 40, 10), 0, 0, 5), At(Primitives.Prism(3, 20, 24), 0, 0, 20));

    /// <summary>An L standing on end: a 60 mm upright with a 24 mm foot out of one side.</summary>
    private static Mesh LBracket() =>
        LocalCsg.Union(At(Primitives.Box(12, 12, 60), 0, 0, 30), At(Primitives.Box(24, 12, 12), 12, 0, 6));

    /// <summary>A cup with a 4 mm floor and 3 mm walls, upside down with its rim on the bed.</summary>
    private static Mesh CupOnItsRim() =>
        LocalCsg.Subtract(At(Primitives.Prism(20, 40, 32), 0, 0, 20), At(Primitives.Prism(17, 40, 32), 0, 0, 16));

    [Fact]
    public void ABlockWithAPegStaysOnItsBase()
    {
        var best = Rank(BlockWithAPeg())[0];

        Assert.True(best.IsCurrent, "it was already the right way up and the tool wanted to turn it");
        Assert.True(Faces(best.Normal, -Vector3.UnitZ), "the face picked is not the one on the bed");
        Assert.True(best.SupportMm2 < 10, $"{best.SupportMm2:0} mm2 of support for a block and a peg");
        Assert.True(best.FootprintMm2 > 1000, $"{best.FootprintMm2:0} mm2 is not the 40 x 40 base");
    }

    [Fact]
    public void AnLBracketIsLaidFlatRatherThanStoodOnEnd()
    {
        var choices = Rank(LBracket());
        var best = choices[0];
        var standing = choices.Single(c => c.IsCurrent);

        // Flat on its side is 12 mm of print instead of 60, and neither way needs support - which
        // is the whole point of the tie-break: two faces cost nothing and one takes five times as
        // long.
        Assert.False(best.IsCurrent, "it was left standing on end");
        Assert.Equal(12f, best.HeightMm, 1);
        Assert.Equal(60f, standing.HeightMm, 1);
        Assert.True(best.SupportMm2 < 10, $"{best.SupportMm2:0} mm2 of support for a flat plate");
    }

    [Fact]
    public void ACupIsStoodTheRightWayUpRatherThanOnItsRim()
    {
        var choices = Rank(CupOnItsRim());
        var best = choices[0];
        var onItsRim = choices.Single(c => c.IsCurrent);

        // Upside down, the floor of the cup is a ceiling 36 mm up with nothing under it.
        Assert.True(onItsRim.SupportMm2 > 500, $"{onItsRim.SupportMm2:0} mm2 - the floor should be hanging");
        Assert.False(best.IsCurrent);
        Assert.True(Faces(best.Normal, Vector3.UnitZ), "the face picked is not the closed end");
        Assert.True(best.SupportMm2 < 20, $"{best.SupportMm2:0} mm2 of support for a cup standing up");
    }

    [Fact]
    public void TheBestFaceNeverNeedsMoreSupportThanTheOneItIsStandingOn()
    {
        foreach (var part in new[] { BlockWithAPeg(), LBracket(), CupOnItsRim() })
        {
            var choices = Rank(part);
            var standing = choices.Single(c => c.IsCurrent);

            Assert.True(choices[0].SupportMm2 <= standing.SupportMm2 + 10,
                $"turning it over asks for {choices[0].SupportMm2:0} mm2 where it already had {standing.SupportMm2:0}");
        }
    }

    [Fact]
    public void ACubeIsOfferedOneWayUpRatherThanSix()
    {
        var choices = Rank(At(Primitives.Box(20, 20, 20), 0, 0, 10));

        Assert.Single(choices);
        Assert.Equal(6, choices[0].Alike);
    }

    [Fact]
    public void AskingForTheShortestPrintPutsTheLowestFirst()
    {
        var choices = Rank(LBracket());
        float lowest = choices.Min(c => c.HeightMm);

        var shortest = BestFace.Order(choices, FaceChoice.ShorterPrint)[0];

        // Half a millimetre is the band two heights count as the same within, so the first row can
        // be that much off the true minimum and still be right.
        Assert.True(shortest.HeightMm <= lowest + 0.51f,
            $"{shortest.HeightMm:0.#} mm came first where {lowest:0.#} mm was on offer");
    }

    [Fact]
    public void AFirmFootingIsWideForItsHeight()
    {
        var choices = Rank(LBracket());

        var firmest = BestFace.Order(choices, FaceChoice.FirmerFooting)[0];
        var wobbliest = BestFace.Order(choices, FaceChoice.FirmerFooting)[^1];

        Assert.True(firmest.TipDegrees > wobbliest.TipDegrees,
            $"a face that tips at {firmest.TipDegrees:0} deg was put above one at {wobbliest.TipDegrees:0} deg");
    }
}
