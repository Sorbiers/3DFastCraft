using System.Globalization;
using System.Numerics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace FastCraft3D.Generators;

/// <summary>The unit lengths are shown in, as the transform boxes show them.</summary>
/// <param name="Label">What the boxes are labelled with: "mm", "in".</param>
/// <param name="Millimetres">How many millimetres one of these is.</param>
public sealed record DisplayUnit(string Label, float Millimetres)
{
    public static DisplayUnit Millimetre { get; } = new("mm", 1f);
}

/// <summary>What a generator's panel needs to know about where it is being used.</summary>
/// <param name="Memory">Where presets of the person's own are kept; null offers only the shipped ones.</param>
/// <param name="ModelScale">What the model is drawn at: 87 for 1:87. For readouts in real units.</param>
/// <param name="Target">The part selected when the panel opened, which a cutter can be cut into; or null.</param>
public sealed record GeneratorContext(
    Printer Printer, DisplayUnit Unit, float PlateWidth, float PlateDepth,
    LibraryMemory? Memory = null, float ModelScale = 1f, string? Target = null)
{
    public static GeneratorContext Default { get; } = new(Printer.Default, DisplayUnit.Millimetre, 200f, 200f);
}

/// <summary>
/// A generator's settings as a panel, built from its settings record: one row per setting under
/// its heading, the preview rebuilt as the numbers change, and what would be made said
/// underneath.
///
/// Built in code rather than XAML, because there is no one panel to write: there is one per
/// generator, and each is the same few kinds of row in a different order. The styles are the
/// main window's own, looked up by name, so it reads like every other panel in the app.
/// </summary>
public sealed class GeneratorView : UserControl
{
    private readonly Generator generator;
    private readonly GeneratorContext context;
    private readonly Action<object?, Generated?> preview;
    private readonly PreviewRunner<object, Generated>? runner;
    private Printer printer;

    private readonly object[] values;
    private readonly string?[] faults;

    /// <summary>
    /// Which settings still hold the printer's number rather than one somebody typed. Those follow
    /// the printer when it is changed; the rest are left alone, since the printer sets where a
    /// setting starts, not what it was last told.
    /// </summary>
    private readonly bool[] fromPrinter;

    private readonly List<Row> rows = [];
    private readonly TextBlock summary = new() { TextWrapping = TextWrapping.Wrap, Foreground = Brush(0xFF3A3F45) };
    private readonly Border summaryBox = new()
    {
        Margin = new Thickness(0, 8, 0, 0),
        Padding = new Thickness(8, 6, 8, 6),
        CornerRadius = new CornerRadius(3),
        BorderThickness = new Thickness(1)
    };
    private readonly Button insert = new();
    private readonly Button cut = new() { Visibility = Visibility.Collapsed };
    private readonly Button turn = new() { Content = "Turn it", Visibility = Visibility.Collapsed };

    /// <summary>What turning the set found, said under the rest; and whether it was a jam.</summary>
    private (string Text, bool Problem)? motion;
    private readonly Expander printerSection = new() { Margin = new Thickness(0, 8, 0, 0) };

    private readonly ComboBox presetList = new() { MinWidth = 150, Margin = new Thickness(0, 2, 4, 2) };
    private readonly Button forgetPreset = new() { Content = "Forget", Visibility = Visibility.Collapsed };
    private readonly Button savePreset = new() { Content = "Save as preset" };
    private readonly StackPanel naming = new() { Orientation = Orientation.Horizontal, Visibility = Visibility.Collapsed, Margin = new Thickness(96, 2, 0, 0) };
    private readonly TextBox presetName = new() { Width = 140, Margin = new Thickness(0, 2, 4, 2) };
    private List<(string Label, string? Mine, Func<object?> Settings)> presets = [];
    private bool fillingPresets;
    private bool reading = true;
    private bool stopped;

    /// <summary>What the next change to a box is: the printer's number (true) or somebody's own.</summary>
    private bool assigningFromPrinter;

    /// <summary>The settings the shown result was made from, and the result.</summary>
    private (object Settings, Generated Made)? shown;
    private (object Settings, Exception Error)? failed;

    private sealed record Row(int Index, GeneratorParameter Parameter, FrameworkElement Element, Action<object> Set, TextBlock? Unit);

    /// <summary>Each heading, and the rows under it: a heading with nothing shown under it goes too.</summary>
    private readonly List<(TextBlock Heading, List<Row> Rows)> headings = [];

    /// <param name="start">
    /// The settings to open with. Null opens at the generator's defaults for this printer.
    /// </param>
    /// <param name="preview">Shows a result on the plate as it is made, or takes it away when given nulls.</param>
    /// <param name="action">What the button that finishes is called: Insert, or Apply for an edit.</param>
    /// <param name="notice">A line above the settings, for something the person should know first.</param>
    /// <param name="live">
    /// Build off the UI thread once the typing settles, as in the app. Off, each change builds at
    /// once on the caller's thread, which is what a test wants.
    /// </param>
    public GeneratorView(
        Generator generator,
        object? start,
        GeneratorContext context,
        Action<object?, Generated?> preview,
        string action = "Insert",
        string? notice = null,
        bool live = true)
    {
        this.generator = generator;
        this.context = context;
        this.preview = preview;
        printer = context.Printer;

        values = generator.Shape.Values(generator.Shape.Sane(start ?? generator.Defaults(printer)));
        faults = new string?[values.Length];
        fromPrinter = generator.Parameters.Select(p => start is null && p.FromPrinter).ToArray();
        insert.Content = action;

        if (live)
        {
            runner = new PreviewRunner<object, Generated>(
                (settings, token) => generator.Make(settings, printer, token),
                Present,
                TimeSpan.FromMilliseconds(150),
                action => Dispatcher.BeginInvoke(action));

            runner.BusyChanged += _ => Describe();
            runner.Failed += (settings, error) =>
            {
                failed = (settings, error);
                shown = null;
                this.preview(null, null);
                Describe();
            };
        }

        Content = Lay(notice);
        reading = false;

        Loaded += (_, _) =>
        {
            if (rows.Select(r => r.Element).OfType<StackPanel>().SelectMany(r => r.Children.OfType<TextBox>()).FirstOrDefault() is { } first)
            {
                first.Focus();
                first.SelectAll();
            }
        };

        Changed();
    }

    /// <summary>The settings the finishing button was pressed on.</summary>
    public object? Result { get; private set; }

    /// <summary>What those settings made, already built for the preview.</summary>
    public Generated? Made { get; private set; }

    /// <summary>Whether it was Cut into the target rather than inserted.</summary>
    public bool Cuts { get; private set; }

    /// <summary>
    /// Whether a set that moves offers to be turned. Only where the preview is the set put
    /// together - inserting, not editing, where the parts stand wherever they were left.
    /// </summary>
    public bool OffersTurning { get; set; }

    /// <summary>Turn it was pressed, on these parts - or Stop, while they are turning.</summary>
    public event Action<Generated>? TurnPressed;

    /// <summary>Says whether the set is turning, for the button to offer to stop it.</summary>
    public void Turning(bool on) => turn.Content = on ? "Stop" : "Turn it";

    /// <summary>What turning the set found, said in the panel; red for a jam. Null takes it away.</summary>
    public void SayMotion(string? text, bool problem)
    {
        motion = text is null ? null : (text, problem);
        Describe();
    }

    /// <summary>The printer as the panel was left, which may have been changed in it.</summary>
    public Printer Printer => printer;

    /// <summary>Insert or Apply (true), or Cancel (false).</summary>
    public event Action<bool>? Finished;

    /// <summary>The printer was changed in the panel, for it to be remembered.</summary>
    public event Action<Printer>? PrinterChanged;

    /// <summary>The settings as the boxes stand, whether or not they can be made.</summary>
    public object Current => generator.Shape.From(values);

    /// <summary>What the panel says under the buttons.</summary>
    internal string SummaryText => summary.Text;

    internal bool CanInsert => insert.IsEnabled;

    internal void PressInsert() => insert.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));

    /// <summary>Whether Cut is offered, for the tests.</summary>
    internal bool CutOffered => cut.Visibility == Visibility.Visible;

    /// <summary>Whether a heading is shown - it is not, with nothing under it shown - for the tests.</summary>
    internal bool HeadingShown(string heading) =>
        headings.Single(h => h.Heading.Text == heading).Heading.Visibility == Visibility.Visible;

    internal void Choose(string name, object value) => rows.Single(r => r.Parameter.Name == name).Set(value);

    /// <summary>What the unit after a setting's box says, for the tests.</summary>
    internal string UnitText(string name) => rows.Single(r => r.Parameter.Name == name).Unit?.Text ?? "";

    /// <summary>Stops the preview and takes it off the plate. Call when the panel closes.</summary>
    public void Stop()
    {
        if (stopped) return;
        stopped = true;

        runner?.Dispose();
        preview(null, null);
    }

    private StackPanel Lay(string? notice)
    {
        var panel = new StackPanel();

        if (notice is not null)
            panel.Children.Add(new Border
            {
                Margin = new Thickness(0, 0, 0, 8),
                Padding = new Thickness(8, 5, 8, 5),
                CornerRadius = new CornerRadius(3),
                Background = Brush(0xFFFFF4D6),
                BorderBrush = Brush(0xFFE3C46A),
                BorderThickness = new Thickness(1),
                Child = new TextBlock { Text = notice, TextWrapping = TextWrapping.Wrap, Foreground = Brush(0xFF5C4708) }
            });

        if (generator.Summary.Length > 0)
            panel.Children.Add(new TextBlock
            {
                Text = generator.Summary,
                TextWrapping = TextWrapping.Wrap,
                Foreground = Brush(0xFF3A4550),
                Margin = new Thickness(0, 0, 0, 8)
            });

        if (generator.Presets.Count > 0 || context.Memory is not null)
            panel.Children.Add(PresetRow());

        string? group = null;
        for (int i = 0; i < generator.Parameters.Count; i++)
        {
            var p = generator.Parameters[i];

            if (p.Group is not null && p.Group != group)
            {
                group = p.Group;
                var heading = new TextBlock
                {
                    Text = group,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = Brush(0xFF3A4550),
                    Margin = new Thickness(0, panel.Children.Count > 1 ? 8 : 0, 0, 2)
                };
                headings.Add((heading, []));
                panel.Children.Add(heading);
            }

            var row = MakeRow(p, i);
            rows.Add(row);
            if (p.Group is not null) headings[^1].Rows.Add(row);
            panel.Children.Add(row.Element);
        }

        MarkPrinterSettings();

        insert.SetResourceReference(StyleProperty, "PanelButton");
        AutomationProperties.SetName(insert, "GeneratorInsert");
        insert.Click += (_, _) =>
        {
            if (Insertable() is not { } ready) return;

            Result = ready.Settings;
            Made = ready.Made;
            Finished?.Invoke(true);
        };

        cut.Content = "Cut";
        cut.ToolTip = $"Cut it into {context.Target} now, where it stands";
        cut.SetResourceReference(StyleProperty, "PanelButton");
        AutomationProperties.SetName(cut, "GeneratorCut");
        cut.Click += (_, _) =>
        {
            if (Insertable() is not { } ready) return;

            Result = ready.Settings;
            Made = ready.Made;
            Cuts = true;
            Finished?.Invoke(true);
        };

        turn.SetResourceReference(StyleProperty, "PanelButton");
        turn.ToolTip = "Turn the mechanism a whole turn on the plate, as the motion check does: each part moves only when pushed";
        AutomationProperties.SetName(turn, "GeneratorTurn");
        turn.Click += (_, _) =>
        {
            if (shown is { } s && s.Made.Motion is not null && !s.Made.IsRefused) TurnPressed?.Invoke(s.Made);
        };

        var cancel = new Button { Content = "Cancel", IsCancel = true };
        cancel.SetResourceReference(StyleProperty, "PanelButton");
        AutomationProperties.SetName(cancel, "GeneratorCancel");
        cancel.Click += (_, _) => Finished?.Invoke(false);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        buttons.Children.Add(insert);
        buttons.Children.Add(cut);
        buttons.Children.Add(turn);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);

        summaryBox.Child = summary;
        Say(problem: false);
        panel.Children.Add(summaryBox);

        panel.Children.Add(PrinterSection());
        return panel;
    }

    /// <summary>
    /// The presets: the generator's own, then the person's, and a way to keep the settings as they
    /// stand under a name. Choosing one fills every box; nothing is made until Insert, as ever.
    /// </summary>
    private StackPanel PresetRow()
    {
        var outer = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var label = new TextBlock { Text = "Preset", Width = 96, ToolTip = "Sizes worth starting from. Choosing one fills in every setting." };
        label.SetResourceReference(StyleProperty, "FieldLabel");
        row.Children.Add(label);

        AutomationProperties.SetAutomationId(presetList, "Preset");
        presetList.SelectionChanged += (_, _) =>
        {
            if (fillingPresets || presetList.SelectedIndex < 0) return;

            var (_, mine, settings) = presets[presetList.SelectedIndex];
            forgetPreset.Visibility = mine is null ? Visibility.Collapsed : Visibility.Visible;
            if (settings() is { } chosen) Apply(chosen);
        };
        row.Children.Add(presetList);
        outer.Children.Add(row);

        if (context.Memory is { } memory)
        {
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(96, 2, 0, 0) };

            savePreset.SetResourceReference(StyleProperty, "PanelButton");
            AutomationProperties.SetAutomationId(savePreset, "PresetSave");
            savePreset.ToolTip = "Keep the settings as they stand under a name of your own";
            savePreset.Click += (_, _) =>
            {
                naming.Visibility = Visibility.Visible;
                presetName.Focus();
            };
            buttons.Children.Add(savePreset);

            forgetPreset.SetResourceReference(StyleProperty, "PanelButton");
            AutomationProperties.SetAutomationId(forgetPreset, "PresetForget");
            forgetPreset.Click += (_, _) =>
            {
                if (presetList.SelectedIndex < 0 || presets[presetList.SelectedIndex].Mine is not { } name) return;

                memory.ForgetPreset(generator.Id, name);
                FillPresets(null);
            };
            buttons.Children.Add(forgetPreset);
            outer.Children.Add(buttons);

            AutomationProperties.SetAutomationId(presetName, "PresetName");
            var keep = new Button { Content = "Keep" };
            keep.SetResourceReference(StyleProperty, "PanelButton");
            AutomationProperties.SetAutomationId(keep, "PresetKeep");
            keep.Click += (_, _) => KeepPreset(presetName.Text);
            presetName.KeyDown += (_, e) =>
            {
                if (e.Key != System.Windows.Input.Key.Enter) return;
                e.Handled = true;
                KeepPreset(presetName.Text);
            };

            naming.Children.Add(presetName);
            naming.Children.Add(keep);
            outer.Children.Add(naming);
        }

        FillPresets(null);
        return outer;
    }

    /// <summary>Keeps the settings as they stand under a name, replacing one of the same name.</summary>
    internal void KeepPreset(string name)
    {
        name = name.Trim();
        if (name.Length == 0 || context.Memory is not { } memory || Fault() is not null) return;

        memory.SavePreset(generator, name, Current);
        naming.Visibility = Visibility.Collapsed;
        presetName.Text = "";
        FillPresets(name);
    }

    /// <summary>The list again, showing <paramref name="mine"/> as chosen without applying it.</summary>
    private void FillPresets(string? mine)
    {
        presets = generator.Presets
            .Select(p => (p.Name, (string?)null, (Func<object?>)(() => p.Settings)))
            .Concat((context.Memory?.PresetNames(generator.Id) ?? [])
                .Select(n => ($"{n} (mine)", (string?)n, (Func<object?>)(() => context.Memory!.Preset(generator, n)))))
            .ToList();

        fillingPresets = true;
        presetList.Items.Clear();
        foreach (var (label, _, _) in presets) presetList.Items.Add(label);
        presetList.SelectedIndex = presets.FindIndex(p => mine is not null && p.Mine == mine);
        fillingPresets = false;

        forgetPreset.Visibility = mine is null ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>The preset names as the list shows them, for the tests.</summary>
    internal IReadOnlyList<string> PresetLabels => presets.Select(p => p.Label).ToList();

    internal void ChoosePreset(string label) => presetList.SelectedIndex = presets.FindIndex(p => p.Label == label);

    /// <summary>Every box filled from these settings at once, then one rebuild.</summary>
    private void Apply(object settings)
    {
        var chosen = generator.Shape.Values(generator.Shape.Sane(settings));

        reading = true;
        foreach (var row in rows)
        {
            row.Set(chosen[row.Index]);
            values[row.Index] = chosen[row.Index];
            faults[row.Index] = null;
            fromPrinter[row.Index] = false;
        }

        reading = false;

        MarkPrinterSettings();
        Changed();
    }

    /// <summary>
    /// The printer's three numbers, folded away under a line that says them. In the panel rather
    /// than somewhere of its own, because the generators are all that ask - and the moment it
    /// matters is when a fit is being chosen, with the preview in front of you.
    /// </summary>
    private Expander PrinterSection()
    {
        var body = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
        body.Children.Add(new TextBlock
        {
            Text = "Walls and clearances start from these. Remembered for next time.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush(0xFF5A5F66),
            Margin = new Thickness(0, 0, 0, 4)
        });

        body.Children.Add(PrinterRow("Nozzle", "Printer.Nozzle", printer.Nozzle, Printer.LeastNozzle, Printer.MostNozzle,
            "The nozzle's width, near enough the width of one printed line. Walls are made a whole number of these thick.",
            v => printer with { Nozzle = v }));
        body.Children.Add(PrinterRow("Layer", "Printer.Layer", printer.Layer, Printer.LeastLayer, Printer.MostLayer,
            "Layer height.",
            v => printer with { Layer = v }));
        body.Children.Add(PrinterRow("Clearance", "Printer.Clearance", printer.XyClearance, Printer.LeastClearance, Printer.MostClearance,
            "The gap, each side, two printed parts need to slide past each other. 0.2 suits most printers.",
            v => printer with { XyClearance = v }));

        printerSection.Content = body;
        printerSection.Header = "Printer: " + PrinterProfile.Describe(printer);
        AutomationProperties.SetAutomationId(printerSection, "Printer");
        return printerSection;
    }

    private StackPanel PrinterRow(string label, string id, float value, float least, float most, string hint, Func<float, Printer> changed)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 3) };

        var name = new TextBlock { Text = label, Width = 96, ToolTip = hint };
        name.SetResourceReference(StyleProperty, "FieldLabel");
        row.Children.Add(name);

        var box = new TextBox { Width = 64, Text = value.ToString("0.###", CultureInfo.CurrentCulture) };
        box.SetResourceReference(StyleProperty, "NumberBox");
        AutomationProperties.SetAutomationId(box, id);
        var normalBorder = box.BorderBrush;

        box.TextChanged += (_, _) =>
        {
            bool good = Number(box.Text, out double typed) && typed >= least && typed <= most;
            box.BorderBrush = good ? normalBorder : Brush(0xFFD04A3A);
            box.ToolTip = good ? null : $"From {least:0.##} to {most:0.##} mm";
            if (good) SetPrinter(changed((float)typed));
        };

        row.Children.Add(box);
        row.Children.Add(new TextBlock { Text = "mm", Foreground = Brush(0xFF9AA0A8), VerticalAlignment = VerticalAlignment.Center });
        return row;
    }

    private void SetPrinter(Printer changed)
    {
        if (changed == printer) return;

        printer = changed;
        printerSection.Header = "Printer: " + PrinterProfile.Describe(printer);

        // Settings still on the printer's number move to the new one, through their own boxes,
        // so what is shown and what is built cannot come apart.
        reading = true;
        foreach (var row in rows.Where(r => fromPrinter[r.Index]))
        {
            var value = row.Parameter.DefaultFor(printer);

            assigningFromPrinter = true;
            row.Set(value);
            assigningFromPrinter = false;

            values[row.Index] = value;
            faults[row.Index] = null;
        }

        reading = false;

        MarkPrinterSettings();
        PrinterChanged?.Invoke(printer);
        Changed();
    }

    /// <summary>One setting's row, and how to put a value into it from outside.</summary>
    private Row MakeRow(GeneratorParameter p, int index)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 3) };

        var label = new TextBlock { Text = p.Label, Width = 96 };
        label.SetResourceReference(StyleProperty, "FieldLabel");
        label.ToolTip = (p.Hint is null ? "" : p.Hint + Environment.NewLine + Environment.NewLine)
                        + (p.FromPrinter
                            ? "Double-click to go back to the printer's number."
                            : $"Double-click to go back to {Say(p, p.Default)}.");
        row.Children.Add(label);

        string id = $"{generator.Id}.{p.Name}";
        Action<object> set;
        TextBlock? unit = null;

        switch (p.Kind)
        {
            case ParameterKind.Choice:
            {
                var list = new ComboBox { MinWidth = 120, Margin = new Thickness(0, 2, 4, 2) };
                var choices = p.Choices;
                foreach (var (_, text) in choices) list.Items.Add(text);
                list.SelectedIndex = IndexOf(choices, values[index]);
                AutomationProperties.SetAutomationId(list, id);
                list.SelectionChanged += (_, _) =>
                {
                    if (list.SelectedIndex < 0 || reading) return;
                    var before = Current;
                    values[index] = choices[list.SelectedIndex].Value;
                    Follow(before, p.Name);
                    Changed();
                };
                set = v => list.SelectedIndex = IndexOf(choices, v);
                row.Children.Add(list);
                break;
            }

            case ParameterKind.Toggle:
            {
                var tick = new CheckBox { IsChecked = (bool)values[index], VerticalAlignment = VerticalAlignment.Center };
                AutomationProperties.SetAutomationId(tick, id);
                tick.Click += (_, _) =>
                {
                    var before = Current;
                    values[index] = tick.IsChecked == true;
                    Follow(before, p.Name);
                    Changed();
                };
                set = v =>
                {
                    var before = Current;
                    tick.IsChecked = (bool)v;
                    values[index] = v;
                    if (!reading) Follow(before, p.Name);
                    Changed();
                };
                row.Children.Add(tick);
                break;
            }

            default:
            {
                var box = new TextBox { Width = 64, Text = Show(p, values[index]) };
                box.SetResourceReference(StyleProperty, "NumberBox");
                AutomationProperties.SetAutomationId(box, id);
                var normalBorder = box.BorderBrush;

                box.TextChanged += (_, _) =>
                {
                    fromPrinter[index] = assigningFromPrinter;
                    MarkPrinterSettings();
                    if (reading) return;

                    var before = Current;
                    faults[index] = Parse(p, box.Text, out object? value);
                    if (value is not null) values[index] = value;

                    box.BorderBrush = faults[index] is null ? normalBorder : Brush(0xFFD04A3A);
                    if (value is not null) Follow(before, p.Name);
                    Changed();
                };

                set = v =>
                {
                    box.Text = Show(p, v);
                    box.BorderBrush = normalBorder;
                };
                row.Children.Add(box);

                if (p.IsLength || p.Unit is not null)
                {
                    unit = new TextBlock { Foreground = Brush(0xFF9AA0A8), VerticalAlignment = VerticalAlignment.Center };
                    row.Children.Add(unit);
                }

                break;
            }
        }

        label.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 2) Reset(index);
        };

        return new Row(index, p, row, set, unit);
    }

    /// <summary>
    /// Fills in whatever the generator says follows from a setting changed by hand, through the
    /// boxes, so that what is shown and what is built cannot come apart.
    /// </summary>
    private void Follow(object before, string changed)
    {
        var adjusted = generator.Shape.Values(generator.Shape.Sane(generator.Adjusted(before, Current, changed)));

        bool wasReading = reading;
        reading = true;
        foreach (var row in rows)
        {
            if (row.Parameter.Name == changed || Equals(adjusted[row.Index], values[row.Index])) continue;

            row.Set(adjusted[row.Index]);
            values[row.Index] = adjusted[row.Index];
            faults[row.Index] = null;
            fromPrinter[row.Index] = false;
        }

        reading = wasReading;
        MarkPrinterSettings();
    }

    /// <summary>Puts a setting back to where it starts: the printer's number for a wall or a clearance.</summary>
    internal void Reset(int index)
    {
        var row = rows[index];

        assigningFromPrinter = row.Parameter.FromPrinter;
        row.Set(row.Parameter.DefaultFor(printer));
        assigningFromPrinter = false;
    }

    /// <summary>The unit after each box, saying where a wall or a clearance still follows the printer.</summary>
    private void MarkPrinterSettings()
    {
        foreach (var row in rows)
        {
            if (row.Unit is null) continue;

            string text = row.Parameter.IsLength ? context.Unit.Label : row.Parameter.Unit ?? "";
            row.Unit.Text = fromPrinter[row.Index] ? text + ", from the printer" : text;
        }
    }

    /// <summary>After any change: which rows are shown, what can be made, and a new preview.</summary>
    private void Changed()
    {
        if (reading || stopped) return;

        var current = Current;
        foreach (var row in rows)
            row.Element.Visibility = generator.Shows(current, row.Parameter) ? Visibility.Visible : Visibility.Collapsed;
        foreach (var (heading, under) in headings)
            heading.Visibility = under.Any(r => r.Element.Visibility == Visibility.Visible) ? Visibility.Visible : Visibility.Collapsed;

        failed = null;
        motion = null;

        if (Fault() is not null)
        {
            // Nothing is built from a number that was not typed as meant, and what is on the
            // plate is from the last one that was.
            Describe();
            return;
        }

        if (runner is not null)
        {
            _ = runner.Request(current);
            Describe();
            return;
        }

        try
        {
            Present(current, generator.Make(current, printer));
        }
        catch (Exception ex)
        {
            failed = (current, ex);
            shown = null;
            preview(null, null);
            Describe();
        }
    }

    private void Present(object settings, Generated made)
    {
        if (stopped) return;

        shown = (settings, made);
        preview(settings, made.IsRefused ? null : made);
        Describe();
    }

    /// <summary>The settings and parts the finishing button would use, if there are any to use.</summary>
    private (object Settings, Generated Made)? Insertable()
    {
        if (Fault() is not null || runner?.IsBusy == true) return null;
        if (shown is not { } s || s.Made.IsRefused || s.Made.Parts.Count == 0) return null;

        return Equals(s.Settings, Current) ? s : null;
    }

    private string? Fault()
    {
        var current = Current;
        for (int i = 0; i < faults.Length; i++)
            if (faults[i] is { } fault && generator.Shows(current, generator.Parameters[i]))
                return fault;

        return null;
    }

    /// <summary>
    /// Whether what the panel says under the buttons is why nothing can be made - a number out of
    /// range, settings that do not go together, a build that failed - which is said in red, in the
    /// panel, rather than in a box that has to be dismissed.
    /// </summary>
    private void Say(bool problem)
    {
        summary.Foreground = problem ? Brush(0xFFB3261E) : Brush(0xFF3A3F45);
        summaryBox.Background = problem ? Brush(0xFFFDECEA) : Brushes.White;
        summaryBox.BorderBrush = problem ? Brush(0xFFE4A09A) : Brush(0xFFD5DEEA);
    }

    /// <summary>Whether the panel is saying why nothing can be made, for the tests.</summary>
    internal bool ShowsProblem => summary.Foreground is SolidColorBrush b && b.Color == Color.FromRgb(0xB3, 0x26, 0x1E);

    private void Describe()
    {
        insert.IsEnabled = cut.IsEnabled = Insertable() is not null;
        bool cutOnly = shown?.Made.Parts.Any(p => p.CutOnly) == true;
        insert.Visibility = cutOnly ? Visibility.Collapsed : Visibility.Visible;
        cut.Visibility = context.Target is not null && shown is { } cutting && cutting.Made.Parts.Any(p => p.Cutter)
            ? Visibility.Visible
            : Visibility.Collapsed;
        turn.Visibility = OffersTurning && shown is { Made: { Motion: not null, IsRefused: false } } && Fault() is null
            ? Visibility.Visible
            : Visibility.Collapsed;

        if (Fault() is { } fault)
        {
            summary.Text = fault;
            Say(problem: true);
            return;
        }

        if (failed is { } f)
        {
            summary.Text = $"Something went wrong making it: {f.Error.Message}";
            Say(problem: true);
            return;
        }

        if (shown is not { } s)
        {
            summary.Text = "Working...";
            Say(problem: false);
            return;
        }

        string working = runner?.IsBusy == true ? "Working... " : "";

        if (s.Made.IsRefused)
        {
            summary.Text = working + s.Made.Refusal;
            Say(problem: true);
            return;
        }

        if (cutOnly && context.Target is null)
        {
            summary.Text = "Select the part to cut this into first, then open this again.";
            Say(problem: true);
            return;
        }

        Say(problem: false);

        var lines = new List<string> { working + Size(s.Made) };
        lines.AddRange(generator.Readouts(s.Settings, context.ModelScale));

        var tooBig = s.Made.Parts
            .Select(part => (part.Name, Size: part.Mesh.ComputeBounds().Size))
            .Where(part => part.Size.X > context.PlateWidth || part.Size.Y > context.PlateDepth)
            .ToList();

        foreach (var (name, _) in tooBig)
            lines.Add($"{name} is bigger than the plate ({Length(context.PlateWidth)} x {Length(context.PlateDepth)} {context.Unit.Label}).");

        lines.AddRange(s.Made.Notes);
        if (motion is { } said) lines.Add(said.Text);
        summary.Text = string.Join(Environment.NewLine, lines);

        // A jam is the one thing here that is wrong with what was made rather than with what was asked.
        if (motion is { Problem: true }) Say(problem: true);
    }

    private string Size(Generated made)
    {
        var bounds = made.Parts.Select(p => p.Mesh.ComputeBounds()).ToList();
        var size = new Vector3(bounds.Max(b => b.Size.X), bounds.Max(b => b.Size.Y), bounds.Max(b => b.Size.Z));

        string across = $"{Length(size.X)} x {Length(size.Y)} x {Length(size.Z)} {context.Unit.Label}";
        return made.Parts.Count == 1 ? across : $"{made.Parts.Count} parts, the largest {across}";
    }

    private string Length(float millimetres) =>
        (millimetres / context.Unit.Millimetres).ToString("0.##", CultureInfo.CurrentCulture);

    /// <summary>A value as its box shows it: lengths in the display unit.</summary>
    private string Show(GeneratorParameter p, object value) => p.Kind switch
    {
        ParameterKind.Count => ((int)value).ToString(CultureInfo.CurrentCulture),
        _ when p.IsLength => ((float)value / context.Unit.Millimetres).ToString("0.###", CultureInfo.CurrentCulture),
        _ => ((float)value).ToString("0.###", CultureInfo.CurrentCulture)
    };

    /// <summary>A value as the tooltip says it.</summary>
    private string Say(GeneratorParameter p, object value) => p.Kind switch
    {
        ParameterKind.Choice => p.Choices[IndexOf(p.Choices, value)].Label.ToLower(CultureInfo.CurrentCulture),
        ParameterKind.Toggle => (bool)value ? "on" : "off",
        _ when p.IsLength => $"{Show(p, value)} {context.Unit.Label}",
        _ => p.Unit is null ? Show(p, value) : $"{Show(p, value)} {p.Unit}"
    };

    /// <summary>What is wrong with what was typed, or null with the value in millimetres.</summary>
    private string? Parse(GeneratorParameter p, string text, out object? value)
    {
        value = null;

        if (!Number(text, out double typed)) return $"{p.Label} is not a number.";

        if (p.IsLength) typed *= context.Unit.Millimetres;

        if (p.Kind == ParameterKind.Count && typed != Math.Floor(typed))
            return $"{p.Label} is a whole number.";

        if (!p.InRange(typed))
        {
            string unit = p.IsLength ? context.Unit.Label : p.Unit ?? "";
            double scale = p.IsLength ? context.Unit.Millimetres : 1;
            return $"{p.Label} goes from {p.Min / scale:0.###} to {p.Max / scale:0.###} {unit}".TrimEnd() + ".";
        }

        value = p.Kind == ParameterKind.Count ? (object)(int)typed : (float)typed;
        return null;
    }

    private static bool Number(string text, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value)
        || double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    private static int IndexOf(IReadOnlyList<(object Value, string Label)> choices, object value)
    {
        for (int i = 0; i < choices.Count; i++)
            if (Equals(choices[i].Value, value)) return i;

        return 0;
    }

    private static SolidColorBrush Brush(uint argb) =>
        new(Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb));
}
