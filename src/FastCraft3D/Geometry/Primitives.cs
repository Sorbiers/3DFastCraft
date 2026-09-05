using System.Numerics;

namespace FastCraft3D.Geometry;

public enum PrimitiveKind
{
    Cube,
    Cylinder,
    Cone,
    Sphere,
    Pyramid,
    Wedge,
    Torus,
    Hexagon,
    Tetrahedron
}

/// <summary>
/// Generators for the Insert toolbar. Every shape is centred on the origin, Z-up, sized in
/// millimetres, watertight, and wound so that <see cref="Mesh.ComputeSignedVolume"/> is
/// positive - i.e. normals face outwards, which is what STL requires.
///
/// Round shapes default to 32 segments: the GPU would happily take far more, but every
/// extra triangle multiplies the cost of the BSP tree that boolean operations build.
/// </summary>
public static class Primitives
{
    public const int DefaultSegments = 32;

    public static Mesh Create(PrimitiveKind kind, float size = 20f) => kind switch
    {
        PrimitiveKind.Cube => Box(size, size, size),
        PrimitiveKind.Cylinder => Prism(size / 2f, size, DefaultSegments),
        PrimitiveKind.Cone => Cone(size / 2f, size, DefaultSegments),
        PrimitiveKind.Sphere => Sphere(size / 2f, DefaultSegments, DefaultSegments / 2),
        PrimitiveKind.Pyramid => Pyramid(size, size),
        PrimitiveKind.Wedge => Wedge(size, size, size),
        PrimitiveKind.Torus => Torus(size * 0.35f, size * 0.15f, DefaultSegments, DefaultSegments / 2),
        PrimitiveKind.Hexagon => Prism(size / 2f, size, 6),
        PrimitiveKind.Tetrahedron => Tetrahedron(size),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    public static Mesh Box(float sx, float sy, float sz)
    {
        float x = sx / 2f, y = sy / 2f, z = sz / 2f;
        var b = new Builder();
        int v0 = b.Add(new Vector3(-x, -y, -z));
        int v1 = b.Add(new Vector3(x, -y, -z));
        int v2 = b.Add(new Vector3(x, y, -z));
        int v3 = b.Add(new Vector3(-x, y, -z));
        int v4 = b.Add(new Vector3(-x, -y, z));
        int v5 = b.Add(new Vector3(x, -y, z));
        int v6 = b.Add(new Vector3(x, y, z));
        int v7 = b.Add(new Vector3(-x, y, z));

        b.Quad(v0, v3, v2, v1); // -Z
        b.Quad(v4, v5, v6, v7); // +Z
        b.Quad(v0, v1, v5, v4); // -Y
        b.Quad(v1, v2, v6, v5); // +X
        b.Quad(v2, v3, v7, v6); // +Y
        b.Quad(v3, v0, v4, v7); // -X
        return b.ToMesh();
    }

    /// <summary>Cylinder and hexagon share this: a regular n-gon prism about the Z axis.</summary>
    public static Mesh Prism(float radius, float height, int sides)
    {
        sides = Math.Max(3, sides);
        float h = height / 2f;
        var b = new Builder();

        var bottom = new int[sides];
        var top = new int[sides];
        for (int i = 0; i < sides; i++)
        {
            float a = MathF.Tau * i / sides;
            float cx = radius * MathF.Cos(a), cy = radius * MathF.Sin(a);
            bottom[i] = b.Add(new Vector3(cx, cy, -h));
            top[i] = b.Add(new Vector3(cx, cy, h));
        }

        int centreBottom = b.Add(new Vector3(0, 0, -h));
        int centreTop = b.Add(new Vector3(0, 0, h));

        for (int i = 0; i < sides; i++)
        {
            int j = (i + 1) % sides;
            b.Quad(bottom[i], bottom[j], top[j], top[i]);
            b.Tri(centreTop, top[i], top[j]);
            b.Tri(centreBottom, bottom[j], bottom[i]);
        }
        return b.ToMesh();
    }

    public static Mesh Cone(float radius, float height, int sides)
    {
        sides = Math.Max(3, sides);
        float h = height / 2f;
        var b = new Builder();

        var ring = new int[sides];
        for (int i = 0; i < sides; i++)
        {
            float a = MathF.Tau * i / sides;
            ring[i] = b.Add(new Vector3(radius * MathF.Cos(a), radius * MathF.Sin(a), -h));
        }
        int apex = b.Add(new Vector3(0, 0, h));
        int centre = b.Add(new Vector3(0, 0, -h));

        for (int i = 0; i < sides; i++)
        {
            int j = (i + 1) % sides;
            b.Tri(apex, ring[i], ring[j]);
            b.Tri(centre, ring[j], ring[i]);
        }
        return b.ToMesh();
    }

    /// <summary>
    /// UV sphere with poles on Z. The pole rings collapse to a point, producing degenerate
    /// triangles that the final weld removes - simpler and less error-prone than special-casing.
    /// </summary>
    public static Mesh Sphere(float radius, int segments, int rings)
    {
        segments = Math.Max(3, segments);
        rings = Math.Max(2, rings);
        var b = new Builder();

        var grid = new int[rings + 1, segments];
        for (int j = 0; j <= rings; j++)
        {
            float phi = MathF.PI * j / rings;
            float sp = MathF.Sin(phi), cp = MathF.Cos(phi);
            for (int i = 0; i < segments; i++)
            {
                float theta = MathF.Tau * i / segments;
                grid[j, i] = b.Add(new Vector3(
                    radius * sp * MathF.Cos(theta),
                    radius * sp * MathF.Sin(theta),
                    radius * cp));
            }
        }

        for (int j = 0; j < rings; j++)
        {
            for (int i = 0; i < segments; i++)
            {
                int i2 = (i + 1) % segments;
                b.Quad(grid[j, i], grid[j + 1, i], grid[j + 1, i2], grid[j, i2]);
            }
        }

        return b.ToMesh();
    }

    public static Mesh Pyramid(float baseSize, float height)
    {
        float s = baseSize / 2f, h = height / 2f;
        var b = new Builder();
        int c0 = b.Add(new Vector3(-s, -s, -h));
        int c1 = b.Add(new Vector3(s, -s, -h));
        int c2 = b.Add(new Vector3(s, s, -h));
        int c3 = b.Add(new Vector3(-s, s, -h));
        int apex = b.Add(new Vector3(0, 0, h));

        b.Quad(c0, c3, c2, c1); // base, -Z
        b.Tri(apex, c0, c1);
        b.Tri(apex, c1, c2);
        b.Tri(apex, c2, c3);
        b.Tri(apex, c3, c0);
        return b.ToMesh();
    }

    /// <summary>Right triangular prism: a box whose upper +X edge is sloped away.</summary>
    public static Mesh Wedge(float sx, float sy, float sz)
    {
        float x = sx / 2f, y = sy / 2f, z = sz / 2f;
        var bld = new Builder();
        int a0 = bld.Add(new Vector3(-x, -y, -z));
        int b0 = bld.Add(new Vector3(x, -y, -z));
        int c0 = bld.Add(new Vector3(-x, -y, z));
        int a1 = bld.Add(new Vector3(-x, y, -z));
        int b1 = bld.Add(new Vector3(x, y, -z));
        int c1 = bld.Add(new Vector3(-x, y, z));

        bld.Tri(a0, b0, c0);      // -Y cap
        bld.Tri(a1, c1, b1);      // +Y cap
        bld.Quad(a0, a1, b1, b0); // -Z
        bld.Quad(a0, c0, c1, a1); // -X
        bld.Quad(b0, b1, c1, c0); // slope
        return bld.ToMesh();
    }

    public static Mesh Torus(float majorRadius, float minorRadius, int segments, int sides)
    {
        segments = Math.Max(3, segments);
        sides = Math.Max(3, sides);
        var b = new Builder();

        var grid = new int[segments, sides];
        for (int i = 0; i < segments; i++)
        {
            float theta = MathF.Tau * i / segments;
            float ct = MathF.Cos(theta), st = MathF.Sin(theta);
            for (int j = 0; j < sides; j++)
            {
                float phi = MathF.Tau * j / sides;
                float r = majorRadius + minorRadius * MathF.Cos(phi);
                grid[i, j] = b.Add(new Vector3(r * ct, r * st, minorRadius * MathF.Sin(phi)));
            }
        }

        for (int i = 0; i < segments; i++)
        {
            for (int j = 0; j < sides; j++)
            {
                int i2 = (i + 1) % segments, j2 = (j + 1) % sides;
                b.Quad(grid[i, j], grid[i2, j], grid[i2, j2], grid[i, j2]);
            }
        }

        return b.ToMesh();
    }

    /// <summary>Regular tetrahedron inscribed in a cube of edge <paramref name="size"/>.</summary>
    public static Mesh Tetrahedron(float size)
    {
        float s = size / 2f;
        var b = new Builder();
        int v0 = b.Add(new Vector3(s, s, s));
        int v1 = b.Add(new Vector3(s, -s, -s));
        int v2 = b.Add(new Vector3(-s, s, -s));
        int v3 = b.Add(new Vector3(-s, -s, s));

        b.Tri(v0, v1, v2);
        b.Tri(v0, v2, v3);
        b.Tri(v0, v3, v1);
        b.Tri(v1, v3, v2);
        return b.ToMesh();
    }

    private sealed class Builder
    {
        private readonly List<Vector3> positions = new();
        private readonly List<int> indices = new();

        public int Add(Vector3 v)
        {
            positions.Add(v);
            return positions.Count - 1;
        }

        public void Tri(int a, int b, int c)
        {
            indices.Add(a);
            indices.Add(b);
            indices.Add(c);
        }

        /// <summary>Quad wound counter-clockwise seen from outside.</summary>
        public void Quad(int a, int b, int c, int d)
        {
            Tri(a, b, c);
            Tri(a, c, d);
        }

        public Mesh ToMesh() => new Mesh(positions, indices).Welded();
    }
}
