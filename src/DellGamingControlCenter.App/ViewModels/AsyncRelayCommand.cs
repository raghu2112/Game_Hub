namespace DellGamingControlCenter.App.ViewModels;

using System;
using System.Threading.Tasks;
using System.Windows.Input;

public class AsyncRelayCommand : ICommand
{
    private readonly Func<object?, Task> _executeAsync;
    private readonly Predicate<object?>? _canExecute;
    private bool _isExecuting;

    public AsyncRelayCommand(Func<object?, Task> executeAsync, Predicate<object?>? canExecute = null)
    {
        _executeAsync = executeAsync ?? throw new ArgumentNullException(nameof(executeAsync));
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged
    {
        add { CommandManager.RequerySuggested += value; }
        remove { CommandManager.RequerySuggested -= value; }
    }

    public void RaiseCanExecuteChanged()
    {
        CommandManager.InvalidateRequerySuggested();
    }

    public bool CanExecute(object? parameter)
    {
        if (_isExecuting) return false;
        return _canExecute == null || _canExecute(parameter);
    }

    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter)) return;

        _isExecuting = true;
        RaiseCanExecuteChanged();

        try
        {
            await _executeAsync(parameter);
        }
        catch (Exception ex)
        {
            // B11: keep the app alive but leave a real diagnostic trail and surface a
            // visible message — the previous Debug-only swallow hid every command failure.
            DellGamingControlCenter.App.App.Logger?.Log($"Command '{_executeAsync.Method.Name}' failed: {ex}", DellGamingControlCenter.Core.Enums.LogSeverity.Error, "UI");
            var mainWindow = System.Windows.Application.Current?.MainWindow;
            if (mainWindow != null)
            {
                System.Windows.MessageBox.Show(mainWindow, $"{ex.Message}", "Action failed", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }
        finally
        {
            _isExecuting = false;
            RaiseCanExecuteChanged();
        }
    }
}
