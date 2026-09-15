using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;

namespace FastCraft3D.View;

/// <summary>
/// A tool's settings, shown in the side panel rather than in a window of their own.
///
/// These tools used to ask in a dialog, and a dialog shuts the scene away: the model could not be
/// turned to see the preview from another side while the numbers were being chosen. In the panel
/// the viewport stays live and everything else stands down, as it does for Split.
///
/// It keeps the shape of a dialog - ShowDialog, DialogResult, Close, OnClosed - so each tool's code
/// reads as it did. ShowDialog waits in a nested dispatcher frame, which is what Window.ShowDialog
/// does as well; the difference is only that the main window is left enabled.
/// </summary>
public class ToolPanel : UserControl
{
    private DispatcherFrame? frame;
    private bool? result;
    private bool closed;

    public ToolPanel()
    {
        // A Cancel button closed a dialog by being marked IsCancel, which only a Window acts on.
        // The mark is kept, and read here.
        AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler((_, e) =>
        {
            if (e.OriginalSource is Button { IsCancel: true }) DialogResult = false;
        }));
    }

    /// <summary>Puts a panel in the window, or takes it away when given null. Set by the main window.</summary>
    public static Action<ToolPanel?>? Host { get; set; }

    /// <summary>The panel being shown, if any.</summary>
    public static ToolPanel? Open { get; private set; }

    /// <summary>The heading the panel is shown under.</summary>
    public string Title { get; set; } = "";

    /// <summary>Setting it closes the panel, as it closes a dialog.</summary>
    public bool? DialogResult
    {
        get => result;
        set
        {
            result = value;
            Close();
        }
    }

    public event EventHandler? Closed;

    /// <summary>Shows the panel and waits until it is accepted or cancelled.</summary>
    public bool? ShowDialog()
    {
        if (Host is null) throw new InvalidOperationException("There is no window to show the panel in.");
        if (closed) return result;

        Open = this;
        Host(this);
        try
        {
            frame = new DispatcherFrame();
            Dispatcher.PushFrame(frame);
        }
        finally
        {
            Open = null;
            Host(null);
        }

        return result;
    }

    public void Close()
    {
        if (closed) return;
        closed = true;

        OnClosed(EventArgs.Empty);
        if (frame is not null) frame.Continue = false;
    }

    protected virtual void OnClosed(EventArgs e) => Closed?.Invoke(this, e);
}
