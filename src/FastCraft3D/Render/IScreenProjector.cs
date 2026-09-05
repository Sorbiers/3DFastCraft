using System.Numerics;
using System.Windows;
using HelixToolkit.Wpf.SharpDX;
using Media3D = System.Windows.Media.Media3D;

namespace FastCraft3D.Render;

/// <summary>
/// The only thing the manipulator needs from the camera: where a world point lands on screen,
/// and which way the viewer is looking.
///
/// Narrowing the dependency to this keeps <see cref="GizmoController"/> free of Direct3D, so
/// the handle layout and every drag rule can be exercised against a predictable stand-in
/// camera instead of requiring a live GPU and a rendered frame.
/// </summary>
public interface IScreenProjector
{
    bool TryProject(Vector3 world, out Point screen);

    /// <summary>Direction the camera looks along, used to tell which side of a ring faces the viewer.</summary>
    Vector3 ViewDirection { get; }

    /// <summary>
    /// False while the viewport has no usable size - during a resize, or before the first
    /// layout pass. Projections taken then are meaningless rather than merely inaccurate.
    /// </summary>
    bool IsReady { get; }
}

public sealed class Viewport3DXProjector(Viewport3DX view) : IScreenProjector
{
    public bool TryProject(Vector3 world, out Point screen)
    {
        try
        {
            Point p = view.Project(new Media3D.Point3D(world.X, world.Y, world.Z));
            if (double.IsNaN(p.X) || double.IsNaN(p.Y) || double.IsInfinity(p.X) || double.IsInfinity(p.Y))
            {
                screen = default;
                return false;
            }
            screen = p;
            return true;
        }
        catch
        {
            // The viewport refuses to project before its first render pass.
            screen = default;
            return false;
        }
    }

    public bool IsReady => view.ActualWidth > 0 && view.ActualHeight > 0 && view.IsLoaded;

    public Vector3 ViewDirection
    {
        get
        {
            if (view.Camera is not PerspectiveCamera camera) return -Vector3.UnitZ;
            var d = camera.LookDirection;
            return new Vector3((float)d.X, (float)d.Y, (float)d.Z);
        }
    }
}
