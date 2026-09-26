using System.Numerics;
using FastCraft3D.Geometry;

namespace FastCraft3D.Generators.Mechanisms;

/// <summary>
/// A plate for a mechanism to stand on and be turned by hand: a pin standing in each shaft's
/// bore, a clearance thinner than the bore, the plate a rounded bar joining them all.
/// </summary>
internal static class Base
{
    public const float Thickness = 3f;

    /// <summary>How far the set is lifted to stand on it: the plate, and a gap so nothing rubs on it.</summary>
    public const float Lift = Thickness + 0.4f;

    /// <param name="shafts">Each shaft's axis, its bore, and how high the pin reaches.</param>
    public static Mesh Plate(IReadOnlyList<(Vector2 At, float Bore, float Top)> shafts, Printer printer)
    {
        float reach = shafts.Max(p => p.Bore) / 2f + 4f;
        float x0 = shafts.Min(p => p.At.X), x1 = shafts.Max(p => p.At.X);
        var plate = Shapes.Prism(Shapes.RoundedRect(x1 - x0 + 2 * reach, 2 * reach, reach, new Vector2((x0 + x1) / 2f, 0)), 0, Thickness);

        // No pin where the bore is too small for one to stand in it with the printer's clearance.
        var pins = shafts.Where(p => p.Bore / 2f - printer.XyClearance >= 0.4f)
            .Select(p => Shapes.Cylinder(p.Bore / 2f - printer.XyClearance, Thickness - 0.01f, p.Top - 0.5f, p.At));
        return Shapes.Union([plate, .. pins]);
    }
}
