using System.Numerics;
using FastCraft3D.Geometry.Csg;

namespace FastCraft3D.Geometry;

/// <summary>Which way a connector runs through the cut.</summary>
public enum ConnectorDirection
{
    /// <summary>Square to the cut face, whichever way the plane is turned.</summary>
    Perpendicular,

    /// <summary>Straight up and down, so a half on a tilted cut still lifts off vertically.</summary>
    Vertical,

    /// <summary>Level, towards the cut, so a half on a leaning cut slides off sideways.</summary>
    Horizontal
}

public enum ConnectorStyle
{
    /// <summary>Holes in both halves, and pins made as parts of their own.</summary>
    Pins,

    /// <summary>Pegs standing out of one half, sockets in the other.</summary>
    Pegs
}

/// <param name="Count">How many to place, at most. Fewer go in when the cut face has no room.</param>
/// <param name="Diameter">Of the pin or peg itself; the hole is bigger by the clearance on every side.</param>
/// <param name="Depth">How far a connector reaches into each half.</param>
/// <param name="Clearance">Per side, the same meaning Subtract gives it.</param>
/// <param name="EdgeDistance">
/// Material to leave between a hole and the outside of the face. Pins go out towards the edges
/// until this much is left, since registration is better the further apart they are.
/// </param>
/// <param name="Direction">Which way the connectors run through the cut.</param>
public readonly record struct ConnectorOptions(
    ConnectorStyle Style, int Count, float Diameter, float Depth, float Clearance, float EdgeDistance,
    ConnectorDirection Direction)
{
    /// <summary>
    /// Written out, because a record struct's new() skips the defaults and hands back zeros - a
    /// pin with no diameter going nowhere.
    /// </summary>
    public static ConnectorOptions Default =>
        new(ConnectorStyle.Pins, 2, 5f, 6f, 0.2f, 3f, ConnectorDirection.Perpendicular);

    public float Radius => Diameter / 2f;
}

/// <summary>
/// Joining the two halves of a split so they go back together one way only: where the pins go
/// on the cut face, and cutting them in.
///
/// Where: only where a pin has solid round it. The cut face is found from the same rings the cap
/// is filled from, and a place counts only if it is inside the section, outside any hole in it,
/// and a margin clear of every edge - a pin broken through the side of a thin wall is worse than
/// no pin. Then spread evenly over the face, and out towards its edges.
///
/// How: every cut is a round rod crossing the face square on, so the booleans meet the halves in a
/// clean circle rather than along a face. Nothing lies in the cut plane - that is the contact this
/// engine tears on - and holes are cut locally, so a pin through a scan costs what the pin costs.
/// </summary>
public static class Connectors
{
    /// <summary>
    /// The thinnest wall worth leaving beside a hole, whatever is asked for: about one printed line.
    ///
    /// It was 1.5 mm, and it quietly beat the From edge typed into the panel - ask for 0.8 and get
    /// 1.5 - so a thin-walled part got no connectors and no reason why. The number asked for is the
    /// margin now; this is only the floor under it.
    /// </summary>
    public const float MinimumWall = 0.4f;

    /// <summary>
    /// How steeply a connector has to cross the cut, as the cosine of its angle from square on.
    /// Shallower than about seventy degrees a pin slices along the face rather than through it,
    /// and its hole opens into a long slot.
    /// </summary>
    public const float SteepestSlant = 0.35f;

    /// <summary>
    /// The way a connector runs for a cut with this normal, pointing into the front half; or null
    /// when that way does not cross the cut steeply enough to join anything - vertical pins through
    /// a vertical cut, level ones through a level cut.
    /// </summary>
    public static Vector3? Axis(Vector3 normal, ConnectorDirection direction)
    {
        normal = Vector3.Normalize(normal);

        Vector3 along = direction switch
        {
            ConnectorDirection.Vertical => new Vector3(0f, 0f, normal.Z < 0f ? -1f : 1f),
            ConnectorDirection.Horizontal => new Vector3(normal.X, normal.Y, 0f),
            _ => normal
        };

        if (along.LengthSquared() < 1e-8f) return null;
        along = Vector3.Normalize(along);

        return Vector3.Dot(along, normal) >= SteepestSlant ? along : null;
    }

    /// <param name="Points">Where connectors go, on the cut plane.</param>
    /// <param name="Crosses">False when the direction asked for runs along the cut instead of through it.</param>
    /// <param name="ThickestWall">The thickest the solid gets anywhere on the face, near enough.</param>
    /// <param name="WallNeeded">How thick solid has to be to take one connector with the wall asked for.</param>
    public sealed record Layout(List<Vector3> Points, bool Crosses, float ThickestWall, float WallNeeded);

    /// <summary>How far a peg is sunk into its own half, so the join is inside material rather than on the face.</summary>
    private const float Embed = 1.5f;

    private const int Sides = 32;

    /// <summary>
    /// Points on the cut a plane makes through a solid where connectors fit, as many as asked for
    /// or as will go.
    /// </summary>
    public static List<Vector3> Place(Mesh world, Vector3 normal, float offset, ConnectorOptions options) =>
        Survey(world, normal, offset, options).Points;

    /// <summary>
    /// Where connectors go on a cut face, and - when none do - the two numbers that say why: how
    /// thick the solid is, and how thick it would have to be.
    /// </summary>
    public static Layout Survey(Mesh world, Vector3 normal, float offset, ConnectorOptions options)
    {
        float reach = Reach(normal, options);
        return Survey([(world, offset), (world, offset + reach), (world, offset - reach)], normal, offset, options);
    }

    /// <summary>
    /// How far a connector goes into a part, measured square to the face.
    ///
    /// Read as well as the face itself, because a pin has to have solid round it all the way in, not
    /// only where it enters. Checking the face alone put 6 mm holes into the 2 mm floor of a 1:87
    /// house, where they broke out into the room above and the boolean tore on what was left.
    /// </summary>
    private static float Reach(Vector3 normal, ConnectorOptions options)
    {
        var axis = Axis(normal, options.Direction);
        float cosine = axis is { } way ? Vector3.Dot(way, Vector3.Normalize(normal)) : 1f;
        return (MathF.Max(0f, options.Depth) + MathF.Max(0f, options.Clearance)) * cosine;
    }

    /// <summary>
    /// How near two parts may be and still count as resting on each other. Parts laid against each
    /// other by eye, or by typing sizes that were rounded, are rarely exactly touching.
    /// </summary>
    public const float ContactGap = 0.5f;

    /// <summary>How far into each part its face is read, clear of the face itself.</summary>
    private const float SectionInset = 0.1f;

    /// <param name="Normal">Square to the shared face, pointing into the front part.</param>
    /// <param name="Offset">Where the shared face lies along the normal: halfway across any gap.</param>
    /// <param name="Gap">How far apart the two faces actually are.</param>
    /// <param name="SecondIsFront">Whether the second part is the one the normal points into.</param>
    /// <param name="Overlap">The area the two boxes share on the face, for choosing between faces.</param>
    public sealed record Contact(Vector3 Normal, float Offset, float Gap, bool SecondIsFront, float Overlap);

    /// <summary>
    /// The face two parts rest against each other on, if they do: the top of one against the
    /// bottom of the other, or side against side, square to X, Y or Z.
    ///
    /// Read off the two boxes, which is exact for parts built square to the plate - a basement and
    /// the floor that sits on it - and says nothing about parts turned at an angle, which get no
    /// contact rather than a wrong one. Where two faces qualify, the one they share most of wins.
    /// </summary>
    public static Contact? SharedFace(Bounds first, Bounds second, float gap = ContactGap)
    {
        Contact? best = null;

        for (int axis = 0; axis < 3; axis++)
        {
            float overlap = Overlap(first, second, axis);
            if (overlap <= 0f) continue;

            var normal = axis switch { 0 => Vector3.UnitX, 1 => Vector3.UnitY, _ => Vector3.UnitZ };

            // The second part beyond the first along the axis, or the first beyond the second.
            float secondAbove = Along(second.Min, axis) - Along(first.Max, axis);
            float firstAbove = Along(first.Min, axis) - Along(second.Max, axis);

            if (MathF.Abs(secondAbove) <= gap && (best is null || overlap > best.Overlap))
                best = new Contact(normal, (Along(second.Min, axis) + Along(first.Max, axis)) / 2f, MathF.Abs(secondAbove), true, overlap);

            if (MathF.Abs(firstAbove) <= gap && (best is null || overlap > best.Overlap))
                best = new Contact(normal, (Along(first.Min, axis) + Along(second.Max, axis)) / 2f, MathF.Abs(firstAbove), false, overlap);
        }

        return best;
    }

    /// <summary>Where connectors go through the face two parts share: inside both, with room in both.</summary>
    public static Layout Survey(Mesh front, Mesh back, Contact contact, ConnectorOptions options)
    {
        float reach = contact.Gap / 2f + SectionInset;
        float deep = Reach(contact.Normal, options);

        return Survey(
            [
                (front, contact.Offset + reach), (front, contact.Offset + reach + deep),
                (back, contact.Offset - reach), (back, contact.Offset - reach - deep)
            ],
            contact.Normal, contact.Offset, options);
    }

    private static float Overlap(Bounds a, Bounds b, int axis)
    {
        float Span(int k) => MathF.Max(0f, MathF.Min(Along(a.Max, k), Along(b.Max, k)) - MathF.Max(Along(a.Min, k), Along(b.Min, k)));

        return axis switch { 0 => Span(1) * Span(2), 1 => Span(0) * Span(2), _ => Span(0) * Span(1) };
    }

    private static float Along(Vector3 v, int axis) => axis switch { 0 => v.X, 1 => v.Y, _ => v.Z };

    /// <summary>
    /// The survey itself, over one solid or several read on the same plane. A point counts only if
    /// it is inside every one of them, and its room is the least any of them gives it - so on a
    /// floor resting on a basement, pins go where both have material, which is the walls.
    /// </summary>
    private static Layout Survey(
        IReadOnlyList<(Mesh Mesh, float Section)> solids, Vector3 normal, float offset, ConnectorOptions options)
    {
        normal = Vector3.Normalize(normal);
        var axis = Axis(normal, options.Direction);

        // The hole's footprint on the face: a circle square on, an ellipse at a slant, longer by
        // the cosine. Taken as a circle of the longer reach, which is the safe side of it.
        float cosine = axis is { } way ? Vector3.Dot(way, normal) : 1f;
        float hole = (options.Radius + MathF.Max(0f, options.Clearance)) / cosine;
        float wall = MathF.Max(options.EdgeDistance, MinimumWall);
        float needed = hole + wall;

        if (axis is null) return new([], false, 0f, 2f * needed);
        if (options.Count <= 0 || !(options.Radius > 0f)) return new([], true, 0f, 2f * needed);

        Vector3 u = Perpendicular(normal);
        Vector3 v = Vector3.Cross(normal, u);
        Vector3 origin = normal * offset;

        var low = new Vector2(float.MinValue);
        var high = new Vector2(float.MaxValue);
        var sections = new List<(List<List<Vector2>> Loops, List<(List<Vector2> Outline, List<List<Vector2>> Holes)> Shapes)>();

        foreach (var (mesh, section) in solids)
        {
            var (_, rings) = PlaneClip.KeepOpen(mesh, Matrix4x4.Identity, normal, section);
            if (rings.Count == 0) return new([], true, 0f, 2f * needed);

            // Flattened into the one frame on the shared plane: reading a part a little way in
            // moves its section along the normal only, which the frame does not see.
            var loops = rings
                .Select(r => r.Select(p => new Vector2(Vector3.Dot(p - origin, u), Vector3.Dot(p - origin, v))).ToList())
                .ToList();

            var own = (Low: new Vector2(float.MaxValue), High: new Vector2(float.MinValue));
            foreach (var loop in loops)
                foreach (var p in loop)
                {
                    own.Low = Vector2.Min(own.Low, p);
                    own.High = Vector2.Max(own.High, p);
                }

            low = Vector2.Max(low, own.Low);
            high = Vector2.Min(high, own.High);
            sections.Add((loops, Polygon2.Nest(loops)));
        }

        if (high.X <= low.X || high.Y <= low.Y) return new([], true, 0f, 2f * needed);

        // A grid fine enough to find the middle of a thin wall on a big part. It was capped at eighty
        // steps across, which on a 1:87 house 110 mm long put a point every 1.4 mm - and walls there
        // are under 3 mm thick, so the middle of one fell between points, the room in it was
        // misread by a third, and a pin that fitted was never offered. A fifth of a millimetre at
        // the finest, and never more than about two hundred and forty steps across.
        float extent = MathF.Max(high.X - low.X, high.Y - low.Y);
        float step = MathF.Max(MathF.Min(hole * 0.5f, 0.5f), MathF.Max(extent / 240f, 0.2f));

        // Centred on the face's box, so a face that is the same on both sides gets a grid that is
        // too. Stepping in from one corner instead left the last row short by whatever the step did
        // not divide into, and four pins on a square came out half a millimetre lopsided.
        int columns = Math.Max(1, (int)MathF.Ceiling((high.X - low.X) / step));
        int rows = Math.Max(1, (int)MathF.Ceiling((high.Y - low.Y) / step));
        float firstX = (low.X + high.X) / 2f - (columns - 1) * step / 2f;
        float firstY = (low.Y + high.Y) / 2f - (rows - 1) * step / 2f;

        var area = new List<Vector2>();
        var room = new List<Vector2>();
        float deepest = 0f;

        for (int column = 0; column < columns; column++)
        {
            for (int row = 0; row < rows; row++)
            {
                var p = new Vector2(firstX + column * step, firstY + row * step);
                float clear = Clear(p);
                if (clear < 0f) continue;

                area.Add(p);
                deepest = MathF.Max(deepest, clear);
                if (clear >= needed) room.Add(p);
            }
        }

        if (room.Count == 0) return new([], true, 2f * deepest, 2f * needed);

        var middle = Vector2.Zero;
        foreach (var p in area) middle += p;
        middle /= area.Count;

        // Out towards the edge, each along the line from the middle of the face through the middle of
        // its own share, until the wall left beside the hole is what was asked for. Evenly spread
        // alone put four pins on a square a quarter of the way in from every side - square and tidy,
        // and much nearer each other than registration wants.
        //
        // Along the line through the share's true middle, not through the nearest grid point to it.
        // A grid point sits a step to one side, the line through it is skewed by that much, and four
        // pins on a square came out 12.6 by 14.3 instead of on the diagonal.
        float apart = 2f * hole + wall;
        var placed = new List<Vector2>();

        foreach (var centre in Spread(area, room, middle, options.Count))
        {
            var start = Clear(centre) >= needed ? centre : room.MinBy(r => Vector2.DistanceSquared(r, centre));
            var at = TowardsEdge(start, centre - middle, p => Clear(p) >= needed, step);

            // A face with room for fewer than asked: two connectors in the same place are one.
            if (placed.All(q => Vector2.Distance(q, at) >= apart)) placed.Add(at);
        }

        return new(placed.Select(p => origin + u * p.X + v * p.Y).ToList(), true, 2f * deepest, 2f * needed);

        // How far a point is from the nearest edge of every section it has to be inside, or -1 when
        // it is outside any of them.
        float Clear(Vector2 p)
        {
            float least = float.MaxValue;
            foreach (var (loops, shapes) in sections)
            {
                if (!Inside(shapes, p)) return -1f;
                least = MathF.Min(least, NearestEdge(loops, p));
            }

            return least;
        }
    }

    /// <summary>
    /// A pin moved along the line from the middle of the face until it just fits: outward if it
    /// has more room than it needs, inward if it has less.
    ///
    /// Both ways, because the wall is a number asked for. Outward only, a wall wider than the even
    /// spread happened to leave was simply ignored. Inward it goes no further than the middle, and a
    /// pin that finds no such place on the way stays where it was.
    /// </summary>
    private static Vector2 TowardsEdge(Vector2 pin, Vector2 away, Func<Vector2, bool> fits, float step)
    {
        if (away.LengthSquared() < 1e-4f) return pin;

        float reach = away.Length();
        away /= reach;
        float stride = MathF.Max(step * 0.25f, 0.05f);

        if (fits(pin))
        {
            Vector2 at = pin;
            while (fits(at + away * stride)) at += away * stride;

            // The last stride halved down, so the wall comes out the number asked for rather than
            // whatever a whole stride happened to leave.
            return at + away * Bisect(at, away, stride, fits);
        }

        // Too near the edge for what was asked: back towards the middle until it is not.
        for (float travelled = stride; travelled <= reach; travelled += stride)
        {
            Vector2 next = pin - away * travelled;
            if (fits(next)) return next + away * Bisect(next, away, stride, fits);
        }

        return pin;
    }

    /// <summary>How far past a point that fits, along a direction, the last point that still fits lies.</summary>
    private static float Bisect(Vector2 fits, Vector2 along, float stride, Func<Vector2, bool> test)
    {
        float low = 0f, high = stride;
        for (int i = 0; i < 12; i++)
        {
            float mid = (low + high) / 2f;
            if (test(fits + along * mid)) low = mid; else high = mid;
        }

        return low;
    }

    /// <summary>
    /// Pins spread evenly over a face: each in the middle of the part of the face nearest to it.
    ///
    /// Spreading them as far apart as possible was tried first, starting from the roomiest spot.
    /// That spot is the middle of the face, so one pin always stood dead centre and the rest were
    /// pushed out to the corners - four on a square came out as a centre and three corners, which
    /// holds the part no better than it looks. Sharing the face out instead puts four on a square
    /// in its four quarters, three on a disc a third of the way round each, two either side of the
    /// middle: the layout anyone would draw.
    ///
    /// It is Lloyd's relaxation over the face - start spread out, move each pin to the middle of its
    /// share, share again, until nothing moves. What comes back are those middles; the caller puts
    /// each pin on a spot with room.
    /// </summary>
    private static List<Vector2> Spread(List<Vector2> area, List<Vector2> room, Vector2 middle, int count)
    {
        // Started as far apart as they will go, from the spot farthest from the middle - which is
        // what keeps the result the same shape every time, rather than depending on the grid.
        var pins = new List<Vector2> { room.MaxBy(p => Vector2.DistanceSquared(p, middle)) };
        while (pins.Count < count && pins.Count < room.Count)
            pins.Add(room.MaxBy(r => pins.Min(p => Vector2.DistanceSquared(p, r))));

        var sums = new Vector2[pins.Count];
        var shares = new int[pins.Count];

        for (int pass = 0; pass < 50; pass++)
        {
            Array.Clear(sums);
            Array.Clear(shares);

            foreach (var p in area)
            {
                int nearest = 0;
                for (int k = 1; k < pins.Count; k++)
                    if (Vector2.DistanceSquared(p, pins[k]) < Vector2.DistanceSquared(p, pins[nearest]))
                        nearest = k;

                sums[nearest] += p;
                shares[nearest]++;
            }

            bool moved = false;
            for (int k = 0; k < pins.Count; k++)
            {
                if (shares[k] == 0) continue;

                // The share's true middle, not the nearest grid point to it: snapping on every pass
                // let a symmetric face settle lopsided by a grid step.
                var centre = sums[k] / shares[k];
                if (Vector2.DistanceSquared(centre, pins[k]) > 1e-8f)
                {
                    pins[k] = centre;
                    moved = true;
                }
            }

            if (!moved) break;
        }

        return pins;
    }

    /// <summary>
    /// The two halves with connectors cut in at these points, and the pins to print separately if
    /// the style has any; or null when a half would not come back watertight.
    ///
    /// Refused rather than returned torn, so the split can go ahead plain: two good halves without
    /// pins can still be drilled, and a torn half cannot be printed at all.
    /// </summary>
    public static (Mesh Front, Mesh Back, List<Mesh> Pins)? Join(
        Mesh front, Mesh back, IReadOnlyList<Vector3> points, Vector3 normal, ConnectorOptions options,
        CancellationToken token = default)
    {
        normal = Vector3.Normalize(normal);
        if (Axis(normal, options.Direction) is not { } axis) return null;

        float r = options.Radius;
        float c = MathF.Max(0f, options.Clearance);
        float depth = options.Depth;

        // Measured along the connector, so at a slant a peg is sunk as far into its half, square
        // on, as it is when it crosses straight.
        float embed = Embed / Vector3.Dot(axis, normal);

        // Pegs on whichever part is lower, standing up. They went on the part the normal points
        // into, which for a level cut is the top one - so they hung down off its underside and
        // could not be printed without supports. A cut with nothing above or below, side by side,
        // puts them on the back part, as good as either.
        bool pegsBelow = normal.Z >= 0f;

        var pins = new List<Mesh>();

        foreach (var at in points)
        {
            token.ThrowIfCancellationRequested();

            if (options.Style == ConnectorStyle.Pins)
            {
                // One rod through both halves, a pin's depth into each and the clearance beyond.
                var hole = Rod(at, axis, r + c, -(depth + c), depth + c);
                front = LocalCsg.Subtract(front, hole, token);
                back = LocalCsg.Subtract(back, hole, token);

                pins.Add(Primitives.Prism(r, 2f * depth, Sides));
            }
            else if (pegsBelow)
            {
                // The peg stands up out of the lower part into the upper, sunk a little way into its
                // own part so the union meets solid material and not the cut face. The socket
                // reaches past the upper part's face for the same reason.
                var peg = Rod(at, axis, r, -embed, depth);
                var socket = Rod(at, axis, r + c, -embed, depth + c);
                back = LocalCsg.Union(back, peg, token);
                front = LocalCsg.Subtract(front, socket, token);
            }
            else
            {
                var peg = Rod(at, axis, r, -depth, embed);
                var socket = Rod(at, axis, r + c, -(depth + c), embed);
                front = LocalCsg.Union(front, peg, token);
                back = LocalCsg.Subtract(back, socket, token);
            }
        }

        front = Closed(front, token);
        back = Closed(back, token);

        if (!front.CheckHealth().IsWatertight || !back.CheckHealth().IsWatertight) return null;
        return (front, back, pins);
    }

    private static Mesh Closed(Mesh mesh, CancellationToken token) =>
        mesh.CheckHealth().IsWatertight ? mesh : MeshHealer.Heal(mesh, token: token).Mesh;

    /// <summary>A round rod along the normal through a point, from one distance along it to another.</summary>
    private static Mesh Rod(Vector3 through, Vector3 normal, float radius, float from, float to)
    {
        float length = to - from;
        float middle = (from + to) / 2f;

        return MeshTransform.Transformed(
            Primitives.Prism(radius, length, Sides),
            MeshTransform.RotationBetween(Vector3.UnitZ, normal)
            * Matrix4x4.CreateTranslation(through + normal * middle));
    }

    private static bool Inside(List<(List<Vector2> Outline, List<List<Vector2>> Holes)> shapes, Vector2 p)
    {
        foreach (var (outline, holes) in shapes)
            if (Polygon2.Contains(outline, p) && !holes.Any(h => Polygon2.Contains(h, p)))
                return true;

        return false;
    }

    private static float NearestEdge(List<List<Vector2>> loops, Vector2 p)
    {
        float nearest = float.MaxValue;

        foreach (var loop in loops)
        {
            for (int i = 0; i < loop.Count; i++)
            {
                Vector2 a = loop[i], b = loop[(i + 1) % loop.Count];
                Vector2 ab = b - a;
                float t = ab.LengthSquared() < 1e-12f ? 0f : Math.Clamp(Vector2.Dot(p - a, ab) / ab.LengthSquared(), 0f, 1f);
                nearest = MathF.Min(nearest, Vector2.Distance(p, a + ab * t));
            }
        }

        return nearest;
    }

    private static Vector3 Perpendicular(Vector3 n)
    {
        var other = MathF.Abs(n.Z) < 0.9f ? Vector3.UnitZ : Vector3.UnitX;
        return Vector3.Normalize(Vector3.Cross(other, n));
    }
}
