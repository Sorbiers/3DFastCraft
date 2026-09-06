using System.Numerics;

namespace FastCraft3D.Geometry;

public enum Axis
{
    X,
    Y,
    Z
}

public static class MeshTransform
{
    /// <summary>
    /// Applies a transform to every vertex.
    ///
    /// A mirror (or any negative-scale) matrix has a negative determinant, which reverses
    /// the handedness of every triangle: normals end up pointing inwards and the STL exports
    /// inside-out. Detecting that here and flipping the winding is the single fix for the
    /// whole app, so no caller has to remember it.
    /// </summary>
    public static Mesh Transformed(Mesh mesh, Matrix4x4 matrix)
    {
        var positions = new List<Vector3>(mesh.Positions.Count);
        foreach (var p in mesh.Positions)
            positions.Add(Vector3.Transform(p, matrix));

        var result = new Mesh(positions, mesh.Indices);
        if (matrix.GetDeterminant() < 0)
            result.FlipWinding();
        return result;
    }

    public static Mesh Mirrored(Mesh mesh, Axis axis)
    {
        var scale = axis switch
        {
            Axis.X => new Vector3(-1, 1, 1),
            Axis.Y => new Vector3(1, -1, 1),
            _ => new Vector3(1, 1, -1)
        };
        return Transformed(mesh, Matrix4x4.CreateScale(scale));
    }

    /// <summary>Drops the mesh so its lowest point rests on the build plate (Z = 0).</summary>
    public static Mesh AlignedToPlate(Mesh mesh)
    {
        var bounds = mesh.ComputeBounds();
        if (bounds.IsEmpty || MathF.Abs(bounds.Min.Z) < 1e-6f) return mesh.Clone();
        return Transformed(mesh, Matrix4x4.CreateTranslation(0, 0, -bounds.Min.Z));
    }

    /// <summary>
    /// The rotation that carries one direction onto another, used to point a shape along an
    /// arbitrary axis - such as swinging the split plane's half-space box onto its normal.
    /// </summary>
    public static Matrix4x4 RotationBetween(Vector3 from, Vector3 to)
    {
        from = Vector3.Normalize(from);
        to = Vector3.Normalize(to);
        float alignment = Vector3.Dot(from, to);

        if (alignment > 0.99999f) return Matrix4x4.Identity;

        if (alignment < -0.99999f)
        {
            // Exactly opposed: the cross product is degenerate, so any perpendicular axis will
            // do for the half turn. Pick one that is definitely not parallel to "from".
            Vector3 fallback = MathF.Abs(from.X) < 0.9f ? Vector3.UnitX : Vector3.UnitY;
            return Matrix4x4.CreateFromAxisAngle(Vector3.Normalize(Vector3.Cross(from, fallback)), MathF.PI);
        }

        Vector3 axis = Vector3.Normalize(Vector3.Cross(from, to));
        return Matrix4x4.CreateFromAxisAngle(axis, MathF.Acos(Math.Clamp(alignment, -1f, 1f)));
    }

    /// <summary>
    /// Builds the object-to-world matrix. Scale, then rotate (Z-Y-X intrinsic, degrees),
    /// then translate - the order the properties panel presents them in.
    /// </summary>
    public static Matrix4x4 Compose(Vector3 position, Vector3 rotationDegrees, Vector3 scale)
    {
        return Matrix4x4.CreateScale(scale)
             * Rotation(rotationDegrees)
             * Matrix4x4.CreateTranslation(position);
    }

    /// <summary>Just the turn: X, then Y, then Z, which is the order everything here assumes.</summary>
    public static Matrix4x4 Rotation(Vector3 degrees)
    {
        const float toRad = MathF.PI / 180f;
        return Matrix4x4.CreateRotationX(degrees.X * toRad)
             * Matrix4x4.CreateRotationY(degrees.Y * toRad)
             * Matrix4x4.CreateRotationZ(degrees.Z * toRad);
    }

    /// <summary>
    /// The three angles that would build this turn, in degrees.
    ///
    /// Needed because turning something about an axis on screen is not the same as adding to one
    /// of its three angles. Only the last of the three - Z here - happens to line up with the
    /// world; adding to the other two turns the object about its own axes instead, which is why
    /// a second turn used to go somewhere other than where the ring said it would. Composing the
    /// turn properly and reading the angles back out is the way round it.
    ///
    /// Two of the three go missing when the middle angle reaches a right angle: the first and
    /// last then turn about the same line and only their sum is knowable. The whole of it is put
    /// into the first, which gives the same orientation and a readout that at least holds still.
    /// </summary>
    public static Vector3 EulerFrom(Matrix4x4 m)
    {
        const float toDeg = 180f / MathF.PI;

        float pitch = MathF.Asin(Math.Clamp(-m.M13, -1f, 1f));
        float cosPitch = MathF.Cos(pitch);

        float roll, yaw;
        if (MathF.Abs(cosPitch) < 1e-5f)
        {
            yaw = 0f;
            roll = m.M13 < 0
                ? MathF.Atan2(m.M21, m.M22)     // pitched up: only roll minus yaw is knowable
                : MathF.Atan2(-m.M21, m.M22);   // pitched down: only roll plus yaw is
        }
        else
        {
            roll = MathF.Atan2(m.M23, m.M33);
            yaw = MathF.Atan2(m.M12, m.M11);
        }

        return new Vector3(
            Tidy(roll * toDeg), Tidy(pitch * toDeg), Tidy(yaw * toDeg));
    }

    /// <summary>
    /// The nearest turn that leaves the object square with the world.
    ///
    /// Not the same as throwing the turn away. Something stood on its side and then nudged a few
    /// degrees off wants to stay on its side and lose the few degrees; going back to nothing
    /// would stand it up again, undoing work rather than tidying it.
    ///
    /// Each of the object's three axes is sent to whichever world axis it is closest to, and no
    /// two may claim the same one - so the most confident goes first and the others take what is
    /// left. That can leave the three in a mirrored arrangement, which is not a turn at all, so
    /// the least confident of them is flipped back.
    /// </summary>
    public static Vector3 SquareToAxes(Vector3 rotationDegrees)
    {
        var turn = Rotation(rotationDegrees);

        var rows = new[]
        {
            Vector3.TransformNormal(Vector3.UnitX, turn),
            Vector3.TransformNormal(Vector3.UnitY, turn),
            Vector3.TransformNormal(Vector3.UnitZ, turn)
        };

        var axis = new int[3];
        var sign = new float[3];
        var confidence = new float[3];

        for (int i = 0; i < 3; i++)
        {
            axis[i] = Nearest(rows[i], out sign[i], out confidence[i]);
        }

        // Most confident first, so a row that is nearly along an axis keeps it and the doubtful
        // ones make way.
        var order = new[] { 0, 1, 2 };
        Array.Sort(order, (a, b) => confidence[b].CompareTo(confidence[a]));

        var taken = new bool[3];
        foreach (int i in order)
        {
            if (!taken[axis[i]])
            {
                taken[axis[i]] = true;
                continue;
            }

            axis[i] = Free(taken, rows[i], out sign[i]);
            taken[axis[i]] = true;
        }

        var squared = FromRows(Unit(axis[0], sign[0]), Unit(axis[1], sign[1]), Unit(axis[2], sign[2]));

        // Three axes at right angles can still be a reflection rather than a turn. Flipping the
        // one that was least sure of itself is the smallest change that puts it right.
        if (Determinant(squared) < 0)
        {
            int weakest = order[2];
            sign[weakest] = -sign[weakest];
            squared = FromRows(Unit(axis[0], sign[0]), Unit(axis[1], sign[1]), Unit(axis[2], sign[2]));
        }

        return EulerFrom(squared);
    }

    /// <summary>The world axis a direction points most nearly along, and which way round.</summary>
    private static int Nearest(Vector3 direction, out float sign, out float confidence)
    {
        float x = MathF.Abs(direction.X), y = MathF.Abs(direction.Y), z = MathF.Abs(direction.Z);

        int axis = x >= y && x >= z ? 0 : y >= z ? 1 : 2;
        float along = axis == 0 ? direction.X : axis == 1 ? direction.Y : direction.Z;

        sign = along < 0 ? -1f : 1f;
        confidence = MathF.Max(x, MathF.Max(y, z));

        return axis;
    }

    /// <summary>The best of the axes nobody has claimed yet.</summary>
    private static int Free(bool[] taken, Vector3 direction, out float sign)
    {
        int best = -1;
        float strongest = -1f;

        for (int i = 0; i < 3; i++)
        {
            if (taken[i]) continue;

            float along = i == 0 ? direction.X : i == 1 ? direction.Y : direction.Z;
            if (MathF.Abs(along) <= strongest) continue;

            strongest = MathF.Abs(along);
            best = i;
        }

        float pick = best == 0 ? direction.X : best == 1 ? direction.Y : direction.Z;
        sign = pick < 0 ? -1f : 1f;

        return best;
    }

    private static Vector3 Unit(int axis, float sign) => axis switch
    {
        0 => new Vector3(sign, 0, 0),
        1 => new Vector3(0, sign, 0),
        _ => new Vector3(0, 0, sign)
    };

    private static Matrix4x4 FromRows(Vector3 x, Vector3 y, Vector3 z) => new(
        x.X, x.Y, x.Z, 0,
        y.X, y.Y, y.Z, 0,
        z.X, z.Y, z.Z, 0,
        0, 0, 0, 1);

    private static float Determinant(Matrix4x4 m) =>
        m.M11 * (m.M22 * m.M33 - m.M23 * m.M32)
      - m.M12 * (m.M21 * m.M33 - m.M23 * m.M31)
      + m.M13 * (m.M21 * m.M32 - m.M22 * m.M31);

    /// <summary>
    /// Rounds off the last of the arithmetic. A quarter turn should read as 90, not 89.999997,
    /// and the difference is far below anything that could be printed.
    /// </summary>
    private static float Tidy(float degrees)
    {
        float rounded = MathF.Round(degrees, 4);
        return rounded == 0f ? 0f : rounded;
    }
}
