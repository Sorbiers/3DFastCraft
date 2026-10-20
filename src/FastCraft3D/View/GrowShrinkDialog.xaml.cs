using System.Globalization;
using System.Windows;

namespace FastCraft3D.View;

/// <summary>Grow and Shrink's choices: which, by how much, and along which axes.</summary>
public partial class GrowShrinkDialog : ToolPanel
{
    /// <summary>Remembered for the session: the next part usually wants the same.</summary>
    private static float distance = 0.2f;
    private static bool alongX = true, alongY = true, alongZ = true;

    public GrowShrinkDialog(bool grow, int objects)
    {
        InitializeComponent();

        SubjectText.Text = objects == 1 ? "Every face of the selected object moves by the amount." : $"Every face of the {objects} selected objects moves by the amount.";
        DistanceBox.Text = distance.ToString("0.###", CultureInfo.CurrentCulture);
        AlongX.IsChecked = alongX;
        AlongY.IsChecked = alongY;
        AlongZ.IsChecked = alongZ;
        (grow ? GrowChoice : ShrinkChoice).IsChecked = true;
    }

    /// <summary>How far, out above zero and in below, and along which axes; null when cancelled.</summary>
    public (float Distance, bool X, bool Y, bool Z)? Result { get; private set; }

    private void OnModeChanged(object sender, RoutedEventArgs e)
    {
        if (AcceptButton is null) return;

        string word = ShrinkChoice.IsChecked == true ? "Shrink" : "Grow";
        Title = word;
        AcceptButton.Content = word;
    }

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
            Warn("Pick at least one axis to move along.");
            return;
        }

        distance = by;
        (alongX, alongY, alongZ) = (x, y, z);

        Result = (ShrinkChoice.IsChecked == true ? -by : by, x, y, z);
        DialogResult = true;
    }

    private void Warn(string text)
    {
        WarningText.Text = text;
        WarningText.Visibility = Visibility.Visible;
    }
}
