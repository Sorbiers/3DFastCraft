using System.Numerics;

namespace FastCraft3D.Geometry.Csg;

/// <summary>
/// Double-precision vector used only inside the CSG engine.
/// Float32 would leave ~1.5e-5 mm of noise at typical build-plate coordinates, which is
/// enough to misclassify vertices against a cutting plane and produce leaky solids.
/// </summary>
public readonly struct Vec3d(double x, double y, double z)
{
    public readonly double X = x, Y = y, Z = z;

    public static Vec3d operator +(Vec3d a, Vec3d b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Vec3d operator -(Vec3d a, Vec3d b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static Vec3d operator *(Vec3d a, double s) => new(a.X * s, a.Y * s, a.Z * s);
    public static Vec3d operator -(Vec3d a) => new(-a.X, -a.Y, -a.Z);

    public static double Dot(Vec3d a, Vec3d b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    public static Vec3d Cross(Vec3d a, Vec3d b) => new(
        a.Y * b.Z - a.Z * b.Y,
        a.Z * b.X - a.X * b.Z,
        a.X * b.Y - a.Y * b.X);

    public double Length => Math.Sqrt(Dot(this, this));

    public Vec3d Normalized()
    {
        double len = Length;
        return len > 0 ? this * (1.0 / len) : this;
    }

    public static Vec3d Lerp(Vec3d a, Vec3d b, double t) => a + (b - a) * t;

    public Vector3 ToVector3() => new((float)X, (float)Y, (float)Z);
    public static Vec3d From(Vector3 v) => new(v.X, v.Y, v.Z);
}
