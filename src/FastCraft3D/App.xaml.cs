using System.Windows;
using System.Windows.Threading;
using FastCraft3D.Io;

namespace FastCraft3D;

public partial class App : Application
{
    private int reported;

    /// <summary>
    /// What the app was asked to open, from the command line.
    ///
    /// This is how Windows opens a file with a program: it runs the exe with the path as an
    /// argument. So this is all that "Open with" and a file association need on this side -
    /// there is no other message, and a program that ignores its arguments comes up empty and
    /// looks broken.
    ///
    /// Read before the window is made, because the window reads it back on the way up.
    /// </summary>
    public static IReadOnlyList<string> Opening { get; private set; } = [];

    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnUnhandled;
        Opening = IncomingFiles.Wanted(e.Args);
        base.OnStartup(e);
    }

    /// <summary>
    /// Says what went wrong instead of vanishing.
    ///
    /// Worth having for one failure in particular. The viewport is Direct3D 11, and the device is
    /// built in the window's constructor - so on a machine that cannot provide one the app died
    /// before it drew anything, with a stack trace and no clue that graphics were the problem.
    /// That is not an exotic case: a remote desktop, a virtual machine without a display driver,
    /// or a bare Windows install with only the Basic Display Adapter will all do it.
    /// </summary>
    private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;

        // Once, and never from inside itself.
        //
        // Showing a dialog pumps the dispatcher, so whatever threw gets another go while the box
        // is up - it throws again, lands here again, opens another box, and round it goes until
        // the stack runs out. That is not theoretical: this handler in its first form turned a
        // single layout fault into a stack overflow, which kills the process outright with no
        // dialog at all and is exactly the crash it was written to prevent.
        if (Interlocked.Exchange(ref reported, 1) == 1) return;

        bool graphics = LooksLikeGraphics(e.Exception);

        string message = graphics
            ? "3DFastCraft could not start its 3D view.\n\n"
              + "It needs a graphics adapter that supports Direct3D 11 - which almost any "
              + "machine since about 2010 has, including integrated graphics. It is usually "
              + "missing on a virtual machine with no display driver, over some remote desktop "
              + "connections, or on a fresh Windows install still using the Basic Display "
              + "Adapter. Installing the graphics driver for the machine normally fixes it.\n\n"
              + e.Exception.Message
            : "Something went wrong:\n\n" + e.Exception.Message;

        MessageBox.Show(message, "3DFastCraft", MessageBoxButton.OK, MessageBoxImage.Error);

        // And then out. An unhandled exception on the UI thread leaves the window in a state
        // nobody can reason about, and carrying on would only mean a second one nothing reports.
        Shutdown();
    }

    /// <summary>
    /// Whether this is the graphics device failing rather than anything else.
    ///
    /// Matched on the text as well as the type: the device failure surfaces as a SharpDX
    /// exception, but it also arrives wrapped by whatever was constructing it at the time.
    /// </summary>
    private static bool LooksLikeGraphics(Exception ex)
    {
        for (var at = ex; at is not null; at = at.InnerException)
        {
            if (at.GetType().FullName?.StartsWith("SharpDX", StringComparison.Ordinal) == true)
                return true;

            string text = at.Message;
            if (text.Contains("D3D", StringComparison.OrdinalIgnoreCase)
                || text.Contains("Direct3D", StringComparison.OrdinalIgnoreCase)
                || text.Contains("adapter", StringComparison.OrdinalIgnoreCase)
                || text.Contains("device", StringComparison.OrdinalIgnoreCase)
                   && text.Contains("create", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
