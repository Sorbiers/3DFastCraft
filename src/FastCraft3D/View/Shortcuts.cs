using System.Windows.Input;

namespace FastCraft3D.View;

/// <summary>What a key does that the window does itself, rather than a view-model command.</summary>
public enum KeyAction
{
    None,
    Move,
    Rotate,
    Resize,
    OneWayOnly,
    StopOnContact,
    NudgeLeft,
    NudgeRight,
    NudgeBack,
    NudgeForward,
    NudgeUp,
    NudgeDown,
    ZoomToFit,
    ViewTop,
    ViewFront,
    ViewRight,
    ViewIsometric,
    HideSelection,
    ShowAll,
    LockSelection,
    UnlockAll,
    ShowShortcuts
}

/// <param name="Group">The heading it is listed under.</param>
/// <param name="What">What it does, as the list says it.</param>
/// <param name="Key">The key, or <see cref="Key.None"/> for something that is not a key of its own - a drag, a modifier held during one.</param>
/// <param name="Modifiers">Held with the key.</param>
/// <param name="Command">The view model's command it runs, by property name.</param>
/// <param name="Parameter">Handed to that command.</param>
/// <param name="Action">What the window does with it instead, when there is no command.</param>
/// <param name="Text">How to write it when it is not a key, or not only one.</param>
/// <param name="Advanced">For a tool only Advanced mode shows: left off the Classic list, though the key still works.</param>
public sealed record Shortcut(
    string Group, string What, Key Key = Key.None, ModifierKeys Modifiers = ModifierKeys.None,
    string? Command = null, object? Parameter = null, KeyAction Action = KeyAction.None, string? Text = null,
    bool Advanced = false)
{
    /// <summary>The keys as they are written in the list: "Ctrl+Shift+S", "Del", "Page Up".</summary>
    public string Keys => Text ?? Shortcuts.Describe(Key, Modifiers);
}

public sealed record ShortcutGroup(string Name, IReadOnlyList<Shortcut> Items);

/// <summary>
/// Every key the app answers to, and the drags and held keys worth knowing, in one table.
///
/// The window binds its keys from here, and F1 lists them from here, so the list cannot say a key
/// does something it does not. Before this they were a block of key bindings in the window's
/// markup, a switch in its code and a word or two in some tooltips, and the only complete list was
/// reading all three.
/// </summary>
public static class Shortcuts
{
    private const ModifierKeys Ctrl = ModifierKeys.Control;
    private const ModifierKeys Shift = ModifierKeys.Shift;

    public static IReadOnlyList<Shortcut> All { get; } =
    [
        new("File", "Start an empty plate", Key.N, Ctrl, "NewCommand"),
        new("File", "Open a project", Key.O, Ctrl, "OpenCommand"),
        new("File", "Save", Key.S, Ctrl, "SaveCommand"),
        new("File", "Save as", Key.S, Ctrl | Shift, "SaveAsCommand"),
        new("File", "Import", Key.I, Ctrl, "ImportCommand"),
        new("File", "Export", Key.E, Ctrl, "ExportCommand"),
        new("File", "Blueprint: three views with dimensions, to print", Key.P, Ctrl, "PrintDrawingCommand", Advanced: true),

        new("Edit", "Undo", Key.Z, Ctrl, "UndoCommand"),
        new("Edit", "Redo", Key.Y, Ctrl, "RedoCommand"),
        new("Edit", "Copy", Key.C, Ctrl, "CopyCommand"),
        new("Edit", "Cut", Key.X, Ctrl, "CutCommand"),
        new("Edit", "Paste, where it came from", Key.V, Ctrl, "PasteCommand"),
        new("Edit", "Duplicate beside", Key.D, Ctrl, "DuplicateCommand"),
        new("Edit", "Duplicate in place", Key.D, Ctrl | Shift, "DuplicateCommand", "InPlace", Advanced: true),
        new("Edit", "Delete", Key.Delete, Command: "DeleteCommand"),
        new("Edit", "Select everything", Key.A, Ctrl, "SelectAllCommand"),
        new("Edit", "Select nothing", Key.A, Ctrl | Shift, "DeselectAllCommand"),
        new("Edit", "Group", Key.G, Ctrl, "GroupCommand"),
        new("Edit", "Ungroup", Key.G, Ctrl | Shift, "UngroupCommand"),
        new("Edit", "Hide the selection", Key.H, Action: KeyAction.HideSelection),
        new("Edit", "Show everything hidden", Key.H, ModifierKeys.Alt, Action: KeyAction.ShowAll),
        new("Edit", "Lock the selection, so nothing can select or change it", Key.L, Action: KeyAction.LockSelection),
        new("Edit", "Unlock everything", Key.L, ModifierKeys.Alt, Action: KeyAction.UnlockAll),
        new("Edit", "Repeat the last tool, with what it was last given", Key.Space, Ctrl, "RepeatLastCommand"),

        new("Move, rotate, resize", "Move handles", Key.M, Action: KeyAction.Move),
        new("Move, rotate, resize", "Rotate handles", Key.R, Action: KeyAction.Rotate),
        new("Move, rotate, resize", "Resize handles", Key.S, Action: KeyAction.Resize),
        new("Move, rotate, resize", "One way only, when resizing", Key.O, Action: KeyAction.OneWayOnly),
        new("Move, rotate, resize", "Stop on contact, when moving", Key.C, Action: KeyAction.StopOnContact),
        new("Move, rotate, resize", "Nudge left, along -X", Key.Left, Action: KeyAction.NudgeLeft),
        new("Move, rotate, resize", "Nudge right, along +X", Key.Right, Action: KeyAction.NudgeRight),
        new("Move, rotate, resize", "Nudge back, along +Y", Key.Up, Action: KeyAction.NudgeBack),
        new("Move, rotate, resize", "Nudge forward, along -Y", Key.Down, Action: KeyAction.NudgeForward),
        new("Move, rotate, resize", "Nudge up, along +Z", Key.PageUp, Action: KeyAction.NudgeUp),
        new("Move, rotate, resize", "Nudge down, along -Z", Key.PageDown, Action: KeyAction.NudgeDown),
        new("Move, rotate, resize", "A nudge is the snap step, or 1 mm with snap off; Shift makes it ten", Text: "Shift"),
        new("Move, rotate, resize", "Put down the tool in hand", Key.Escape, Action: KeyAction.None, Text: "Esc"),

        new("Sketching", "Close the outline being drawn", Text: "Enter", Advanced: true),
        new("Sketching", "Take back the last point, or the last outline", Text: "Backspace", Advanced: true),
        new("Sketching", "Drop the outline being drawn; again, leave the sketch", Text: "Esc", Advanced: true),

        new("Dragging a resize handle", "Keep proportions, the other way to the button", Text: "Ctrl"),
        new("Dragging a resize handle", "One way only, the other way to the button", Text: "Alt"),
        new("Dragging a resize handle", "Snap the size to whole millimetres", Text: "Shift"),

        new("View", "Zoom to fit", Key.F, Action: KeyAction.ZoomToFit),
        new("View", "Look from the top", Key.D1, Action: KeyAction.ViewTop),
        new("View", "Look from the front", Key.D2, Action: KeyAction.ViewFront),
        new("View", "Look from the right", Key.D3, Action: KeyAction.ViewRight),
        new("View", "Isometric", Key.D4, Action: KeyAction.ViewIsometric),
        new("View", "Orbit", Text: "Left drag"),
        new("View", "Pan", Text: "Right drag"),
        new("View", "Zoom", Text: "Wheel"),
        new("View", "This list", Key.F1, Action: KeyAction.ShowShortcuts),

        new("Number boxes", "Apply what is typed", Text: "Enter"),
        new("Number boxes", "Step by one unit; Shift for ten, Ctrl for a tenth", Text: "Up / Down"),
        new("Number boxes", "Change by an amount instead of to it", Text: "+=5   -=5")
    ];

    /// <summary>The table under its headings, in the order it is written.</summary>
    public static IReadOnlyList<ShortcutGroup> Groups { get; } =
        All.GroupBy(s => s.Group).Select(g => new ShortcutGroup(g.Key, g.ToList())).ToList();

    /// <summary>The same without the entries for tools only Advanced mode shows.</summary>
    public static IReadOnlyList<ShortcutGroup> ClassicGroups { get; } =
        All.Where(s => !s.Advanced).GroupBy(s => s.Group).Select(g => new ShortcutGroup(g.Key, g.ToList())).ToList();

    /// <summary>The entries a key press should run: those bound to a key, not the ones only described.</summary>
    public static IEnumerable<Shortcut> Keyed => All.Where(s => s.Key != Key.None && (s.Command is not null || s.Action != KeyAction.None));

    /// <summary>
    /// What the window should do for a key it was given, or <see cref="KeyAction.None"/>. The number
    /// pad's digits count as the row above the letters; Shift is let through on a nudge, which it
    /// makes ten times as long.
    /// </summary>
    public static KeyAction ActionFor(Key key, ModifierKeys modifiers)
    {
        key = key switch
        {
            Key.NumPad1 => Key.D1,
            Key.NumPad2 => Key.D2,
            Key.NumPad3 => Key.D3,
            Key.NumPad4 => Key.D4,
            _ => key
        };

        foreach (var s in Keyed)
        {
            if (s.Action == KeyAction.None || s.Key != key) continue;
            if (s.Modifiers == modifiers) return s.Action;
            if (modifiers == Shift && s.Modifiers == ModifierKeys.None && IsNudge(s.Action)) return s.Action;
        }

        return KeyAction.None;
    }

    public static bool IsNudge(KeyAction action) => action is >= KeyAction.NudgeLeft and <= KeyAction.NudgeDown;

    public static string Describe(Key key, ModifierKeys modifiers)
    {
        var parts = new List<string>();
        if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");

        parts.Add(key switch
        {
            >= Key.D0 and <= Key.D9 => ((char)('0' + (key - Key.D0))).ToString(),
            Key.Delete => "Del",
            Key.Escape => "Esc",
            Key.PageUp => "Page Up",
            Key.PageDown => "Page Down",
            Key.Left => "Left",
            Key.Right => "Right",
            Key.Up => "Up",
            Key.Down => "Down",
            Key.None => "",
            _ => key.ToString()
        });

        return string.Join("+", parts);
    }
}
