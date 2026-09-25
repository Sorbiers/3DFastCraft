using System.Numerics;

namespace FastCraft3D.Geometry;

/// <summary>
/// Whether a point is inside a closed mesh, asked many times over of the same mesh.
///
/// Counted crossings: a ray from the point crosses an odd number of triangles if it started
/// inside. The ray runs along +Z, and the triangles are bucketed by where they sit in X and Y
/// first, so a cast looks at a handful of them rather than at the whole model. Without that,
/// testing a few thousand points against a few thousand triangles is seconds rather than the
/// millisecond it needs to be while a number is being typed.
///
/// The sample point is nudged by a fraction of a micron first. A ray through a shared edge is
/// counted twice or not at all, and those cases are not rare: the diagonal splitting a box's face
/// into two triangles runs corner to corner, so the middle of any face is exactly on one. The
/// nudge is the same every time for the same point - an answer that changed between the preview
/// and the apply would be worse than a wrong one - and the two amounts are in a ratio no face's
/// diagonal is likely to share, since a point moved along such a diagonal is not moved off it.
/// </summary>
public sealed class SolidLookup
{
    /// <summary>
    /// How wide a bucket is, as a share of the model. Sixty-four cells across is small enough
    /// that a cast sees a few triangles and large enough that building the index is cheap.
    /// </summary>
    private const int Across = 64;

    private readonly Mesh mesh;
    private readonly List<int>[] buckets;
    private readonly float originX, originY, cell;
    private readonly int nx, ny;

    public SolidLookup(Mesh mesh)
    {
        this.mesh = mesh;

        var bounds = mesh.ComputeBounds();
        var size = bounds.Max - bounds.Min;

        cell = MathF.Max(MathF.Max(size.X, size.Y) / Across, 1e-3f);
        originX = bounds.Min.X;
        originY = bounds.Min.Y;

        nx = Math.Clamp((int)MathF.Ceiling(size.X / cell) + 1, 1, Across * 4);
        ny = Math.Clamp((int)MathF.Ceiling(size.Y / cell) + 1, 1, Across * 4);

        buckets = new List<int>[nx * ny];

        for (int t = 0; t + 2 < mesh.Indices.Count; t += 3)
        {
            Vector3 a = mesh.Positions[mesh.Indices[t]];
            Vector3 b = mesh.Positions[mesh.Indices[t + 1]];
            Vector3 c = mesh.Positions[mesh.Indices[t + 2]];

            int i0 = Column(MathF.Min(a.X, MathF.Min(b.X, c.X)), originX, nx);
            int i1 = Column(MathF.Max(a.X, MathF.Max(b.X, c.X)), originX, nx);
            int j0 = Column(MathF.Min(a.Y, MathF.Min(b.Y, c.Y)), originY, ny);
            int j1 = Column(MathF.Max(a.Y, MathF.Max(b.Y, c.Y)), originY, ny);

            for (int i = i0; i <= i1; i++)
                for (int j = j0; j <= j1; j++)
                    (buckets[j * nx + i] ??= []).Add(t);
        }
    }

    private int Column(float value, float origin, int count) =>
        Math.Clamp((int)MathF.Floor((value - origin) / cell), 0, count - 1);

    /// <summary>
    /// How far the sample is moved off whatever edge it may be sitting on. Well under a micron,
    /// and in a ratio that is not the aspect of anything anybody models.
    /// </summary>
    private const float NudgeX = 7.3e-4f;
    private const float NudgeY = -NudgeX * 0.7548776662f;

    public bool Contains(Vector3 point)
    {
        float x = point.X + NudgeX, y = point.Y + NudgeY;

        var found = buckets[Column(y, originY, ny) * nx + Column(x, originX, nx)];
        if (found is null) return false;

        int crossings = 0;

        foreach (int t in found)
        {
            Vector3 a = mesh.Positions[mesh.Indices[t]];
            Vector3 b = mesh.Positions[mesh.Indices[t + 1]];
            Vector3 c = mesh.Positions[mesh.Indices[t + 2]];

            if (Crosses(a, b, c, x, y, point.Z)) crossings++;
        }

        return (crossings & 1) == 1;
    }

    /// <summary>
    /// Whether the upward ray from (<paramref name="x"/>, <paramref name="y"/>,
    /// <paramref name="z"/>) passes through this triangle.
    /// </summary>
    private static bool Crosses(Vector3 a, Vector3 b, Vector3 c, float x, float y, float z)
    {
        // Barycentric weights of the point on the triangle seen from above. A triangle edge-on to
        // the ray has no area up there and cannot be crossed by it.
        float area = (b.Y - c.Y) * (a.X - c.X) + (c.X - b.X) * (a.Y - c.Y);
        if (MathF.Abs(area) < 1e-12f) return false;

        float first = ((b.Y - c.Y) * (x - c.X) + (c.X - b.X) * (y - c.Y)) / area;
        float second = ((c.Y - a.Y) * (x - c.X) + (a.X - c.X) * (y - c.Y)) / area;
        float third = 1f - first - second;

        if (first < 0f || second < 0f || third < 0f) return false;

        // And it only counts if the triangle is above the point rather than below it.
        return first * a.Z + second * b.Z + third * c.Z > z;
    }
}
