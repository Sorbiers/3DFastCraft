using System.Numerics;
using FastCraft3D.Geometry;
using FastCraft3D.Model;
using FastCraft3D.Model.Commands;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>
/// Repeating the selection round a circle.
///
/// The straight repeat cannot reach any of this: a bolt circle, a ring of teeth, crenellations
/// round a tower, and - once there is a rise per copy - a spiral stair. All of it used to be a
/// cosine and a sine typed into a position box, once per copy, with the sign of the angle got
/// wrong at least once a model.
/// </summary>
public class RepeatRingTests
{
    private static SceneObject Box(string name, float w, float d, float h) =>
        new(name, Primitives.Box(w, d, h));

    private static Ring Made(IReadOnlyList<SceneObject> sources, RingSettings settings)
    {
        int n = 0;
        return RepeatArray.MakeRing(sources, settings, name => $"{name} {++n}");
    }

    /// <summary>The common case: spread evenly the whole way round.</summary>
    private static RingSettings FullTurn(int copies, float radius, bool face = true) =>
        new(copies, Vector2.Zero, radius, 360f / (copies + 1), 0f, face, Vector3.Zero);

    private static float FromTheAxis(SceneObject o) =>
        new Vector2(o.PositionX, o.PositionY).Length();

    private static SceneObject Pin()
    {
        var pin = Box("pin", 3, 3, 12);
        pin.Position = new Vector3(30, 0, 6);
        return pin;
    }

    [Fact]
    public void EveryCopyLandsOnTheRadius()
    {
        var ring = Made([Pin()], FullTurn(5, 30f));

        Assert.Equal(5, ring.Copies.Count);
        Assert.All(ring.Copies, c => Assert.Equal(30f, FromTheAxis(c), 3));
    }

    /// <summary>
    /// The off-by-one that a circular array exists to get wrong. Copies are counted over and above
    /// the original, so a full turn divides by six, not five - otherwise the last copy lands on top
    /// of the original and there is a gap where the sixth should have been.
    /// </summary>
    [Fact]
    public void TheLastOfSixLeavesRoomForTheFirst()
    {
        var ring = Made([Pin()], FullTurn(5, 30f));

        var last = ring.Copies[4];
        float at300 = 300f * MathF.PI / 180f;

        Assert.Equal(30f * MathF.Cos(at300), last.PositionX, 2);
        Assert.Equal(30f * MathF.Sin(at300), last.PositionY, 2);

        // And nothing sits where the original is.
        Assert.All(ring.Copies, c => Assert.True(Vector3.Distance(c.Position, new Vector3(30, 0, 6)) > 1f));
    }

    /// <summary>Anticlockwise seen from above, which is what the dialog promises.</summary>
    [Fact]
    public void TheFirstCopyTurnsTowardsPositiveY()
    {
        var ring = Made([Pin()], FullTurn(3, 30f));

        Assert.Equal(0f, ring.Copies[0].PositionX, 2);
        Assert.Equal(30f, ring.Copies[0].PositionY, 2);
    }

    [Fact]
    public void FacingTheCentreTurnsEachCopyByTheAngleItTravelled()
    {
        var ring = Made([Pin()], FullTurn(5, 30f));

        Assert.Equal(60f, ring.Copies[0].RotationZ, 3);
        Assert.Equal(300f, ring.Copies[4].RotationZ, 3);
    }

    /// <summary>A round hole does not care which way it points, so a bolt circle leaves it alone.</summary>
    [Fact]
    public void ABoltCircleLeavesEveryHolePointingTheSameWay()
    {
        var ring = Made([Pin()], FullTurn(5, 30f, face: false));

        Assert.All(ring.Copies, c => Assert.Equal(0f, c.RotationZ, 3));
        Assert.All(ring.Copies, c => Assert.Equal(30f, FromTheAxis(c), 3));
    }

    /// <summary>
    /// Asking for a radius the selection is not at has to move the selection. A ring whose first
    /// object is still out at the old radius is not a ring.
    /// </summary>
    [Fact]
    public void ADifferentRadiusSeatsTheOriginalOnTheCircleToo()
    {
        var pin = Pin();
        var ring = Made([pin], FullTurn(5, 45f));

        Assert.Equal(45f, ring.Seats[0].Position.X, 3);
        Assert.Equal(0f, ring.Seats[0].Position.Y, 3);
        Assert.All(ring.Copies, c => Assert.Equal(45f, FromTheAxis(c), 3));

        // Moving in or out along the radius does not turn anything, and does not lift anything.
        Assert.Equal(6f, ring.Seats[0].Position.Z, 3);
        Assert.Equal(Vector3.Zero, ring.Seats[0].Rotation);
    }

    /// <summary>The seat is handed back as data, so the caller decides when - and whether - it happens.</summary>
    [Fact]
    public void MakingTheRingDoesNotMoveTheSourceItself()
    {
        var pin = Pin();
        Made([pin], FullTurn(5, 45f));

        Assert.Equal(30f, pin.PositionX, 3);
    }

    /// <summary>Repeating where it already stands must not push a no-op move onto the undo stack.</summary>
    [Fact]
    public void TheRadiusItIsAlreadyAtSeatsItExactlyWhereItIs()
    {
        var pin = Pin();
        var ring = Made([pin], FullTurn(5, 30f));

        Assert.Equal(TransformState.Capture(pin), ring.Seats[0]);
    }

    /// <summary>
    /// Three parts that make up one bracket have to keep their arrangement while the bracket goes
    /// round. Snapping each of them onto the circle separately would take the bracket apart.
    /// </summary>
    [Fact]
    public void AGroupTravelsAsOnePiece()
    {
        var a = Box("a", 4, 4, 4);
        var b = Box("b", 4, 4, 4);
        a.Position = new Vector3(20, 0, 2);
        b.Position = new Vector3(24, 0, 2);

        var ring = Made([a, b], FullTurn(3, 40f));

        // The middle of the pair sits at 22 and is asked for 40, so both move out by 18.
        Assert.Equal(38f, ring.Seats[0].Position.X, 3);
        Assert.Equal(42f, ring.Seats[1].Position.X, 3);

        // Six copies, and each station holds the pair 4 mm apart as it was.
        Assert.Equal(6, ring.Copies.Count);
        Assert.Equal(4f, Vector3.Distance(ring.Copies[0].Position, ring.Copies[1].Position), 3);
        Assert.Equal(4f, Vector3.Distance(ring.Copies[4].Position, ring.Copies[5].Position), 3);
    }

    /// <summary>Sitting on the centre there is no direction to go out along, so +X is chosen.</summary>
    [Fact]
    public void SomethingOnTheCentreGoesOutAlongX()
    {
        var ring = Made([Box("o", 4, 4, 4)], FullTurn(3, 25f));

        Assert.Equal(25f, ring.Seats[0].Position.X, 3);
        Assert.Equal(0f, ring.Seats[0].Position.Y, 3);
    }

    /// <summary>A ring with a rise is a spiral stair, which the straight repeat cannot make at all.</summary>
    [Fact]
    public void ARiseTurnsTheRingIntoASpiralStair()
    {
        var tread = Box("tread", 60, 20, 4);
        tread.Position = new Vector3(35, 0, 2);

        var ring = Made([tread], new RingSettings(11, Vector2.Zero, 35f, 30f, 20f, true, Vector3.Zero));

        Assert.Equal(11, ring.Copies.Count);
        Assert.Equal(22f, ring.Copies[0].PositionZ, 3);
        Assert.Equal(222f, ring.Copies[10].PositionZ, 3);

        // Eleven treads at 30 degrees is 330: one short of the whole turn, so the twelfth place is
        // where the original stands.
        Assert.Equal(330f, ring.Copies[10].RotationZ, 3);
        Assert.All(ring.Copies, c => Assert.Equal(35f, FromTheAxis(c), 3));
    }

    /// <summary>Turning about a vertical axis cannot change a height.</summary>
    [Fact]
    public void WithoutARiseEverythingStaysAtTheSameHeight()
    {
        var ring = Made([Pin()], FullTurn(5, 30f));

        Assert.All(ring.Copies, c => Assert.Equal(6f, c.PositionZ, 3));
    }

    [Fact]
    public void GrowingMakesEachCopyLargerThanTheOneBefore()
    {
        var block = Box("block", 4, 4, 4);
        block.Position = new Vector3(20, 0, 2);

        var ring = Made([block], new RingSettings(3, Vector2.Zero, 20f, 90f, 0f, true, Vector3.One));

        Assert.Equal(5f, ring.Copies[0].SizeX, 3);
        Assert.Equal(6f, ring.Copies[1].SizeX, 3);
        Assert.Equal(7f, ring.Copies[2].SizeZ, 3);
    }

    /// <summary>
    /// A radius of nothing is a rosette - an off-centre part turned about the middle - not a
    /// mistake to refuse.
    /// </summary>
    [Fact]
    public void ARadiusOfNothingSpinsItInPlace()
    {
        var petal = Box("petal", 10, 2, 2);
        petal.Position = new Vector3(30, 0, 1);

        var ring = Made([petal], FullTurn(5, 0f));

        Assert.All(ring.Copies, c => Assert.Equal(0f, FromTheAxis(c), 3));
        Assert.Equal(60f, ring.Copies[0].RotationZ, 3);
    }

    [Fact]
    public void ACentreAwayFromTheOriginIsWhatItTurnsAbout()
    {
        var o = Box("o", 4, 4, 4);
        o.Position = new Vector3(60, 40, 0);

        var ring = Made([o], new RingSettings(3, new Vector2(50, 40), 10f, 90f, 0f, true, Vector3.Zero));

        // Already 10 from that centre, so nothing has to move first.
        Assert.Equal(60f, ring.Seats[0].Position.X, 3);

        // A quarter turn takes it from due east of the centre to due north of it.
        Assert.Equal(50f, ring.Copies[0].PositionX, 3);
        Assert.Equal(50f, ring.Copies[0].PositionY, 3);
    }

    [Fact]
    public void MoreCopiesThanTheLimitIsAMistakeRatherThanAModel()
    {
        var ring = Made([Pin()], new RingSettings(5000, Vector2.Zero, 10f, 1f, 0f, true, Vector3.Zero));

        Assert.Equal(RepeatArray.MaximumCopies, ring.Copies.Count);
    }

    /// <summary>The names have to be ones the scene will take, or the copies collide.</summary>
    [Fact]
    public void EveryCopyIsNamedThroughTheSceneItJoins()
    {
        var ring = Made([Pin()], FullTurn(5, 30f));

        Assert.Equal(5, ring.Copies.Select(c => c.Name).Distinct().Count());
    }

    /// <summary>
    /// And the naming has to count what it has already given out, not only what is on the plate.
    ///
    /// Handing Scene.UniqueName straight to the ring asked it for twelve names before any of the
    /// copies had joined the scene, so it answered "Tread 2" every time: a spiral stair whose
    /// twelve treads all had the same name, indistinguishable in the object list.
    /// </summary>
    [Fact]
    public void TheSceneDoesNotGiveOutOneNameTwiceInAnOperation()
    {
        var scene = new Scene();
        scene.Objects.Add(new SceneObject("Tread", Primitives.Box(60, 20, 5)));

        List<string> given = [];
        for (int i = 0; i < 5; i++) given.Add(scene.UniqueName("Tread", given));

        Assert.Equal(["Tread 2", "Tread 3", "Tread 4", "Tread 5", "Tread 6"], given);
    }

    /// <summary>The same thing end to end: a ring of treads is a dozen distinct objects.</summary>
    [Fact]
    public void AStairOfTwelveTreadsHasTwelveNames()
    {
        var scene = new Scene();
        var tread = Box("Tread", 62, 22, 5);
        tread.Position = new Vector3(42, 0, 8);
        scene.Objects.Add(tread);

        List<string> given = [];
        var ring = RepeatArray.MakeRing([tread], FullTurn(11, 42f), baseName =>
        {
            string name = scene.UniqueName(baseName, given);
            given.Add(name);
            return name;
        });

        Assert.Equal(11, ring.Copies.Select(c => c.Name).Distinct().Count());
        Assert.DoesNotContain(ring.Copies, c => c.Name == "Tread");
    }

    /// <summary>This is what seeds the radius box, so opening the dialog changes nothing by itself.</summary>
    [Fact]
    public void TheDistanceReadsWhereTheSelectionStandsNow()
    {
        var a = Box("a", 2, 2, 2);
        var b = Box("b", 2, 2, 2);
        a.Position = new Vector3(0, 30, 0);
        b.Position = new Vector3(0, 50, 0);

        Assert.Equal(30f, RepeatArray.DistanceFromCentre([a], Vector2.Zero), 3);
        Assert.Equal(40f, RepeatArray.DistanceFromCentre([a, b], Vector2.Zero), 3);
        Assert.Equal(0f, RepeatArray.DistanceFromCentre([], Vector2.Zero), 3);
    }

    /// <summary>
    /// Seating the original and adding the copies are two commands but one thing that happened,
    /// and one Ctrl+Z has to take all of it back. Undoing to a half-made ring - the original moved
    /// out to a radius with nothing around it - would be worse than not having made one.
    /// </summary>
    [Fact]
    public void TheWholeRingIsOneUndoStep()
    {
        var scene = new Scene();
        var pin = Pin();
        scene.Objects.Add(pin);

        var before = new[] { TransformState.Capture(pin) };
        var ring = RepeatArray.MakeRing([pin], FullTurn(5, 45f), name => scene.UniqueName(name));

        var undo = new UndoStack(scene);
        undo.Execute(new CompoundCommand("Repeat round a circle",
        [
            new TransformCommand("Repeat", [pin], before, ring.Seats),
            new AddObjectsCommand("Repeat", ring.Copies),
        ]));

        Assert.Equal(6, scene.Objects.Count);
        Assert.Equal(45f, pin.PositionX, 3);

        undo.Undo();

        Assert.Single(scene.Objects);
        Assert.Equal(30f, pin.PositionX, 3);

        undo.Redo();

        Assert.Equal(6, scene.Objects.Count);
        Assert.Equal(45f, pin.PositionX, 3);
    }
}
