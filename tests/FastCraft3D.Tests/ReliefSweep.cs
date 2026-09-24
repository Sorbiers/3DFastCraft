using System.Numerics;
using FastCraft3D.Geometry;
using FastCraft3D.Geometry.Engraving;
using Xunit;
using Xunit.Abstractions;

namespace FastCraft3D.Tests;

/// <summary>
/// Every shaped texture, over the settings and faces anybody would put them on, judged on the
/// three things that decide whether the tool works at all: does something come back, is it closed,
/// and does the panel's figure match what is actually built.
///
/// The same sweep the flat textures get, and for the same reason - three faults in a row there
/// were all one fault, and each got through because it was tested on the one case in front of me.
/// </summary>
public class ReliefSweep(ITestOutputHelper log)
{
    /// <summary>Past this a field is not worth building in a test, and the panel has to say so.</summary>
    private const long TooHeavy = 400_000;

    private static PlanarSurface Face(float side)
    {
        var wall = MeshTransform.Transformed(
            Primitives.Box(side, side, side), Matrix4x4.CreateTranslation(0, 0, side / 2f));

        return new PlanarSurface(FacePatch.Find(wall, new Vector3(0, -side / 2f, side / 2f), -Vector3.UnitY)!);
    }

    [Fact]
    public void EverySettingBuildsSomethingClosed()
    {
        TextureKind[] kinds = [TextureKind.Siding, TextureKind.RoofTiles, TextureKind.Planks];
        float[] pitches = [1f, 2f, 5f, 10f, 20f, 40f];
        float[] grooves = [0.2f, 0.6f, 1.5f];
        float[] depths = [0.2f, 0.8f, 2f];
        float[] aspects = [0f, 3f, 12f];
        (float A, float U)[] faces = [(12f, 12f), (40f, 40f), (120f, 80f)];

        // One face per size, since building a box and finding its face again for every setting is
        // most of what the sweep would otherwise cost.
        var surfaces = faces.ToDictionary(f => f, f => Face(MathF.Max(f.A, f.U) + 4f));

        var complaints = new List<string>();
        int cases = 0, skipped = 0;
        long worst = 0;

        foreach (var kind in kinds)
            foreach (float pitch in pitches)
                foreach (float groove in grooves)
                    foreach (float depth in depths)
                        foreach (float aspect in aspects)
                            foreach (var face in faces)
                            {
                                var o = new TextureOptions(kind, pitch, groove, 45f, false, aspect);
                                var relief = SurfaceTexture.ProfileOf(o, depth)!;
                                cases++;

                                var cost = ReliefField.Cost(relief, face.A, face.U);

                                // A field this heavy is one the panel has to refuse, not one to build.
                                if (!cost.CanBuild || cost.Triangles > TooHeavy)
                                {
                                    // Refused is a result, so long as it says why and builds nothing.
                                    if (!cost.CanBuild)
                                        Assert.Equal(0, ReliefField.Build(surfaces[face], relief, face.A, face.U).TriangleCount);

                                    skipped++;
                                    continue;
                                }

                                var built = ReliefField.Build(surfaces[face], relief, face.A, face.U);
                                worst = Math.Max(worst, built.TriangleCount);

                                string why = Judge(built, cost.Triangles, depth, face.U);
                                if (why.Length > 0)
                                    complaints.Add(
                                        $"{kind,-9} pitch={pitch,-4} groove={groove,-4} depth={depth,-4} " +
                                        $"aspect={aspect,-4} face={face.A}x{face.U}: {why}");
                            }

        foreach (var line in complaints.Take(20)) log.WriteLine(line);
        log.WriteLine($"=== {complaints.Count} of {cases} bad, {skipped} too heavy to build; " +
                      $"the heaviest built was {worst:N0} triangles");

        Assert.Empty(complaints);
    }

    /// <summary>
    /// The settings off the screenshot that said nothing worked: lap siding, 10 mm courses, on the
    /// face of an ordinary cube.
    /// </summary>
    [Theory]
    [InlineData(0.6f, 0.8f)]
    [InlineData(0.2f, 0.4f)]
    public void SidingOnACubeBuildsAndSaysWhatItCost(float groove, float depth)
    {
        var o = new TextureOptions(TextureKind.Siding, 10f, groove, 45f);
        var relief = SurfaceTexture.ProfileOf(o, depth)!;

        var cost = ReliefField.Cost(relief, 38f, 38f);
        var built = ReliefField.Build(Face(40f), relief, 38f, 38f);

        log.WriteLine($"groove {groove}, depth {depth}: {cost.Across} x {cost.Up} samples, " +
                      $"panel says {cost.Triangles}, built {built.TriangleCount}");

        Assert.True(built.TriangleCount > 0, "nothing came back for a plain cube");
        Assert.True(built.CheckHealth().IsWatertight, built.CheckHealth().Describe());

        // Four courses on a 38 mm face at a 10 mm pitch, so the figure is not a wild guess either.
        Assert.InRange(cost.Triangles, built.TriangleCount / 2, built.TriangleCount * 2);
    }

    private static string Judge(Mesh built, long promised, float depthMm, float upMm)
    {
        if (built.TriangleCount == 0)
            return $"nothing came back, where the panel promised about {promised:N0} triangles";

        if (!built.CheckHealth().IsWatertight) return built.CheckHealth().Describe();

        if (promised < built.TriangleCount / 2 || promised > built.TriangleCount * 2)
            return $"promised {promised:N0} triangles and built {built.TriangleCount:N0}";

        // It has to stand proud by roughly what was asked for. The face is at y = -half and the
        // relief runs out along -Y, so the furthest point out is the most negative.
        float proud = -built.Positions.Min(p => p.Y) - (built.Positions.Max(p => p.Y) - ReliefField.SinkMm);

        return proud < depthMm * 0.5f
            ? $"stands {proud:0.###} mm proud where {depthMm:0.##} mm was asked for"
            : "";
    }
}
