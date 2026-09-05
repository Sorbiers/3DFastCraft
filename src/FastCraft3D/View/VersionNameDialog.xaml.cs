using System.Windows;

namespace FastCraft3D.View;

/// <summary>Asks what to call a version before it is kept.</summary>
public partial class VersionNameDialog : Window
{
    public VersionNameDialog()
    {
        InitializeComponent();

        // A date is a better default than nothing: an unnamed version is hard to choose between.
        LabelBox.Text = DateTime.Now.ToString("d MMM HH:mm");
        Loaded += (_, _) => { LabelBox.SelectAll(); LabelBox.Focus(); };
    }

    public string VersionLabel { get; private set; } = string.Empty;

    private void OnAccept(object sender, RoutedEventArgs e)
    {
        VersionLabel = string.IsNullOrWhiteSpace(LabelBox.Text) ? "Version" : LabelBox.Text.Trim();
        DialogResult = true;
    }
}
