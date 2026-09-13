using System.Numerics;

namespace FastCraft3D.Geometry;

/// <summary>
/// Everything below a height replaced by straight walls down to a flat base on the plate.
///
/// For a model with the right top and the wrong bottom: a scanned bust that ends in a ragged neck,
/// a relief that is only a skin, a figure tilted so one toe touches the plate. The model is cut at
/// the height, the cut's outline is carried straight down, and the base is filled - holes and all,
/// so a hollow neck stays hollow.
///
/// Cut first rather than extending whatever the bottom happens to be. That is what 3D Builder did,
/// and it carries a ragged edge all the way to the plate; cutting above the ragged part and
/// building down from a clean section is the difference between a plinth and a skirt.
/// </summary>
public static class ExtrudeDown
{
    /// <summary>The least height worth building: below this the walls are not walls.</summary>
    public const float MinimumHeight = 0.05f;

    /// <summary>
    /// The model in plate coordinates, cut at <paramref name="height"/> and built down to Z = 0;
    /// or null when that cannot be done watertight.
    ///
    /// Refused rather than handed back open. A height that misses the model, or a section that
    /// does not close - a model with holes of its own where the cut passes - leaves nothing that
    /// would print, and a base on a torn model is a torn model with a base.
    /// </summary>
    public static Mesh? Apply(Mesh world, float height)
    {
        if (!float.IsFinite(height) || height < MinimumHeight) return null;

        var bounds = world.ComputeBounds();
        if (bounds.IsEmpty || height >= bounds.Max.Z || height <= bounds.Min.Z) return null;

        var (kept, rings) = PlaneClip.KeepOpen(world, Matrix4x4.Identity, Vector3.UnitZ, height);
        if (rings.Count == 0 || kept.TriangleCount == 0) return null;

        foreach (var ring in rings)
        {
            for (int i = 0; i < ring.Count; i++)
            {
                Vector3 a = ring[i];
                Vector3 b = ring[(i + 1) % ring.Count];
                var qa = new Vector3(a.X, a.Y, 0f);
                var qb = new Vector3(b.X, b.Y, 0f);

                // The ring runs the way the piece's own edges do, so the wall takes it the other
                // way round at the top - that is what joins them - and the base takes it the
                // other way again at the bottom.
                kept.AddTriangle(b, a, qa);
                kept.AddTriangle(b, qa, qb);
            }
        }

        // Laid out in (x, -y): a frame turned the same way as the one the cap is filled in, so
        // anticlockwise in it faces down, which is the way a base faces.
        var dropped = new Dictionary<Vector2, Vector3>();
        var loops = new List<List<Vector2>>(rings.Count);

        foreach (var ring in rings)
        {
            var loop = new List<Vector2>(ring.Count);
            foreach (var p in ring)
            {
                var flat = new Vector2(p.X, -p.Y);
                dropped[flat] = new Vector3(p.X, p.Y, 0f);
                loop.Add(flat);
            }

            loops.Add(loop);
        }

        foreach (var (outline, holes) in Polygon2.Nest(loops))
        {
            var (points, triangles) = Polygon2.Triangulate(outline, holes);

            for (int t = 0; t + 2 < triangles.Count; t += 3)
                kept.AddTriangle(Down(points[triangles[t]]), Down(points[triangles[t + 1]]), Down(points[triangles[t + 2]]));
        }

        var result = kept.Welded();
        return result.CheckHealth().IsWatertight ? result : null;

        // Exactly the corners the walls came down to, not worked out again from the flat point.
        Vector3 Down(Vector2 flat) =>
            dropped.TryGetValue(flat, out var p) ? p : new Vector3(flat.X, -flat.Y, 0f);
    }
}
