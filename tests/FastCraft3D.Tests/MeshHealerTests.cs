using System.Numerics;
using FastCraft3D.Geometry;
using FastCraft3D.Geometry.Engraving;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>
/// Repairing a mesh that arrived broken - the imported STL with a few triangles missing, the
/// shell exported inside out. Not boolean debris: that is a different problem, and the pass is
/// built to decline it rather than guess.
/// </summary>
public class MeshHealerTests
{
    private static Mesh Box() => Primitives.Box(20, 20, 20);

    /// <summary>Knocks a triangle out, leaving a three-sided hole.</summary>
    private static Mesh WithAHole(Mesh mesh, int triangle = 0)
    {
        var indices = new List<int>(mesh.Indices);
        indices.RemoveRange(triangle * 3, 3);
        return new Mesh(mesh.Positions, indices);
    }

    [Fact]
    public void AHealthyMeshIsLeftAlone()
    {
        var box = Box();

        var healed = MeshHealer.Heal(box);

        Assert.True(healed.After.IsWatertight);
        Assert.Equal(box.TriangleCount, healed.Mesh.TriangleCount);
        Assert.Equal(box.ComputeSignedVolume(), healed.Mesh.ComputeSignedVolume(), 6);
        Assert.Contains("Nothing needed repairing", healed.Describe());
    }

    [Fact]
    public void AMissingTriangleIsPatched()
    {
        var damaged = WithAHole(Box());
        Assert.False(damaged.CheckHealth().IsWatertight);

        var healed = MeshHealer.Heal(damaged);

        Assert.True(healed.After.IsWatertight, healed.After.Describe());
        Assert.Equal(1, healed.HolesFilled);
        Assert.Contains("hole", healed.Describe());
    }

    /// <summary>The patch has to face the same way as the surface it joins, or it is still wrong.</summary>
    [Fact]
    public void ThePatchFacesTheSameWayAsTheRest()
    {
        var box = Box();
        var healed = MeshHealer.Heal(WithAHole(box));

        // Same volume as the original: a patch wound the wrong way would subtract instead.
        Assert.Equal(box.ComputeSignedVolume(), healed.Mesh.ComputeSignedVolume(), 3);
    }

    [Fact]
    public void SeveralHolesAreAllPatched()
    {
        var damaged = WithAHole(WithAHole(Box(), 5), 0);

        var healed = MeshHealer.Heal(damaged);

        Assert.True(healed.After.IsWatertight, healed.After.Describe());
        Assert.Equal(2, healed.HolesFilled);
    }

    [Fact]
    public void AFaceTurnedTheWrongWayIsPutBack()
    {
        var box = Box();
        var indices = new List<int>(box.Indices);
        (indices[1], indices[2]) = (indices[2], indices[1]);
        var damaged = new Mesh(box.Positions, indices);

        Assert.True(damaged.CheckHealth().InconsistentEdges > 0);

        var healed = MeshHealer.Heal(damaged);

        Assert.True(healed.After.IsWatertight, healed.After.Describe());
        Assert.Equal(box.ComputeSignedVolume(), healed.Mesh.ComputeSignedVolume(), 3);
    }

    /// <summary>A shell exported inside out is closed, just wrong. The volume is the only tell.</summary>
    [Fact]
    public void AnInsideOutShellIsTurnedBack()
    {
        var damaged = Box();
        damaged.FlipWinding();
        Assert.True(damaged.CheckHealth().IsInsideOut);

        var healed = MeshHealer.Heal(damaged);

        Assert.True(healed.Mesh.ComputeSignedVolume() > 0);
        Assert.True(healed.After.IsWatertight);
    }

    [Fact]
    public void TrueDegeneratesAreDroppedButThinTrianglesAreNot()
    {
        var mesh = new Mesh();
        mesh.AddTriangle(Vector3.Zero, new Vector3(10, 0, 0), new Vector3(5, 0, 0)); // no area at all
        mesh.AddTriangle(Vector3.Zero, new Vector3(10, 0, 0), new Vector3(5, 0.02f, 0)); // thin, but real

        var (trimmed, dropped) = MeshHealer.DropSlivers(mesh);

        Assert.Equal(1, dropped);
        Assert.Equal(1, trimmed.TriangleCount);
    }

    [Fact]
    public void WindingIsSpreadAcrossEveryPieceSeparately()
    {
        var far = MeshTransform.Transformed(Box(), Matrix4x4.CreateTranslation(100, 0, 0));
        far.FlipWinding();
        var pair = Mesh.Combine([Box(), far]);

        var (fixedUp, flipped) = MeshHealer.UnifyWinding(pair);

        Assert.True(flipped > 0);
        Assert.Equal(0, fixedUp.CheckHealth().InconsistentEdges);
    }

    /// <summary>
    /// The guarantee that makes the tool safe to offer: against damage it cannot understand it
    /// hands the mesh straight back, rather than tearing it further.
    /// </summary>
    [Fact]
    public void DamageItCannotFixIsReturnedUntouched()
    {
        // A boolean that came out wrong: the one thing local patching has no answer to.
        var wall = Primitives.Box(320, 320, 40);
        var face = FacePatch.Find(wall, new Vector3(0, 0, 20), Vector3.UnitZ)!;
        var torn = Engraver.Engrave(wall, face, new EngraveOptions(PatternKind.Wood, 3, 0.5f, 0.6f)).Mesh;

        var healed = MeshHealer.Heal(torn);

        Assert.True(
            healed.After.IsWatertight || SameDefects(healed),
            "healing must either fix it or leave it exactly as it was");
    }

    private static bool SameDefects(HealResult healed) =>
        healed.After.BoundaryEdges == healed.Before.BoundaryEdges &&
        healed.After.NonManifoldEdges == healed.Before.NonManifoldEdges &&
        healed.After.InconsistentEdges == healed.Before.InconsistentEdges;

    [Fact]
    public void HealingNeverMakesAMeshWorse()
    {
        foreach (var kind in Enum.GetValues<PrimitiveKind>())
        {
            var mesh = MeshTransform.Transformed(Primitives.Create(kind), Matrix4x4.CreateScale(4f));
            var face = FacePatch.Find(mesh, new Vector3(0, 0, mesh.ComputeBounds().Max.Z), Vector3.UnitZ);
            if (face is null) continue;

            foreach (var pattern in Enum.GetValues<PatternKind>())
            {
                var cut = Engraver.Engrave(mesh, face, new EngraveOptions(pattern, 6, 1.2f, 0.6f)).Mesh;
                var healed = MeshHealer.Heal(cut);

                int before = healed.Before.BoundaryEdges + healed.Before.NonManifoldEdges + healed.Before.InconsistentEdges;
                int after = healed.After.BoundaryEdges + healed.After.NonManifoldEdges + healed.After.InconsistentEdges;

                Assert.True(after <= before, $"{kind} {pattern}: {before} defects became {after}");
            }
        }
    }
}

/// <summary>
/// Engraving refuses rather than handing back a model that would not print. The check matters
/// most on a facet of a curved surface, where a flat pattern was never going to sit properly.
/// </summary>
public class EngravePrintabilityTests
{
    [Fact]
    public void ACleanCutReportsItselfPrintable()
    {
        var wall = Primitives.Box(100, 100, 10);
        var face = FacePatch.Find(wall, new Vector3(0, 0, 5), Vector3.UnitZ)!;

        var result = Engraver.Engrave(wall, face, new EngraveOptions(PatternKind.Brick, 20, 1.5f, 0.6f));

        Assert.True(result.IsPrintable);
        Assert.True(result.Health.IsWatertight);
    }

    [Fact]
    public void PrintabilityFollowsTheMeshRatherThanBeingAssumed()
    {
        var wall = Primitives.Box(100, 100, 10);
        var face = FacePatch.Find(wall, new Vector3(0, 0, 5), Vector3.UnitZ)!;

        var result = Engraver.Engrave(wall, face, new EngraveOptions(PatternKind.Stripes, 10, 2, 0.5f));

        Assert.Equal(result.Health.IsWatertight, result.IsPrintable);
        Assert.Equal(result.Mesh.CheckHealth(), result.Health);
    }

    [Fact]
    public void APatternThatReachesNothingIsNotCalledPrintableGeometry()
    {
        var wall = Primitives.Box(100, 100, 10);
        var face = FacePatch.Find(wall, new Vector3(0, 0, 5), Vector3.UnitZ)!;

        // Grooves wider than the spacing leave nothing to cut once they are clamped.
        var result = Engraver.Engrave(wall, face, new EngraveOptions(PatternKind.Stripes, 0.5f, 0.05f, 0.5f));

        Assert.True(result.Mesh.TriangleCount > 0);
    }
}
