namespace FastCraft3D.Geometry.Csg;

/// <summary>
/// A BSP tree node. Faithful to the reference csg.js formulation, including traversal
/// order, so results stay reproducible and comparable against it when debugging.
/// </summary>
public sealed class CsgNode
{
    private CsgPlane? plane;
    private CsgNode? front, back;
    private readonly List<CsgPolygon> polygons = new();

    public void Build(List<CsgPolygon> polys, bool parallel)
    {
        if (polys.Count == 0) return;

        plane ??= polys[0].Plane;

        var f = new List<CsgPolygon>();
        var b = new List<CsgPolygon>();
        PolygonSplitter.SplitMany(plane.Value, polys, SplitBuckets.ForBuild(polygons, f, b), parallel);

        if (f.Count > 0)
        {
            front ??= new CsgNode();
            front.Build(f, parallel);
        }
        if (b.Count > 0)
        {
            back ??= new CsgNode();
            back.Build(b, parallel);
        }
    }

    /// <summary>Removes the parts of <paramref name="polys"/> that lie inside this solid.</summary>
    public List<CsgPolygon> ClipPolygons(List<CsgPolygon> polys, bool parallel)
    {
        if (plane is null) return new List<CsgPolygon>(polys);

        var f = new List<CsgPolygon>();
        var b = new List<CsgPolygon>();
        PolygonSplitter.SplitMany(plane.Value, polys, SplitBuckets.ForClip(f, b), parallel);

        var result = front is not null ? front.ClipPolygons(f, parallel) : f;

        // With no back child, everything behind the plane is interior and is discarded.
        if (back is not null)
            result.AddRange(back.ClipPolygons(b, parallel));

        return result;
    }

    public void ClipTo(CsgNode bsp, bool parallel)
    {
        var clipped = bsp.ClipPolygons(polygons, parallel);
        polygons.Clear();
        polygons.AddRange(clipped);
        front?.ClipTo(bsp, parallel);
        back?.ClipTo(bsp, parallel);
    }

    /// <summary>Turns the solid inside out - the basis of subtract and intersect.</summary>
    public void Invert()
    {
        for (int i = 0; i < polygons.Count; i++)
            polygons[i] = polygons[i].Flipped();

        if (plane is not null)
        {
            var p = plane.Value;
            p.Flip();
            plane = p;
        }

        front?.Invert();
        back?.Invert();
        (front, back) = (back, front);
    }

    /// <summary>Pre-order traversal (node, front subtree, back subtree), matching the recursive form.</summary>
    public List<CsgPolygon> AllPolygons()
    {
        var result = new List<CsgPolygon>();
        var stack = new Stack<CsgNode>();
        stack.Push(this);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            result.AddRange(node.polygons);
            if (node.back is not null) stack.Push(node.back);
            if (node.front is not null) stack.Push(node.front);
        }
        return result;
    }
}
