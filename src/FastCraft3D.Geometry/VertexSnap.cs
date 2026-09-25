using System.Numerics;

namespace FastCraft3D.Geometry;

/// <summary>
/// Pulls a picked point onto a real feature of the model.
///
/// A measurement taken wherever the pointer happened to land is worth very little. What people
/// actually want to know is the distance between two corners, or from a corner to the middle of
/// an edge, and those are places the mesh already has. Snapping turns an approximate click into
/// an exact answer.
/// </summary>
public static class VertexSnap
{
    /// <summary>
    /// The nearest corner or edge midpoint to <paramref name="point"/>, within
    /// <paramref name="radiusMm"/>, or the point itself when nothing is close enough.
    ///
    /// Corners win ties against midpoints at the same distance: a corner is the more definite
    /// feature and the one someone reaching for it usually means.
    /// </summary>
    public static Vector3 Nearest(Mesh mesh, Vector3 point, float radiusMm)
    {
        if (mesh.VertexCount == 0 || radiusMm <= 0) return point;

        float best = radiusMm * radiusMm;
        var found = point;
        bool onCorner = false;

        foreach (var vertex in mesh.Positions)
        {
            float distance = Vector3.DistanceSquared(vertex, point);
            if (distance >= best) continue;

            best = distance;
            found = vertex;
            onCorner = true;
        }

        for (int t = 0; t + 2 < mesh.Indices.Count; t += 3)
        {
            for (int k = 0; k < 3; k++)
            {
                var middle = (mesh.Positions[mesh.Indices[t + k]]
                            + mesh.Positions[mesh.Indices[t + (k + 1) % 3]]) * 0.5f;

                float distance = Vector3.DistanceSquared(middle, point);
                if (distance >= best || (onCorner && distance >= best)) continue;

                best = distance;
                found = middle;
                onCorner = false;
            }
        }

        return found;
    }
}
