using System.Numerics;

namespace FastCraft3D.Geometry;

/// <param name="RiserMm">Height of one step, in real millimetres at the model's scale.</param>
/// <param name="GoingMm">Depth of one tread, likewise.</param>
public readonly record struct StairCheck(float RiserMm, float GoingMm, string Advice)
{
    /// <summary>
    /// Whether a person could climb it. The ranges are the ones building regulations settle on
    /// nearly everywhere: a riser between about 150 and 190 mm, a going of at least 240.
    /// </summary>
    public bool IsClimbable => RiserMm is >= 150f and <= 190f && GoingMm >= 240f;
}

/// <summary>
/// A straight flight of steps.
///
/// It is here because getting a stair right is arithmetic rather than modelling - total rise,
/// how many risers that divides into, the going that follows, and whether the result is a stair
/// or a ladder. Building one by hand meant a dozen boxes typed in one at a time and the numbers
/// worked out on paper, and the first house model had two of them wrong.
///
/// The flight is built as one solid rather than a stack of boxes. Stacked boxes meet on part of
/// a face, which is exactly the contact the boolean is worst at; a single closed profile
/// extruded sideways has no internal seams at all.
/// </summary>
public static class StairBuilder
{
    /// <summary>Beyond this it is a staircase in a cathedral, and probably a typo.</summary>
    public const int MaximumSteps = 200;

    /// <summary>
    /// A flight rising <paramref name="rise"/> over <paramref name="run"/> in
    /// <paramref name="steps"/> risers, <paramref name="width"/> across.
    ///
    /// The top tread lands at the full rise, so the flight arrives level with the floor above
    /// rather than a step below it. The flight is centred on the origin like every other
    /// primitive.
    /// </summary>
    public static Mesh Build(float rise, float run, float width, int steps)
    {
        steps = Math.Clamp(steps, 1, MaximumSteps);
        if (rise <= 0 || run <= 0 || width <= 0) return new Mesh();

        float riser = rise / steps;
        float going = run / steps;

        // The profile, walked once: up the nosing and along the tread, step by step, then back
        // along the underside. Closed, so extruding it gives a solid with no seams inside.
        var profile = new List<Vector2> { new(0, 0) };

        for (int n = 1; n <= steps; n++)
        {
            profile.Add(new Vector2((n - 1) * going, n * riser));
            profile.Add(new Vector2(n * going, n * riser));
        }

        profile.Add(new Vector2(run, 0));

        var centred = profile
            .Select(p => new Vector2(p.X - run / 2f, p.Y - rise / 2f))
            .ToList();

        return Extrude(centred, width);
    }

    /// <summary>What the flight would be to climb, at the scale the model is drawn to.</summary>
    public static StairCheck Measure(float rise, float run, int steps, float scale)
    {
        steps = Math.Clamp(steps, 1, MaximumSteps);

        float riser = rise / steps * scale;
        float going = run / steps * scale;

        string advice =
            riser > 190f ? "The risers are too tall to climb comfortably - add steps or lower the rise."
            : riser < 150f ? "The risers are shallow. Fewer steps would suit the rise better."
            : going < 240f ? "The treads are too narrow to stand on - a longer run, or fewer steps."
            : "";

        return new StairCheck(riser, going, advice);
    }

    /// <summary>
    /// Sweeps a closed profile in the XZ plane along Y. The profile runs anticlockwise, so the
    /// walls come out facing away from the solid and the caps close it at both ends.
    /// </summary>
    private static Mesh Extrude(List<Vector2> profile, float width)
    {
        var mesh = new Mesh();
        if (profile.Count < 3) return mesh;

        if (Polygon2.SignedArea(profile) < 0) profile.Reverse();

        float half = width / 2f;
        var (points, triangles) = Polygon2.Triangulate(profile, []);

        // The two ends, wound opposite ways so both face outward.
        for (int i = 0; i + 2 < triangles.Count; i += 3)
        {
            Vector2 a = points[triangles[i]], b = points[triangles[i + 1]], c = points[triangles[i + 2]];

            mesh.AddTriangle(At(a, -half), At(c, -half), At(b, -half));
            mesh.AddTriangle(At(a, half), At(b, half), At(c, half));
        }

        // And the wall round the outside.
        for (int i = 0; i < profile.Count; i++)
        {
            var p = profile[i];
            var q = profile[(i + 1) % profile.Count];

            mesh.AddTriangle(At(p, -half), At(q, -half), At(q, half));
            mesh.AddTriangle(At(p, -half), At(q, half), At(p, half));
        }

        var built = mesh.Welded();

        // Which way a swept profile comes out depends on a handedness argument that is easy to
        // get backwards and cheap to check: the signed volume says outright whether the normals
        // ended up facing in, and turning them is one pass.
        if (built.ComputeSignedVolume() < 0) built.FlipWinding();

        return built;

        static Vector3 At(Vector2 p, float y) => new(p.X, y, p.Y);
    }
}
