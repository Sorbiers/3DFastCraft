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
/// It was a boolean against an oversized box covering one half-space: the CSG engine produced
/// the cap for free and, in principle, guaranteed both halves stayed closed - which is the whole
/// point, since an uncapped half will not print. In practice it did not. On a 214,000 triangle
/// scan cut through the middle it took twenty seconds, gave back two and a half million
/// triangles, and left nearly twenty thousand open edges in one half. The BSP is fragile against
/// dense curved surfaces and this is the worst case for it: a plane meeting a scan everywhere at
/// once.
///
/// Cutting the triangles directly does the same job in a twenty-fifth of a second, keeps the
/// original triangles untouched away from the cut, and comes back watertight - the cap is
/// stitched from the edges the cut left. See <see cref="PlaneClip"/>.
///
/// The boolean is still here for the one thing it is better at. It does not cut the surface it
/// is given; it works out inside from outside and builds a new one, so it can close a solid that
/// arrived slightly open - which the mould does routinely, since what it splits is a boolean's
/// own output. So the cut is tried the honest way first and the boolean is kept for when it does
/// not come back closed, and then only on a solid small enough for it to finish.
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

        if (mesh.ComputeBounds().IsEmpty) return (null, null);

        normal = Vector3.Normalize(normal);

        // Welded, as everything that builds a mesh here ends up doing. The clip hands back loose
        // triangles, which draw the same but describe nothing: the selection outline works from
        // shared corners, and without them every edge looks like a boundary and the part comes
        // back covered in white lines. The preview skips this - nothing traces its outline, and
        // it would rather have the time.
        Mesh? front = keep is SplitKeep.Front or SplitKeep.Both
            ? NullIfEmpty(PlaneClip.Keep(mesh, Matrix4x4.Identity, normal, offset).Welded())
            : null;

        token.ThrowIfCancellationRequested();

        Mesh? back = keep is SplitKeep.Back or SplitKeep.Both
            ? NullIfEmpty(PlaneClip.Keep(mesh, Matrix4x4.Identity, -normal, -offset).Welded())
            : null;

        if (mesh.TriangleCount <= BooleanLimit && (Torn(front) || Torn(back)))
            return Carve(mesh, normal, offset, keep, token);

        return (front, back);

        static Mesh? NullIfEmpty(Mesh m) => m.TriangleCount > 0 ? m : null;

        static bool Torn(Mesh? half) => half is not null && !half.CheckHealth().IsWatertight;
    }

    /// <summary>
    /// How big a solid the boolean is worth falling back to.
    ///
    /// It is quadratic. At twenty thousand triangles it already takes seconds; at two hundred
    /// thousand it took twenty of them and left twenty thousand open edges, which is worse than
    /// whatever the clip handed back and a great deal slower to arrive at.
    /// </summary>
    private const int BooleanLimit = 20_000;

    /// <summary>
    /// The old way: subtract, or intersect with, an oversized box covering one half-space.
    ///
    /// Slow and fragile, and worth keeping only because it rebuilds the surface rather than
    /// cutting it, so it can close a solid that was already a little open.
    /// </summary>
    private static (Mesh? Front, Mesh? Back) Carve(Mesh mesh, Vector3 normal, float offset,
                                                   SplitKeep keep, CancellationToken token)
    {
        var halfSpace = BuildHalfSpaceBox(mesh.ComputeBounds(), normal, offset);

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
