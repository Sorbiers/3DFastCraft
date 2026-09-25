using System.IO;
using System.Numerics;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation.Metadata;
using Windows.Graphics.Printing3D;

namespace FastCraft3D.Geometry;

/// <param name="Mesh">The mended mesh, or null when nothing usable came back.</param>
/// <param name="Problem">
/// Why nothing came back, in a sentence someone can act on, or null when something did. Worth
/// carrying rather than swallowing: this runs as a quiet second step, so a machine where it can
/// never work would otherwise look exactly like a model that is genuinely beyond mending.
/// </param>
public readonly record struct OsRepairResult(Mesh? Mesh, string? Problem);

/// <summary>
/// The repair engine that ships inside Windows, used as the last resort before rebuilding.
///
/// <see cref="MeshHealer"/> repairs locally - it welds, drops what is nothing, makes the winding
/// agree and caps holes - and has no answer to a surface that passes through itself. The other
/// end of the ladder, <see cref="VoxelRebuild"/>, always succeeds and always costs detail. This
/// sits between them: it re-solves what is inside the model and what is outside over the whole
/// shell, trimming self-intersections and dropping inverted parts, but leaves the original
/// triangles alone wherever they were already sound.
///
/// It is the Netfabb code Microsoft licensed for 3D Builder, which stayed in the operating system
/// when the app was retired - the same call PrusaSlicer, Bambu Studio and Orca offer as "repair by
/// the Windows service". It is not a service in any sense that shows up in services.msc; it is the
/// Windows.Graphics.Printing3D runtime component, and it is local.
///
/// Being somebody else's engine, it is a black box: it can weld detail away and it can return
/// something quite different from what went in. So nothing here is trusted on faith - the caller
/// measures the result and keeps it only when it is genuinely less broken.
/// </summary>
public static class WindowsRepair
{
    /// <summary>
    /// Whether this machine has the engine at all.
    ///
    /// The component is present on every desktop Windows 10 and 11, but not on all the Server and
    /// N builds, so it is asked for rather than assumed. Probed once: the answer cannot change
    /// while the app is running, and the caller reaches for it in a loop.
    /// </summary>
    public static bool IsAvailable { get; } = Probe();

    private static bool Probe()
    {
        try
        {
            return OperatingSystem.IsWindowsVersionAtLeast(10)
                && ApiInformation.IsTypePresent("Windows.Graphics.Printing3D.Printing3DModel");
        }
        catch
        {
            // An older Windows has no WinRT metadata to ask, and the question itself throws.
            return false;
        }
    }

    /// <summary>
    /// Hands the mesh to Windows and gives back what comes out, or says why it could not.
    ///
    /// Never throws for a mesh it dislikes - a repair that cannot run is a result, not a fault.
    /// Cancellation is the one exception, since the caller is waiting on it.
    /// </summary>
    public static async Task<OsRepairResult> TryRepairAsync(Mesh mesh, CancellationToken token = default)
    {
        if (!IsAvailable)
            return new(null, "Windows' own repair engine is not installed on this machine.");

        if (mesh.TriangleCount == 0)
            return new(null, "There are no triangles to mend.");

        try
        {
            // 3MF measures from a corner of the build volume, and an object's own mesh is centred
            // on its origin, so half of it sits behind the corner. Whether the engine minds is not
            // documented anywhere, and the only way to find out is a model that fails in someone
            // else's hands. Moving it whole and moving the result back is exact - a translation
            // cannot change what is broken - so the question is simply taken away.
            var lift = -mesh.ComputeBounds().Min;

            var model = new Printing3DModel { Unit = Printing3DModelUnit.Millimeter };
            var given = Pack(mesh, lift);
            model.Meshes.Add(given);

            var component = new Printing3DComponent { Mesh = given };
            model.Components.Add(component);
            model.Build.Components.Add(new Printing3DComponentWithMatrix
            {
                Component = component,
                Matrix = Matrix4x4.Identity
            });

            await model.RepairAsync().AsTask(token);

            var parts = new List<Mesh>();
            foreach (var part in model.Meshes)
            {
                token.ThrowIfCancellationRequested();
                if (Unpack(part, lift) is { } got && got.TriangleCount > 0) parts.Add(got);
            }

            if (parts.Count == 0)
                return new(null, "Windows' repair returned an empty model.");

            // Repair can split one shell into several - an object that overlapped itself was
            // never one solid - and they are all wanted. Welding is left to the caller, which
            // measures the result anyway.
            return new(Mesh.Combine(parts), null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception refused)
        {
            return new(null, $"Windows' repair could not run: {refused.Message.Trim()}");
        }
    }

    /// <summary>
    /// Our mesh as one the runtime understands: doubles for the corners, unsigned ints for the
    /// triangles, both in threes. The counts are in threes as well - the runtime counts vertices
    /// and triangles, not the numbers it took to write them, which is the easy mistake here.
    /// </summary>
    private static Printing3DMesh Pack(Mesh mesh, Vector3 lift)
    {
        var packed = new Printing3DMesh
        {
            VertexPositionsDescription = new Printing3DBufferDescription
            {
                Format = Printing3DBufferFormat.Printing3DDouble,
                Stride = 3
            },
            TriangleIndicesDescription = new Printing3DBufferDescription
            {
                Format = Printing3DBufferFormat.Printing3DUInt,
                Stride = 3
            }
        };

        packed.CreateVertexPositions((uint)(sizeof(double) * 3 * mesh.VertexCount));
        using (var stream = packed.GetVertexPositions().AsStream())
        using (var writer = new BinaryWriter(stream))
        {
            foreach (var corner in mesh.Positions)
            {
                writer.Write((double)(corner.X + lift.X));
                writer.Write((double)(corner.Y + lift.Y));
                writer.Write((double)(corner.Z + lift.Z));
            }
        }
        packed.VertexCount = (uint)mesh.VertexCount;

        packed.CreateTriangleIndices((uint)(sizeof(uint) * mesh.Indices.Count));
        using (var stream = packed.GetTriangleIndices().AsStream())
        using (var writer = new BinaryWriter(stream))
        {
            foreach (int corner in mesh.Indices) writer.Write((uint)corner);
        }
        packed.IndexCount = (uint)mesh.TriangleCount;

        return packed;
    }

    /// <summary>
    /// What came back, or null when it came back in a shape this does not read. The engine keeps
    /// the formats it was given, so that is the only pair handled; anything else means the runtime
    /// changed under us and guessing at the bytes would be worse than declining.
    /// </summary>
    private static Mesh? Unpack(Printing3DMesh packed, Vector3 lift)
    {
        if (packed.VertexPositionsDescription.Format != Printing3DBufferFormat.Printing3DDouble) return null;
        if (packed.TriangleIndicesDescription.Format != Printing3DBufferFormat.Printing3DUInt) return null;

        var mesh = new Mesh();

        using (var stream = packed.GetVertexPositions().AsStream())
        using (var reader = new BinaryReader(stream))
        {
            for (uint corner = 0; corner < packed.VertexCount; corner++)
                mesh.Positions.Add(new Vector3(
                    (float)reader.ReadDouble() - lift.X,
                    (float)reader.ReadDouble() - lift.Y,
                    (float)reader.ReadDouble() - lift.Z));
        }

        using (var stream = packed.GetTriangleIndices().AsStream())
        using (var reader = new BinaryReader(stream))
        {
            for (uint triangle = 0; triangle < packed.IndexCount * 3; triangle++)
            {
                uint index = reader.ReadUInt32();
                if (index >= mesh.Positions.Count) return null;
                mesh.Indices.Add((int)index);
            }
        }

        return mesh;
    }
}
