using System.Numerics;
using FastCraft3D.Geometry;
using FastCraft3D.Text;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>
/// Lettering made as an object of its own, laid out in a line or bent round something. What
/// matters is where it ends up: a ring of words has to close round its circle and stand on the
/// plate, not sit in a line somewhere near it.
/// </summary>
public class TextLayoutTests
{
    private static TextOptions Words => new()
    {
        Text = "ABCDEF", Font = "Arial", Height = 8f, Depth = 2f
    };

    /// <summary>Some machines have no Arial; then there is nothing to measure and nothing to test.</summary>
    private static bool HasFont => TextObject.Build(Words).TriangleCount > 0;

    [Fact]
    public void LyingFlatTheLetteringStandsOnThePlateAndReadsFromAbove()
    {
        if (!HasFont) return;

        var box = TextObject.Build(Words).ComputeBounds();

        Assert.Equal(0f, box.Min.Z, 3);
        Assert.Equal(2f, box.Size.Z, 3);          // the depth, which is up
        Assert.True(box.Size.X > box.Size.Y, "a line of letters is wider than it is deep");
    }

    [Fact]
    public void StandingUpTheDepthGoesTowardsTheFront()
    {
        if (!HasFont) return;

        var box = TextObject.Build(Words with { Layout = TextLayout.Upright }).ComputeBounds();

        Assert.Equal(8f, box.Size.Z, 0.5f);       // the cap height, now up
        Assert.Equal(2f, box.Size.Y, 3);          // the depth, now towards the front
    }

    // --- Round a circle ------------------------------------------------------------------

    [Fact]
    public void RoundACircleTheLettersSitOutsideTheRadiusAskedFor()
    {
        if (!HasFont) return;

        var flat = TextObject.Build(Words).ComputeBounds();
        var made = TextObject.Build(Words with { Layout = TextLayout.Circle, Radius = 30f });

        Assert.True(made.TriangleCount > 0);

        // What was up in the layout becomes distance out from the middle, so the band the letters
        // occupy is the radius plus whatever the layout's own top and bottom were.
        foreach (var p in made.Positions)
        {
            float from = new Vector2(p.X, p.Y).Length();
            Assert.InRange(from, 30f + flat.Min.Y - 0.1f, 30f + flat.Max.Y + 0.1f);
        }
    }

    [Fact]
    public void RoundACircleTheMiddleOfTheLetteringSitsAtTheTop()
    {
        if (!HasFont) return;

        var box = TextObject.Build(Words with { Layout = TextLayout.Circle, Radius = 30f })
            .ComputeBounds();

        // At the top of the circle, and running away either side of the middle rather than off
        // to one side of it. Not exactly symmetric: an A and an F are not the same width.
        Assert.True(box.Min.Y > 0f, $"the lettering should stay in the top half, not reach {box.Min.Y:0.#}");
        Assert.True(MathF.Abs(box.Center.X) < box.Size.X / 5f,
            $"it should straddle the top, not sit {box.Center.X:0.#} mm to one side of it");
        Assert.Equal(0f, box.Min.Z, 3);
    }

    /// <summary>
    /// The one thing a ring of words has to get right. Bending anticlockwise as the lettering
    /// runs makes the map a mirror - its determinant comes out negative - and every letter comes
    /// back reversed, which is exactly how it looked.
    ///
    /// Caught by where the weight of the lettering sits. "IIIWWW" is light on the left and heavy
    /// on the right; bent round a circle big enough to be nearly straight, it must stay that way.
    /// </summary>
    [Fact]
    public void RoundACircleTheLetteringReadsTheRightWayRoundRatherThanMirrored()
    {
        if (!HasFont) return;

        var lopsided = Words with { Text = "IIIWWW" };

        float straight = Middle(TextObject.Build(lopsided)).X;
        float bent = Middle(TextObject.Build(lopsided with { Layout = TextLayout.Circle, Radius = 400f })).X;

        Assert.True(straight > 0.5f, "the test string should lean right when it is straight");
        Assert.True(bent > 0f, $"bent, its weight went to {bent:0.#} - the lettering is mirrored");
    }

    [Fact]
    public void FacingInTheLetteringRunsRoundTheBottomAndStillReadsTheRightWayRound()
    {
        if (!HasFont) return;

        var lopsided = Words with { Text = "IIIWWW", Layout = TextLayout.Circle, Radius = 400f };

        var outward = TextObject.Build(lopsided).ComputeBounds();
        var inward = TextObject.Build(lopsided with { Inward = true }).ComputeBounds();

        // The other side of the circle from the one it runs round facing out.
        Assert.True(outward.Min.Y > 0f, "facing out it runs round the top");
        Assert.True(inward.Max.Y < 0f, "facing in it runs round the bottom");

        // And not mirrored either: the weight is still on the right.
        Assert.True(Middle(TextObject.Build(lopsided with { Inward = true })).X > 0f,
            "facing in, the lettering came back mirrored");
    }

    /// <summary>The average of the vertices, which leans towards the heavier end of a word.</summary>
    private static Vector3 Middle(Mesh mesh)
    {
        var total = Vector3.Zero;
        foreach (var p in mesh.Positions) total += p;

        return mesh.Positions.Count == 0 ? Vector3.Zero : total / mesh.Positions.Count;
    }

    /// <summary>
    /// A glyph's stem is one straight line from foot to head. Bending only its ends would leave it
    /// a chord across the arc - straight where the letter beside it curves - so every segment is
    /// cut up first, and that shows as far more points than the flat layout has.
    /// </summary>
    [Fact]
    public void RoundACircleTheStraightStrokesAreBentRatherThanLeftAsChords()
    {
        if (!HasFont) return;

        var flat = TextObject.Build(Words);
        var bent = TextObject.Build(Words with { Layout = TextLayout.Circle, Radius = 20f });

        Assert.True(bent.TriangleCount > flat.TriangleCount * 2,
            $"{bent.TriangleCount:N0} against {flat.TriangleCount:N0} - the strokes were not cut up");
    }

    [Fact]
    public void ATighterCircleWrapsFurtherRound()
    {
        if (!HasFont) return;

        var wide = TextObject.Build(Words with { Layout = TextLayout.Circle, Radius = 80f }).ComputeBounds();
        var tight = TextObject.Build(Words with { Layout = TextLayout.Circle, Radius = 12f }).ComputeBounds();

        // On a big circle the words are nearly a straight line; on a small one they curl round,
        // so they reach back down past the middle.
        Assert.True(wide.Min.Y > 0f, "at 80 mm it should still be near the top");
        Assert.True(tight.Min.Y < wide.Min.Y, "at 12 mm it should curl further round");
    }

    // --- Round a cylinder -----------------------------------------------------------------

    [Fact]
    public void RoundACylinderTheLettersStandUpOnTheSurface()
    {
        if (!HasFont) return;

        var made = TextObject.Build(Words with { Layout = TextLayout.Cylinder, Radius = 25f });
        Assert.True(made.TriangleCount > 0);

        var box = made.ComputeBounds();

        Assert.Equal(0f, box.Min.Z, 3);                     // set down on the plate
        Assert.True(box.Size.Z > 7f, "the cap height should be the upright one now");

        foreach (var p in made.Positions)
        {
            // On the cylinder, and standing proud of it by the depth.
            float from = new Vector2(p.X, p.Y).Length();
            Assert.InRange(from, 24f, 27.5f);
        }
    }

    /// <summary>
    /// Read from outside, at the front of the cylinder, the viewer's right is +Y - so lettering
    /// that leans right when it is straight must lean towards +Y once it is wrapped. The circle
    /// got this wrong; the cylinder wraps the other way round and does not.
    /// </summary>
    [Fact]
    public void RoundACylinderTheLetteringReadsTheRightWayRoundFromOutside()
    {
        if (!HasFont) return;

        var lopsided = Words with { Text = "IIIWWW" };

        Assert.True(Middle(TextObject.Build(lopsided)).X > 0.5f, "it should lean right when straight");
        Assert.True(Middle(TextObject.Build(lopsided with { Layout = TextLayout.Cylinder, Radius = 300f })).Y > 0f,
            "wrapped, the lettering came back mirrored");
    }

    [Fact]
    public void ALetterKeepsItsWidthWhateverTheRadius()
    {
        if (!HasFont) return;

        // Across is arc length, so the lettering takes the same length of surface either way.
        float flat = TextObject.Build(Words).ComputeBounds().Size.X;

        foreach (float radius in new[] { 15f, 60f })
        {
            var box = TextObject.Build(Words with { Layout = TextLayout.Cylinder, Radius = radius })
                .ComputeBounds();

            // The chord across a wrapped line is shorter than the arc, and never longer.
            Assert.True(box.Size.X <= flat + 0.5f, $"at {radius} mm it grew to {box.Size.X:0.#}");
        }
    }

    [Fact]
    public void ARadiusIsHeldToSomethingThatCanBeBuilt()
    {
        var mad = (Words with { Layout = TextLayout.Circle, Radius = -4f }).Sane();

        Assert.Equal(1f, mad.Radius);
        Assert.True(mad.IsRound);
        Assert.False(Words.IsRound);
    }
}
