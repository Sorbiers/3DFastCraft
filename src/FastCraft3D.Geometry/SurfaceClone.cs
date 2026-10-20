using System.Numerics;

namespace FastCraft3D.Geometry;

/// <summary>
/// A flat face made into a thin solid of its own, standing on the face it was taken from.
///
/// The face's own triangles are its underside, exactly where the face is, and a copy of them
/// lifted along the normal is its top, joined round the outline. Lifted outward, so the copy sits
/// on the part rather than inside it: a pad, a label plate, the outline of an opening to build a
/// frame on. Then laid again from its corners, since a face that came out of a boolean is usually
/// cut into slivers, and there is no reason for the copy to keep them.
/// </summary>
public static class SurfaceClone
{
    public const float DefaultThicknessMm = 0.1f;

    /// <summary>The face as a solid <paramref name="thickness"/> thick, or null when its outline does not make one.</summary>
    public static Mesh? Extrude(FacePatch face, float thickness = DefaultThicknessMm)
    {
        if (face.Triangles.Count == 0 || face.Boundary.Count < 3 || !(thickness > 0)) return null;

        var source = face.Mesh;
        var lift = Vector3.Normalize(face.Normal) * thickness;

        var points = new List<Vector3>();
        var below = new Dictionary<int, int>();
        var above = new Dictionary<int, int>();

        int Below(int v)
        {
            if (below.TryGetValue(v, out int i)) return i;
            below[v] = i = points.Count;
            points.Add(source.Positions[v]);
            return i;
        }

        int Above(int v)
        {
            if (above.TryGetValue(v, out int i)) return i;
            above[v] = i = points.Count;
            points.Add(source.Positions[v] + lift);
            return i;
        }

        var indices = new List<int>();
        foreach (int t in face.Triangles)
        {
            int a = source.Indices[t], b = source.Indices[t + 1], c = source.Indices[t + 2];

            // Underneath faces into the part it came from, so it is wound the other way round.
            indices.AddRange([Below(a), Below(c), Below(b)]);
            indices.AddRange([Above(a), Above(b), Above(c)]);
        }

        // The outline runs the face's way round, so each side faces out from it.
        foreach (var (a, b) in face.Boundary)
        {
            indices.AddRange([Below(a), Below(b), Above(b)]);
            indices.AddRange([Below(a), Above(b), Above(a)]);
        }

        var solid = new Mesh(points, indices);
        if (!solid.CheckHealth().IsWatertight) return null;

        var tidy = OutlineRebuild.Rebuild(solid);
        return tidy.Changed ? tidy.Mesh : solid;
    }
}
