using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FastCraft3D.Io;

namespace FastCraft3D.View;

/// <summary>
/// The session kept in a project's .3dfp: every step, newest first, with the plate after the one
/// chosen; and a way back to it, or to forget the steps before it. Or, for the object selected,
/// only the steps that changed it, and a way to bring back just that object as it was.
/// </summary>
public partial class SessionDialog : Window
{
    private readonly string file;
    private readonly string projectName;
    private readonly (Guid Id, string Name)? subject;
    private int listing;

    // Drawn a moment after the choice settles: running down the list with the arrow keys would
    // otherwise read and draw every step passed on the way.
    private readonly DispatcherTimer settle = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private int drawing;

    /// <param name="subject">The object selected, whose own history can be shown, or null.</param>
    public SessionDialog(string file, string projectName, (Guid Id, string Name)? subject = null)
    {
        this.file = file;
        this.projectName = projectName;
        this.subject = subject;
        InitializeComponent();

        if (subject is { } s)
        {
            OnlyObject.Content = $"Only {s.Name}: the steps that changed it";
            OnlyObject.Visibility = Visibility.Visible;
            OnlyObject.IsChecked = true;
        }

        settle.Tick += (_, _) =>
        {
            settle.Stop();
            _ = DrawAsync();
        };
        Closed += (_, _) => settle.Stop();

        Refresh();
    }

    /// <summary>The step chosen to go back to, or null.</summary>
    public int? GoBackTo { get; private set; }

    /// <summary>The step to bring the selected object back from as it was, or null.</summary>
    public int? RestoreFrom { get; private set; }

    /// <param name="Present">Whether there is anything to go back to: not for the step that took the object off the plate.</param>
    private sealed record Row(int Number, string Label, string Detail, bool Present = true);

    private Guid? OnObject => OnlyObject.IsChecked == true ? subject?.Id : null;

    private void Refresh()
    {
        ++listing;
        Modes();
        if (OnObject is not null)
        {
            _ = ListObjectAsync();
            return;
        }

        var steps = ProjectSession.ReadSteps(file);

        StepList.ItemsSource = steps
            .Select(s => new Row(s.Number, s.Label,
                $"Step {s.Number} - {s.Utc.ToLocalTime():d MMM yyyy HH:mm:ss} - " + (s.Objects == 1 ? "1 object" : $"{s.Objects} objects")))
            .Reverse()
            .ToList();

        HeaderText.Text = steps.Count == 0
            ? $"No steps kept for {projectName} yet. Every step taken from now on is kept in {Path.GetFileName(file)} as you work."
            : $"{steps.Count} step(s) taken in {projectName}, kept in {Path.GetFileName(file)}. Newest first.";

        if (steps.Count > 0) StepList.SelectedIndex = 0;
        UpdateButtons();
    }

    /// <summary>
    /// The steps that changed the one object. Every step is read for it, which for a long session
    /// is a moment's work, so away from the window.
    /// </summary>
    private async Task ListObjectAsync()
    {
        var (id, name) = subject!.Value;
        int mine = listing;

        StepList.ItemsSource = null;
        HeaderText.Text = $"Reading the steps that changed {name}...";
        UpdateButtons();

        var steps = await Task.Run(() => ProjectSession.ReadObjectSteps(file, id));
        if (mine != listing) return;

        StepList.ItemsSource = steps
            .Select(s => new Row(s.Number, s.Label, $"Step {s.Number} - {s.Utc.ToLocalTime():d MMM yyyy HH:mm:ss} - {s.Change}", s.Present))
            .Reverse()
            .ToList();

        HeaderText.Text = steps.Count == 0
            ? $"No step kept in {Path.GetFileName(file)} has changed {name} yet."
            : $"{steps.Count} step(s) changed {name}. Newest first.";

        if (steps.Count > 0) StepList.SelectedIndex = 0;
        UpdateButtons();
    }

    /// <summary>The buttons and the note for the whole plate, or for the one object.</summary>
    private void Modes()
    {
        bool one = OnObject is not null;
        GoBackButton.Content = one ? $"Restore {subject!.Value.Name}" : "Go back to this step";
        ForgetButton.Visibility = one ? Visibility.Collapsed : Visibility.Visible;
        FooterText.Text = one
            ? "Restoring brings back only this object, as it was after the step and where it stood then; nothing else on the plate changes. It is a normal undo step."
            : "Going back replaces what is on the plate. It is a normal undo step, so Ctrl+Z brings the current model back.";
    }

    private void OnScopeClicked(object sender, RoutedEventArgs e) => Refresh();

    private Row? Selected => StepList.SelectedItem as Row;

    private void UpdateButtons()
    {
        GoBackButton.IsEnabled = Selected is { Present: true };

        // The oldest step has nothing before it to forget.
        ForgetButton.IsEnabled = Selected is not null && StepList.SelectedIndex < StepList.Items.Count - 1;
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateButtons();
        Preview.Source = null;
        PreviewNote.Text = Selected is null ? "" : "Drawing...";
        settle.Stop();
        settle.Start();
    }

    private async Task DrawAsync()
    {
        if (Selected is not { } row) return;
        int mine = ++drawing;
        var one = OnObject;

        if (!row.Present)
        {
            PreviewNote.Text = "Not on the plate after this step";
            return;
        }

        try
        {
            // Read and put together away from the window; drawn on it, since the picture is a WPF one.
            var parts = await Task.Run(() =>
                one is { } id
                    ? ProjectSession.LoadObject(file, row.Number, id) is { } o ? [(o.ToWorldMesh(), o.Colour)] : []
                    : ExportComposer.ComposeForObj(ProjectSession.LoadStep(file, row.Number).Where(o => !o.IsHidden))
                        .Select(p => (p.Mesh, p.Colour)).ToList());
            if (mine != drawing) return;

            byte[]? png = MeshThumbnail.Png(parts, 400);
            Preview.Source = Decode(png);
            PreviewNote.Text = png is null ? "Nothing on the plate" : "";
        }
        catch
        {
            if (mine == drawing) PreviewNote.Text = "This step cannot be read";
        }
    }

    private static BitmapImage? Decode(byte[]? png)
    {
        if (png is not { Length: > 0 }) return null;

        using var stream = new MemoryStream(png);
        var picture = new BitmapImage();
        picture.BeginInit();
        picture.CacheOption = BitmapCacheOption.OnLoad;
        picture.StreamSource = stream;
        picture.EndInit();
        picture.Freeze();
        return picture;
    }

    private void OnGoBack(object sender, RoutedEventArgs e)
    {
        if (Selected is not { Present: true } row) return;

        if (OnObject is not null) RestoreFrom = row.Number;
        else GoBackTo = row.Number;
        DialogResult = true;
    }

    /// <summary>Only on a step: the list takes a double click on its scroll bar and the space below the last step too.</summary>
    private void OnListDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source
            && ItemsControl.ContainerFromElement(StepList, source) is ListBoxItem)
        {
            OnGoBack(sender, e);
        }
    }

    private void OnForget(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } row) return;

        int older = StepList.Items.Count - 1 - StepList.SelectedIndex;
        var answer = MessageBox.Show(this,
            $"Forget the {older} step(s) before step {row.Number}?\n\nThe project file gets smaller. This cannot be undone.",
            "Forget older steps", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
        if (answer != MessageBoxResult.OK) return;

        try
        {
            Cursor = Cursors.Wait;
            ProjectSession.ForgetBefore(file, row.Number);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Could not forget the steps", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            Cursor = null;
        }

        Refresh();
    }
}
