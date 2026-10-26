using System.Windows;

namespace FastCraft3D.View;

/// <summary>Asked when a project is started: whether to keep a .3dfp beside its .3mf.</summary>
public partial class ProjectFileDialog : Window
{
    public ProjectFileDialog() => InitializeComponent();

    /// <summary>The answer. Closing the window is the same as Just the .3mf: nothing extra is made.</summary>
    public bool Keep { get; private set; }

    public bool Remember => RememberBox.IsChecked == true;

    private void OnKeep(object sender, RoutedEventArgs e)
    {
        Keep = true;
        DialogResult = true;
    }

    private void OnPlain(object sender, RoutedEventArgs e) => DialogResult = false;
}
