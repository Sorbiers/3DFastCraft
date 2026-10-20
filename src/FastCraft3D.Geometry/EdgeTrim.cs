using System.Numerics;
using Clipper2Lib;
using FastCraft3D.Geometry.Csg;

namespace FastCraft3D.Geometry;

/// <param name="Mesh">The trimmed part, or null when it could not be.</param>
/// <param name="Refusal">Why not, when it could not.</param>
public sealed record TrimResult(Mesh? Mesh, string? Refusal);

/// <summary>
/// Cuts a part back round its outer edge by a distance, along the axes chosen, leaving everything
/// inside that edge as it was: an insert made to fit where nothing is there to fit it against.
///
/// Its outer edge is taken from above - the shadow it casts on the plate, holes filled, since a
/// courtyard is not an outside - drawn in by the distance, and the part keeps what stands inside
/// it. Along X and Y together the outline is drawn in as an offset, corners kept square; along
/// one of them alone it is cut from both ends of that axis only. Along Z the top and the bottom
/// are cut off by the distance.
/// </summary>
public static class EdgeTrim
{
    private const int Decimals = 4;

    public static TrimResult Trim(Mesh part, float by, bool alongX, bool alongY, bool alongZ, CancellationToken token = default)
    {
        if (part.TriangleCount == 0) return new(null, "there is nothing to trim");
        if (!(by > 0)) return new(null, "the distance has to be above zero");
        if (!alongX && !alongY && !alongZ) return new(null, "no axis chosen");

        var box = part.ComputeBounds();
        var result = part;

        if (alongX || alongY)
        {
            var outline = Shadow(part);
            if (outline.Count == 0) return new(null, "it casts no outline on the plate");

            var drawn = alongX && alongY
                ? Clipper.InflatePaths(outline, -by, JoinType.Miter, EndType.Polygon, 4.0, Decimals)
                : Eroded(outline, alongX ? new Vector2(by, 0) : new Vector2(0, by));
            if (drawn.Count == 0 || drawn.Sum(Clipper.Area) <= 1e-6) return new(null, $"it is nowhere wider than twice {by:0.##} mm");

            token.ThrowIfCancellationRequested();

            if (Prism(drawn, box.Min.Z - 1f, box.Max.Z + 1f) is not { } prism) return new(null, "the outline inside could not be filled");
            result = LocalCsg.Apply(result, prism, BooleanOp.Intersect, token);
        }

        if (alongZ)
        {
            if (box.Size.Z <= 2 * by) return new(null, $"it is not taller than twice {by:0.##} mm");
            result = PlaneClip.Keep(result, Matrix4x4.Identity, Vector3.UnitZ, box.Min.Z + by);
            result = PlaneClip.Keep(result, Matrix4x4.Identity, -Vector3.UnitZ, -(box.Max.Z - by));
        }

        result = MeshHealer.Heal(result, token: token).Mesh;
        if (result.TriangleCount == 0) return new(null, "nothing of it is left");
        if (!result.CheckHealth().IsWatertight) return new(null, "the trimmed part would not be watertight");

        return new(result, null);
    }

    /// <summary>The outer outlines of the part seen from above, holes filled.</summary>
    private static PathsD Shadow(Mesh mesh)
    {
        var triangles = new PathsD();
        for (int t = 0; t + 2 < mesh.Indices.Count; t += 3)
        {
            var a = mesh.Positions[mesh.Indices[t]];
            var b = mesh.Positions[mesh.Indices[t + 1]];
            var c = mesh.Positions[mesh.Indices[t + 2]];

            double area = ((double)b.X - a.X) * ((double)c.Y - a.Y) - ((double)b.Y - a.Y) * ((double)c.X - a.X);
            if (Math.Abs(area) < 1e-9) continue; // edge-on from above: a wall, which casts only a line

            triangles.Add(area > 0
                ? new PathD { new(a.X, a.Y), new(b.X, b.Y), new(c.X, c.Y) }
                : new PathD { new(a.X, a.Y), new(c.X, c.Y), new(b.X, b.Y) });
        }

        var merging = new ClipperD(Decimals);
        merging.AddSubject(triangles);
        var union = new PathsD();
        merging.Execute(ClipType.Union, FillRule.NonZero, union);

        var outer = new PathsD();
        foreach (var path in union)
            if (Clipper.Area(path) > 0) outer.Add(path);
        return outer;
    }

    /// <summary>
    /// The outline cut back along one direction only: what stays is every point that is still
    /// inside with the whole step either side of it - sampled along the step, so a gap narrower
    /// than it is not jumped.
    /// </summary>
    private static PathsD Eroded(PathsD outline, Vector2 step)
    {
        const int Samples = 8;
        var kept = outline;
        for (int i = -Samples; i <= Samples; i++)
        {
            if (i == 0) continue;
            var moved = Clipper.TranslatePaths(outline, step.X * i / Samples, step.Y * i / Samples);
            kept = Clipper.Intersect(kept, moved, FillRule.NonZero, Decimals);
            if (kept.Count == 0) break;
        }

        return kept;
    }

    private static Mesh? Prism(PathsD paths, float bottom, float top)
    {
        var loops = paths.Select(p => p.Select(q => new Vector2((float)q.x, (float)q.y)).ToList()).Where(l => l.Count >= 3).ToList();
        var points = new List<Vector3>();
        var indices = new List<int>();
        var below = new Dictionary<Vector2, int>();
        var above = new Dictionary<Vector2, int>();

        int At(Dictionary<Vector2, int> level, Vector2 p, float z)
        {
            if (level.TryGetValue(p, out int i)) return i;
            level[p] = i = points.Count;
            points.Add(new Vector3(p.X, p.Y, z));
            return i;
        }

        foreach (var (outline, holes) in Polygon2.Nest(loops))
        {
            var (laid, triangles) = Polygon2.Triangulate(outline, holes.Cast<IReadOnlyList<Vector2>>().ToList());
            if (triangles.Count < 3) return null;

            for (int i = 0; i + 2 < triangles.Count; i += 3)
            {
                Vector2 a = laid[triangles[i]], b = laid[triangles[i + 1]], c = laid[triangles[i + 2]];
                indices.AddRange([At(above, a, top), At(above, b, top), At(above, c, top)]);
                indices.AddRange([At(below, a, bottom), At(below, c, bottom), At(below, b, bottom)]);
            }

            // Walls: the outline anticlockwise and the holes clockwise, so each faces out.
            void Wall(List<Vector2> loop, bool anticlockwise)
            {
                if (Polygon2.SignedArea(loop) > 0 != anticlockwise) loop = Enumerable.Reverse(loop).ToList();
                for (int i = 0; i < loop.Count; i++)
                {
                    var a = loop[i];
                    var b = loop[(i + 1) % loop.Count];
                    indices.AddRange([At(below, a, bottom), At(below, b, bottom), At(above, b, top)]);
                    indices.AddRange([At(below, a, bottom), At(above, b, top), At(above, a, top)]);
                }
            }

            Wall(outline, true);
            foreach (var hole in holes) Wall(hole, false);
        }

        var prism = new Mesh(points, indices);
        return prism.CheckHealth().IsWatertight ? prism : null;
    }
}
