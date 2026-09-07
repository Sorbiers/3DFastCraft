using FastCraft3D.Geometry;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>
/// Hollowing something and leaving one side open.
///
/// The roof of the house wanted a shell with its underside open, and Hollow could only make a
/// sealed one. So the roof had to be built twice - full size and shrunk - and the second
/// subtracted from the first, with the offset worked out by hand as the skin thickness over the
/// cosine of the pitch. That is not arithmetic a user should be doing.
/// </summary>
public class HollowOpenSideTests
{
    [Fact]
    public void ASealedShellIsStillSealed()
    {
        var box = Primitives.Box(40, 30, 20);
        var shell = MeshHollow.Hollow(box, 2f, 48).Mesh;

        Assert.True(shell.CheckHealth().IsWatertight, shell.CheckHealth().Describe());

        // Two surfaces, inside and out, so a good deal less material than the solid.
        Assert.True(shell.ComputeSignedVolume() < box.ComputeSignedVolume() * 0.75);
    }

    /// <summary>
    /// Opened underneath, the cavity reaches daylight - so the shell holds less again, and its
    /// lowest point is the rim rather than a floor.
    /// </summary>
    [Theory]
    [InlineData(OpenSide.Bottom)]
    [InlineData(OpenSide.Top)]
    [InlineData(OpenSide.Front)]
    [InlineData(OpenSide.Right)]
    public void AnOpenedSideTakesTheSkinOffThatFace(OpenSide side)
    {
        var box = Primitives.Box(40, 30, 20);

        var sealedShell = MeshHollow.Hollow(box, 2f, 48, OpenSide.None).Mesh;
        var opened = MeshHollow.Hollow(box, 2f, 48, side).Mesh;

        Assert.True(opened.CheckHealth().IsWatertight, opened.CheckHealth().Describe());
        Assert.True(opened.ComputeSignedVolume() < sealedShell.ComputeSignedVolume(),
            "opening a side should take a face off, not add one");

        // And it is still the same object, not a fragment of one. The surface is rebuilt on a
        // grid and smoothed, so it comes back a little under size rather than exactly on it.
        var bounds = opened.ComputeBounds();
        Assert.InRange(bounds.Max.X - bounds.Min.X, 38f, 40.5f);
        Assert.InRange(bounds.Max.Y - bounds.Min.Y, 28f, 30.5f);
        Assert.InRange(bounds.Max.Z - bounds.Min.Z, 18f, 20.5f);
    }

    /// <summary>A part too thin to hollow is handed back as it was, open side or not.</summary>
    [Fact]
    public void SomethingTooThinIsLeftAlone()
    {
        var sheet = Primitives.Box(40, 30, 1.5f);
        var result = MeshHollow.Hollow(sheet, 2f, 48, OpenSide.Bottom);

        Assert.Equal(0, result.VolumeSavedCm3);
    }
}
