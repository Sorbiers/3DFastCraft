using System.Numerics;
using FastCraft3D.Geometry;

namespace FastCraft3D.View;

/// <summary>
/// A small picture of a mesh, drawn in software: for the few places a panel wants a view of
/// something that is not on the plate - a corner of log wall for the Cladding panel - and a render
/// through the viewport would be a window and a GPU for a thumbnail.
///
/// Perspective, a depth buffer, Gouraud shading with normals smoothed only across gentle creases
/// so a round log is round and a square corner is square, and supersampling for the edges.
/// </summary>
public static class MeshShot
{
    /// <summary>A mesh and the colour it is drawn in.</summary>
    public sealed record Piece(Mesh Mesh, Vector3 Colour);

    /// <summary>
    /// A square picture, <paramref name="size"/> pixels a side, as RGB bytes row by row from the
    /// top. Z is up. <paramref name="smoothDegrees"/> is the sharpest crease a normal is smoothed across.
    /// </summary>
    public static byte[] Render(
        IReadOnlyList<Piece> pieces, Vector3 eye, Vector3 target, float fovDegrees, int size, Vector3 background,
        int supersample = 3, float smoothDegrees = 40f)
    {
        int ss = Math.Clamp(supersample, 1, 4);
        int side = size * ss;

        var view = Matrix4x4.CreateLookAt(eye, target, Vector3.UnitZ);
        float near = Math.Max(Vector3.Distance(eye, target) * 0.05f, 0.01f);
        var clip = view * Matrix4x4.CreatePerspectiveFieldOfView(fovDegrees * MathF.PI / 180f, 1f, near, 1e6f);

        var colour = new float[side * side * 3];
        for (int i = 0; i < side * side; i++)
        {
            colour[i * 3] = background.X;
            colour[i * 3 + 1] = background.Y;
            colour[i * 3 + 2] = background.Z;
        }

        var depth = new float[side * side];
        Array.Fill(depth, float.MaxValue);

        var key = Vector3.Normalize(new Vector3(-0.55f, -0.65f, 0.95f));
        var fill = Vector3.Normalize(eye - target);
        float crease = MathF.Cos(smoothDegrees * MathF.PI / 180f);

        foreach (var piece in pieces)
        {
            var positions = piece.Mesh.Positions;
            var indices = piece.Mesh.Indices;
            int triangles = piece.Mesh.TriangleCount;
            if (triangles == 0) continue;

            // Area-weighted face normals, and the faces round every vertex, to smooth between.
            var faceNormal = new Vector3[triangles];
            for (int t = 0; t < triangles; t++)
            {
                var a = positions[indices[t * 3]];
                faceNormal[t] = Vector3.Cross(positions[indices[t * 3 + 1]] - a, positions[indices[t * 3 + 2]] - a);
            }

            var start = new int[positions.Count + 1];
            for (int i = 0; i < indices.Count; i++) start[indices[i] + 1]++;
            for (int v = 0; v < positions.Count; v++) start[v + 1] += start[v];
            var around = new int[indices.Count];
            var next = (int[])start.Clone();
            for (int t = 0; t < triangles; t++)
                for (int c = 0; c < 3; c++) around[next[indices[t * 3 + c]]++] = t;

            var screen = new Vector3[3];
            var shade = new float[3];

            for (int t = 0; t < triangles; t++)
            {
                float length = faceNormal[t].Length();
                if (length < 1e-9f) continue;
                var own = faceNormal[t] / length;

                bool behind = false;
                for (int c = 0; c < 3; c++)
                {
                    int v = indices[t * 3 + c];
                    var p = Vector4.Transform(new Vector4(positions[v], 1f), clip);
                    if (p.W < near) { behind = true; break; }

                    screen[c] = new Vector3((p.X / p.W * 0.5f + 0.5f) * side, (1f - (p.Y / p.W * 0.5f + 0.5f)) * side, p.Z / p.W);

                    var sum = Vector3.Zero;
                    for (int k = start[v]; k < start[v + 1]; k++)
                    {
                        var other = faceNormal[around[k]];
                        float len = other.Length();
                        if (len > 1e-9f && Vector3.Dot(other / len, own) > crease) sum += other;
                    }

                    var normal = sum.LengthSquared() > 1e-12f ? Vector3.Normalize(sum) : own;
                    if (Vector3.Dot(normal, Vector3.Normalize(eye - positions[v])) < 0f) normal = -normal;

                    shade[c] = 0.34f + 0.66f * MathF.Max(Vector3.Dot(normal, key), 0f) + 0.16f * MathF.Max(Vector3.Dot(normal, fill), 0f);
                }

                if (behind) continue;

                Vector3 a2 = screen[0], b2 = screen[1], c2 = screen[2];
                float area = (b2.X - a2.X) * (c2.Y - a2.Y) - (c2.X - a2.X) * (b2.Y - a2.Y);
                if (MathF.Abs(area) < 1e-9f) continue;

                int x0 = Math.Max((int)MathF.Floor(MathF.Min(a2.X, MathF.Min(b2.X, c2.X))), 0);
                int x1 = Math.Min((int)MathF.Ceiling(MathF.Max(a2.X, MathF.Max(b2.X, c2.X))), side - 1);
                int y0 = Math.Max((int)MathF.Floor(MathF.Min(a2.Y, MathF.Min(b2.Y, c2.Y))), 0);
                int y1 = Math.Min((int)MathF.Ceiling(MathF.Max(a2.Y, MathF.Max(b2.Y, c2.Y))), side - 1);

                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        float px = x + 0.5f, py = y + 0.5f;
                        float l0 = ((b2.X - px) * (c2.Y - py) - (c2.X - px) * (b2.Y - py)) / area;
                        float l1 = ((c2.X - px) * (a2.Y - py) - (a2.X - px) * (c2.Y - py)) / area;
                        float l2 = 1f - l0 - l1;
                        if (l0 < -1e-5f || l1 < -1e-5f || l2 < -1e-5f) continue;

                        float z = l0 * a2.Z + l1 * b2.Z + l2 * c2.Z;
                        int at = y * side + x;
                        if (z >= depth[at]) continue;

                        depth[at] = z;
                        float s = l0 * shade[0] + l1 * shade[1] + l2 * shade[2];
                        colour[at * 3] = piece.Colour.X * s;
                        colour[at * 3 + 1] = piece.Colour.Y * s;
                        colour[at * 3 + 2] = piece.Colour.Z * s;
                    }
            }
        }

        var pixels = new byte[size * size * 3];
        float each = 1f / (ss * ss);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float r = 0f, g = 0f, b = 0f;
                for (int dy = 0; dy < ss; dy++)
                    for (int dx = 0; dx < ss; dx++)
                    {
                        int at = ((y * ss + dy) * side + x * ss + dx) * 3;
                        r += colour[at]; g += colour[at + 1]; b += colour[at + 2];
                    }

                int k = (y * size + x) * 3;
                pixels[k] = (byte)(255 * Math.Clamp(r * each, 0f, 1f));
                pixels[k + 1] = (byte)(255 * Math.Clamp(g * each, 0f, 1f));
                pixels[k + 2] = (byte)(255 * Math.Clamp(b * each, 0f, 1f));
            }

        return pixels;
    }
}
