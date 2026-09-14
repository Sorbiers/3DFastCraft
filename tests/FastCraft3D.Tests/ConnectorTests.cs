using System.Numerics;
using FastCraft3D.Geometry;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>Split with connectors: where the pins go on the cut face, and what cutting them in leaves.</summary>
public class ConnectorTests
{
    private static ConnectorOptions Pins(int count, float diameter = 5f, float edge = 3f, float clearance = 0.2f) =>
        ConnectorOptions.Default with { Count = count, Diameter = diameter, EdgeDistance = edge, Clearance = clearance };

    /// <summary>A hollow cube: a 30 mm box with a 24 mm box inside it turned inside out - 3 mm walls.</summary>
    private static Mesh HollowBox()
    {
        var cavity = Primitives.Box(24, 24, 24).Clone();
        cavity.FlipWinding();
        return Mesh.Combine([Primitives.Box(30, 30, 30), cavity]);
    }

    private static float SignedVolume(Mesh mesh)
    {
        double total = 0;
        for (int t = 0; t + 2 < mesh.Indices.Count; t += 3)
        {
            Vector3 a = mesh.Positions[mesh.Indices[t]];
            Vector3 b = mesh.Positions[mesh.Indices[t + 1]];
            Vector3 c = mesh.Positions[mesh.Indices[t + 2]];
            total += Vector3.Dot(a, Vector3.Cross(b, c));
        }

        return (float)(total / 6.0);
    }

    /// <summary>Distance from a point on a centred square face to its nearest side.</summary>
    private static float ToSide(Vector3 p, float half) => half - MathF.Max(MathF.Abs(p.X), MathF.Abs(p.Y));

    [Fact]
    public void PinsGoInsideTheCutFaceClearOfItsEdgesAndApart()
    {
        var points = Connectors.Place(Primitives.Box(40, 40, 40), Vector3.UnitZ, 0f, Pins(2));

        Assert.Equal(2, points.Count);
        foreach (var p in points)
        {
            Assert.True(ToSide(p, 20f) >= 2.5f + 0.2f + 3f - 1e-2f, $"{p} is too near an edge");
            Assert.Equal(0f, p.Z, 3);
        }

        Assert.True(Vector3.Distance(points[0], points[1]) >= 2f * 2.7f + 3f);
    }

    /// <summary>
    /// Four on a square go out towards its corners, the wall beside each hole the width asked for.
    /// They came out a quarter of the way in from every side before - evenly spread, and much nearer
    /// each other than registration wants.
    /// </summary>
    [Fact]
    public void FourPinsOnASquareFaceGoOutTowardsItsCorners()
    {
        var points = Connectors.Place(Primitives.Box(40, 40, 40), Vector3.UnitZ, 0f, Pins(4));

        Assert.Equal(4, points.Count);
        Assert.Equal(4, points.Select(p => (MathF.Sign(p.X), MathF.Sign(p.Y))).Distinct().Count());

        // Radius 2.5, clearance 0.2, then 3 mm of wall: 5.7 from the side.
        foreach (var p in points)
        {
            Assert.Equal(5.7f, ToSide(p, 20f), 1);
            Assert.Equal(MathF.Abs(p.X), MathF.Abs(p.Y), 1);
        }
    }

    [Fact]
    public void TheWallBesideAHoleIsWhatWasAskedFor()
    {
        var near = Connectors.Place(Primitives.Box(40, 40, 40), Vector3.UnitZ, 0f, Pins(4, edge: 2f));
        var far = Connectors.Place(Primitives.Box(40, 40, 40), Vector3.UnitZ, 0f, Pins(4, edge: 8f));
        var tooNear = Connectors.Place(Primitives.Box(40, 40, 40), Vector3.UnitZ, 0f, Pins(4, edge: 0f));

        Assert.All(near, p => Assert.Equal(4.7f, ToSide(p, 20f), 1));

        // Asking for no wall at all still leaves the floor under it: radius, clearance, one printed line.
        Assert.All(tooNear, p => Assert.Equal(2.5f + 0.2f + Connectors.MinimumWall, ToSide(p, 20f), 1));
        Assert.All(far, p => Assert.Equal(10.7f, ToSide(p, 20f), 1));
    }

    [Fact]
    public void ThreePinsOnARoundFaceAreEvenlySpaced()
    {
        var points = Connectors.Place(Primitives.Prism(20, 40, 64), Vector3.UnitZ, 0f, Pins(3));

        Assert.Equal(3, points.Count);

        var fromMiddle = points.Select(p => new Vector2(p.X, p.Y).Length()).ToList();
        Assert.True(fromMiddle.Max() - fromMiddle.Min() < 1f, "not the same distance from the middle");
        Assert.InRange(fromMiddle.Average(), 13.5f, 15f); // 20 less 5.7, near enough on 64 sides

        var between = new[]
        {
            Vector3.Distance(points[0], points[1]),
            Vector3.Distance(points[1], points[2]),
            Vector3.Distance(points[2], points[0])
        };
        Assert.True(between.Max() - between.Min() < 2f, "not evenly spaced round");
    }

    [Fact]
    public void TwoPinsSitEitherSideOfTheMiddleTowardsTheEnds()
    {
        var points = Connectors.Place(Primitives.Box(60, 30, 30), Vector3.UnitZ, 0f, Pins(2));

        Assert.Equal(2, points.Count);
        Assert.True((points[0] + points[1]).Length() < 1.5f, "not balanced about the middle");
        Assert.InRange(MathF.Abs(points[0].X), 23.5f, 25f); // 30 less 5.7
    }

    /// <summary>One pin has no outward to go, and stays in the middle.</summary>
    [Fact]
    public void ASinglePinStaysInTheMiddle()
    {
        var point = Assert.Single(Connectors.Place(Primitives.Box(40, 40, 40), Vector3.UnitZ, 0f, Pins(1)));
        Assert.True(new Vector2(point.X, point.Y).Length() < 1f);
    }

    /// <summary>A section with a hole in it: pins in the solid band, none in the hole or its rim.</summary>
    [Fact]
    public void ARingsHoleIsNoPlaceForAPin()
    {
        // Shallow, since a round tube is thinner a few millimetres either side of its middle.
        var points = Connectors.Place(Primitives.Torus(20, 7, 64, 32), Vector3.UnitZ, 0f, Pins(4, diameter: 4f) with { Depth = 2f });

        Assert.NotEmpty(points);
        foreach (var p in points)
        {
            float from = new Vector2(p.X, p.Y).Length();
            Assert.InRange(from, 13f + 5.2f - 0.6f, 27f - 5.2f + 0.6f); // radius 2, clearance 0.2, wall 3
        }
    }

    /// <summary>
    /// A thin-walled part: the From edge typed in is the margin. It used to be overruled by a fixed
    /// 1.5 mm, and a hollow box got no pins and no reason why.
    /// </summary>
    [Fact]
    public void AThinWallTakesConnectorsWhenTheWallAskedForFits()
    {
        var box = HollowBox();

        // 1.6 mm with 0.1 clearance and 1 mm either side needs 3.8 mm; the walls are 3, and even
        // across the inside of a corner - where a hollow square is thickest - only about 3.5.
        var tooMuch = Connectors.Survey(box, Vector3.UnitZ, 0f, Pins(4, diameter: 1.6f, edge: 1f, clearance: 0.1f));
        Assert.Empty(tooMuch.Points);
        Assert.Equal(3.8f, tooMuch.WallNeeded, 2);
        Assert.InRange(tooMuch.ThickestWall, 2.4f, 3.6f);

        // 0.4 either side needs 2.6 mm, which fits.
        var fits = Connectors.Survey(box, Vector3.UnitZ, 0f, Pins(4, diameter: 1.6f, edge: 0.4f, clearance: 0.1f));
        Assert.NotEmpty(fits.Points);
        foreach (var p in fits.Points)
        {
            float outer = MathF.Max(MathF.Abs(p.X), MathF.Abs(p.Y));
            Assert.InRange(outer, 12f, 15f); // in the wall, between the inner and outer faces
        }
    }

    [Fact]
    public void UprightConnectorsCannotCrossAnUprightCutNorLevelOnesALevelCut()
    {
        Assert.Null(Connectors.Axis(Vector3.UnitX, ConnectorDirection.Vertical));
        Assert.Null(Connectors.Axis(Vector3.UnitZ, ConnectorDirection.Horizontal));
        Assert.Equal(Vector3.UnitZ, Connectors.Axis(Vector3.UnitZ, ConnectorDirection.Vertical));

        var layout = Connectors.Survey(Primitives.Box(40, 40, 40), Vector3.UnitX, 0f,
            Pins(2) with { Direction = ConnectorDirection.Vertical });
        Assert.False(layout.Crosses);
        Assert.Empty(layout.Points);
    }

    /// <summary>On a tilted cut, vertical pins stay vertical - the top half still lifts straight off.</summary>
    [Fact]
    public void OnATiltedCutVerticalConnectorsStandUprightAndBothHalvesStayWhole()
    {
        var block = Primitives.Box(40, 40, 40);
        var tilted = Vector3.Normalize(new Vector3(0f, 0.5f, 0.866f)); // 30 degrees off level
        var options = Pins(2) with { Direction = ConnectorDirection.Vertical };

        Assert.Equal(Vector3.UnitZ, Connectors.Axis(tilted, ConnectorDirection.Vertical));

        var (front, back) = PlaneSplit.Split(block, tilted, 0f, SplitKeep.Both);
        var points = Connectors.Place(block, tilted, 0f, options);
        Assert.NotEmpty(points);

        var joined = Connectors.Join(front!, back!, points, tilted, options);
        Assert.NotNull(joined);
        Assert.True(joined.Value.Front.CheckHealth().IsWatertight);
        Assert.True(joined.Value.Back.CheckHealth().IsWatertight);
    }

    private static Bounds BoxAt(float sx, float sy, float sz, Vector3 centre) =>
        MeshTransform.Transformed(Primitives.Box(sx, sy, sz), Matrix4x4.CreateTranslation(centre)).ComputeBounds();

    [Fact]
    public void OnePartStandingOnAnotherShareTheLevelFace()
    {
        var basement = BoxAt(40, 40, 10, new Vector3(0, 0, 5));  // 0 to 10
        var floor = BoxAt(40, 40, 10, new Vector3(0, 0, 15));    // 10 to 20

        var contact = Connectors.SharedFace(basement, floor);

        Assert.NotNull(contact);
        Assert.Equal(Vector3.UnitZ, contact.Normal);
        Assert.Equal(10f, contact.Offset, 3);
        Assert.True(contact.SecondIsFront);
    }

    /// <summary>Laid against each other by eye, and a hair apart: still resting on each other.</summary>
    [Fact]
    public void PartsSideBySideAHairApartStillShareAFace()
    {
        var left = BoxAt(10, 20, 20, new Vector3(-5.2f, 0, 10));   // right face at -0.2
        var right = BoxAt(10, 20, 20, new Vector3(5.1f, 0, 10));   // left face at 0.1

        var contact = Connectors.SharedFace(right, left);

        Assert.NotNull(contact);
        Assert.Equal(Vector3.UnitX, contact.Normal);
        Assert.Equal(-0.05f, contact.Offset, 3);
        Assert.False(contact.SecondIsFront); // the first, on the right, is the one X points into
        Assert.Equal(0.3f, contact.Gap, 3);
    }

    [Fact]
    public void PartsApartOrOverlappingShareNothing()
    {
        var one = BoxAt(20, 20, 10, new Vector3(0, 0, 5));
        Assert.Null(Connectors.SharedFace(one, BoxAt(20, 20, 10, new Vector3(0, 0, 17))));  // 2 mm gap
        Assert.Null(Connectors.SharedFace(one, BoxAt(20, 20, 10, new Vector3(30, 0, 15)))); // not over it
    }

    /// <summary>
    /// A floor on a basement: pins only where both have material. The basement is a box of walls
    /// round an open inside, so the pins go into its walls - not into the floor over its rooms.
    /// </summary>
    [Fact]
    public void ConnectorsBetweenTwoPartsGoWhereBothHaveMaterial()
    {
        // Walls 8 mm thick round rooms open at the top, 0 to 10; the floor on it, 10 to 20.
        var basement = MeshTransform.Transformed(
            FastCraft3D.Geometry.Csg.CsgSolid.Subtract(
                Primitives.Box(40, 40, 10),
                MeshTransform.Transformed(Primitives.Box(24, 24, 12), Matrix4x4.CreateTranslation(0, 0, 2f))),
            Matrix4x4.CreateTranslation(0, 0, 5));
        var floor = MeshTransform.Transformed(Primitives.Box(40, 40, 10), Matrix4x4.CreateTranslation(0, 0, 15));

        var contact = Connectors.SharedFace(basement.ComputeBounds(), floor.ComputeBounds());
        Assert.NotNull(contact);

        var layout = Connectors.Survey(floor, basement, contact, Pins(4, diameter: 3f, edge: 1f));

        Assert.NotEmpty(layout.Points);
        foreach (var p in layout.Points)
        {
            float outer = MathF.Max(MathF.Abs(p.X), MathF.Abs(p.Y));
            Assert.InRange(outer, 12f, 20f); // in the walls, between the rooms and the outside
        }

        var joined = Connectors.Join(floor, basement, layout.Points, contact.Normal, Pins(4, diameter: 3f, edge: 1f));
        Assert.NotNull(joined);
        Assert.True(joined.Value.Front.CheckHealth().IsWatertight);
        Assert.True(joined.Value.Back.CheckHealth().IsWatertight);
    }

    /// <summary>
    /// A pin deeper than the part is thick is placed, and said to break out, so the user is asked
    /// rather than refused: a pin through a lid is a thing people make on purpose. It was refused
    /// outright for a while, and a 12 mm pin through a 10 mm half reported a wall of 0 mm.
    /// </summary>
    [Fact]
    public void AConnectorDeeperThanThePartIsThickSaysItBreaksOut()
    {
        var slab = BoxAt(40, 40, 3, new Vector3(0, 0, 11.5f));     // 10 to 13: 3 mm thick
        var block = BoxAt(40, 40, 10, new Vector3(0, 0, 5));      // 0 to 10
        var slabMesh = MeshTransform.Transformed(Primitives.Box(40, 40, 3), Matrix4x4.CreateTranslation(0, 0, 11.5f));
        var blockMesh = MeshTransform.Transformed(Primitives.Box(40, 40, 10), Matrix4x4.CreateTranslation(0, 0, 5));

        var contact = Connectors.SharedFace(block, slab);
        Assert.NotNull(contact);

        var through = Connectors.Survey(slabMesh, blockMesh, contact, Pins(2) with { Depth = 6f });
        Assert.NotEmpty(through.Points);
        Assert.True(through.BreaksOut);
        Assert.True(through.ThickestWall > 30f, "the wall was read at the depth rather than at the face");

        var inside = Connectors.Survey(slabMesh, blockMesh, contact, Pins(2) with { Depth = 2f });
        Assert.NotEmpty(inside.Points);
        Assert.False(inside.BreaksOut);
    }

    [Fact]
    public void AFaceTooSmallTakesNoPinsRatherThanOneThroughItsSide() =>
        Assert.Empty(Connectors.Place(Primitives.Box(6, 6, 6), Vector3.UnitZ, 0f, Pins(2)));

    [Fact]
    public void WithSeparatePinsBothHalvesAreBoredAndStayWatertight()
    {
        var block = Primitives.Box(40, 40, 40);
        var (front, back) = PlaneSplit.Split(block, Axis.Z, 0f, SplitKeep.Both);
        var points = Connectors.Place(block, Vector3.UnitZ, 0f, Pins(2));

        var joined = Connectors.Join(front!, back!, points, Vector3.UnitZ, ConnectorOptions.Default);

        Assert.NotNull(joined);
        var (bored, boredBack, pins) = joined.Value;

        Assert.True(bored.CheckHealth().IsWatertight);
        Assert.True(boredBack.CheckHealth().IsWatertight);
        Assert.True(SignedVolume(bored) < SignedVolume(front!) - 1f, "no hole in the front half");
        Assert.True(SignedVolume(boredBack) < SignedVolume(back!) - 1f, "no hole in the back half");
        Assert.Equal(points.Count, pins.Count);
    }

    /// <summary>
    /// Pegs stand up out of the lower part, sockets go in the upper. They used to go on the part the
    /// normal points into - the top one on a level cut - and hung down off its underside, which
    /// cannot be printed without supports.
    /// </summary>
    [Fact]
    public void PegsStandUpOutOfTheLowerPart()
    {
        var block = Primitives.Box(40, 40, 40);
        var (upper, lower) = PlaneSplit.Split(block, Axis.Z, 0f, SplitKeep.Both); // front is on +Z
        var points = Connectors.Place(block, Vector3.UnitZ, 0f, Pins(2));

        var joined = Connectors.Join(upper!, lower!, points, Vector3.UnitZ,
            ConnectorOptions.Default with { Style = ConnectorStyle.Pegs });

        Assert.NotNull(joined);
        var (socketed, pegged, pins) = joined.Value;

        Assert.True(pegged.CheckHealth().IsWatertight);
        Assert.True(socketed.CheckHealth().IsWatertight);
        Assert.True(SignedVolume(pegged) > SignedVolume(lower!) + 1f, "no pegs on the lower part");
        Assert.True(SignedVolume(socketed) < SignedVolume(upper!) - 1f, "no sockets in the upper part");
        Assert.True(pegged.ComputeBounds().Max.Z > 1f, "the pegs do not stand up above the cut");
        Assert.Empty(pins);
    }

    /// <summary>The same when the cut faces down: the lower part is then the front one.</summary>
    [Fact]
    public void PegsStandUpOutOfTheLowerPartWhicheverWayTheCutFaces()
    {
        var block = Primitives.Box(40, 40, 40);
        var down = -Vector3.UnitZ;
        var (lower, upper) = PlaneSplit.Split(block, down, 0f, SplitKeep.Both); // front is on -Z
        var points = Connectors.Place(block, down, 0f, Pins(2));

        var joined = Connectors.Join(lower!, upper!, points, down,
            ConnectorOptions.Default with { Style = ConnectorStyle.Pegs });

        Assert.NotNull(joined);
        var (pegged, socketed, _) = joined.Value;

        Assert.True(SignedVolume(pegged) > SignedVolume(lower!) + 1f, "no pegs on the lower part");
        Assert.True(SignedVolume(socketed) < SignedVolume(upper!) - 1f, "no sockets in the upper part");
        Assert.True(pegged.ComputeBounds().Max.Z > 1f, "the pegs do not stand up above the cut");
    }
}
