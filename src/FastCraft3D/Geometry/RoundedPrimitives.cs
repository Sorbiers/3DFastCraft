using System.Numerics;

namespace FastCraft3D.Geometry;

/// <summary>Which groups of edges a rounding applies to.</summary>
[Flags]
public enum RoundEdges
{
    None = 0,

    /// <summary>The four edges around the top face; the top rim of a cylinder.</summary>
    Top = 1,

    /// <summary>The four edges around the bottom face; the bottom rim of a cylinder.</summary>
    Bottom = 2,

    /// <summary>The four upright edges of a box. A cylinder has none.</summary>
    Sides = 4,

    All = Top | Bottom | Sides
}

/// <summary>
/// Primitives with rounded edges.
///
/// These are generated directly at the requested radius rather than filleted afterwards.
/// Filleting an arbitrary mesh means detecting edge loops, rolling a ball along them and
/// re-stitching the surface - a genuinely hard problem, and one that goes wrong exactly where
/// booleans have already left an untidy edge. Generating the rounded form from its parameters
/// is exact, fast and always watertight, at the cost of only working on shapes we can describe
/// parametrically. That is why rounding is offered on a box and a cylinder and refused
/// elsewhere, rather than half-working everywhere.
/// </summary>
public static class RoundedPrimitives
{
    /// <summary>Which primitives can be regenerated with a radius.</summary>
    public static bool Supports(PrimitiveKind kind) =>
        kind is PrimitiveKind.Cube or PrimitiveKind.Cylinder;

    /// <summary>A cylinder has no upright edges, so only its two rims can be rounded.</summary>
    public static RoundEdges AvailableEdges(PrimitiveKind kind) => kind switch
    {
        PrimitiveKind.Cube => RoundEdges.All,
        PrimitiveKind.Cylinder => RoundEdges.Top | RoundEdges.Bottom,
        _ => RoundEdges.None
    };

    /// <summary>
    /// The largest radius that still leaves a shape. It depends on which edges are being
    /// rounded: rounding only the upright edges of a tall thin box is limited by its footprint,
    /// not its height.
    /// </summary>
    public static float MaximumRadius(PrimitiveKind kind, Vector3 size, RoundEdges edges)
    {
        edges &= AvailableEdges(kind);
        if (edges == RoundEdges.None) return 0f;

        float limit = float.MaxValue;

        if (kind == PrimitiveKind.Cylinder)
        {
            limit = MathF.Min(size.X, size.Y) / 2f; // the rim cannot exceed the radius
        }
        else if (edges.HasFlag(RoundEdges.Sides))
        {
            limit = MathF.Min(limit, MathF.Min(size.X, size.Y) / 2f);
        }

        if (edges.HasFlag(RoundEdges.Top) || edges.HasFlag(RoundEdges.Bottom))
            limit = MathF.Min(limit, size.Z / 2f);

        return limit is float.MaxValue ? 0f : MathF.Max(limit, 0f);
    }

    public static Mesh Create(PrimitiveKind kind, Vector3 size, float radius,
        RoundEdges edges = RoundEdges.All, int arcSteps = 5) => kind switch
    {
        PrimitiveKind.Cube => RoundedBox(size.X, size.Y, size.Z, radius, edges, arcSteps),
        PrimitiveKind.Cylinder => RoundedCylinder(MathF.Min(size.X, size.Y) / 2f, size.Z, radius,
            edges, Primitives.DefaultSegments, arcSteps),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), $"{kind} cannot be rounded.")
    };

    /// <summary>
    /// A box with any combination of its edge groups rounded.
    ///
    /// Built by sweeping a horizontal cross-section up the shape. The cross-section is a
    /// rounded rectangle whose corner radius gives the upright edges, and it is inset along a
    /// quarter-circle near the top and bottom to give those edges. One sweep therefore produces
    /// all eight combinations, including the cases where a fillet has to die into a sharp edge -
    /// which is exactly where treating the three groups as separate pieces of geometry would
    /// need special cases to stitch them together.
    /// </summary>
    public static Mesh RoundedBox(float sx, float sy, float sz, float radius,
        RoundEdges edges = RoundEdges.All, int arcSteps = 5)
    {
        arcSteps = Math.Max(1, arcSteps);
        radius = Math.Clamp(radius, 0f, MaximumRadius(PrimitiveKind.Cube, new Vector3(sx, sy, sz), edges));
        if (radius <= 1e-4f || edges == RoundEdges.None) return Primitives.Box(sx, sy, sz);

        float hx = sx / 2f, hy = sy / 2f, hz = sz / 2f;
        float cornerRadius = edges.HasFlag(RoundEdges.Sides) ? radius : 0f;

        // Heights to sweep through, ordered top to bottom - the same direction a lathe profile
        // runs, so both share one winding rule.
        var levels = new List<(float Z, float Inset)>();

        if (edges.HasFlag(RoundEdges.Top))
        {
            for (int k = 0; k <= arcSteps; k++)
            {
                float t = MathF.PI / 2f * k / arcSteps;
                levels.Add((hz - radius * (1f - MathF.Cos(t)), radius * (1f - MathF.Sin(t))));
            }
        }
        else
        {
            levels.Add((hz, 0f));
        }

        if (edges.HasFlag(RoundEdges.Bottom))
        {
            for (int k = 0; k <= arcSteps; k++)
            {
                float t = MathF.PI / 2f * k / arcSteps;
                levels.Add((-hz + radius - radius * MathF.Sin(t), radius - radius * MathF.Cos(t)));
            }
        }
        else
        {
            levels.Add((-hz, 0f));
        }

        int ringSize = 4 * (arcSteps + 1);
        var positions = new List<Vector3>(levels.Count * ringSize);

        foreach (var (z, inset) in levels)
        {
            float ex = MathF.Max(hx - inset, 0f);
            float ey = MathF.Max(hy - inset, 0f);
            float rc = Math.Clamp(cornerRadius - inset, 0f, MathF.Min(ex, ey));

            for (int q = 0; q < 4; q++)
            {
                float cx = (q is 0 or 3 ? 1f : -1f) * (ex - rc);
                float cy = (q is 0 or 1 ? 1f : -1f) * (ey - rc);

                for (int k = 0; k <= arcSteps; k++)
                {
                    float theta = MathF.PI / 2f * (q + k / (float)arcSteps);
                    positions.Add(new Vector3(cx + rc * MathF.Cos(theta), cy + rc * MathF.Sin(theta), z));
                }
            }
        }

        var mesh = new Mesh(positions, BuildSweepIndices(levels.Count, ringSize));

        // The rings are convex, so a fan from the centre closes each end.
        AddFan(mesh, positions, 0, ringSize, levels[0].Z, up: true);
        AddFan(mesh, positions, (levels.Count - 1) * ringSize, ringSize, levels[^1].Z, up: false);

        return mesh.Welded();
    }

    /// <summary>A cylinder with either or both rims rounded.</summary>
    public static Mesh RoundedCylinder(float radius, float height, float cornerRadius,
        RoundEdges edges = RoundEdges.All, int segments = Primitives.DefaultSegments, int arcSteps = 5)
    {
        arcSteps = Math.Max(1, arcSteps);
        edges &= AvailableEdges(PrimitiveKind.Cylinder);
        cornerRadius = Math.Clamp(cornerRadius, 0f, MathF.Min(radius, height / 2f));
        if (cornerRadius <= 1e-4f || edges == RoundEdges.None)
            return Primitives.Prism(radius, height, segments);

        float h = height / 2f;
        float straight = radius - cornerRadius;

        // Half a cross-section, from the top axis down to the bottom axis.
        var profile = new List<Vector2> { new(0f, h) };

        if (edges.HasFlag(RoundEdges.Top))
        {
            for (int i = 0; i <= arcSteps; i++)
            {
                float t = MathF.PI / 2f * i / arcSteps;
                profile.Add(new Vector2(straight + cornerRadius * MathF.Sin(t),
                    h - cornerRadius + cornerRadius * MathF.Cos(t)));
            }
        }
        else
        {
            profile.Add(new Vector2(radius, h));
        }

        if (edges.HasFlag(RoundEdges.Bottom))
        {
            for (int i = 0; i <= arcSteps; i++)
            {
                float t = MathF.PI / 2f * i / arcSteps;
                profile.Add(new Vector2(straight + cornerRadius * MathF.Cos(t),
                    -h + cornerRadius - cornerRadius * MathF.Sin(t)));
            }
        }
        else
        {
            profile.Add(new Vector2(radius, -h));
        }

        profile.Add(new Vector2(0f, -h));
        return Revolve(profile, segments);
    }

    /// <summary>
    /// Sweeps a profile around the Z axis. Profile points are (radius, height) and must run from
    /// top to bottom; points on the axis collapse to a single vertex when the result is welded.
    /// </summary>
    public static Mesh Revolve(IReadOnlyList<Vector2> profile, int segments)
    {
        segments = Math.Max(3, segments);

        var positions = new List<Vector3>(profile.Count * segments);
        foreach (var point in profile)
        {
            for (int i = 0; i < segments; i++)
            {
                float theta = MathF.Tau * i / segments;
                positions.Add(new Vector3(point.X * MathF.Cos(theta), point.X * MathF.Sin(theta), point.Y));
            }
        }

        return new Mesh(positions, BuildSweepIndices(profile.Count, segments)).Welded();
    }

    /// <summary>
    /// Quads between consecutive rings of equal size, wound so the normal faces away from the
    /// axis. Shared by the box sweep and the lathe.
    /// </summary>
    private static List<int> BuildSweepIndices(int rings, int ringSize)
    {
        var indices = new List<int>((rings - 1) * ringSize * 6);

        for (int j = 0; j + 1 < rings; j++)
        {
            for (int i = 0; i < ringSize; i++)
            {
                int i2 = (i + 1) % ringSize;
                int a = j * ringSize + i, b = j * ringSize + i2;
                int c = (j + 1) * ringSize + i2, d = (j + 1) * ringSize + i;
                indices.AddRange([a, d, c, a, c, b]);
            }
        }

        return indices;
    }

    private static void AddFan(Mesh mesh, List<Vector3> positions, int start, int ringSize, float z, bool up)
    {
        var centre = new Vector3(0, 0, z);

        for (int i = 0; i < ringSize; i++)
        {
            Vector3 a = positions[start + i];
            Vector3 b = positions[start + (i + 1) % ringSize];
            if (up) mesh.AddTriangle(centre, a, b);
            else mesh.AddTriangle(centre, b, a);
        }
    }
}
