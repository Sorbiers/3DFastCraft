using System.Numerics;
using FastCraft3D.Geometry;

namespace FastCraft3D.Generators.Buildings;

/// <summary>
/// A straight flight of steps, arriving level with the floor above.
///
/// The numbers are the whole of the job: a riser somewhere between 150 and 190 mm on a going of
/// 240 or more is a stair, and anything else is a ladder or a ramp. At the model's scale that is
/// arithmetic nobody should be doing on paper, so the panel says in real millimetres what the
/// flight would be to climb. The solid itself is <see cref="StairBuilder"/>'s, one closed profile
/// with no seams inside.
/// </summary>
public sealed class Stair : Generator<Stair.Settings>
{
    public override string Id => "building.stair";
    public override int Version => 1;
    public override string Category => "Buildings";
    public override string Title => "Stair";
    public override string Summary => "A straight flight, arriving level with the floor above.";
    public override bool IsBeta => false;

    public sealed record Settings(
        [Length("Rise", 0.5, 1000, Hint = "Floor to floor")] float Rise = 26.5f,
        [Length("Run", 0.5, 1000, Hint = "How far it travels along the floor")] float Run = 37.7f,
        [Length("Width", 0.5, 1000, Hint = "Across the flight")] float Width = 14f,
        [Count("Steps", 1, StairBuilder.MaximumSteps, UnitText = "risers", Hint = "Risers, the last of them onto the floor above")] int Steps = 13);

    protected override Generated Build(Settings s, Printer printer, CancellationToken token)
    {
        // Built centred on the origin like a primitive; stood on the plate here, so its origin is
        // the middle of its footprint and a taller flight grows up rather than into the floor.
        var mesh = MeshTransform.Transformed(
            StairBuilder.Build(s.Rise, s.Run, s.Width, s.Steps),
            Matrix4x4.CreateTranslation(0, 0, s.Rise / 2f));

        return new Generated([new GeneratedPart("Stair", mesh, Role: "stair")], []);
    }

    protected override IEnumerable<string> Describe(Settings s, float modelScale)
    {
        float scale = modelScale <= 0 ? 1f : modelScale;
        var check = StairBuilder.Measure(s.Rise, s.Run, s.Steps, scale);

        string real = scale > 1.5f
            ? $"At 1:{scale:0}, that is {check.RiserMm:0} mm risers on {check.GoingMm:0} mm treads."
            : $"Risers of {check.RiserMm:0.#} mm on treads of {check.GoingMm:0.#} mm.";

        yield return $"{s.Steps} risers of {s.Rise / s.Steps:0.##} mm. {real}";

        if (check.Advice.Length > 0) yield return check.Advice;
        else if (check.IsClimbable) yield return "A comfortable flight.";
    }
}
