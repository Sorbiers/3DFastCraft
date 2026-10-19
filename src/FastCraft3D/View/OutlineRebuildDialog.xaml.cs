using System.Windows;
using FastCraft3D.Geometry;

namespace FastCraft3D.View;

/// <summary>Rebuild by outline's one choice: the exact tolerance, or merging faces a hair apart.</summary>
public partial class OutlineRebuildDialog : ToolPanel
{
    /// <summary>Remembered for the session: a model that needed merging once will need it again.</summary>
    private static bool merge;

    public OutlineRebuildDialog()
    {
        InitializeComponent();
        MergeBox.IsChecked = merge;
    }

    /// <summary>The tolerance chosen, or null when the panel was cancelled.</summary>
    public float? Result { get; private set; }

    private void OnAccept(object sender, RoutedEventArgs e)
    {
        merge = MergeBox.IsChecked == true;
        Result = merge ? OutlineRebuild.MergeToleranceMm : OutlineRebuild.ToleranceMm;
        DialogResult = true;
    }
}
