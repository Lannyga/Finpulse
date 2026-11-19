using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Finpulse.Application;
using Finpulse.Desktop;
using Finpulse.Desktop.ViewModels;
using Finpulse.Desktop.Controls;
using Finpulse.Domain;
using Finpulse.Infrastructure;

internal static class DesktopChecks
{
    public static void Run()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
            string folder = Path.Combine(Path.GetTempPath(), "Finpulse-ui-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            var app = new System.Windows.Application();
            app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/Finpulse.Desktop;component/Themes/Theme.xaml", UriKind.Relative) });
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            app.Dispatcher.InvokeAsync(async () =>
            {
                var listener = new BindingListener(); PresentationTraceSources.DataBindingSource.Listeners.Add(listener);
                PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;
                try
                {
                    var store = new SqliteStore(Path.Combine(folder, "ui.db"));
                    await store.CreateAsync("Prueba visual", "visual@example.test", new Pbkdf2PasswordHasher().HashPassword(Guid.NewGuid().ToString("N")));
                    var data = new SqliteStore(Path.Combine(folder, "ui.db"));
                    var today = DateOnly.FromDateTime(DateTime.Today);
                    await data.SaveMovementAsync(1, new(0, MovementKind.Income, 1500, "Beca", today, "Dato exclusivo de prueba"));
                    await data.SaveMovementAsync(1, new(0, MovementKind.Expense, 420, "Alimentación", today, "Dato exclusivo de prueba"));
                    await data.SaveBudgetAsync(1, new(0, today.ToString("yyyy-MM"), "General", 500));
                    await data.SaveGoalAsync(1, new(0, "Computadora de prueba", "Prueba visual aislada", 3000, today.AddYears(1)));
                    var window = new WorkspaceWindow(data, new(1, "Prueba visual", "visual@example.test"), new(), data.DatabasePath);
                    var vm = (WorkspaceViewModel)window.DataContext;
                    await vm.RefreshAsync();
                    string output = ArtifactDirectory(); Directory.CreateDirectory(output);
                    Console.WriteLine("Artefactos WPF: " + output);
                    foreach (var section in vm.Sections)
                    {
                        vm.Section = section;
                        if (vm.Page is MovementsViewModel movements) movements.Selected = movements.Rows.FirstOrDefault();
                        if (vm.Page is BudgetsViewModel budgets) budgets.Selected = budgets.Rows.FirstOrDefault();
                        if (vm.Page is GoalsViewModel goals) goals.Selected = goals.Rows.FirstOrDefault();
                        if (vm.Page is ProjectionsViewModel projection) await ((AsyncCommand)projection.CalculateCommand).ExecuteAsync();
                        var content = (FrameworkElement)window.Content;
                        content.Measure(new Size(1320, 820)); content.Arrange(new Rect(0, 0, 1320, 820)); content.UpdateLayout();
                        await app.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                        content.UpdateLayout();
                        var bitmap = new RenderTargetBitmap(1320, 820, 96, 96, PixelFormats.Pbgra32); bitmap.Render(content);
                        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                        using var file = File.Create(Path.Combine(output, section + ".png")); encoder.Save(file);
                        if (listener.Errors.Count > 0) throw new InvalidOperationException(string.Join("\n", listener.Errors));
                        Console.WriteLine("WPF OK: " + section);
                    }
                    // Exercise actual ViewModel commands against temporary SQLite.
                    vm.Section = "Ingresos";
                    var edit = (MovementsViewModel)vm.Page!;
                    edit.EditAmount = "250"; edit.Category = "Trabajo"; edit.Description = "=1+1";
                    await ((AsyncCommand)edit.SaveCommand).ExecuteAsync();
                    if (vm.Data.Movements.Count != 3) throw new InvalidOperationException("Guardar desde ViewModel");
                    vm.Section = "Gastos"; var filter = (MovementsViewModel)vm.Page!;
                    filter.FilterCategory = "Salud"; await ((AsyncCommand)filter.FilterCommand).ExecuteAsync();
                    if (filter.Rows.Count != 0) throw new InvalidOperationException("Filtro de categoría");
                    vm.Section = "Metas de ahorro"; var goalsVm = (GoalsViewModel)vm.Page!; goalsVm.Selected = goalsVm.Rows.Single(); goalsVm.ContributionAmount = "100";
                    await ((AsyncCommand)goalsVm.ContributeCommand).ExecuteAsync();
                    if (vm.Data.Contributions.Count != 1) throw new InvalidOperationException("Guardar aporte desde ViewModel");
                    var dialogs = new TestDialogs(Path.Combine(folder, "reporte.csv"));
                    var reportOwner = new WorkspaceViewModel(data, new(1, "Prueba", "visual@example.test"), dialogs, new(), data.DatabasePath, () => { });
                    await reportOwner.RefreshAsync(); reportOwner.Section = "Reportes";
                    var reports = (ReportsViewModel)reportOwner.Page!;
                    await ((AsyncCommand)reports.ExportCommand).ExecuteAsync();
                    if (!File.Exists(dialogs.Path) || !File.ReadAllText(dialogs.Path).Contains("'=1+1")) throw new InvalidOperationException("CSV y neutralización de fórmulas");
                    reportOwner.Section = "Ingresos"; var delete = (MovementsViewModel)reportOwner.Page!;
                    delete.Selected = delete.Rows.First();
                    await ((AsyncCommand)delete.DeleteCommand).ExecuteAsync();
                    if (reportOwner.Data.Movements.Count != 3) throw new InvalidOperationException("Cancelar eliminación");
                    dialogs.Accept = true; await ((AsyncCommand)delete.DeleteCommand).ExecuteAsync();
                    if (reportOwner.Data.Movements.Count != 2) throw new InvalidOperationException("Confirmar eliminación");
                    // New analysis receives only the authenticated owner's repository snapshot.
                    vm.Section = "Proyecciones"; await vm.RefreshAsync();
                    var live = (ProjectionsViewModel)vm.Page!;
                    live.InitialCapital = "0"; live.Income = "1200"; live.Expense = "850"; live.Months = "60";
                    if (live.HasErrors || live.Rows[^1].Balance != 21000 || live.Chart.Count != 3) throw new InvalidOperationException("Actualización dinámica del caso solicitado");
                    live.ScenarioInputs[0].ExpenseChange = "5"; live.ScenarioInputs[2].IncomeChange = "5";
                    if (!(live.ConservativeResult?.Nominal < 21000 && live.OptimisticResult?.Nominal > 21000)) throw new InvalidOperationException("Comparación de tres escenarios");
                    live.ExpenseReduction = "50";
                    if (live.BaseResult?.Nominal != 24000) throw new InvalidOperationException("Impacto de reducir gastos");
                    live.ExpenseReduction = "0"; live.AnnualView = true;
                    if (live.Rows.Count != 5 || live.Chart[0].Values.Count != 6) throw new InvalidOperationException("Agregación anual");
                    live.RealValues = true; live.ScenarioInputs[1].Inflation = "10";
                    if (!(live.BaseResult?.Real < live.BaseResult?.Nominal)) throw new InvalidOperationException("Poder adquisitivo dinámico");
                    live.Months = "0";
                    if (!live.HasErrors || live.Rows.Count != 0 || live.Chart.Count != 0) throw new InvalidOperationException("Validación sin resultados obsoletos");
                    live.Months = "17";
                    if (live.HasErrors || live.Rows.Count != 2) throw new InvalidOperationException("Recuperación del formulario y plazo personalizado");
                    live.Months = "60"; live.AnnualView = false; live.RealValues = false;
                    var content2 = (FrameworkElement)window.Content;
                    content2.Measure(new Size(1320, 820)); content2.Arrange(new Rect(0,0,1320,820)); content2.UpdateLayout();
                    await app.Dispatcher.InvokeAsync(() => {}, DispatcherPriority.ApplicationIdle);
                    var chart = FindCharts(content2).Single(); chart.SelectPoint(60);
                    if (!chart.SelectedText.Contains("Base") || !chart.SelectedText.Contains("Optimista")) throw new InvalidOperationException("Consulta de puntos del gráfico");
                    SaveImage(content2, Path.Combine(output,"Etapa4-Proyecciones.png"),1320,820);
                    SaveImage(chart,Path.Combine(output,"Etapa4-Escenarios-Lineas.png"),(int)Math.Ceiling(chart.ActualWidth),(int)Math.Ceiling(chart.ActualHeight));
                    content2.Measure(new Size(1000, 680)); content2.Arrange(new Rect(0,0,1000,680)); content2.UpdateLayout();
                    SaveImage(content2,Path.Combine(output,"Etapa4-Proyecciones-1000.png"),1000,680);
                    var before = (await data.LoadAsync(1)).Movements.Count;
                    await data.CreateAsync("Otro usuario", "otro@example.test",new Pbkdf2PasswordHasher().HashPassword(Guid.NewGuid().ToString("N")));
                    var second = new WorkspaceViewModel(data,new(2,"Otro usuario","otro@example.test"),dialogs,new(),data.DatabasePath,()=>{});
                    await second.RefreshAsync(); second.Section="Proyecciones";
                    if (((ProjectionsViewModel)second.Page!).Results.Any(r=>r.Nominal != 0)) throw new InvalidOperationException("Aislamiento de las proyecciones");
                    if ((await data.LoadAsync(1)).Movements.Count != before) throw new InvalidOperationException("Simular no modifica transacciones");
                    // A six-month history fixture makes both improvement and deterioration visible.
                    for(int month=1;month<=6;month++)
                    {
                        await data.SaveMovementAsync(1,new(0,MovementKind.Income,1200,"Trabajo",today.AddMonths(-month),"Historial exclusivo de QA"));
                        await data.SaveMovementAsync(1,new(0,MovementKind.Expense,month%2==0?700:1000,"Educación",today.AddMonths(-month),"Historial exclusivo de QA"));
                    }
                    vm.Section="Dashboard"; await vm.RefreshAsync();
                    var dash=(DashboardViewModel)vm.Page!; dash.Range=6; dash.Metric="Ingresos y gastos";
                    if(dash.Chart.Count!=2 || dash.Chart[0].Values.Count!=6) throw new InvalidOperationException("Filtro de historial dinámico");
                    dash.Metric="Saldo y ahorro";
                    if(dash.History[^2].Income!=1200) throw new InvalidOperationException("Historial registrado");
                    content2.Measure(new Size(1320,820));content2.Arrange(new Rect(0,0,1320,820));content2.UpdateLayout();
                    await app.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
                    chart=FindCharts(content2).Single();chart.SelectPoint(5);
                    SaveImage(content2,Path.Combine(output,"Etapa4-Dashboard.png"),1320,820);
                    SaveImage(chart,Path.Combine(output,"Etapa4-Historial-Lineas.png"),(int)Math.Ceiling(chart.ActualWidth),(int)Math.Ceiling(chart.ActualHeight));
                    vm.Section="Proyecciones";
                    if(!((ProjectionsViewModel)vm.Page!).BaselineMessage.Contains("Promedio")) throw new InvalidOperationException("Carga automática de historial suficiente");
                    if(listener.Errors.Count>0)throw new InvalidOperationException(string.Join("\n",listener.Errors));
                    Console.WriteLine("OK: 14 comprobaciones adicionales de actualización dinámica, escenarios, inflación, validación, gráficos, historial y aislamiento.");
                    window.Close();
                    Console.WriteLine("OK: 8 vistas WPF renderizadas, 0 errores de binding; 6 operaciones de ViewModel comprobadas (incluye CSV y confirmación de eliminación).");
                }
                catch (Exception ex) { failure = ex; }
                finally
                {
                    PresentationTraceSources.DataBindingSource.Listeners.Remove(listener);
                    try { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); Directory.Delete(folder, true); }
                    catch (Exception cleanupError) { failure = failure is null ? cleanupError : new AggregateException(failure, cleanupError); }
                    finally { app.Shutdown(); }
                }
            });
            app.Run();
            }
            catch (Exception startupError) { failure = startupError; }
        });
        thread.IsBackground = true;
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        if (!thread.Join(TimeSpan.FromMinutes(2))) throw new TimeoutException("Las pruebas WPF no terminaron en dos minutos.");
        if (failure is not null) throw new InvalidOperationException("Validación WPF fallida", failure);
    }
    private sealed class BindingListener : TraceListener
    {
        public List<string> Errors { get; } = [];
        public override void Write(string? message) { if (message is not null) Errors.Add(message); }
        public override void WriteLine(string? message) { if (message is not null) Errors.Add(message); }
    }
    private static IEnumerable<FinancialLineChart> FindCharts(DependencyObject root)
    {
        if(root is FinancialLineChart chart) yield return chart;
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)
            foreach(var child in FindCharts(VisualTreeHelper.GetChild(root,i)))yield return child;
    }
    private static string ArtifactDirectory()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "tests", "Finpulse.Smoke", "Finpulse.Smoke.csproj")))
                return Path.Combine(dir.FullName, "docs", "qa");
        return Path.Combine(Path.GetTempPath(), "Finpulse-QA-artifacts-" + Guid.NewGuid().ToString("N"));
    }
    private static void SaveImage(Visual root,string path,int width,int height)
    {
        var bitmap=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);bitmap.Render(root);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(path);encoder.Save(file);
    }
    private sealed class TestDialogs(string path) : IDialogService
    {
        public string Path { get; } = path;
        public bool Accept { get; set; }
        public bool Confirm(string message) => Accept;
        public string? CsvPath() => Path;
    }
}
