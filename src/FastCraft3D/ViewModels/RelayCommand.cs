using System.Windows.Input;

namespace FastCraft3D.ViewModels;

public sealed class RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null) : ICommand
{
    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) => canExecute?.Invoke(parameter) ?? true;

    public void Execute(object? parameter) => execute(parameter);

    public static RelayCommand Simple(Action execute, Func<bool>? canExecute = null) =>
        new(_ => execute(), canExecute is null ? null : _ => canExecute());
}

/// <summary>
/// Command for work that must not block the UI thread - boolean operations on a dense mesh can
/// take seconds. Re-entry is blocked while running so a double click cannot start two.
/// </summary>
public sealed class AsyncRelayCommand(Func<object?, Task> execute, Func<object?, bool>? canExecute = null) : ICommand
{
    private bool running;

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) => !running && (canExecute?.Invoke(parameter) ?? true);

    public async void Execute(object? parameter)
    {
        if (running) return;
        running = true;
        CommandManager.InvalidateRequerySuggested();
        try
        {
            await execute(parameter);
        }
        finally
        {
            running = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public static AsyncRelayCommand Simple(Func<Task> execute, Func<bool>? canExecute = null) =>
        new(_ => execute(), canExecute is null ? null : _ => canExecute());
}
