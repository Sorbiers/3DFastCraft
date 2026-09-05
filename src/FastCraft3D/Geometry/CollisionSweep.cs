using System.Numerics;

namespace FastCraft3D.Geometry;

/// <summary>
/// Works out how far something can travel along one axis before it runs into something else.
///
/// Boxes rather than meshes, deliberately. Sliding one part against another until they touch is
/// how things get assembled, and for the boxes, plates and walls this app is mostly used to make,
/// the bounding box is the shape. Testing the triangles themselves would cost far more per drag
/// and would only differ on rounded or angled faces - where it would stop later than the box
/// does, never sooner, so nothing is ever allowed to pass through anything.
/// </summary>
public static class CollisionSweep
{
    /// <summary>
    /// A hair of clearance left at the contact, so two parts meet rather than share a face.
    /// Coplanar faces are a nuisance for every boolean that might follow.
    /// </summary>
    public const float ClearanceMm = 0.001f;

    /// <summary>
    /// The largest part of <paramref name="travel"/> that can be made along
    /// <paramref name="axis"/> without any of <paramref name="moving"/> overlapping any of
    /// <paramref name="obstacles"/>.
    ///
    /// Returns the full travel when nothing is in the way, and never returns more than was
    /// asked for or a value of the opposite sign - a blocked drag stops, it does not reverse.
    /// </summary>
    public static float Limit(
        IReadOnlyList<Bounds> moving, IReadOnlyList<Bounds> obstacles, Axis axis, float travel)
    {
        if (travel == 0 || moving.Count == 0 || obstacles.Count == 0) return travel;

        float allowed = Math.Abs(travel);
        int direction = travel > 0 ? 1 : -1;

        foreach (var mover in moving)
        {
            if (mover.IsEmpty) continue;

            foreach (var obstacle in obstacles)
            {
                if (obstacle.IsEmpty) continue;
                if (!OverlapsAcross(mover, obstacle, axis)) continue;

                float gap = Gap(mover, obstacle, axis, direction);

                // Already touching or overlapping: this pair permits no movement at all.
                if (gap <= 0) return 0;

                allowed = Math.Min(allowed, gap);
            }
        }

        return allowed * direction;
    }

    /// <summary>
    /// Whether two boxes overlap on the two axes the movement is not along. If they miss each
    /// other sideways they can never meet however far they slide.
    /// </summary>
    private static bool OverlapsAcross(Bounds a, Bounds b, Axis axis)
    {
        for (int i = 0; i < 3; i++)
        {
            if (i == (int)axis) continue;
            if (Component(a.Max, i) <= Component(b.Min, i)) return false;
            if (Component(b.Max, i) <= Component(a.Min, i)) return false;
        }

        return true;
    }

    /// <summary>How much clear space lies between the two along the axis, in the given direction.</summary>
    private static float Gap(Bounds mover, Bounds obstacle, Axis axis, int direction)
    {
        int i = (int)axis;

        // Already inside one another, which cannot have happened with this turned on: parts get
        // overlapped deliberately on their way to a boolean, and the toggle is switched on
        // afterwards. There is no contact to stop at, and holding the object fast until it is
        // switched off again would be nothing but obstruction, so the pair imposes no limit.
        if (Straddles(mover, obstacle, i)) return float.MaxValue;

        float distance = direction > 0
            ? Component(obstacle.Min, i) - Component(mover.Max, i)
            : Component(mover.Min, i) - Component(obstacle.Max, i);

        // Behind us: it cannot be run into by moving this way.
        if (distance < 0) return float.MaxValue;

        return distance - ClearanceMm;
    }

    /// <summary>Whether the two already overlap along the axis.</summary>
    private static bool Straddles(Bounds a, Bounds b, int i) =>
        Component(a.Max, i) > Component(b.Min, i) && Component(b.Max, i) > Component(a.Min, i);

    private static float Component(Vector3 v, int i) => i switch { 0 => v.X, 1 => v.Y, _ => v.Z };
}
