namespace FastCraft3D.Geometry.Csg;

/// <summary>
/// A convex polygon carrying its own plane. Only positions are tracked - shading normals
/// are regenerated from the final mesh by crease angle, so there is nothing else to
/// interpolate through a split.
/// </summary>
public sealed class CsgPolygon(Vec3d[] vertices, CsgPlane plane)
{
    public Vec3d[] Vertices = vertices;
    public CsgPlane Plane = plane;

    public CsgPolygon Flipped()
    {
        var reversed = new Vec3d[Vertices.Length];
        for (int i = 0; i < Vertices.Length; i++)
            reversed[i] = Vertices[Vertices.Length - 1 - i];
        var p = Plane;
        p.Flip();
        return new CsgPolygon(reversed, p);
    }
}

[Flags]
internal enum VertexClass
{
    Coplanar = 0,
    Front = 1,
    Back = 2,
    Spanning = 3
}

/// <summary>
/// The four output buckets of a split, with the aliasing the caller wants already applied.
///
/// The reference csg.js algorithm passes the same list twice (coplanar-front and
/// coplanar-back both land in the node's own polygon list; during clipping the coplanar
/// buckets alias the front/back lists). That aliasing determines the interleaved order of
/// the result, which in turn determines the shape of the BSP tree built from it. Modelling
/// it explicitly lets the parallel path build per-chunk buckets with identical aliasing and
/// concatenate them, reproducing the serial ordering exactly.
/// </summary>
internal readonly struct SplitBuckets
{
    private enum Mode { Build, Clip }

    public readonly List<CsgPolygon> CoplanarFront, CoplanarBack, Front, Back;
    private readonly Mode mode;

    private SplitBuckets(List<CsgPolygon> cf, List<CsgPolygon> cb, List<CsgPolygon> f, List<CsgPolygon> b, Mode m)
    {
        CoplanarFront = cf; CoplanarBack = cb; Front = f; Back = b; mode = m;
    }

    /// <summary>Node building: both coplanar sides collapse into the node's own list.</summary>
    public static SplitBuckets ForBuild(List<CsgPolygon> coplanar, List<CsgPolygon> front, List<CsgPolygon> back)
        => new(coplanar, coplanar, front, back, Mode.Build);

    /// <summary>Clipping: coplanar polygons follow the side their normal faces.</summary>
    public static SplitBuckets ForClip(List<CsgPolygon> front, List<CsgPolygon> back)
        => new(front, back, front, back, Mode.Clip);

    public SplitBuckets CreateLocal() => mode == Mode.Build
        ? ForBuild(new List<CsgPolygon>(), new List<CsgPolygon>(), new List<CsgPolygon>())
        : ForClip(new List<CsgPolygon>(), new List<CsgPolygon>());

    /// <summary>Appends a chunk's results, visiting each distinct underlying list exactly once.</summary>
    public void Append(in SplitBuckets local)
    {
        if (mode == Mode.Build) CoplanarFront.AddRange(local.CoplanarFront);
        Front.AddRange(local.Front);
        Back.AddRange(local.Back);
    }
}

internal static class PolygonSplitter
{
    /// <summary>Below this many polygons the parallel path costs more than it saves.</summary>
    public const int ParallelThreshold = 512;

    /// <summary>
    /// Splits <paramref name="polygon"/> against <paramref name="plane"/> into the buckets.
    /// This is the innermost operation of the CSG engine and the hottest path in the app.
    /// </summary>
    public static void Split(in CsgPlane plane, CsgPolygon polygon, in SplitBuckets buckets)
    {
        var verts = polygon.Vertices;
        int n = verts.Length;

        VertexClass polygonType = VertexClass.Coplanar;
        Span<VertexClass> types = n <= 32 ? stackalloc VertexClass[n] : new VertexClass[n];

        for (int i = 0; i < n; i++)
        {
            double d = plane.DistanceTo(verts[i]);
            var t = d < -CsgPlane.Epsilon ? VertexClass.Back
                  : d > CsgPlane.Epsilon ? VertexClass.Front
                  : VertexClass.Coplanar;
            types[i] = t;
            polygonType |= t;
        }

        switch (polygonType)
        {
            case VertexClass.Coplanar:
                // Facing the same way as the splitting plane decides which side it belongs to.
                (Vec3d.Dot(plane.Normal, polygon.Plane.Normal) > 0
                    ? buckets.CoplanarFront
                    : buckets.CoplanarBack).Add(polygon);
                break;

            case VertexClass.Front:
                buckets.Front.Add(polygon);
                break;

            case VertexClass.Back:
                buckets.Back.Add(polygon);
                break;

            default:
                var f = new List<Vec3d>(n + 1);
                var b = new List<Vec3d>(n + 1);

                for (int i = 0; i < n; i++)
                {
                    int j = (i + 1) % n;
                    VertexClass ti = types[i], tj = types[j];
                    Vec3d vi = verts[i], vj = verts[j];

                    if (ti != VertexClass.Back) f.Add(vi);
                    if (ti != VertexClass.Front) b.Add(vi);

                    if ((ti | tj) == VertexClass.Spanning)
                    {
                        double denom = Vec3d.Dot(plane.Normal, vj - vi);
                        if (Math.Abs(denom) > 1e-15)
                        {
                            double t = (plane.W - Vec3d.Dot(plane.Normal, vi)) / denom;
                            Vec3d v = Vec3d.Lerp(vi, vj, t);
                            f.Add(v);
                            b.Add(v);
                        }
                    }
                }

                // Reuse the parent plane rather than recomputing it from the new vertices:
                // sliver fragments can yield a garbage normal and leak the solid.
                if (f.Count >= 3) buckets.Front.Add(new CsgPolygon(f.ToArray(), polygon.Plane));
                if (b.Count >= 3) buckets.Back.Add(new CsgPolygon(b.ToArray(), polygon.Plane));
                break;
        }
    }

    /// <summary>
    /// Splits a whole batch against one plane. Above <see cref="ParallelThreshold"/> the work is
    /// chunked across cores; each chunk fills buckets with identical aliasing and they are
    /// concatenated in order, so the output matches the serial path exactly (unit-tested).
    /// </summary>
    public static void SplitMany(in CsgPlane plane, List<CsgPolygon> polygons, in SplitBuckets buckets,
                                bool parallel, CancellationToken token = default)
    {
        if (!parallel || polygons.Count < ParallelThreshold)
        {
            // Every so often rather than every polygon. The root node of a dense mesh is one
            // call holding hundreds of thousands of them, and without a look at the token in
            // here Abort could only be honoured once that one call had finished.
            for (int i = 0; i < polygons.Count; i++)
            {
                if ((i & 1023) == 0) token.ThrowIfCancellationRequested();
                Split(plane, polygons[i], buckets);
            }
            return;
        }

        int chunks = Math.Min(Environment.ProcessorCount, Math.Max(1, polygons.Count / 256));
        int chunkSize = (polygons.Count + chunks - 1) / chunks;
        var locals = new SplitBuckets[chunks];
        var planeCopy = plane;
        var template = buckets;

        // The token goes to Parallel.For as well as being read inside the chunk: given to the
        // options it surfaces as a plain OperationCanceledException rather than an
        // AggregateException wrapping one per worker.
        Parallel.For(0, chunks, new ParallelOptions { CancellationToken = token }, c =>
        {
            var local = template.CreateLocal();
            int start = c * chunkSize;
            int end = Math.Min(start + chunkSize, polygons.Count);
            for (int i = start; i < end; i++)
            {
                if ((i & 1023) == 0) token.ThrowIfCancellationRequested();
                Split(planeCopy, polygons[i], local);
            }
            locals[c] = local;
        });

        foreach (var local in locals)
            buckets.Append(local);
    }
}
