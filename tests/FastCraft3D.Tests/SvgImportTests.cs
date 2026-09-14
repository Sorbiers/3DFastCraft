using System.Numerics;
using FastCraft3D.Geometry;
using FastCraft3D.Io;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>
/// An SVG drawing imported as a solid: its filled shapes, as thick as asked, lying flat.
/// </summary>
public class SvgImportTests
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

    /// <summary>A 40 by 20 frame - a rectangle with a 20 by 10 hole - and a separate square beside it.</summary>
    private const string FrameAndSquare = """
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 60 20">
          <path d="M0 0 H40 V20 H0 Z M10 5 V15 H30 V5 Z" fill="black" fill-rule="evenodd" />
          <rect x="50" y="5" width="10" height="10" fill="black" />
        </svg>
        """;

    [Fact]
    public void ADrawingBecomesOneSolidAtTheWidthAndThicknessAsked()
    {
        var solids = SvgImport.Build(FrameAndSquare, new SvgImportOptions(60f, 3f, Separate: false));

        var solid = Assert.Single(solids);
        Assert.True(solid.CheckHealth().IsWatertight, solid.CheckHealth().Describe());

        var bounds = solid.ComputeBounds();
        Assert.Equal(60f, bounds.Size.X, 0.01f);
        Assert.Equal(20f, bounds.Size.Y, 0.01f);
        Assert.Equal(3f, bounds.Size.Z, 0.01f);

        // The frame keeps its hole: 40x20 less 20x10, plus the 10x10 square, all 3 mm thick.
        double expected = (40 * 20 - 20 * 10 + 10 * 10) * 3.0;
        Assert.Equal(expected, Volume(solid), expected * 0.001);
    }

    [Fact]
    public void EachShapeCanComeInAsItsOwnObject()
    {
        var solids = SvgImport.Build(FrameAndSquare, new SvgImportOptions(60f, 2f, Separate: true));

        Assert.Equal(2, solids.Count);
        Assert.All(solids, s => Assert.True(s.CheckHealth().IsWatertight, s.CheckHealth().Describe()));
    }

    [Fact]
    public void ShapesThatOverlapAreJoinedIntoOneClosedSolid()
    {
        const string overlapping = """
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 30 10">
              <rect x="0" y="0" width="20" height="10" fill="black" />
              <circle cx="22" cy="5" r="5" fill="black" />
            </svg>
            """;

        var solid = Assert.Single(SvgImport.Build(overlapping, new SvgImportOptions(30f, 2f, Separate: false)));

        Assert.True(solid.CheckHealth().IsWatertight, solid.CheckHealth().Describe());

        // Less than the same two shapes apart: the part they share is counted once.
        double apart = SvgImport.Build(overlapping, new SvgImportOptions(30f, 2f, Separate: true)).Sum(Volume);
        Assert.True(Volume(solid) < apart - 5, $"{Volume(solid):0} against {apart:0} for both apart");
    }

    [Fact]
    public void ADrawingOfUnfilledLinesHasNothingToMakeSolid()
    {
        const string lines = """
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 10 10">
              <line x1="0" y1="0" x2="10" y2="10" stroke="black" />
            </svg>
            """;

        Assert.Empty(SvgImport.Build(lines, SvgImportOptions.Default));
    }

    [Fact]
    public void AnSvgIsAFileTheAppTakes() => Assert.True(IncomingFiles.Understood("logo.SVG"));
}
