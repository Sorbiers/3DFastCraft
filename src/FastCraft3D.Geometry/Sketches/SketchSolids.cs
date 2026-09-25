using System.Numerics;
using FastCraft3D.Geometry.Engraving;

namespace FastCraft3D.Geometry.Sketches;

/// <summary>Which line on the plate a profile is turned about.</summary>
public enum RevolveAxis
{
    /// <summary>The green Y axis: the solid stands up, the sketch's Y becoming its height.</summary>
    Y,

    /// <summary>The red X axis: the solid lies along X.</summary>
    X
}

/// <summary>
/// Solids made from a sketch: pushed straight up, or turned round an axis. Both are built as closed
/// surfaces directly - walls between rings, caps from the outline - so no boolean is involved and
/// nothing can come back torn.
/// </summary>
public static class SketchSolids
{
    /// <summary>The sketch's shapes stood up from the plate to a height, holes and all.</summary>
    public static Mesh Extrude(Sketch sketch, float height)
    {
        var shapes = sketch.Shapes().Select(s => new TextShape(s.Outline, s.Holes.Cast<IReadOnlyList<Vector2>>().ToList())).ToList();
        return shapes.Count == 0 || !(height > 0f) ? new Mesh() : TextSolid.Extrude(shapes, 0f, height);
    }

    /// <summary>
    /// The sketch turned about an axis through the plate's origin, all the way round or part of the
    /// way; or null, with the reason, when it cannot be. Every outline has to lie on one side of
    /// the axis: one reaching across it would turn through itself.
    ///
    /// Outlines touching the axis close up on it, which is how a vase gets a floor. Part of a turn
    /// is closed with the outline itself at each end.
    /// </summary>
    public static Mesh? Revolve(Sketch sketch, RevolveAxis axis, float degrees, int segments, out string? why)
    {
        why = null;
        if (sketch.Loops.Count == 0)
        {
            why = "Draw a closed outline first.";
            return null;
        }

        // In the plane of the profile: how far out from the axis, and how far along it.
        Vector2 Profile(Vector2 p) => axis == RevolveAxis.Y ? new(p.X, p.Y) : new(p.Y, p.X);

        if (sketch.Loops.SelectMany(l => l).Any(p => Profile(p).X < -1e-4f))
        {
            why = axis == RevolveAxis.Y
                ? "Every outline has to lie to the right of the Y axis - its left edge on the axis at the furthest."
                : "Every outline has to lie on the far side of the X axis - its near edge on the axis at the furthest.";
            return null;
        }

        degrees = Math.Clamp(float.IsFinite(degrees) ? degrees : 360f, 1f, 360f);
        bool whole = degrees >= 359.99f;
        float sweep = degrees * MathF.PI / 180f;
        int steps = Math.Max(whole ? 3 : 1, (int)MathF.Ceiling(Math.Clamp(segments, 3, 512) * degrees / 360f));

        Vector3 At(Vector2 profile, int step)
        {
            float r = MathF.Max(profile.X, 0f), h = profile.Y;
            if (r == 0f) return Place(0f, 0f, h);

            float angle = whole ? MathF.Tau * (step % steps) / steps : sweep * step / steps;
            return Place(r * MathF.Cos(angle), r * MathF.Sin(angle), h);
        }

        // About Y the profile's height goes up Z; about X, along X. The second is the first with
        // the axes turned round in order, which keeps the walls facing outward in both.
        Vector3 Place(float a, float b, float h) => axis == RevolveAxis.Y ? new(a, b, h) : new(h, a, b);

        var mesh = new Mesh();
        foreach (var (outline, holes) in sketch.Shapes())
        {
            var profileOutline = Wound(outline.Select(Profile).ToList(), anticlockwise: true);
            var profileHoles = holes.Select(h => Wound(h.Select(Profile).ToList(), anticlockwise: false)).ToList();

            foreach (var loop in profileHoles.Prepend(profileOutline))
                for (int i = 0; i < loop.Count; i++)
                {
                    var a = loop[i];
                    var b = loop[(i + 1) % loop.Count];
                    for (int j = 0; j < steps; j++)
                    {
                        Face(At(a, j), At(a, j + 1), At(b, j + 1));
                        Face(At(a, j), At(b, j + 1), At(b, j));
                    }
                }

            if (whole) continue;

            var (points, triangles) = Polygon2.Triangulate(profileOutline, profileHoles.Cast<IReadOnlyList<Vector2>>().ToList());
            for (int t = 0; t + 2 < triangles.Count; t += 3)
            {
                var p = points[triangles[t]];
                var q = points[triangles[t + 1]];
                var s = points[triangles[t + 2]];
                Face(At(p, 0), At(q, 0), At(s, 0));
                Face(At(p, steps), At(s, steps), At(q, steps));
            }
        }

        var solid = mesh.Welded();
        if (solid.ComputeSignedVolume() < 0) solid.FlipWinding();

        if (!solid.CheckHealth().IsWatertight)
        {
            why = "The turned outline would not close into a solid.";
            return null;
        }

        return solid;

        // A face with two corners in the same place - on the axis - is not a face at all.
        void Face(Vector3 a, Vector3 b, Vector3 c)
        {
            if (Vector3.DistanceSquared(a, b) < 1e-12f || Vector3.DistanceSquared(b, c) < 1e-12f || Vector3.DistanceSquared(a, c) < 1e-12f) return;
            mesh.AddTriangle(a, b, c);
        }
    }

    private static List<Vector2> Wound(List<Vector2> loop, bool anticlockwise)
    {
        if (Polygon2.SignedArea(loop) > 0 != anticlockwise) loop.Reverse();
        return loop;
    }
}
