using System.Windows;
using Finpulse.Application;
using Finpulse.Domain;
using Finpulse.Infrastructure;
using Finpulse.Desktop.ViewModels;

namespace Finpulse.Desktop;

public partial class WorkspaceWindow : Window
{
    private readonly WorkspaceViewModel _viewModel;
    public WorkspaceWindow(IFinanceRepository repo, UserSession user, StorageConfiguration configuration, string path)
    {
        InitializeComponent();
        _viewModel = new(repo, user, new WindowsDialogs(this), configuration, path, Close);
        DataContext = _viewModel;
        Loaded += LoadWorkspace;
    }
    private async void LoadWorkspace(object sender, RoutedEventArgs e)
    {
        try { await _viewModel.RefreshAsync(); }
        catch (Exception ex) { _viewModel.Error(ex); }
    }
}
public sealed class WindowsDialogs(Window owner) : IDialogService
{
    public bool Confirm(string message) => MessageBox.Show(owner, message, "Confirmar eliminación", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;
    public string? CsvPath()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "Archivo CSV (*.csv)|*.csv", DefaultExt = ".csv", FileName = "FINPULSE-reporte.csv" };
        return dialog.ShowDialog(owner) == true ? dialog.FileName : null;
    }
}
