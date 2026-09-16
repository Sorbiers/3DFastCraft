using System.Numerics;
using FastCraft3D.Geometry.Csg;

namespace FastCraft3D.Geometry.Engraving;

/// <summary>
/// Where the stud grid falls, in plate coordinates, so two faces can share one.
///
/// Each face measures from its own corner along its own axes, and the two faces of a cut - one
/// looking up, one looking down - need not agree on either. Studs on one and tubes on the other
/// laid out by each face for itself came out of step, so the grid is fixed once and both use it.
/// </summary>
/// <param name="StudMargin">How far inside the outline a stud has to stand.</param>
public sealed record BrickGrid(Vector3 Middle, Vector3 U, Vector3 V, int Across, int Along, float StudMargin)
{
    /// <summary>The grid a face lays out for itself: centred on its box, counted across its box.</summary>
    public static BrickGrid Of(FacePatch face, EngraveOptions options, float studMargin = BrickStuds.EdgeStudMargin) =>
        new(face.ToLocal((face.Min + face.Max) * 0.5f + new Vector2(options.OffsetU, options.OffsetV)),
            face.U, face.V,
            BrickStuds.StudsAlong(face.Size.X), BrickStuds.StudsAlong(face.Size.Y),
            studMargin);
}

/// <summary>
/// Studs on a face, or the hollow underside that goes over them, on the 8 mm grid of the common
/// building bricks - so a printed part takes real bricks on top, or sits on them.
///
/// Every size here follows from three numbers: studs 4.8 mm across, 8 mm apart, and a brick 0.1 mm
/// short of its grid on each side. A tube in the underside stands between four studs and touches
/// all of them, which puts its outside at 6.51 mm; a rod in a one-stud-wide brick stands between
/// two, at 3.2 mm; a wall touches the studs along it, 1.5 mm thick. Those are the published sizes,
/// and deriving them rather than typing them in is what keeps them agreeing when the fit is eased.
///
/// Any flat face takes them, not only a rectangle. The grid is laid over the face and a stud goes
/// wherever the whole of it fits inside the outline; the underside's hollow follows the outline in
/// by a wall's thickness, and a tube goes wherever the whole of it fits inside that. On a face that
/// is not a whole brick only the studs that happen to line up meet real bricks, which is the
/// nature of the thing rather than something to prevent.
///
/// Not a groove pattern, though it is chosen with them: studs are solids standing on the face and
/// the underside is a pocket with tubes in it, so both go to the boolean as solids.
/// </summary>
public static class BrickStuds
{
    public const float Pitch = 8f;
    public const float StudDiameter = 4.8f;
    public const float StudHeight = 1.7f;

    /// <summary>A brick is this much short of its grid on each side, so neighbours do not jam.</summary>
    public const float EdgePlay = 0.1f;

    /// <summary>The least material worth leaving over the hollow of an underside: about five layers.</summary>
    public const float RoofThickness = 1f;

    /// <summary>The depth of the hollow in a real brick, 9.6 mm tall with a 1 mm top.</summary>
    public const float BrickHollow = 8.6f;

    /// <summary>
    /// How far inside the face's edge a stud has to stand. The same as a brick's own play at its
    /// edge, so a rectangle exactly a whole number of studs still takes them all.
    /// </summary>
    public const float EdgeStudMargin = EdgePlay - 0.02f;

    /// <summary>
    /// How much deeper than a stud is tall the socket for it is, when studs join two printed
    /// halves: a stud that bottoms out in its socket holds the halves apart by the difference.
    /// </summary>
    public const float SocketClearance = 0.3f;

    /// <summary>
    /// The fit to start from on a filament printer. Printed studs come out a tenth or so fat and
    /// printed walls grip that much harder, so the exact brick sizes are too tight; PETG wants
    /// about −0.15. Measured on your own printer with the fit test, it is worth changing.
    /// </summary>
    public const float FdmFit = -0.1f;

    private const int Sides = 48;

    /// <summary>Round each end of a piece of wall swept along an outline.</summary>
    private const int CapSteps = 8;

    /// <summary>
    /// How far a stud or a tube runs into the material it stands on, so the two overlap and the
    /// join is inside the solid rather than on a shared face.
    /// </summary>
    private const float Embed = 0.2f;

    /// <summary>How far the hollow's cutter stands out past the face, so its opening is cut clean.</summary>
    private const float Lift = 0.5f;

    public static bool Handles(PatternKind kind) => kind is PatternKind.Studs or PatternKind.StudUnderside;

    public static float StudRadius(float fit) => StudDiameter / 2f + fit / 2f;

    /// <summary>Between four studs and touching all of them.</summary>
    public static float TubeRadius(float fit) => Pitch / MathF.Sqrt(2f) - StudDiameter / 2f + fit / 2f;

    /// <summary>Between two studs and touching both.</summary>
    public static float RodRadius(float fit) => Pitch / 2f - StudDiameter / 2f + fit / 2f;

    /// <summary>Touching the studs that run along it.</summary>
    public static float Wall(float fit) => Pitch / 2f - StudDiameter / 2f - EdgePlay + fit / 2f;

    /// <summary>How many studs fit along a length: a brick n studs long is n × 8 − 0.2 mm.</summary>
    public static int StudsAlong(float length) => Math.Max(0, (int)MathF.Floor((length + 2f * EdgePlay + 0.05f) / Pitch));

    /// <summary>Studs across and along the face's own box, which sets where the grid falls.</summary>
    public static (int Across, int Along) Count(FacePatch face) => (StudsAlong(face.Size.X), StudsAlong(face.Size.Y));

    /// <summary>Whether the face fills its own rectangle, as a brick's top does.</summary>
    public static bool IsRectangular(FacePatch face) =>
        face.Area >= face.Size.X * face.Size.Y * 0.98f;

    /// <summary>
    /// Grid positions over the face's box, a step beyond it each way so that a shifted grid still
    /// covers the face. <paramref name="between"/> puts them between studs rather than on them.
    ///
    /// The grid falls as a brick's does: centred on the box, studs on whole steps from the middle
    /// when the box takes an odd number and on half steps when it takes an even one, so a rectangle
    /// the size of a brick comes out exactly as that brick.
    /// </summary>
    private static IEnumerable<Vector2> Grid(FacePatch face, BrickGrid grid, bool between) =>
        GridPoints(face, grid, between).Select(face.ToUv);

    /// <summary>The grid in plate coordinates, far enough out each way to cover the face.</summary>
    private static IEnumerable<Vector3> GridPoints(FacePatch face, BrickGrid grid, bool between)
    {
        // How far the face reaches from the grid's middle along each of its axes - the face need
        // not be centred on the grid when the grid was laid out by another face.
        float farU = 0, farV = 0;
        foreach (var corner in new[] { face.Min, face.Max, new Vector2(face.Min.X, face.Max.Y), new Vector2(face.Max.X, face.Min.Y) })
        {
            var offset = face.ToLocal(corner) - grid.Middle;
            farU = MathF.Max(farU, MathF.Abs(Vector3.Dot(offset, grid.U)));
            farV = MathF.Max(farV, MathF.Abs(Vector3.Dot(offset, grid.V)));
        }

        int reachU = (int)MathF.Ceiling(farU / Pitch) + 1;
        int reachV = (int)MathF.Ceiling(farV / Pitch) + 1;
        float phaseU = ((Math.Max(grid.Across, 1) - (between ? 2 : 1)) / 2f) % 1f;
        float phaseV = ((Math.Max(grid.Along, 1) - (between ? 2 : 1)) / 2f) % 1f;

        for (int i = -reachU; i <= reachU; i++)
            for (int j = -reachV; j <= reachV; j++)
                yield return grid.Middle + grid.U * ((i - phaseU) * Pitch) + grid.V * ((j - phaseV) * Pitch);
    }

    /// <summary>Where the studs stand, in the face's own frame: every grid point whose stud fits inside the outline.</summary>
    public static List<Vector2> StudCentres(FacePatch face, EngraveOptions options) =>
        StudCentres(face, FaceOutline.Of(face), options, BrickGrid.Of(face, options));

    private static List<Vector2> StudCentres(FacePatch face, FaceOutline outline, EngraveOptions options, BrickGrid grid)
    {
        float reach = StudRadius(options.StudFit) + grid.StudMargin;
        return Grid(face, grid, between: false).Where(c => outline.Holds(c, reach)).ToList();
    }

    /// <summary>
    /// What stands in the hollow of an underside to grip the studs it goes over: tubes between
    /// every four, or on a face one stud wide, rods between every two - each only where the whole
    /// of it fits inside the hollow.
    /// </summary>
    public static List<(Vector2 At, float Outer, float Inner)> Supports(FacePatch face, EngraveOptions options) =>
        Supports(face, FaceOutline.Of(face), options, BrickGrid.Of(face, options));

    private static List<(Vector2 At, float Outer, float Inner)> Supports(FacePatch face, FaceOutline outline, EngraveOptions options, BrickGrid grid)
    {
        float fit = options.StudFit;
        float wall = Wall(fit);

        // Rods only where the face is a single stud wide the whole way; anything wider gets tubes.
        bool rods = grid.Across == 1 || grid.Along == 1;
        float outer = rods ? RodRadius(fit) : TubeRadius(fit);
        float inner = rods ? 0f : StudDiameter / 2f;

        IEnumerable<Vector2> places;
        if (!rods)
        {
            places = Grid(face, grid, between: true);
        }
        else
        {
            // Between studs along the length, on the middle line across it.
            bool longAcross = grid.Across >= grid.Along;
            var across = longAcross ? grid.V : grid.U;
            places = GridPoints(face, grid, between: true)
                .Select(p => p - across * Vector3.Dot(p - grid.Middle, across))
                .Select(face.ToUv)
                .Distinct();
        }

        return places
            .Where(p => outline.Holds(p, wall + outer))
            .Select(p => (p, outer, inner))
            .ToList();
    }

    /// <summary>How deep the hollow can go and still leave a roof over it.</summary>
    public static float DeepestHollow(Mesh mesh, FacePatch face) =>
        MathF.Max(0f, Engraver.MaterialBehind(mesh, face) - RoofThickness);

    /// <summary>How many pieces the current settings make: studs, or the tubes and rods of an underside.</summary>
    public static int CountPieces(FacePatch face, EngraveOptions options) =>
        options.Kind == PatternKind.Studs ? StudCentres(face, options).Count : Supports(face, options).Count;

    /// <summary>Outlines drawn on the face while the settings are chosen.</summary>
    public static GrooveSet Preview(FacePatch face, EngraveOptions options)
    {
        const float line = 0.25f;
        var outline = FaceOutline.Of(face);
        var grid = BrickGrid.Of(face, options);
        var ribbons = new List<Polyline2>();

        if (options.Kind == PatternKind.Studs)
        {
            float r = StudRadius(options.StudFit);
            ribbons.AddRange(StudCentres(face, outline, options, grid).Select(c => new Polyline2(Circle(c, r), line, Closed: true)));
        }
        else
        {
            foreach (var (at, outer, inner) in Supports(face, outline, options, grid))
            {
                ribbons.Add(new Polyline2(Circle(at, outer), line, Closed: true));
                if (inner > 0) ribbons.Add(new Polyline2(Circle(at, inner), line, Closed: true));
            }
        }

        return GrooveSet.Of(ribbons);

        static List<Vector2> Circle(Vector2 centre, float radius) =>
            Enumerable.Range(0, 32)
                .Select(i => centre + radius * new Vector2(MathF.Cos(i * MathF.Tau / 32), MathF.Sin(i * MathF.Tau / 32)))
                .ToList();
    }

    /// <summary>Stands the studs on the face, or hollows the underside out of it.</summary>
    public static EngraveResult Apply(Mesh mesh, FacePatch face, EngraveOptions options, CancellationToken token = default) =>
        Apply(mesh, face, options, BrickGrid.Of(face, options), token);

    /// <summary>The same, on a grid laid out elsewhere - the other face of a cut, say.</summary>
    public static EngraveResult Apply(Mesh mesh, FacePatch face, EngraveOptions options, BrickGrid grid, CancellationToken token = default) =>
        Apply(mesh, face, options, grid, null, token);

    /// <param name="room">
    /// Whether a stud may stand at a point, given in the coordinates the mesh is in. It is what
    /// the other half of a cut has room to take a socket at; null lets every stud that fits this
    /// face stand.
    /// </param>
    public static EngraveResult Apply(Mesh mesh, FacePatch face, EngraveOptions options, BrickGrid grid,
                                     Func<Vector3, bool>? room, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();

        var outline = FaceOutline.Of(face);
        if (outline.Shapes.Count == 0) return Nothing(mesh, options);

        return options.Kind == PatternKind.Studs
            ? Studs(mesh, face, outline, options, grid, room, token)
            : Underside(mesh, face, outline, options, grid, room, token);
    }

    private static EngraveResult Nothing(Mesh mesh, EngraveOptions options) =>
        new(mesh, 0, 0, mesh.CheckHealth(), options);

    private static EngraveResult Studs(Mesh mesh, FacePatch face, FaceOutline outline, EngraveOptions options,
                                       BrickGrid grid, Func<Vector3, bool>? room, CancellationToken token)
    {
        float r = StudRadius(options.StudFit);
        var centres = StudCentres(face, outline, options, grid);
        if (room is not null) centres = centres.Where(c => room(face.ToLocal(c))).ToList();
        if (centres.Count == 0) return Nothing(mesh, options);

        // Studs stand apart, so all of them together are already one solid and go in one union.
        var studs = Mesh.Combine(centres.Select(c => Placed(Primitives.Prism(r, StudHeight + Embed, Sides), face, c, -Embed, StudHeight)));
        var result = LocalCsg.Union(mesh, studs, token);

        return new EngraveResult(result, centres.Count, studs.TriangleCount, result.CheckHealth(), options);
    }

    private static EngraveResult Underside(Mesh mesh, FacePatch face, FaceOutline outline, EngraveOptions options,
                                           BrickGrid grid, Func<Vector3, bool>? room, CancellationToken token)
    {
        float depth = MathF.Max(options.Depth, 0.2f);
        float wall = Wall(options.StudFit);

        // The cells the studs below stand in - every grid square whose middle is on this face, and,
        // when the other half says so, only those it has room to take a socket at. A pocket cut
        // where the part is thinner than itself comes out through the far side: on a roof it opened
        // a row of holes along the slope, with the tubes inside them showing through.
        var cells = Grid(face, grid, between: false)
            .Where(outline.Contains)
            .Where(c => room is null || room(face.ToLocal(c)))
            .ToList();

        var hollow = Hollow(face, outline, cells, depth, wall, token);
        if (hollow is null || hollow.TriangleCount == 0) return Nothing(mesh, options);

        var result = LocalCsg.Subtract(mesh, hollow, token);

        var supports = Supports(face, outline, options, grid);
        if (supports.Count > 0)
        {
            token.ThrowIfCancellationRequested();

            // Tubes run from inside the roof to flush with the face, so they bear on whatever the
            // part is pressed onto, as a brick's do.
            var pieces = supports.Select(s =>
            {
                var outer = Placed(Primitives.Prism(s.Outer, depth + Embed, Sides), face, s.At, -depth - Embed, 0f);
                if (s.Inner <= 0) return outer;

                // Longer than the tube at both ends, so no end of the bore lies on an end of the tube.
                var bore = Placed(Primitives.Prism(s.Inner, depth + 3f * Embed + Lift, Sides), face, s.At, -depth - 2f * Embed, Embed + Lift);
                return LocalCsg.Subtract(outer, bore, token);
            });

            result = LocalCsg.Union(result, Mesh.Combine(pieces), token);
        }

        return new EngraveResult(result, supports.Count, hollow.TriangleCount, result.CheckHealth(), options);
    }

    /// <summary>
    /// The pocket an underside takes out: the studs' own cells, brought in by a wall's thickness,
    /// and clipped to the face so it never breaks out of the side.
    ///
    /// A brick's hollow is set by its studs and not by its outline: one stud wide it is 5 mm across
    /// and grips the stud on both sides, two by two it is 13 mm with a tube in the middle. Following
    /// the outline alone, a wall wider than the 8 mm module - a 10 mm one, say - came out 7.1 mm wide
    /// for a 4.65 mm stud, which located the halves and gripped nothing.
    ///
    /// Bringing an irregular outline in is where polygon offsetting goes wrong - a wiggly scan
    /// outline folds over itself the moment the offset is wider than its wiggles. So it is not
    /// offset at all: the outline is made solid, and a wall's width swept along every edge of it
    /// is taken away in the same boolean. Where the sweeps overlap, Manifold simply unions them.
    /// A rectangle, where that is not needed, is a plain box - which is also what is left when
    /// Manifold cannot run.
    /// </summary>
    private static Mesh? Hollow(FacePatch face, FaceOutline outline, IReadOnlyList<Vector2> cells,
                                float depth, float wall, CancellationToken token)
    {
        if (cells.Count == 0) return null;

        // How far the studs' cells reach, which is what the cavity is cut from.
        var low = new Vector2(cells.Min(c => c.X), cells.Min(c => c.Y)) - new Vector2(Pitch / 2f);
        var high = new Vector2(cells.Max(c => c.X), cells.Max(c => c.Y)) + new Vector2(Pitch / 2f);

        if (IsRectangular(face) && outline.Loops.Count == 1)
        {
            // The cells of a rectangle fill a rectangle, so the two rectangles are all it takes.
            var min = Vector2.Max(face.Min, low) + new Vector2(wall);
            var max = Vector2.Min(face.Max, high) - new Vector2(wall);
            var size = max - min;
            if (size.X <= 0 || size.Y <= 0) return null;

            return Placed(Primitives.Box(size.X, size.Y, depth + Lift), face, (min + max) * 0.5f, -depth, Lift);
        }

        var surface = new PlanarSurface(face);
        var middle = surface.Middle;

        var region = outline.Shapes
            .Select(s => new TextShape(
                s.Outline.Select(p => p - middle).ToList(),
                s.Holes.Select(h => (IReadOnlyList<Vector2>)h.Select(p => p - middle).ToList()).ToList()))
            .ToList();

        var solid = TextSolid.Build(region, surface, Lift, -depth);

        var walls = new List<Mesh>();
        foreach (var edge in outline.Loops)
        {
            token.ThrowIfCancellationRequested();

            // Swept along a simpler outline than the face's own. A scan cut flat has thousands of
            // edges a fraction of a millimetre long, and each is a piece of wall in the boolean:
            // four thousand of them took sixteen seconds. Straightened where it strays less than
            // a twentieth of a millimetre, the same outline is a few hundred.
            var loop = Simplified(edge, WallTolerance);

            for (int i = 0; i < loop.Count; i++)
            {
                var a = loop[i] - middle;
                var b = loop[(i + 1) % loop.Count] - middle;
                if (Vector2.DistanceSquared(a, b) < 1e-8f) continue;

                walls.Add(TextSolid.Build([new TextShape(Capsule(a, b, wall), [])], surface, Lift + 1f, -depth - 1f));
            }
        }

        var hollow = ManifoldCsg.SubtractAll(solid, walls, token);
        if (hollow is null) return null;

        // Held to the studs' cells. Without Manifold there is nothing to hold it with, and the
        // wider pocket of the face itself is better than none.
        return ManifoldCsg.Intersect(hollow, Cells(face, cells, depth, wall, token), token) ?? hollow;
    }

    /// <summary>
    /// The cells the studs stand in, each brought in by a wall, joined up where two cells are
    /// neighbours - a brick's hollow is one space with tubes in it, not a pocket for every stud.
    /// </summary>
    private static Mesh Cells(FacePatch face, IReadOnlyList<Vector2> cells, float depth, float wall, CancellationToken token)
    {
        float inner = Pitch - 2f * wall;
        float height = depth + 2f * Lift;
        var pieces = new List<Mesh>(cells.Count * 2);

        foreach (var at in cells)
        {
            pieces.Add(Placed(Primitives.Box(inner, inner, height), face, at, -depth - Lift, Lift));

            foreach (var step in new[] { new Vector2(Pitch, 0f), new Vector2(0f, Pitch) })
            {
                if (!cells.Any(other => Vector2.DistanceSquared(other, at + step) < 0.01f)) continue;

                // The bridge between two cells: as wide as the cavity, as long as the step.
                var size = step.X > 0 ? new Vector2(Pitch, inner) : new Vector2(inner, Pitch);
                pieces.Add(Placed(Primitives.Box(size.X, size.Y, height), face, at + step * 0.5f, -depth - Lift, Lift));
            }
        }

        return ManifoldCsg.UnionAll(pieces, token) ?? Mesh.Combine(pieces);
    }

    /// <summary>
    /// The fits a test set is printed at, loosest last. Chosen round the filament default, since
    /// a printer that needs more than two tenths either way has a calibration problem, not a fit.
    /// </summary>
    public static readonly float[] CouponFits = [-0.2f, -0.1f, 0f, 0.1f];

    /// <summary>A brick plate is 3.2 mm tall; its hollow leaves the usual roof over it.</summary>
    public const float PlateHeight = 3.2f;

    /// <summary>
    /// One plate of a fit test: a two-by-two brick plate, studs on top and the underside below, at
    /// one fit, with <paramref name="notches"/> cut along one edge to say which it is once printed.
    ///
    /// Notches rather than numbers: a digit small enough to fit between studs is past what a
    /// nozzle draws, and a count of notches reads at a glance on any printer. Null if the plate
    /// would not come out watertight.
    /// </summary>
    public static Mesh? FitCoupon(float fit, int notches, CancellationToken token = default)
    {
        const float side = 2 * Pitch - 2 * EdgePlay;
        var plate = MeshTransform.Transformed(Primitives.Box(side, side, PlateHeight), Matrix4x4.CreateTranslation(0, 0, PlateHeight / 2f));
        var options = EngraveOptions.Default with { StudFit = fit };

        var top = FacePatch.Find(plate, new Vector3(0, 0, PlateHeight), Vector3.UnitZ)!;
        var studded = Apply(plate, top, options with { Kind = PatternKind.Studs }, token);
        if (!studded.IsPrintable) return null;

        var bottom = FacePatch.Find(studded.Mesh, Vector3.Zero, -Vector3.UnitZ)!;
        var hollowed = Apply(studded.Mesh, bottom, options with { Kind = PatternKind.StudUnderside, Depth = PlateHeight - RoofThickness }, token);
        if (!hollowed.IsPrintable) return null;

        if (notches <= 0) return hollowed.Mesh;

        // Along the top of one side, inside the wall, clear of the studs and the hollow.
        var cuts = Enumerable.Range(0, notches).Select(k =>
            MeshTransform.Transformed(Primitives.Box(1f, 1.6f, 1.5f),
                Matrix4x4.CreateTranslation(-4.5f + k * 3f, -side / 2f, PlateHeight)));

        var marked = LocalCsg.Subtract(hollowed.Mesh, Mesh.Combine(cuts), token);
        return marked.CheckHealth().IsWatertight ? marked : null;
    }

    /// <summary>How far the outline the walls are swept along may stray from the face's own.</summary>
    private const float WallTolerance = 0.05f;

    /// <summary>
    /// A closed loop with the corners dropped that lie within <paramref name="tolerance"/> of the
    /// line between their neighbours - Douglas and Peucker's method, split at the two corners
    /// furthest apart so a closed loop has somewhere to start.
    /// </summary>
    private static List<Vector2> Simplified(List<Vector2> loop, float tolerance)
    {
        if (loop.Count <= 8) return loop;

        int far = 0;
        float longest = 0;
        for (int i = 1; i < loop.Count; i++)
        {
            float d = Vector2.DistanceSquared(loop[0], loop[i]);
            if (d > longest) { longest = d; far = i; }
        }

        var keep = new bool[loop.Count];
        keep[0] = keep[far] = true;
        Mark(0, far);
        Mark(far, loop.Count);

        var kept = new List<Vector2>();
        for (int i = 0; i < loop.Count; i++)
            if (keep[i]) kept.Add(loop[i]);

        return kept.Count >= 3 ? kept : loop;

        // Keeps the corner furthest from the chord between two kept ones, if it is far enough off
        // it to matter, and looks again either side. The end index may be the loop's length,
        // standing for its first corner again.
        void Mark(int from, int to)
        {
            var stack = new Stack<(int, int)>();
            stack.Push((from, to));

            while (stack.Count > 0)
            {
                var (a, b) = stack.Pop();
                if (b - a < 2) continue;

                Vector2 start = loop[a], end = loop[b % loop.Count];
                Vector2 chord = end - start;
                float length = chord.LengthSquared();

                int worst = -1;
                float furthest = tolerance;
                for (int i = a + 1; i < b; i++)
                {
                    var p = loop[i];
                    float t = length < 1e-12f ? 0f : Math.Clamp(Vector2.Dot(p - start, chord) / length, 0f, 1f);
                    float d = Vector2.Distance(p, start + chord * t);
                    if (d > furthest) { furthest = d; worst = i; }
                }

                if (worst < 0) continue;

                keep[worst] = true;
                stack.Push((a, worst));
                stack.Push((worst, b));
            }
        }
    }

    /// <summary>A stadium round one edge: the band a wall of this thickness sweeps along it.</summary>
    private static List<Vector2> Capsule(Vector2 a, Vector2 b, float radius)
    {
        var along = Vector2.Normalize(b - a);
        var side = new Vector2(-along.Y, along.X);
        var points = new List<Vector2>(2 * (CapSteps + 1));

        for (int k = 0; k <= CapSteps; k++)
        {
            float t = -MathF.PI / 2f + MathF.PI * k / CapSteps;
            points.Add(b + radius * (MathF.Cos(t) * along + MathF.Sin(t) * side));
        }

        for (int k = 0; k <= CapSteps; k++)
        {
            float t = MathF.PI / 2f + MathF.PI * k / CapSteps;
            points.Add(a + radius * (MathF.Cos(t) * along + MathF.Sin(t) * side));
        }

        return points;
    }

    /// <summary>
    /// A solid built upright about the origin, stood on the face at <paramref name="uv"/> between
    /// two heights measured out of it.
    /// </summary>
    private static Mesh Placed(Mesh upright, FacePatch face, Vector2 uv, float from, float to)
    {
        var origin = face.ToLocal(uv, (from + to) * 0.5f);
        var n = face.Normal;
        var place = new Matrix4x4(
            face.U.X, face.U.Y, face.U.Z, 0f,
            face.V.X, face.V.Y, face.V.Z, 0f,
            n.X, n.Y, n.Z, 0f,
            origin.X, origin.Y, origin.Z, 1f);

        return MeshTransform.Transformed(upright, place);
    }
}
