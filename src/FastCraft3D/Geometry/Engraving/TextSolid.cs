using System.Numerics;

namespace FastCraft3D.Geometry.Engraving;

/// <param name="Outline">The outer loop, anticlockwise, in the face's own 2D frame.</param>
/// <param name="Holes">Loops enclosed by it.</param>
public readonly record struct TextShape(
    IReadOnlyList<Vector2> Outline, IReadOnlyList<IReadOnlyList<Vector2>> Holes);

/// <summary>
/// Builds the solid that lettering is cut from or raised with.
///
/// The shapes come in as outlines and are extruded along the face's normal: caps from
/// <see cref="Polygon2"/>, walls around every loop, and a floor. The same solid serves both
/// jobs - subtracted it engraves, unioned it embosses - because the difference is only which
/// side of the face it stands on and which boolean is asked for.
/// </summary>
public static class TextSolid
{
    /// <summary>
    /// One solid covering every shape.
    ///
    /// <paramref name="from"/> and <paramref name="to"/> are heights along the face normal, so
    /// engraving passes something like 0.02 down to -0.6, and raising passes -0.02 up to 1.5.
    /// Each starts a little past the surface, so the solid always crosses it rather than meeting
    /// it exactly - a coplanar face is the one thing the boolean handles badly.
    /// </summary>
    public static Mesh Build(IReadOnlyList<TextShape> shapes, FacePatch face, float from, float to)
    {
        var mesh = new Mesh();
        if (shapes.Count == 0 || from == to) return mesh;

        float low = Math.Min(from, to), high = Math.Max(from, to);

        foreach (var shape in shapes)
        {
            var (points, triangles) = Polygon2.Triangulate(shape.Outline, shape.Holes);
            if (triangles.Count == 0) continue;

            for (int i = 0; i + 2 < triangles.Count; i += 3)
            {
                var a = points[triangles[i]];
                var b = points[triangles[i + 1]];
                var c = points[triangles[i + 2]];

                mesh.AddTriangle(face.ToLocal(a, high), face.ToLocal(b, high), face.ToLocal(c, high));
                mesh.AddTriangle(face.ToLocal(c, low), face.ToLocal(b, low), face.ToLocal(a, low));
            }

            // Walls come from the loops themselves, never from the triangulated result: that has
            // a bridge cut into it for each hole, and a wall along a bridge would stand in the
            // middle of the letter.
            AddWalls(mesh, face, Wound(shape.Outline, anticlockwise: true), low, high);

            foreach (var hole in shape.Holes)
                AddWalls(mesh, face, Wound(hole, anticlockwise: false), low, high);
        }

        return mesh.Welded();
    }

    /// <summary>
    /// The loop in the direction the cap is wound, which is what makes the walls face outward:
    /// anticlockwise around the outside, clockwise around a hole.
    /// </summary>
    private static IReadOnlyList<Vector2> Wound(IReadOnlyList<Vector2> loop, bool anticlockwise)
    {
        bool already = Polygon2.SignedArea(loop) > 0;
        if (already == anticlockwise) return loop;

        var flipped = new List<Vector2>(loop);
        flipped.Reverse();
        return flipped;
    }

    private static void AddWalls(
        Mesh mesh, FacePatch face, IReadOnlyList<Vector2> loop, float low, float high)
    {
        for (int i = 0; i < loop.Count; i++)
        {
            var a = loop[i];
            var b = loop[(i + 1) % loop.Count];
            if ((b - a).LengthSquared() < 1e-12f) continue;

            Vector3 topA = face.ToLocal(a, high), topB = face.ToLocal(b, high);
            Vector3 lowA = face.ToLocal(a, low), lowB = face.ToLocal(b, low);

            mesh.AddTriangle(topA, lowA, lowB);
            mesh.AddTriangle(topA, lowB, topB);
        }
    }
}
