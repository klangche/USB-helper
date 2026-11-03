using System.Windows.Input;

namespace UsbHelper.App.ViewModels;

/// <summary>Runs <paramref name="execute"/>; when <paramref name="canExecute"/> is given, call <see cref="Refresh"/> after it may have changed.</summary>
public sealed class RelayCommand(Action execute, Func<bool>? canExecute = null) : ICommand
{
    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => canExecute?.Invoke() ?? true;

    public void Execute(object? parameter) => execute();

    public void Refresh() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
