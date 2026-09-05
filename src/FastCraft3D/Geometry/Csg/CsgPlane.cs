namespace FastCraft3D.Geometry.Csg;

/// <summary>An oriented plane: points p with Dot(Normal, p) == W lie on it.</summary>
public struct CsgPlane(Vec3d normal, double w)
{
    /// <summary>
    /// Classification tolerance in millimetres. Chosen to sit above the ~1.5e-5 mm noise
    /// floor of float32 STL coordinates, yet far below any printable feature (~0.1 mm).
    /// </summary>
    public const double Epsilon = 1e-6;

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
