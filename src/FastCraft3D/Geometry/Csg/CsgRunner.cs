using System.Runtime.ExceptionServices;

namespace FastCraft3D.Geometry.Csg;

/// <summary>
/// Runs CSG work on a dedicated thread with a large stack.
///
/// BSP building, clipping and inversion all recurse once per tree level, and tree depth
/// grows with polygon count - a few tens of thousands of triangles is enough to overflow
/// the default 1 MB stack. Reserving a big stack here covers every recursive entry point
/// in one place. The reservation is virtual address space; pages commit only as used.
/// </summary>
public static class CsgRunner
{
    public const int StackSizeBytes = 256 * 1024 * 1024;

    public static T Run<T>(Func<T> work)
    {
        T result = default!;
        ExceptionDispatchInfo? error = null;

        var thread = new Thread(() =>
        {
            try { result = work(); }
            catch (Exception ex) { error = ExceptionDispatchInfo.Capture(ex); }
        }, StackSizeBytes)
        {
            IsBackground = true,
            Name = "CSG"
        };

        thread.Start();
        thread.Join();

        error?.Throw();
        return result;
    }
}
