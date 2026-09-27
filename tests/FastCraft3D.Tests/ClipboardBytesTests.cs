using System.Numerics;
using FastCraft3D.Geometry;
using FastCraft3D.Io;
using FastCraft3D.Model;
using Xunit;

namespace FastCraft3D.Tests;

public class ClipboardBytesTests
{
    [Fact]
    public void ObjectsCopiedInOneWindowComeBackWholeInAnother()
    {
        var box = new SceneObject("Box", Primitives.Box(10, 20, 30)) { Position = new Vector3(5, -7, 15), Rotation = new Vector3(0, 0, 30) };

        var back = Assert.Single(SceneSerializer.FromBytes(SceneSerializer.ToBytes([box])));

        Assert.Equal("Box", back.Name);
        Assert.Equal(box.Position, back.Position);
        Assert.Equal(box.Rotation, back.Rotation);
        Assert.Equal(box.Mesh.TriangleCount, back.Mesh.TriangleCount);
    }
}
