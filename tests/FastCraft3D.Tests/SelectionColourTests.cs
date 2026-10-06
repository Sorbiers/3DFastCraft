using System.Numerics;
using FastCraft3D.Model;
using FastCraft3D.Render;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>A selected object too dense for an outline is marked by its colour, so it has to show.</summary>
public class SelectionColourTests
{
    private static float Moved(Vector3 colour) => Vector3.Distance(colour, SelectionColour.Intensified(colour));

    [Fact]
    public void EveryColourOfThePaletteLooksDifferentOnceSelected()
    {
        foreach (var swatch in Palette.Swatches)
            Assert.True(Moved(swatch.Colour) >= 0.12f, $"{swatch.Name} moves only {Moved(swatch.Colour):0.000}");
    }

    [Fact]
    public void EveryGreyFromBlackToWhiteLooksDifferentOnceSelected()
    {
        for (int i = 0; i <= 10; i++)
        {
            var grey = new Vector3(i / 10f);
            Assert.True(Moved(grey) >= 0.12f, $"grey {i / 10f:0.0} moves only {Moved(grey):0.000}");
        }
    }

    [Fact]
    public void ADeepenedColourStaysInRangeAndKeepsItsHue()
    {
        // The whole cube, in steps of a tenth: nothing leaves the range a colour can take, and a
        // colour with some colour to it is not turned into another one.
        for (int r = 0; r <= 10; r++)
        for (int g = 0; g <= 10; g++)
        for (int b = 0; b <= 10; b++)
        {
            var colour = new Vector3(r, g, b) / 10f;
            var deeper = SelectionColour.Intensified(colour);

            Assert.InRange(deeper.X, 0f, 1f);
            Assert.InRange(deeper.Y, 0f, 1f);
            Assert.InRange(deeper.Z, 0f, 1f);

            // No channel overtakes another that was clearly above it, which is as much of the hue as
            // a clamp leaves.
            if (Chroma(colour) > 0.3f)
            {
                float[] before = [colour.X, colour.Y, colour.Z], after = [deeper.X, deeper.Y, deeper.Z];

                for (int i = 0; i < 3; i++)
                for (int j = 0; j < 3; j++)
                    if (before[i] - before[j] > 0.05f)
                        Assert.True(after[i] >= after[j] - 1e-4f, $"{colour} -> {deeper}");
            }
        }
    }

    private static float Chroma(Vector3 c) =>
        MathF.Max(c.X, MathF.Max(c.Y, c.Z)) - MathF.Min(c.X, MathF.Min(c.Y, c.Z));
}
