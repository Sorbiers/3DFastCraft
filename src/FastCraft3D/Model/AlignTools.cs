using System.Numerics;
using FastCraft3D.Geometry;

namespace FastCraft3D.Model;

/// <summary>Where along an axis the selected objects should line up.</summary>
public enum AlignMode
{
    /// <summary>Flush with the lowest edge of the selection: left, front or bottom.</summary>
    Minimum,

    /// <summary>Centres level with each other.</summary>
    Centre,

    /// <summary>Flush with the highest edge: right, back or top.</summary>
    Maximum,

    /// <summary>Even gaps between the objects, keeping the two outermost where they are.</summary>
    Distribute
}

/// <summary>
/// Lines objects up along an axis.
///
/// Everything works on world bounding boxes rather than object positions, because that is what
/// "flush left" actually means to someone looking at the plate: a large and a small part are
/// aligned when their edges agree, not when their centres do.
/// </summary>
public static class AlignTools
{
    /// <summary>
    /// The offsets that would align the given objects. Returned rather than applied so the
    /// caller can wrap the whole move in a single undo step.
    /// </summary>
    public static IReadOnlyList<Vector3> Offsets(IReadOnlyList<SceneObject> objects, Axis axis, AlignMode mode)
    {
        var offsets = new Vector3[objects.Count];
        if (objects.Count < 2) return offsets; // nothing to line up against

        var direction = AxisVector(axis);
        var boxes = objects.Select(o => o.WorldBounds).ToList();

        if (mode == AlignMode.Distribute)
        {
            Distribute(boxes, axis, direction, offsets);
            return offsets;
        }

        var span = Bounds.Empty;
        foreach (var box in boxes) span = span.Union(box);

        float target = mode switch
        {
            AlignMode.Minimum => Component(span.Min, axis),
            AlignMode.Maximum => Component(span.Max, axis),
            _ => Component(span.Center, axis)
        };

        for (int i = 0; i < boxes.Count; i++)
        {
            float current = mode switch
            {
                AlignMode.Minimum => Component(boxes[i].Min, axis),
                AlignMode.Maximum => Component(boxes[i].Max, axis),
                _ => Component(boxes[i].Center, axis)
            };
            offsets[i] = direction * (target - current);
        }

        return offsets;
    }

    /// <summary>
    /// Spreads the objects so the gaps between them are equal.
    ///
    /// Gaps are equalised rather than centres, so a run of mixed sizes ends up looking evenly
    /// spaced instead of merely evenly indexed. The two outermost objects stay put, since they
    /// define the space being shared out.
    /// </summary>
    private static void Distribute(List<Bounds> boxes, Axis axis, Vector3 direction, Vector3[] offsets)
    {
        if (boxes.Count < 3) return; // two objects have nothing between them to even out

        // Work in the order they currently appear along the axis, not the order they were picked.
        var order = Enumerable.Range(0, boxes.Count)
            .OrderBy(i => Component(boxes[i].Min, axis))
            .ToList();

        float first = Component(boxes[order[0]].Min, axis);
        float last = Component(boxes[order[^1]].Max, axis);

        float occupied = order.Sum(i => Extent(boxes[i], axis));
        float gap = (last - first - occupied) / (order.Count - 1);

        float cursor = first;
        foreach (int i in order)
        {
            offsets[i] = direction * (cursor - Component(boxes[i].Min, axis));
            cursor += Extent(boxes[i], axis) + gap;
        }
    }

    private static Vector3 AxisVector(Axis axis) => axis switch
    {
        Axis.X => Vector3.UnitX,
        Axis.Y => Vector3.UnitY,
        _ => Vector3.UnitZ
    };

    private static float Component(Vector3 v, Axis axis) => axis switch
    {
        Axis.X => v.X,
        Axis.Y => v.Y,
        _ => v.Z
    };

    private static float Extent(Bounds bounds, Axis axis) => Component(bounds.Size, axis);
}
