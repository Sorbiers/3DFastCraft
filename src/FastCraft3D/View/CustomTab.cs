using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;

namespace FastCraft3D.View;

/// <summary>
/// What the Custom tab needs of the ribbon: a name for every button on it, and a copy of a button
/// that does what the original does.
///
/// A copy rather than the button itself, because a button belongs to one place in the tree and
/// the original has to stay where it is. The copy takes the original's bindings - label, icon,
/// tooltip, command, checked state - so it shows and enables exactly as the original does, and
/// takes its click as well: a good many ribbon buttons are a Click handler in the window rather
/// than a command, and those are run by raising the click on the original.
/// </summary>
public static class CustomTab
{
    /// <summary>One button of the ribbon: its name, where it is, and the button itself.</summary>
    public sealed record Entry(string Id, string Tab, string Label, ButtonBase Button);

    /// <summary>
    /// The button a copy is standing in for the click of, while its click is being run. A handler
    /// that opens something beside the button it was clicked on - the recent projects list - asks
    /// for this, since the original may be on a tab that is not showing.
    /// </summary>
    public static FrameworkElement? ClickedCopy { get; private set; }

    /// <summary>Every button on every tab but the custom one, named for the tab it is on and the label it carries.</summary>
    public static List<Entry> Collect(TabControl ribbon, TabItem custom)
    {
        var entries = new List<Entry>();
        var taken = new HashSet<string>();

        foreach (var tab in ribbon.Items.OfType<TabItem>().Where(t => !ReferenceEquals(t, custom)))
        {
            string header = tab.Header?.ToString() ?? "";

            foreach (var button in ButtonsIn(tab.Content as DependencyObject))
            {
                string name = NameOf(button);
                if (name.Length == 0) continue;

                // Two buttons of one label on a tab would be one, so the second is told apart.
                string id = $"{header}/{name}";
                for (int n = 2; !taken.Add(id); n++) id = $"{header}/{name}#{n}";

                entries.Add(new Entry(id, header, button.Content as string ?? name, button));
            }
        }

        return entries;
    }

    private static IEnumerable<ButtonBase> ButtonsIn(DependencyObject? parent)
    {
        if (parent is null) yield break;

        foreach (var child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>())
        {
            if (child is ButtonBase button) yield return button;
            else foreach (var inner in ButtonsIn(child)) yield return inner;
        }
    }

    /// <summary>
    /// What a button is called: its label - or, for one whose label is worked out, as the recorder's
    /// is, what that label is bound to - or failing both the name it is given in the markup.
    /// </summary>
    private static string NameOf(ButtonBase button)
    {
        // A bound label is read as what it is bound to, not as what it says just now: the
        // recorder's reads Record and then Stop, and a name that changed with it would be lost.
        if (BindingOperations.GetBinding(button, ContentControl.ContentProperty)?.Path?.Path is { Length: > 0 } path) return path;
        if (button.Content is string text && text.Length > 0) return text;

        return System.Windows.Automation.AutomationProperties.GetAutomationId(button) ?? "";
    }

    private static readonly DependencyProperty[] Copied =
    [
        ContentControl.ContentProperty, FrameworkElement.TagProperty, FrameworkElement.ToolTipProperty,
        FrameworkElement.MinWidthProperty, FrameworkElement.WidthProperty,
        ButtonBase.CommandProperty, ButtonBase.CommandParameterProperty, ToggleButton.IsCheckedProperty
    ];

    /// <summary>A button that looks and acts as <paramref name="source"/> does.</summary>
    public static ButtonBase Mirror(ButtonBase source)
    {
        ButtonBase copy = source is ToggleButton ? new ToggleButton() : new Button();
        copy.Style = source.Style;

        foreach (var property in Copied)
        {
            // A toggle's checked state means nothing on a plain button, and the other way about.
            if (property == ToggleButton.IsCheckedProperty && copy is not ToggleButton) continue;

            if (BindingOperations.GetBindingBase(source, property) is { } binding)
                BindingOperations.SetBinding(copy, property, binding);
            else if (source.ReadLocalValue(property) is var local && local != DependencyProperty.UnsetValue && local is not Expression)
                copy.SetValue(property, local);
        }

        copy.Click += (_, _) =>
        {
            ClickedCopy = copy;
            try { source.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, copy)); }
            finally { ClickedCopy = null; }
        };

        return copy;
    }
}
