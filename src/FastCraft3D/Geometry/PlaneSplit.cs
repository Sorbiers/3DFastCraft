using System.Numerics;
using FastCraft3D.Geometry.Csg;

namespace FastCraft3D.Geometry;

public enum SplitKeep
{
    Front,
    Back,
    Both
}

/// <summary>
/// Splits a solid with a plane.
///
/// Rather than clipping triangles and then stitching a cap over the opening, the split is
/// expressed as a boolean against an oversized box covering one half-space. The CSG engine then
/// produces the cap for free and guarantees both halves stay closed - which is the whole point,
/// since an uncapped half is not printable.
///
/// The plane is <c>dot(normal, p) == offset</c>. "Front" is the side the normal points towards.
/// </summary>
public static class PlaneSplit
{
    public static Vector3 NormalFor(Axis axis) => axis switch
    {
        Axis.X => Vector3.UnitX,
        Axis.Y => Vector3.UnitY,
        _ => Vector3.UnitZ
    };

    public static (Mesh? Front, Mesh? Back) Split(Mesh mesh, Axis axis, float offset, SplitKeep keep,
                                                 CancellationToken token = default) =>
        Split(mesh, NormalFor(axis), offset, keep, token);

    /// <summary>
    /// Splits at <paramref name="offset"/> millimetres along <paramref name="normal"/>.
    /// A half is null when the plane misses the solid entirely.
    /// </summary>
    public static (Mesh? Front, Mesh? Back) Split(Mesh mesh, Vector3 normal, float offset, SplitKeep keep,
                                                 CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();

        var bounds = mesh.ComputeBounds();
        if (bounds.IsEmpty) return (null, null);

        normal = Vector3.Normalize(normal);
        var halfSpace = BuildHalfSpaceBox(bounds, normal, offset);

        Mesh? front = keep is SplitKeep.Front or SplitKeep.Both
            ? NullIfEmpty(CsgSolid.Subtract(mesh, halfSpace, token: token))
            : null;

        Mesh? back = keep is SplitKeep.Back or SplitKeep.Both
            ? NullIfEmpty(CsgSolid.Intersect(mesh, halfSpace, token: token))
            : null;

        return (front, back);

        static Mesh? NullIfEmpty(Mesh m) => m.TriangleCount > 0 ? m : null;
    }

    /// <summary>
    /// A box covering everything behind the plane.
    ///
    /// It is centred on the point of the plane nearest the solid, not on the plane's closest
    /// point to the world origin: a part sitting far out on the plate would otherwise fall
    /// outside the box sideways and be left uncut.
    /// </summary>
    private static Mesh BuildHalfSpaceBox(Bounds bounds, Vector3 normal, float offset)
    {
        float span = bounds.Diagonal * 2f + 10f;

        float distance = Vector3.Dot(normal, bounds.Center) - offset;
        Vector3 planePoint = bounds.Center - normal * distance;

        // Deep enough to reach past the far side of the solid however far away the plane is.
        // A fixed depth silently fails once the plane travels beyond it: the box stops short,
        // the subtraction removes nothing, and the "split" returns the original solid intact.
        var (nearest, _) = OffsetRange(bounds, normal);
        float depth = MathF.Max(offset - nearest, 0f) + span;

        // A box shifted to sit entirely behind its own origin plane, then swung onto the normal
        // and moved to the plane.
        var box = MeshTransform.Transformed(
            Primitives.Box(span, span, depth),
            Matrix4x4.CreateTranslation(0, 0, -depth / 2f));

        var placement = MeshTransform.RotationBetween(Vector3.UnitZ, normal)
                        * Matrix4x4.CreateTranslation(planePoint);

        return MeshTransform.Transformed(box, placement);
    }

    public static (float Min, float Max) OffsetRange(Bounds bounds, Axis axis) =>
        OffsetRange(bounds, NormalFor(axis));

    /// <summary>How far the plane can travel along its normal and still meet the solid.</summary>
    public static (float Min, float Max) OffsetRange(Bounds bounds, Vector3 normal)
    {
        if (bounds.IsEmpty) return (0f, 0f);

        normal = Vector3.Normalize(normal);
        float min = float.MaxValue, max = float.MinValue;

        for (int i = 0; i < 8; i++)
        {
            var corner = new Vector3(
                (i & 1) == 0 ? bounds.Min.X : bounds.Max.X,
                (i & 2) == 0 ? bounds.Min.Y : bounds.Max.Y,
                (i & 4) == 0 ? bounds.Min.Z : bounds.Max.Z);

            float d = Vector3.Dot(normal, corner);
            min = MathF.Min(min, d);
            max = MathF.Max(max, d);
        }

        return (min, max);
    }
}
