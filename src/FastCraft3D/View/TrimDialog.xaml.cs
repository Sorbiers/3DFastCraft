using System.Globalization;
using System.Windows;

namespace FastCraft3D.View;

/// <summary>Trim's choices: how far to cut the outer edge back, and along which axes.</summary>
public partial class TrimDialog : ToolPanel
{
    /// <summary>Remembered for the session.</summary>
    private static float trimBy = 0.2f;
    private static bool alongX = true, alongY = true, alongZ;

    public TrimDialog(string subject)
    {
        InitializeComponent();

        SubjectText.Text = subject;
        DistanceBox.Text = trimBy.ToString("0.###", CultureInfo.CurrentCulture);
        AlongX.IsChecked = alongX;
        AlongY.IsChecked = alongY;
        AlongZ.IsChecked = alongZ;
    }

    /// <summary>How far, and along which axes; null when cancelled.</summary>
    public (float Distance, bool X, bool Y, bool Z)? Result { get; private set; }

    private void OnAccept(object sender, RoutedEventArgs e)
    {
        if (!float.TryParse(DistanceBox.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out float by) || by <= 0f)
        {
            Warn("Type how far, in millimeters, above zero.");
            return;
        }

        bool x = AlongX.IsChecked == true, y = AlongY.IsChecked == true, z = AlongZ.IsChecked == true;
        if (!x && !y && !z)
        {
            Warn("Pick at least one axis.");
            return;
        }

        trimBy = by;
        (alongX, alongY, alongZ) = (x, y, z);

        Result = (by, x, y, z);
        DialogResult = true;
    }

    private void Warn(string text)
    {
        WarningText.Text = text;
        WarningText.Visibility = Visibility.Visible;
    }
}
