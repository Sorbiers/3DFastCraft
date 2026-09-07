using System.Numerics;
using FastCraft3D.Geometry;
using FastCraft3D.Geometry.Csg;
using FastCraft3D.Geometry.Engraving;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>
/// Bricking a wall that already has its windows in it.
///
/// It used to be impossible: the retiler wanted a face that filled its own rectangle, and a wall
/// with openings does not. So the brick had to go on before the openings were cut, which forces
/// an order nobody would guess and means a wall can never be re-clad once it has windows.
/// </summary>
public class PatternOnHoledFaceTests
{
    private static readonly EngraveOptions Brick = EngraveOptions.Default with
    {
        Kind = PatternKind.Brick, Size = 4f, GrooveWidth = 0.4f, Depth = 0.3f, Raised = true
    };

    private static Mesh Block(float x, float y, float z, float sx, float sy, float sz) =>
        MeshTransform.Transformed(
            Primitives.Box(sx, sy, sz),
            Matrix4x4.CreateTranslation(x + sx / 2f, y + sy / 2f, z + sz / 2f));

    private static FacePatch Front(Mesh mesh) =>
        FacePatch.Find(mesh, new Vector3(0, -4, 0), -Vector3.UnitY)!;

    /// <summary>A wall 60 x 40, 8 mm thick, with two windows and a door already cut through it.</summary>
    private static Mesh Wall()
    {
        var built = Primitives.Box(60, 8, 40);
        var cuts = Mesh.Combine([
            Block(-24, -5, -8, 12, 10, 10),   // window
            Block(10, -5, -8, 12, 10, 10),    // window
            Block(-6, -5, -20, 9, 10, 18)     // door, down to the base
        ]);

        return CsgSolid.Subtract(built, cuts);
    }

    [Fact]
    public void AWallWithWindowsCanStillBeBricked()
    {
        var wall = Wall();
        Assert.True(wall.CheckHealth().IsWatertight, "the wall itself should be sound to start with");

        var result = Engraver.Engrave(wall, Front(wall), Brick);

        Assert.True(result.IsPrintable, result.Health.Describe());
        Assert.True(result.Mesh.ComputeSignedVolume() > wall.ComputeSignedVolume(), "no brick was added");
    }

    /// <summary>The openings stay open - brick laid over a window would close it.</summary>
    [Fact]
    public void TheWindowsAreStillThereAfterwards()
    {
        var wall = Wall();
        var built = Engraver.Engrave(wall, Front(wall), Brick).Mesh;

        // A face found at the middle of each opening should be its reveal, not a wall.
        foreach (var at in new[] { new Vector3(-18, 0, -3), new Vector3(16, 0, -3) })
        {
            var through = FacePatch.Find(built, at with { Y = -4 }, -Vector3.UnitY);
            Assert.True(through is null || through.Area < 100f,
                $"the opening at {at} looks like it was bricked over");
        }

        // And the volume of the wall did not jump by the volume of three filled openings.
        double filled = (12 * 10 + 12 * 10 + 9 * 18) * 8.0;
        Assert.True(built.ComputeSignedVolume() < wall.ComputeSignedVolume() + filled * 0.25,
            "far too much material came back - the openings were filled in");
    }

    /// <summary>And bricking it a second time still works, which is what a re-clad is.</summary>
    [Fact]
    public void AWallCanBeReclad()
    {
        var wall = Wall();
        var once = Engraver.Engrave(wall, Front(wall), Brick);
        Assert.True(once.IsPrintable, once.Health.Describe());

        var twice = Engraver.Engrave(once.Mesh, Front(once.Mesh),
            Brick with { Kind = PatternKind.Stripes, Size = 5f });

        Assert.True(twice.IsPrintable, twice.Health.Describe());
    }
}
