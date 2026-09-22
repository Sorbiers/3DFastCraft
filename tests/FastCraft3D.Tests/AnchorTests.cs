using System.IO;
using System.Numerics;
using FastCraft3D.Geometry;
using FastCraft3D.Io;
using FastCraft3D.Model;
using FastCraft3D.Model.Commands;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>
/// What a tool knew when it made a part, kept so that a shaft can be fitted to a hole without
/// anyone measuring it by eye.
/// </summary>
public class AnchorTests
{
    private static GearOptions Bored => new()
    {
        Module = 2f, Teeth = 20, Thickness = 6f, Bore = BoreShape.Round, BoreSize = 5f
    };

    // --- What the gear tool marks --------------------------------------------------------

    [Fact]
    public void AGearSaysWhereItsShaftHoleIs()
    {
        var made = Gears.Build(Bored);
        var marked = made.Parts[0].Anchors;

        Assert.NotNull(marked);
        var bore = marked![0];

        Assert.Equal(AnchorKind.Bore, bore.Kind);
        Assert.Equal(Vector3.UnitZ, bore.Along);
        Assert.Equal(5f, bore.Size, 3);
        Assert.Equal(6f, bore.Length, 3);        // through the whole thickness
        Assert.Equal(0f, bore.At.X, 3);
        Assert.Equal(0f, bore.At.Y, 3);
    }

    [Fact]
    public void TheHoleRunsThroughTheHubAsWellAsTheGear()
    {
        var made = Gears.Build(Bored with { HubDiameter = 12f, HubHeight = 4f });

        Assert.Equal(10f, made.Parts[0].Anchors![0].Length, 3);
    }

    [Fact]
    public void AGearWithNoBoreHasNothingMarkedOnIt()
    {
        var made = Gears.Build(Bored with { Bore = BoreShape.None });

        Assert.Empty(made.Parts[0].Anchors!);
    }

    /// <summary>
    /// A mate is built about its own axis and then moved beside the gear to print. The hole moves
    /// with it - which was the whole reason for reading it off the placed part.
    /// </summary>
    [Fact]
    public void AMatesHoleIsWhereTheMateIs()
    {
        var made = Gears.Build(Bored with
        {
            PartnerTeeth = 12, MateBore = BoreShape.Round, MateBoreSize = 3f
        });

        Assert.Equal(2, made.Parts.Count);

        var mate = made.Parts[1];
        Assert.Equal(3f, mate.Anchors![0].Size, 3);
        Assert.Equal(mate.Mesh.ComputeBounds().Center.X, mate.Anchors[0].At.X, 3);
    }

    [Fact]
    public void ADShaftHoleSaysWhichWayItsFlatFaces()
    {
        var made = Gears.Build(Bored with { Bore = BoreShape.DShaft, BoreSize = 5f, BoreFlat = 4.5f });
        var bore = made.Parts[0].Anchors![0];

        Assert.Equal(BoreShape.DShaft, bore.Shape);
        Assert.Equal(Vector3.UnitX, bore.Across);
        Assert.Equal(4.5f, bore.Flat, 3);
        Assert.True(bore.IsKeyed);
    }

    /// <summary>
    /// A cut-away gear is a disc with teeth over part of its rim, so the middle of its box is
    /// millimetres off the shaft. Reading the axis off the box put every fitted shaft exactly
    /// that far out, which is what it looked like on screen.
    /// </summary>
    [Fact]
    public void ACutAwayGearsHoleIsOnItsAxisAndNotInTheMiddleOfItsBox()
    {
        var made = Gears.Build(Bored with { KeptTeeth = 5 });
        var part = made.Parts[0];

        var box = part.Mesh.ComputeBounds().Center;
        Assert.True(MathF.Abs(box.X) > 1f, "the box should be off the axis, or this proves nothing");

        Assert.Equal(0f, part.Anchors![0].At.X, 3);
        Assert.Equal(0f, part.Anchors[0].At.Y, 3);
    }

    // --- Where it goes when the object moves ------------------------------------------------

    [Fact]
    public void CentringAnObjectCarriesItsMarksWithTheGeometry()
    {
        var mesh = MeshTransform.Transformed(Primitives.Box(10, 10, 20), Matrix4x4.CreateTranslation(0, 0, 10));
        var o = new SceneObject("Part", mesh)
        {
            Anchors = [new Anchor(AnchorKind.Bore, "Hole", Vector3.Zero, Vector3.UnitZ, Vector3.Zero, 4f, 20f)]
        }.Centred();

        // The mesh came down by 10, so the hole did too - and in world space it has not moved.
        Assert.Equal(-10f, o.Anchors[0].At.Z, 3);
        Assert.Equal(0f, o.WorldAnchors().First().At.Z, 3);
    }

    [Fact]
    public void TurningAndMovingAnObjectTakesItsMarksAlong()
    {
        var o = new SceneObject("Part", Primitives.Box(10, 10, 20))
        {
            Anchors = [new Anchor(AnchorKind.Bore, "Hole", new Vector3(0, 0, -10), Vector3.UnitZ, Vector3.UnitX, 4f, 20f)],
            Rotation = new Vector3(90f, 0f, 0f),
            Position = new Vector3(30f, 0f, 5f)
        };

        var world = o.WorldAnchors().First();

        // Laid on its side: what ran up Z now runs along -Y.
        Assert.Equal(0f, world.Along.Z, 3);
        Assert.Equal(-1f, world.Along.Y, 3);
        Assert.Equal(30f, world.At.X, 3);
    }

    [Fact]
    public void ScalingAnObjectScalesWhatIsMarkedOnIt()
    {
        var o = new SceneObject("Part", Primitives.Box(10, 10, 20))
        {
            Anchors = [new Anchor(AnchorKind.Bore, "Hole", Vector3.Zero, Vector3.UnitZ, Vector3.UnitX, 4f, 20f)],
            Scale = new Vector3(2f, 2f, 3f)
        };

        var world = o.WorldAnchors().First();

        Assert.Equal(60f, world.Length, 3);   // along the axis
        Assert.Equal(8f, world.Size, 3);      // across it
        Assert.Equal(1f, world.Along.Length(), 3);
    }

    /// <summary>A mark describes the shape it was made on, and new geometry is not that shape.</summary>
    [Fact]
    public void ReplacingTheGeometryDropsWhatWasMarkedOnTheOldShape()
    {
        var o = new SceneObject("Part", Primitives.Box(10, 10, 20))
        {
            Anchors = [new Anchor(AnchorKind.Bore, "Hole", Vector3.Zero, Vector3.UnitZ, Vector3.Zero, 4f, 20f)]
        };

        o.Mesh = Primitives.Box(30, 30, 30);

        Assert.Empty(o.Anchors);
    }

    [Fact]
    public void CopyingAnObjectCopiesItsMarks()
    {
        var o = new SceneObject("Part", Primitives.Box(10, 10, 20))
        {
            Anchors = [new Anchor(AnchorKind.Bore, "Hole", Vector3.Zero, Vector3.UnitZ, Vector3.Zero, 4f, 20f)]
        };

        Assert.Equal(4f, o.Clone().Anchors[0].Size, 3);
    }

    [Fact]
    public void MarksSurviveTheProjectFile()
    {
        var scene = new Scene();
        scene.Objects.Add(new SceneObject("Gear", Primitives.Box(10, 10, 6))
        {
            Anchors = [new Anchor(
                AnchorKind.Bore, "Shaft hole", new Vector3(1, 2, -3), Vector3.UnitZ, Vector3.UnitX,
                5f, 6f, BoreShape.DShaft, 4.5f)]
        });
        scene.Objects.Add(new SceneObject("Plain", Primitives.Box(5, 5, 5)));

        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + SceneSerializer.Extension);
        try
        {
            SceneSerializer.Save(path, scene);
            var loaded = SceneSerializer.Load(path);

            var back = loaded[0].Anchors[0];
            Assert.Equal("Shaft hole", back.Name);
            Assert.Equal(BoreShape.DShaft, back.Shape);
            Assert.Equal(new Vector3(1, 2, -3), back.At);
            Assert.Equal(Vector3.UnitX, back.Across);
            Assert.Equal(5f, back.Size, 3);
            Assert.Equal(6f, back.Length, 3);
            Assert.Equal(4.5f, back.Flat, 3);

            Assert.Empty(loaded[1].Anchors);
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }

    // --- The pivot ---------------------------------------------------------------------------

    [Fact]
    public void AnObjectTurnsAboutTheMiddleOfItsBoxUntilSomebodySaysOtherwise()
    {
        var mesh = MeshTransform.Transformed(Primitives.Box(10, 10, 20), Matrix4x4.CreateTranslation(4, 0, 10));
        var o = new SceneObject("Part", mesh).Centred();

        Assert.False(o.PivotIsOwn);
        Assert.Equal(new Vector3(4, 0, 10), o.WorldCentre);
        Assert.Equal(o.WorldBounds.Center, o.WorldCentre);
    }

    [Fact]
    public void SettingThePivotMovesNothingOnThePlate()
    {
        var o = new SceneObject("Part", Primitives.Box(10, 10, 20)) { Position = new Vector3(30, 0, 10) };
        var was = o.WorldBounds;

        // A corner of it, in its own coordinates.
        o.CentredOn(new Vector3(5, 5, -10));

        Assert.True(o.PivotIsOwn);
        Assert.Equal(was.Min, o.WorldBounds.Min);
        Assert.Equal(was.Max, o.WorldBounds.Max);

        // And the position now reads that corner.
        Assert.Equal(new Vector3(35, 5, 0), o.Position);
        Assert.Equal(new Vector3(35, 5, 0), o.WorldCentre);
    }

    [Fact]
    public void APivotSomebodyChoseIsNotTidiedAwayByTheNextTool()
    {
        var o = new SceneObject("Part", Primitives.Box(10, 10, 20));
        o.CentredOn(new Vector3(5, 5, -10));

        var position = o.Position;
        o.Centred();

        Assert.True(o.PivotIsOwn);
        Assert.Equal(position, o.Position);
    }

    [Fact]
    public void ReplacingTheGeometryTakesThePivotWithIt()
    {
        var o = new SceneObject("Part", Primitives.Box(10, 10, 20));
        o.CentredOn(new Vector3(5, 5, -10));

        o.Mesh = Primitives.Box(30, 30, 30);

        Assert.False(o.PivotIsOwn);
    }

    [Fact]
    public void ThePivotSurvivesTheProjectFileAndACopy()
    {
        var scene = new Scene();
        var chosen = new SceneObject("Chosen", Primitives.Box(10, 10, 20));
        chosen.CentredOn(new Vector3(5, 5, -10));
        scene.Objects.Add(chosen);
        scene.Objects.Add(new SceneObject("Plain", Primitives.Box(10, 10, 10)));

        Assert.True(chosen.Clone().PivotIsOwn);

        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + SceneSerializer.Extension);
        try
        {
            SceneSerializer.Save(path, scene);
            var loaded = SceneSerializer.Load(path);

            Assert.True(loaded[0].PivotIsOwn);
            Assert.False(loaded[1].PivotIsOwn);
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }

    [Fact]
    public void MovingThePivotUndoesBackToWhereItWas()
    {
        var scene = new Scene();
        var o = new SceneObject("Part", Primitives.Box(10, 10, 20));
        scene.Objects.Add(o);

        var undo = new UndoStack(scene);
        undo.Execute(new PivotCommand("Set pivot", o, new Vector3(5, 5, -10), own: true, wasOwn: false));

        Assert.True(o.PivotIsOwn);
        Assert.Equal(new Vector3(5, 5, -10), o.Position);

        undo.Undo();

        Assert.False(o.PivotIsOwn);
        Assert.Equal(Vector3.Zero, o.Position);
        Assert.Equal(Vector3.Zero, o.Mesh.ComputeBounds().Center);
    }

    /// <summary>
    /// Two cut-away gears are lined up when their shafts are, not when the outlines of their
    /// teeth are - which is what aligning them by their boxes did.
    /// </summary>
    [Fact]
    public void LiningTwoGearsUpGoesByTheirShafts()
    {
        var made = Gears.Build(Bored with { KeptTeeth = 5 });
        var mesh = made.Parts[0].Mesh;
        var marked = made.Parts[0].Anchors!;

        SceneObject Gear(string name, Vector3 at)
        {
            var o = new SceneObject(name, mesh.Clone()) { Anchors = marked };
            o.CentredOn(new Vector3(marked[0].At.X, marked[0].At.Y, o.Mesh.ComputeBounds().Center.Z));
            o.Position += at;
            return o;
        }

        var first = Gear("A", new Vector3(40, 15, 0));
        var second = Gear("B", Vector3.Zero);

        var offsets = AlignTools.Offsets([first, second], Axis.X, AlignMode.Centre);
        first.Position += offsets[0];

        Assert.Equal(second.WorldCentre.X, first.WorldCentre.X, 3);
        Assert.Equal(0f, first.PositionX, 3);
    }
}
