using System.Windows.Threading;
using HelixToolkit.Wpf.SharpDX;

namespace FastCraft3D.Render;

/// <summary>
/// Asks the viewport for a frame, and then asks again once things have stopped moving.
///
/// Nothing else asks. A frame is drawn only when the host has been told there is one worth
/// drawing, so a change that lands while the window is otherwise idle sits there, right in every
/// respect and never drawn, until a stray mouse move over the viewport asks for one. That is the
/// tool that appears to do nothing until the scene is clicked, and the plate that is not there
/// until it is.
///
/// Two asks, not one, and this is the part that took three goes to get right:
///
/// InvalidateRender alone was tried first. It asks for another frame of the renderables the host
/// collected on its last walk of the scene, and a model added since that walk is not among them -
/// so the plate and everything already on it carried on being drawn while the object just added
/// was not. InvalidateSceneGraph is the one that asks for the walk that would find it, and it
/// rebuilds the per-frame lists as well, which is what a change of Visibility needs.
///
/// That still left the frame that goes out too early. Resize the window and the plate comes back
/// with its border and its axes drawn at the new size and the checked squares missing: a real
/// frame, rendered while the lists the host draws meshes from were still being put together, and
/// then nothing further to correct it. So the second ask, a moment after the last change, draws
/// the frame again from lists that have settled. It costs one frame per burst of changes - a
/// drag restarts the wait rather than adding to it - which is nothing beside a scene that is
/// simply wrong until it is clicked on.
/// </summary>
public sealed class ViewportRepaint
{
    /// <summary>Long enough for a resize or a rebuild to have finished, short enough not to be seen.</summary>
    private static readonly TimeSpan Settled = TimeSpan.FromMilliseconds(150);

    private readonly Viewport3DX viewport;
    private readonly Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
    private readonly DispatcherTimer again;
    private bool queued;

    public ViewportRepaint(Viewport3DX viewport)
    {
        this.viewport = viewport;

        again = new DispatcherTimer(DispatcherPriority.Render, dispatcher) { Interval = Settled };
        again.Tick += (_, _) =>
        {
            again.Stop();
            Draw();
        };
    }

    /// <summary>
    /// Asks for a frame. Posted rather than drawn here and now, at render priority, so a run of
    /// changes - a tool replacing twenty objects - asks once after the last of them has landed
    /// rather than twenty times in the middle of it.
    /// </summary>
    public void Ask()
    {
        if (!queued)
        {
            queued = true;
            dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
            {
                queued = false;
                Draw();
            }));
        }

        // Restarted, not added to: while a drag is under way each move puts this off, and the
        // settling frame is drawn once it stops.
        again.Stop();
        again.Start();
    }

    private void Draw()
    {
        viewport.InvalidateSceneGraph();
        viewport.InvalidateRender();
    }
}
