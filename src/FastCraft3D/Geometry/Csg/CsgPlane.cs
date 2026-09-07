namespace FastCraft3D.Geometry.Csg;

/// <summary>An oriented plane: points p with Dot(Normal, p) == W lie on it.</summary>
public struct CsgPlane(Vec3d normal, double w)
{
    /// <summary>
    /// Classification tolerance in millimetres. It has to sit above the noise floor of the
    /// float32 coordinates that come in, and far below any printable feature.
    ///
    /// It read 1e-6 for a long time while the comment beside it said 1.5e-5, and the comment was
    /// right: at 90 mm from the origin - an ordinary place to be on a 200 mm plate - one step of
    /// a float is 7.6e-6 mm, so two faces that are the same plane can arrive nearly eight times
    /// the old tolerance apart. Vertices that should have been coplanar were sorted to the front
    /// on one triangle and the back on the next, and the seam between them came out as slivers.
    /// A staircase - a dozen boxes each meeting the next on part of a face - tore every time.
    /// A tenth of a micron is still a thousandth of a nozzle.
    /// </summary>
    public const double Epsilon = 1e-4;

    public Vec3d Normal = normal;
    public double W = w;

    public static bool TryFromPoints(Vec3d a, Vec3d b, Vec3d c, out CsgPlane plane)
    {
        Vec3d n = Vec3d.Cross(b - a, c - a);
        double len = n.Length;
        if (len < 1e-14) // degenerate (collinear) triangle
        {
            plane = default;
            return false;
        }
        n = n * (1.0 / len);
        plane = new CsgPlane(n, Vec3d.Dot(n, a));
        return true;
    }

    public void Flip()
    {
        Normal = -Normal;
        W = -W;
    }

    public readonly double DistanceTo(Vec3d p) => Vec3d.Dot(Normal, p) - W;
}
