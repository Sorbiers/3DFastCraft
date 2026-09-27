using System.Numerics;
using FastCraft3D.Geometry;
using FastCraft3D.Geometry.Engraving;
using FastCraft3D.Io;
using Xunit;

namespace FastCraft3D.Tests;

public class PictureTraceTests
{
    /// <summary>Ink where the test says, soft at the edge over a sample as a real picture is.</summary>
    private static Greyscale Picture(int w, int h, Func<float, float, float> inside)
    {
        var samples = new float[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                samples[y * w + x] = Math.Clamp(0.5f - inside(x + 0.5f, y + 0.5f), 0f, 1f);
        return new Greyscale(w, h, samples);
    }

    [Fact]
    public void ARingTracesToOneRoundOutlineWithOneHoleTheHeightAsked()
    {
        var ring = Picture(100, 100, (x, y) =>
        {
            float r = Vector2.Distance(new(x, y), new(50, 50));
            return MathF.Max(r - 40f, 20f - r);
        });

        var shape = Assert.Single(PictureTrace.Outlines(ring, 0.5f, 30f));
        Assert.Single(shape.Holes);

        var min = shape.Outline.Aggregate(new Vector2(float.MaxValue), Vector2.Min);
        var max = shape.Outline.Aggregate(new Vector2(float.MinValue), Vector2.Max);
        Assert.Equal(30f, max.Y - min.Y, 1);
        Assert.Equal(30f, max.X - min.X, 1);

        // Round, not a staircase of pixels: every corner the same distance from the middle.
        foreach (var p in shape.Outline) Assert.InRange(p.Length(), 14.8f, 15.2f);
    }

    [Fact]
    public void TwoBlobsAreTwoShapesAndEachStampsClosed()
    {
        var blobs = Picture(120, 60, (x, y) => MathF.Min(Vector2.Distance(new(x, y), new(30, 30)) - 20f, MathF.Max(MathF.Abs(x - 90) - 18f, MathF.Abs(y - 30) - 25f)));

        var shapes = PictureTrace.Outlines(blobs, 0.5f, 20f);
        Assert.Equal(2, shapes.Count);

        var solid = TextSolid.Extrude(shapes, 0, 2);
        Assert.True(solid.CheckHealth().IsWatertight, solid.CheckHealth().Describe());
    }

    [Fact]
    public void TheThresholdFallsBetweenTheInkAndThePaper()
    {
        var two = new Greyscale(10, 10, Enumerable.Range(0, 100).Select(i => i < 30 ? 0.9f : 0.1f).ToArray());
        Assert.InRange(PictureTrace.Threshold(two), 0.15f, 0.85f);
    }

    [Fact]
    public void ACutOutPictureIsInkWhereItIsNotSeeThroughWhateverItsColour()
    {
        // White on nothing: dark would read it as no ink at all.
        var bgra = new byte[4 * 4 * 4];
        for (int i = 0; i < 16; i++)
        {
            bgra[i * 4] = bgra[i * 4 + 1] = bgra[i * 4 + 2] = 255;
            bgra[i * 4 + 3] = (byte)(i % 4 < 2 ? 255 : 0);
        }

        var ink = PictureReader.Ink(bgra, 4, 4, 4);
        Assert.Equal(1f, ink.Samples[0], 3);
        Assert.Equal(0f, ink.Samples[3], 3);
    }
}
