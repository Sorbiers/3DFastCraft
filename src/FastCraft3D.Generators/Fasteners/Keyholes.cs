using System.Numerics;
using FastCraft3D.Geometry;
using FastCraft3D.Geometry.Engraving;

namespace FastCraft3D.Generators.Fasteners;

/// <summary>What the Wall mount tool cuts, and what it adds beside the part.</summary>
/// <param name="Count">Keyholes side by side.</param>
/// <param name="Apart">Middle to middle.</param>
/// <param name="Head">Across the head of the screw it hangs on.</param>
/// <param name="Shank">Across the screw below its head.</param>
/// <param name="Slot">How far the slot runs up from the hole the head goes in by.</param>
/// <param name="Lip">How thick the part is over the head, holding it.</param>
/// <param name="HeadRoom">How deep the space behind the lip is, for the head.</param>
/// <param name="Studs">Printed studs to screw to the wall, for the keyholes to hang on.</param>
/// <param name="WallScrew">The screw through each stud into the wall.</param>
/// <param name="Template">A strip to hold on the wall and drill through.</param>
public sealed record KeyholeOptions(
    int Count = 2, float Apart = 40f, float Head = 8f, float Shank = 4f, float Slot = 10f, float Lip = 1.6f,
    float HeadRoom = 3f, bool Studs = false, float WallScrew = 3.5f, bool Template = false)
{
    /// <summary>A stud's neck: the wall screw through it with a wall round it a screw will not split.</summary>
    public float Neck => WallScrew + 2.4f;

    /// <summary>A stud's head: over the neck by enough each side to hold the part's lip.</summary>
    public float StudHead => Neck + 4f;

    /// <summary>What the keyholes are sized for: the studs, or the screw asked for.</summary>
    public float HeadSize => Studs ? StudHead : Head;

    public float ShankSize => Studs ? Neck : Shank;

    public float Depth => Lip + HeadRoom;
}

/// <summary>
/// Keyholes for hanging a part on a wall, drawn flat on the face they are cut into: a round hole
/// the head goes in by and a narrow slot running up from it, and behind the lip a wider slot the
/// head slides up in. Up the face is +Y; the holes sit on Y = 0, the row centred across.
/// </summary>
public static class Keyholes
{
    /// <summary>Why these cannot be cut; null when they can.</summary>
    public static string? Problem(KeyholeOptions o)
    {
        if (o.ShankSize >= o.HeadSize - 1) return "The shank is as wide as the head: the head would pull through the slot.";
        if (o.Count > 1 && o.Apart < o.HeadSize + 4) return "The keyholes run into each other.";
        return null;
    }

    private static float Across(KeyholeOptions o, int i) => (i - (o.Count - 1) / 2f) * o.Apart;

    /// <summary>What shows on the face: each round hole and the narrow slot up from it, as one outline.</summary>
    public static List<TextShape> Mouth(KeyholeOptions o, float clearance)
    {
        float r = o.HeadSize / 2f + clearance, w = o.ShankSize / 2f + clearance;
        float rise = MathF.Sqrt(r * r - w * w), start = MathF.Atan2(rise, w);

        var shapes = new List<TextShape>();
        for (int i = 0; i < o.Count; i++)
        {
            var at = new Vector2(Across(o, i), 0);
            var loop = new List<Vector2>();

            // Round the bottom of the hole from where the slot's right side meets it to where
            // its left side does, up the slot's left side, over its rounded top, and down.
            int round = Math.Max(12, Shapes.Sides(r));
            for (int k = 0; k <= round; k++)
            {
                float a = start - (MathF.PI + 2 * start) * k / round;
                loop.Add(at + r * new Vector2(MathF.Cos(a), MathF.Sin(a)));
            }

            int cap = Math.Max(6, Shapes.Sides(w) / 2);
            for (int k = 0; k <= cap; k++)
            {
                float a = MathF.PI - MathF.PI * k / cap;
                loop.Add(at + new Vector2(0, o.Slot) + w * new Vector2(MathF.Cos(a), MathF.Sin(a)));
            }

            if (Polygon2.SignedArea(loop) < 0) loop.Reverse();
            shapes.Add(new TextShape(loop, []));
        }

        return shapes;
    }

    /// <summary>Behind the lip: a slot as wide as the hole, the whole way up.</summary>
    public static List<TextShape> Undercut(KeyholeOptions o, float clearance)
    {
        float r = o.HeadSize / 2f + clearance;
        return Enumerable.Range(0, o.Count)
            .Select(i => new TextShape(Shapes.RoundedRect(2 * r, o.Slot + 2 * r, r, new Vector2(Across(o, i), o.Slot / 2f)), []))
            .ToList();
    }

    /// <summary>
    /// A stud to screw to the wall: its neck against the wall, the length of the part's lip and a
    /// little more, its head in the room behind the lip. Printed head down, countersunk for the screw.
    /// </summary>
    public static Mesh Stud(KeyholeOptions o)
    {
        float head = o.StudHead / 2f, neck = o.Neck / 2f, thick = o.HeadRoom - 0.6f, length = o.Lip + 0.3f;
        var body = Shapes.Union(Shapes.Cylinder(head, 0, thick), Shapes.Cylinder(neck, thick - 0.01f, thick + length));

        float screw = o.WallScrew / 2f + 0.2f, sink = MathF.Min(thick - 0.4f, o.WallScrew);
        int sides = Shapes.Sides(o.WallScrew);
        var countersink = Shapes.Loft([(-0.5f, Shapes.Circle(screw + sink + 0.5f, default, sides)), (sink, Shapes.Circle(screw, default, sides))]);
        return Shapes.Subtract(body, Shapes.Cylinder(screw, -1, thick + length + 1, default, sides), countersink);
    }

    /// <summary>
    /// A thin strip with a hole where each screw goes, the keyholes' spacing apart, and a notch in
    /// its top edge to say which way up. Held level on the wall, it marks or guides the drill.
    /// </summary>
    public static Mesh Template(KeyholeOptions o)
    {
        float wide = (o.Count - 1) * o.Apart + 20f, tall = 20f, hole = MathF.Max(1f, (o.Studs ? o.WallScrew : o.Shank) / 2f - 0.5f);
        var strip = Shapes.Prism(Shapes.RoundedRect(wide, tall, 2f), 0, 1.2f);
        var cuts = Enumerable.Range(0, o.Count)
            .Select(i => Shapes.Cylinder(hole, -1, 3, new Vector2(Across(o, i), 0)))
            .Append(Shapes.Prism([new(-2, tall / 2f + 1), new(2, tall / 2f + 1), new(0, tall / 2f - 3)], -1, 3))
            .ToList();
        return Shapes.Subtract(strip, cuts);
    }
}
