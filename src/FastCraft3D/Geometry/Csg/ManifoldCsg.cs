using System.Numerics;
using ManifoldRust;

namespace FastCraft3D.Geometry.Csg;

/// <summary>
/// Booleans through Manifold, a robust mesh boolean library, by way of the ManifoldRust package.
///
/// The BSP engine splits a solid along the infinite plane of every triangle of the other, not only
/// where the two actually meet. A heart wrapped on the default cylinder came back as twenty-two
/// thousand triangles, many of them hairs a thousandth of a millimetre wide, and rounding tore
/// their edges: 0 clean out of 32 placements, cut or raised, and no nudge, weld or local cut got
/// it through more than once in sixteen. The same cutters through Manifold came back watertight
/// 32 times out of 32, with the volume removed matching the heart's area times its depth, in a few
/// milliseconds rather than half a second, at about a thousand triangles.
///
/// Every call answers null rather than throwing when it cannot help - the native library did not
/// load, an input is not a closed solid, the result has an error - so the caller falls back to the
/// BSP engine and nothing that works today stops working on a PC this cannot run on.
/// </summary>
public static class ManifoldCsg
{
    /// <summary>
    /// Set once the native library has failed to load, so a PC without it pays for the attempt
    /// once and not on every cut.
    /// </summary>
    private static volatile bool unavailable;

    /// <summary>Whether the native library has been found unusable on this PC.</summary>
    public static bool Unavailable => unavailable;

    public static Mesh? Subtract(Mesh solid, Mesh tool, CancellationToken token = default) =>
        Run(solid, tool, ManifoldOpType.Subtract, token);

    public static Mesh? Union(Mesh solid, Mesh tool, CancellationToken token = default) =>
        Run(solid, tool, ManifoldOpType.Add, token);

    public static Mesh? Intersect(Mesh solid, Mesh tool, CancellationToken token = default) =>
        Run(solid, tool, ManifoldOpType.Intersect, token);

    /// <summary>
    /// The first solid with all the others taken away in one go. Many small cutters that overlap
    /// each other - a wall swept round an outline in short pieces - go in as they are, since
    /// Manifold unions them as part of the same operation.
    /// </summary>
    public static Mesh? SubtractAll(Mesh solid, IReadOnlyList<Mesh> tools, CancellationToken token = default)
    {
        if (unavailable) return null;
        if (tools.Count == 0) return solid;

        var operands = new List<Manifold>(tools.Count + 1);
        try
        {
            foreach (var mesh in tools.Prepend(solid))
            {
                token.ThrowIfCancellationRequested();

                var imported = Import(mesh.Welded());
                operands.Add(imported);
                if (imported.Status != ManifoldStatus.NoError) return null;
            }

            using var result = Manifold.BatchBoolean(operands, ManifoldOpType.Subtract, token);
            return result.Status == ManifoldStatus.NoError ? Export(result.GetMeshGL()) : null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException
                                       or BadImageFormatException or TypeInitializationException)
        {
            unavailable = true;
            return null;
        }
        catch (Exception)
        {
            return null;
        }
        finally
        {
            foreach (var operand in operands) operand.Dispose();
        }
    }

    public static Mesh? Apply(Mesh solid, Mesh tool, BooleanOp op, CancellationToken token = default) => op switch
    {
        BooleanOp.Subtract => Subtract(solid, tool, token),
        BooleanOp.Intersect => Intersect(solid, tool, token),
        _ => Union(solid, tool, token)
    };

    private static Mesh? Run(Mesh first, Mesh second, ManifoldOpType op, CancellationToken token)
    {
        if (unavailable) return null;

        try
        {
            // Welded first: Manifold reads a solid from shared corners, and a mesh with a corner
            // per triangle is to it a heap of loose triangles rather than a closed shape.
            using var a = Import(first.Welded());
            using var b = Import(second.Welded());
            if (a.Status != ManifoldStatus.NoError || b.Status != ManifoldStatus.NoError) return null;

            using var result = Manifold.BatchBoolean(new[] { a, b }, op, token);
            if (result.Status != ManifoldStatus.NoError) return null;

            return Export(result.GetMeshGL());
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException
                                       or BadImageFormatException or TypeInitializationException)
        {
            // Most likely the Visual C++ runtime the library needs is not installed.
            unavailable = true;
            return null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static Manifold Import(Mesh mesh)
    {
        var positions = new float[mesh.Positions.Count * 3];
        for (int i = 0; i < mesh.Positions.Count; i++)
        {
            positions[3 * i] = mesh.Positions[i].X;
            positions[3 * i + 1] = mesh.Positions[i].Y;
            positions[3 * i + 2] = mesh.Positions[i].Z;
        }

        var triangles = new uint[mesh.Indices.Count];
        for (int i = 0; i < triangles.Length; i++) triangles[i] = (uint)mesh.Indices[i];

        // AsOriginal hands back a new manifold, so the import itself is disposed here.
        using var imported = Manifold.FromMesh(positions, triangles);
        return imported.AsOriginal();
    }

    private static Mesh Export(MeshGL mesh)
    {
        int stride = (int)mesh.NumProp;
        var positions = new List<Vector3>(mesh.VertProperties.Length / stride);
        for (int i = 0; i + 2 < mesh.VertProperties.Length; i += stride)
            positions.Add(new Vector3(mesh.VertProperties[i], mesh.VertProperties[i + 1], mesh.VertProperties[i + 2]));

        var indices = new List<int>(mesh.TriVerts.Length);
        foreach (uint corner in mesh.TriVerts) indices.Add((int)corner);

        return new Mesh(positions, indices);
    }
}
