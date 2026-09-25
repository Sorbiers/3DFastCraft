using System.Numerics;

namespace FastCraft3D.Geometry;

/// <summary>
/// Twist, taper and bend: each moves every point of a solid by where it stands, and nothing more.
///
/// There is no boolean in any of them, so nothing tears. What there is to get right is detail. A
/// cube has corners only at its top and bottom, and moving those alone would only lean its sides
/// over as flat slants - a twisted cube with straight edges is not twisted. So edges are broken
/// first wherever moving them straight would stray from the curve their points ought to follow.
/// Each edge is judged from its own two ends, so both faces beside it reach the same answer and
/// the solid stays closed.
///
/// All three work on the solid as it stands on the plate: upright is the plate's upright, and the
/// bottom of the solid stays where it is.
/// </summary>
public static class MeshDeform
{
    /// <summary>
    /// How far an edge may stray from the curve it stands in for. Well under a printed layer, and
    /// the same allowance lettering is laid round a barrel with.
    /// </summary>
    public const float ToleranceMm = 0.04f;

    /// <summary>
    /// Where breaking edges stops. A wide part turned several times over asks for a great many,
    /// and a result a little coarser than asked for is better than one that never arrives.
    /// </summary>
    public const int TriangleBudget = 600_000;

    /// <summary>The steepest taper offered: a top a twentieth of the bottom still has a top to print.</summary>
    public const float SmallestTaper = 0.05f;

    /// <summary>
    /// Moves every point by <paramref name="map"/>, breaking edges first where a straight one would
    /// stray from the curve its points are carried along.
    /// </summary>
    public static Mesh Apply(Mesh world, Func<Vector3, Vector3> map, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();

        // How far the middle of the edge, carried as it should be, lies from the middle of the
        // straight edge between its carried ends. Measured rather than estimated: judging a twist
        // by how far each end swings round the axis missed the edges that matter most - a
        // diagonal across a side face, whose ends are far apart and turn by different amounts -
        // and left a twisted column 1.4% fuller than the column it came from.
        var dense = MeshSubdivision.SplitEdgesWhere(
            world.Welded(),
            (a, b) => Vector3.Distance(map((a + b) * 0.5f), (map(a) + map(b)) * 0.5f) > ToleranceMm,
            maxRounds: 12, maxTriangles: TriangleBudget);

        token.ThrowIfCancellationRequested();

        var moved = new List<Vector3>(dense.Positions.Count);
        foreach (var p in dense.Positions) moved.Add(map(p));

        return new Mesh(moved, dense.Indices.ToList());
    }

    /// <summary>
    /// Turns the solid about its upright axis by an angle that grows with height: nothing at the
    /// bottom, <paramref name="degrees"/> at the top. Positive is anticlockwise seen from above.
    /// </summary>
    public static Mesh Twist(Mesh world, float degrees, CancellationToken token = default)
    {
        var bounds = world.ComputeBounds();
        if (bounds.IsEmpty || bounds.Size.Z < 1e-4f || MathF.Abs(degrees) < 1e-3f) return world;

        var axis = new Vector2(bounds.Center.X, bounds.Center.Y);
        float bottom = bounds.Min.Z;
        float perMm = degrees * MathF.PI / 180f / bounds.Size.Z;

        return Apply(world, p =>
        {
            float angle = (p.Z - bottom) * perMm;
            float cos = MathF.Cos(angle), sin = MathF.Sin(angle);
            float x = p.X - axis.X, y = p.Y - axis.Y;

            return new Vector3(axis.X + x * cos - y * sin, axis.Y + x * sin + y * cos, p.Z);
        }, token);
    }

    /// <summary>
    /// Narrows or widens the solid towards the top, about its upright axis: full size at the
    /// bottom, <paramref name="topScale"/> of it at the top, in a straight line between.
    /// </summary>
    public static Mesh Taper(Mesh world, float topScale, CancellationToken token = default)
    {
        var bounds = world.ComputeBounds();
        topScale = MathF.Max(topScale, SmallestTaper);
        if (bounds.IsEmpty || bounds.Size.Z < 1e-4f || MathF.Abs(topScale - 1f) < 1e-4f) return world;

        var axis = new Vector2(bounds.Center.X, bounds.Center.Y);
        float bottom = bounds.Min.Z;
        float height = bounds.Size.Z;

        return Apply(world, p =>
        {
            float k = 1f + (topScale - 1f) * (p.Z - bottom) / height;
            return new Vector3(axis.X + (p.X - axis.X) * k, axis.Y + (p.Y - axis.Y) * k, p.Z);
        }, token);
    }

    /// <summary>
    /// Curves the solid over as if round a pipe: the bottom stays, and the top leans over until it
    /// faces <paramref name="degrees"/> away from straight up, towards +X or +Y for a positive
    /// angle. The middle of the solid keeps its length; the outside of the curve stretches and the
    /// inside is squeezed, as a real bend does.
    /// </summary>
    public static Mesh Bend(Mesh world, float degrees, Axis towards, CancellationToken token = default)
    {
        var bounds = world.ComputeBounds();
        if (bounds.IsEmpty || bounds.Size.Z < 1e-4f || MathF.Abs(degrees) < 1e-3f) return world;
        if (Folds(world, degrees, towards)) return world;

        float theta = degrees * MathF.PI / 180f;
        float height = bounds.Size.Z;
        float bottom = bounds.Min.Z;
        float radius = height / theta;
        bool alongX = towards != Axis.Y;
        float middle = alongX ? bounds.Center.X : bounds.Center.Y;

        return Apply(world, p =>
        {
            float phi = (p.Z - bottom) / height * theta;
            float u = (alongX ? p.X : p.Y) - middle;

            float across = middle + radius - (radius - u) * MathF.Cos(phi);
            float up = bottom + (radius - u) * MathF.Sin(phi);

            return alongX ? new Vector3(across, p.Y, up) : new Vector3(p.X, across, up);
        }, token);
    }

    /// <summary>
    /// Whether a bend this tight would fold the inside of the curve through itself. The curve is
    /// centred as far from the middle of the solid as its height over the angle; a solid reaching
    /// that far towards the centre has nowhere for its inside to go.
    /// </summary>
    public static bool Folds(Mesh world, float degrees, Axis towards)
    {
        var bounds = world.ComputeBounds();
        if (bounds.IsEmpty || MathF.Abs(degrees) < 1e-3f) return false;

        float radius = bounds.Size.Z / MathF.Abs(degrees * MathF.PI / 180f);
        float inside = degrees > 0
            ? (towards == Axis.Y ? bounds.Max.Y - bounds.Center.Y : bounds.Max.X - bounds.Center.X)
            : (towards == Axis.Y ? bounds.Center.Y - bounds.Min.Y : bounds.Center.X - bounds.Min.X);

        return inside >= radius * 0.98f;
    }

    /// <summary>The tightest bend this solid takes without folding, in degrees, a hair short of the limit.</summary>
    public static float LargestBend(Mesh world, Axis towards)
    {
        var bounds = world.ComputeBounds();
        float half = towards == Axis.Y ? bounds.Size.Y / 2f : bounds.Size.X / 2f;
        if (half < 1e-4f) return 360f;

        return MathF.Min(360f, bounds.Size.Z / (half / 0.97f) * 180f / MathF.PI);
    }
}
