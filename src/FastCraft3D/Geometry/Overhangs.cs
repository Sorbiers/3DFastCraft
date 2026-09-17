using System.Numerics;

namespace FastCraft3D.Geometry;

/// <summary>
/// The faces a printer cannot lay plastic on without support: those facing down more steeply than
/// an angle from upright, anywhere but flat on the bed.
///
/// The angle is measured the way slicers measure it. A wall is nought, a ceiling ninety, and a
/// printer bridges or droops somewhere past forty-five; a face tilted by some angle from upright
/// has a normal pointing that far below level, so its Z is minus the sine of the angle. A face
/// flat on the bed faces straight down too, and is the best-supported face there is, so faces
/// touching the bed are left out.
/// </summary>
public static class Overhangs
{
    public const float DefaultAngle = 45f;

    /// <summary>How near the bed a face may be and still count as standing on it.</summary>
    private const float OnTheBed = 0.05f;

    /// <summary>Whether a world-space triangle overhangs past the angle.</summary>
    public static bool IsOverhang(Vector3 a, Vector3 b, Vector3 c, float angleDegrees, float bedZ = 0f)
    {
        var normal = Vector3.Cross(b - a, c - a);
        float length = normal.Length();
        if (length < 1e-12f) return false;

        float limit = MathF.Sin(Math.Clamp(angleDegrees, 0f, 89.9f) * MathF.PI / 180f);
        if (normal.Z / length >= -limit) return false;

        return MathF.Max(a.Z, MathF.Max(b.Z, c.Z)) > bedZ + OnTheBed;
    }

    /// <summary>
    /// The overhanging faces of a world-space mesh, lifted a hair off the surface along their own
    /// normals so they can be drawn over it.
    /// </summary>
    public static Mesh Faces(Mesh world, float angleDegrees, float bedZ = 0f, float lift = 0.03f)
    {
        var faces = new Mesh();
        for (int t = 0; t + 2 < world.Indices.Count; t += 3)
        {
            var a = world.Positions[world.Indices[t]];
            var b = world.Positions[world.Indices[t + 1]];
            var c = world.Positions[world.Indices[t + 2]];
            if (!IsOverhang(a, b, c, angleDegrees, bedZ)) continue;

            var up = Vector3.Normalize(Vector3.Cross(b - a, c - a)) * lift;
            faces.AddTriangle(a + up, b + up, c + up);
        }

        return faces;
    }

    /// <summary>How much of a world-space mesh overhangs, in square millimetres.</summary>
    public static double Area(Mesh world, float angleDegrees, float bedZ = 0f)
    {
        double area = 0;
        for (int t = 0; t + 2 < world.Indices.Count; t += 3)
        {
            var a = world.Positions[world.Indices[t]];
            var b = world.Positions[world.Indices[t + 1]];
            var c = world.Positions[world.Indices[t + 2]];
            if (IsOverhang(a, b, c, angleDegrees, bedZ)) area += Vector3.Cross(b - a, c - a).Length() / 2.0;
        }

        return area;
    }
}
