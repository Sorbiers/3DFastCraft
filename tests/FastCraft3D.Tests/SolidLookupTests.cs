using System.Numerics;
using FastCraft3D.Geometry;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>
/// Whether a point is inside a closed mesh, which is how a wrapped texture finds out where the
/// wall is. There is no face patch round a barrel to ask instead.
/// </summary>
public class SolidLookupTests
{
    /// <summary>A wall with a window, built as the four bars that surround the opening.</summary>
    private static Mesh Frame()
    {
        var wall = new Mesh();

        foreach (var (at, size) in new (Vector3 At, Vector3 Size)[]
        {
            (new(0, 0, 22), new(60, 8, 16)),    // above the opening
            (new(0, 0, -22), new(60, 8, 16)),   // below
            (new(-22, 0, 0), new(16, 8, 28)),   // left
            (new(22, 0, 0), new(16, 8, 28))     // right
        })
        {
            var bar = MeshTransform.Transformed(
                Primitives.Box(size.X, size.Y, size.Z), Matrix4x4.CreateTranslation(at));

            for (int t = 0; t + 2 < bar.Indices.Count; t += 3)
                wall.AddTriangle(
                    bar.Positions[bar.Indices[t]],
                    bar.Positions[bar.Indices[t + 1]],
                    bar.Positions[bar.Indices[t + 2]]);
        }

        return wall;
    }

    [Theory]
    [InlineData(0f, 0f, 0f, true)]
    [InlineData(0f, 0f, 9f, true)]
    [InlineData(9.9f, 9.9f, 9.9f, true)]
    [InlineData(0f, 0f, 11f, false)]
    [InlineData(15f, 0f, 0f, false)]
    public void APointIsInsideABoxOrItIsNot(float x, float y, float z, bool inside)
    {
        var look = new SolidLookup(Primitives.Box(20f, 20f, 20f));

        Assert.Equal(inside, look.Contains(new Vector3(x, y, z)));
    }

    /// <summary>
    /// The middle of a box's face is exactly on the diagonal that splits it into two triangles,
    /// so a ray straight up from the middle of the model hits both of them or neither. It is the
    /// case the nudge exists for, and the one a model made of boxes hits first.
    /// </summary>
    [Fact]
    public void ThePointOnATrianglesSharedEdgeIsStillAnsweredRight()
    {
        var look = new SolidLookup(Frame());

        Assert.True(look.Contains(new Vector3(0f, 0f, 17f)), "the middle of the bar above the opening");
        Assert.True(look.Contains(new Vector3(0f, 0f, 22f)), "the middle of the same bar");
        Assert.True(look.Contains(new Vector3(0f, 0f, -22f)), "and of the one below it");
    }

    /// <summary>Nothing anywhere in the opening may read as material.</summary>
    [Fact]
    public void NoPointInsideTheOpeningReadsAsMaterial()
    {
        var look = new SolidLookup(Frame());
        var over = new List<Vector3>();

        for (int i = 0; i <= 40; i++)
            for (int j = 0; j <= 40; j++)
            {
                var at = new Vector3(-13f + 26f * i / 40f, 0f, -13f + 26f * j / 40f);
                if (look.Contains(at)) over.Add(at);
            }

        Assert.True(over.Count == 0,
            $"{over.Count} of 1681 points in the window read as wall, the first at {over.FirstOrDefault()}");
    }

    /// <summary>And the answer may not change between one call and the next.</summary>
    [Fact]
    public void TheSameQuestionGetsTheSameAnswer()
    {
        var look = new SolidLookup(Frame());

        foreach (var at in new[]
        {
            new Vector3(0f, 0f, 0f), new Vector3(0f, 0f, 22f), new Vector3(-22f, 0f, 0f)
        })
        {
            bool first = look.Contains(at);

            for (int again = 0; again < 8; again++)
                Assert.Equal(first, look.Contains(at));
        }
    }
}
