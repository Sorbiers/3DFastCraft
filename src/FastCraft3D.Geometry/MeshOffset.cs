using System.Numerics;

namespace FastCraft3D.Geometry;

/// <param name="Mesh">The grown or shrunk mesh, or the one given when it was refused.</param>
/// <param name="Refusal">Why it was left as it was, or null.</param>
public sealed record OffsetResult(Mesh Mesh, string? Refusal)
{
    public bool Changed => Refusal is null;
}

/// <summary>
/// Grows or shrinks a solid by moving every face along its normal by the same distance.
///
/// Not scaling. Scaling an H about its middle shrinks its notches with it and moves them, so the
/// smaller H no longer lines up with the larger one; here every wall of it moves in by the same
/// amount and the notches get wider by twice that. A point on one flat face moves straight along
/// its normal; one on an edge, along both faces' normals; one at a corner, to where the moved
/// faces meet - which is the exact offset of anything made of flat faces, corners kept sharp, and
/// a fair one of a faceted curve.
///
/// The axes say which directions the distance is taken along. Leaving Z out keeps the height:
/// top and bottom stay where they are, the walls move in, a sloped face moves sideways by the
/// distance as each horizontal slice of it would.
///
/// A distance bigger than a feature folds the solid through itself - a slot narrower than twice
/// a shrink closes past nothing. That turns triangles over, and a result with any turned over is
/// refused rather than handed on.
/// </summary>
public static class MeshOffset
{
    /// <summary>A point at a sharp corner moves further than the distance; four times is a cap.</summary>
    private const double MostStretch = 4.0;

    public static OffsetResult Offset(Mesh input, float distance, bool alongX = true, bool alongY = true, bool alongZ = true)
    {
        if (MathF.Abs(distance) < 1e-6f || input.TriangleCount == 0) return new(input, "nothing to do");
        if (!alongX && !alongY && !alongZ) return new(input, "no axis chosen");

        // Points in the middle of a flat face, and halfway along its straight edges, go first. One
        // whose only face is square to an axis left out has nothing telling it to move, while the
        // outline round it moves in, and the thin triangles between turned over: an H made by a
        // union shrank sideways was refused for folding when nothing about it was thin.
        var mesh = input.Welded();
        if (OutlineRebuild.Rebuild(mesh) is { Changed: true } tidy) mesh = tidy.Mesh;
        var points = mesh.Positions;
        var indices = mesh.Indices;
        var keep = new Vector3(alongX ? 1 : 0, alongY ? 1 : 0, alongZ ? 1 : 0);

        // Each point's faces, by their normals as seen along the chosen axes: the planes it has to
        // end up the distance off. A face square to an axis left out sets nothing.
        var planes = new List<Vector3>[points.Count];
        var normals = new Vector3[indices.Count / 3];
        for (int t = 0; t + 2 < indices.Count; t += 3)
        {
            var a = points[indices[t]];
            var n = Vector3.Cross(points[indices[t + 1]] - a, points[indices[t + 2]] - a);
            if (n.LengthSquared() < 1e-20f) continue;

            n = Vector3.Normalize(n);
            normals[t / 3] = n;

            var seen = n * keep;
            if (seen.LengthSquared() < 1e-8f) continue;
            seen = Vector3.Normalize(seen);

            for (int k = 0; k < 3; k++)
            {
                var list = planes[indices[t + k]] ??= new List<Vector3>(6);

                // Many triangles of one flat face are one plane, not many.
                bool known = false;
                foreach (var m in list)
                    if (Vector3.Dot(m, seen) > 0.9999f) { known = true; break; }
                if (!known) list.Add(seen);
            }
        }

        var moved = new List<Vector3>(points.Count);
        for (int v = 0; v < points.Count; v++)
            moved.Add(points[v] + (planes[v] is { Count: > 0 } list ? Step(list, distance, keep) : Vector3.Zero));

        var result = new Mesh(moved, [.. indices]);

        // Folded through itself: a triangle that now faces the other way.
        for (int t = 0; t + 2 < indices.Count; t += 3)
        {
            var n = normals[t / 3];
            if (n == Vector3.Zero) continue;

            var a = moved[indices[t]];
            var turned = Vector3.Cross(moved[indices[t + 1]] - a, moved[indices[t + 2]] - a);
            if (Vector3.Dot(turned, n) < 0)
                return new(input, $"{Math.Abs(distance):0.##} mm is more than some part of it is wide - it would fold through itself");
        }

        if (!result.CheckHealth().IsWatertight) return new(input, "the result would not be watertight");
        if (result.ComputeSignedVolume() <= 0) return new(input, "it would shrink to nothing");

        return new(result, null);
    }

    /// <summary>
    /// How far to move a point so it ends up <paramref name="distance"/> off every plane it is on,
    /// along the chosen axes only: the least-squares answer to n·d = distance for each normal n.
    /// </summary>
    private static Vector3 Step(List<Vector3> planes, float distance, Vector3 keep)
    {
        if (planes.Count == 1) return planes[0] * distance;

        if (planes.Count == 2)
        {
            // Along the sum of the two, as far as it takes to be the distance off each.
            double along = 1.0 + Vector3.Dot(planes[0], planes[1]);
            return along < 1e-6 ? Vector3.Zero : Capped((planes[0] + planes[1]) * (float)(distance / along), distance);
        }

        // Three or more: the normal equations, (sum of n nT) d = distance * (sum of n), solved in
        // the chosen axes alone - with one left out the matrix has nothing in that direction.
        var axes = new List<int>(3);
        if (keep.X > 0) axes.Add(0);
        if (keep.Y > 0) axes.Add(1);
        if (keep.Z > 0) axes.Add(2);

        int k = axes.Count;
        var a = new double[k, k + 1];
        foreach (var n in planes)
        {
            for (int i = 0; i < k; i++)
            {
                double ni = Component(n, axes[i]);
                for (int j = 0; j < k; j++) a[i, j] += ni * Component(n, axes[j]);
                a[i, k] += ni * distance;
            }
        }

        if (Solve(a, k) is { } x)
        {
            var step = Vector3.Zero;
            for (int i = 0; i < k; i++) step += Unit(axes[i]) * (float)x[i];
            return Capped(step, distance);
        }

        // Nearly all one way - a point on a gentle curve - has no corner to solve for: straight
        // out along the average, as far as makes the average distance right.
        var mean = Vector3.Zero;
        foreach (var n in planes) mean += n;
        if (mean.LengthSquared() < 1e-12f) return Vector3.Zero;
        mean = Vector3.Normalize(mean);

        double reach = 0;
        foreach (var n in planes) reach += Vector3.Dot(n, mean);
        reach /= planes.Count;

        return reach < 1e-6 ? mean * distance : Capped(mean * (float)(distance / reach), distance);
    }

    /// <summary>Gaussian elimination on an augmented k x (k+1) matrix; null when it is singular.</summary>
    private static double[]? Solve(double[,] a, int k)
    {
        for (int col = 0; col < k; col++)
        {
            int pivot = col;
            for (int row = col + 1; row < k; row++)
                if (Math.Abs(a[row, col]) > Math.Abs(a[pivot, col])) pivot = row;
            if (Math.Abs(a[pivot, col]) < 1e-6) return null;

            if (pivot != col)
                for (int j = 0; j <= k; j++) (a[col, j], a[pivot, j]) = (a[pivot, j], a[col, j]);

            for (int row = 0; row < k; row++)
            {
                if (row == col) continue;
                double f = a[row, col] / a[col, col];
                for (int j = col; j <= k; j++) a[row, j] -= f * a[col, j];
            }
        }

        var x = new double[k];
        for (int i = 0; i < k; i++) x[i] = a[i, k] / a[i, i];
        return x;
    }

    private static double Component(Vector3 v, int axis) => axis == 0 ? v.X : axis == 1 ? v.Y : v.Z;

    private static Vector3 Unit(int axis) => axis == 0 ? Vector3.UnitX : axis == 1 ? Vector3.UnitY : Vector3.UnitZ;

    private static Vector3 Capped(Vector3 step, float distance)
    {
        float most = (float)(MostStretch * Math.Abs(distance));
        float length = step.Length();
        return length > most ? step * (most / length) : step;
    }
}
