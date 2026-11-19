using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace Finpulse.Desktop.ViewModels;

public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Notify([CallerMemberName] string? property = null) => PropertyChanged?.Invoke(this, new(property));
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? property = null)
    { if (EqualityComparer<T>.Default.Equals(field, value)) return false; field = value; Notify(property); return true; }
}
public sealed class AsyncCommand(Func<Task> execute, Action<Exception> error) : ICommand
{
    private bool _busy;
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => !_busy;
    async void ICommand.Execute(object? parameter) => await ExecuteAsync();
    public async Task ExecuteAsync()
    {
        if (_busy) return;
        _busy = true; CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        try { await execute(); }
        catch (Exception ex) { error(ex); } // UI boundary: keep the form open and report the failure.
        finally { _busy = false; CanExecuteChanged?.Invoke(this, EventArgs.Empty); }
    }
}
public sealed class RelayCommand(Action<object?> execute) : ICommand
{
    public event EventHandler? CanExecuteChanged { add { } remove { } }
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => execute(parameter);
}
public interface IDialogService
{
    bool Confirm(string message);
    string? CsvPath();
}
