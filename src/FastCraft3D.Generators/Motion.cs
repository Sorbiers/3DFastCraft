using System.Numerics;
using FastCraft3D.Geometry;
using FastCraft3D.Geometry.Motion;

namespace FastCraft3D.Generators;

/// <summary>A part of a set that moves, and how, where the set is put together.</summary>
/// <param name="Part">Which of the generated parts.</param>
/// <param name="Joint">Turning or sliding.</param>
/// <param name="Centre">What it turns about, for a turning part.</param>
/// <param name="Direction">Which way it slides, for a sliding one.</param>
/// <param name="Reach">The most it moves in a step: radians turning, millimetres sliding. Nought for the run's own.</param>
/// <param name="Returns">Drawn back home when nothing holds it: a follower on its spring.</param>
public sealed record MovingPart(int Part, Joint Joint, Vector2 Centre, Vector2 Direction = default, double Reach = 0, bool Returns = false);

/// <summary>
/// How a generated set moves, for the motion check to turn it and the panel to show it turning:
/// the parts that move, the one that drives, and the heights to cut the set at, where the parts
/// that meet are. Everything else in the set stands still. In the coordinates the set goes
/// together in - each part through its Assembled - so it is the preview that turns.
/// </summary>
/// <param name="Driver">The index, among <paramref name="Moving"/>, of the part that is turned.</param>
/// <param name="Turns">How far the driver is turned, and which way: negative is clockwise.</param>
/// <param name="Step">Degrees of the driver per step.</param>
/// <param name="Reach">The most a pushed part moves in one step, where the part does not say.</param>
public sealed record Mechanism(IReadOnlyList<MovingPart> Moving, int Driver, IReadOnlyList<float> Layers,
    double Turns = 1, double Step = 1, double Reach = 0.3);

/// <summary>A mechanism turned through, pose by pose.</summary>
/// <param name="Frames">Where each part is at each step: an angle in radians or a slide in millimetres, nought for one that stands still.</param>
/// <param name="Jam">What jammed, in words; null if it turned clear.</param>
/// <param name="Jammed">The two parts that met at the jam.</param>
public sealed record MotionFilm(IReadOnlyList<double[]> Frames, Mechanism Mechanism, string? Jam, (int A, int B)? Jammed)
{
    public bool Clear => Jam is null;

    /// <summary>Where part <paramref name="part"/> ended up.</summary>
    public double Last(int part) => Frames.Count == 0 ? 0 : Frames[^1][part];
}

/// <summary>One pose of a set turning: where each part is, and, if it jammed there, which two met and why.</summary>
public sealed record FilmStep(double[] Pose, string? Jam, (int A, int B)? Jammed);

/// <summary>Turns a generated set's mechanism through, from the parts themselves cut across.</summary>
public static class Films
{
    public static MotionFilm Shoot(Generated made, CancellationToken token = default)
    {
        var frames = new List<double[]>();
        string? jam = null;
        (int, int)? jammed = null;

        foreach (var step in Roll(made, token))
        {
            frames.Add(step.Pose);
            jam = step.Jam;
            jammed = step.Jammed;
        }

        return new MotionFilm(frames, made.Motion!, jam, jammed);
    }

    /// <summary>The same run a step at a time, so it can be played while the rest is worked out.</summary>
    public static IEnumerable<FilmStep> Roll(Generated made, CancellationToken token = default)
    {
        var mechanism = made.Motion ?? throw new ArgumentException("Nothing in this set moves.", nameof(made));

        var bodies = new List<PlanarBody>();
        var partOf = new List<int>();
        for (int i = 0; i < made.Parts.Count; i++)
        {
            var part = made.Parts[i];
            var mesh = part.Assembled is { } together ? MeshTransform.Transformed(part.Mesh, together) : part.Mesh;
            var loops = mechanism.Layers
                .SelectMany((z, layer) => Sections.At(mesh, z).Select(l => new PlanarLoop(layer, Sections.Thinned(l, 0.02f))))
                .ToArray();
            if (loops.Length == 0) continue;

            string name = i.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var moving = mechanism.Moving.FirstOrDefault(m => m.Part == i);
            bodies.Add(moving switch
            {
                { Joint: Joint.Revolute } m => new PlanarBody(name, Joint.Revolute, m.Centre, Vector2.UnitX, loops, m.Reach, m.Returns),
                { Joint: Joint.Prismatic } m => new PlanarBody(name, Joint.Prismatic, Vector2.Zero, Vector2.Normalize(m.Direction), loops, m.Reach, m.Returns),
                _ => PlanarBody.Standing(name, loops)
            });
            partOf.Add(i);
        }

        int driver = partOf.IndexOf(mechanism.Moving[mechanism.Driver].Part);
        if (driver < 0) throw new ArgumentException("The driver is not cut by any of the layers.", nameof(made));

        int count = 0;
        foreach (var step in PlanarMotion.Steps(bodies, driver, mechanism.Turns, null, mechanism.Step, mechanism.Reach))
        {
            token.ThrowIfCancellationRequested();
            count++;

            var pose = new double[made.Parts.Count];
            for (int b = 0; b < bodies.Count; b++) pose[partOf[b]] = step.At[b];

            if (step.Jam is { } a && step.Against is { } b2)
            {
                int first = int.Parse(a, System.Globalization.CultureInfo.InvariantCulture);
                int second = int.Parse(b2, System.Globalization.CultureInfo.InvariantCulture);
                yield return new FilmStep(pose,
                    $"It jams {count * mechanism.Step:0} degrees into a turn: {made.Parts[first].Name} runs into {made.Parts[second].Name}.",
                    (first, second));
                yield break;
            }

            yield return new FilmStep(pose, null, null);
        }
    }
}
