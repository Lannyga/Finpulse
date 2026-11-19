using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using Finpulse.Application;
using Finpulse.Domain;
using Finpulse.Infrastructure;

namespace Finpulse.Desktop.ViewModels;

public sealed class WorkspaceViewModel : Observable
{
    public IFinanceRepository Repository { get; }
    public long UserId { get; }
    public IDialogService Dialogs { get; }
    public FinanceData Data { get; private set; } = new([], [], [], []);
    public decimal Warning { get; }
    public string UserName { get; }
    public string ProviderInfo { get; }
    public string[] Sections { get; } = ["Dashboard", "Ingresos", "Gastos", "Presupuestos", "Metas de ahorro", "Proyecciones", "Reportes", "Configuración"];
    private object? _page;
    public object? Page { get => _page; private set => Set(ref _page, value); }
    private string _section = "Dashboard";
    public string Section { get => _section; set { if (Set(ref _section, value)) Navigate(); } }
    private string _status = "";
    public string Status { get => _status; set => Set(ref _status, value); }
    public ICommand RefreshCommand { get; }
    public ICommand LogoutCommand { get; }
    public WorkspaceViewModel(IFinanceRepository repo, UserSession user, IDialogService dialogs, StorageConfiguration configuration, string databasePath, Action logout)
    {
        Repository = repo; UserId = user.Id; UserName = user.NombreCompleto; Dialogs = dialogs; Warning = configuration.BudgetWarningPercent;
        ProviderInfo = $"{configuration.Provider} · {databasePath}";
        RefreshCommand = new AsyncCommand(RefreshAsync, Error);
        LogoutCommand = new RelayCommand(_ => logout());
    }
    public async Task RefreshAsync()
    { Status = "Cargando datos…"; Data = await Repository.LoadAsync(UserId); Navigate(); Status = "Datos actualizados."; }
    private void Navigate()
    {
        Page = Section switch
        {
            "Ingresos" => new MovementsViewModel(this, MovementKind.Income),
            "Gastos" => new MovementsViewModel(this, MovementKind.Expense),
            "Presupuestos" => new BudgetsViewModel(this),
            "Metas de ahorro" => new GoalsViewModel(this),
            "Proyecciones" => new ProjectionsViewModel(this),
            "Reportes" => new ReportsViewModel(this),
            "Configuración" => new SettingsViewModel(ProviderInfo, Warning),
            _ => new DashboardViewModel(this)
        };
    }
    public void Error(Exception ex)
    {
        Status = ex switch
        {
            ArgumentException => ex.Message,
            ArithmeticException => "El resultado supera el rango numérico admitido. Reduce los montos o las tasas.",
            Microsoft.Data.Sqlite.SqliteException { SqliteErrorCode: 19 } => "No se guardó: revisa duplicados, la categoría o la meta seleccionada.",
            _ => "No fue posible completar la operación. Comprueba el acceso al archivo local y vuelve a intentarlo."
        };
    }
}
public sealed record SettingsViewModel(string ProviderInfo, decimal Warning)
{
    public string Instructions => "El proveedor se selecciona en appsettings.json (SQLite u Oracle), o mediante FINPULSE_PROVIDER. Reinicia la aplicación después del cambio. Oracle conserva registro y acceso; sus módulos financieros aún requieren integración. SQLite no necesita credenciales Oracle.";
    public string Privacy => "Los datos se guardan localmente sin cifrado de la base. Protege tu cuenta de Windows y las copias de seguridad. Las contraseñas se guardan como PBKDF2-SHA256 con sal aleatoria.";
}
public abstract class FinancePage(WorkspaceViewModel owner) : Observable
{
    protected WorkspaceViewModel Owner { get; } = owner;
    protected AsyncCommand Command(Func<Task> action) => new(action, Owner.Error);
    protected static decimal Amount(string text)
    {
        if (!decimal.TryParse(text, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.CurrentCulture, out decimal amount))
            throw new ArgumentException("Introduce un monto válido sin separadores de miles.");
        FinanceRules.ValidateAmount(amount); return amount;
    }
    protected static DateOnly Date(DateTime? date)
    { if (date is null) throw new ArgumentException("Selecciona una fecha válida."); return DateOnly.FromDateTime(date.Value); }
}
public sealed record CategoryRow(string Category, decimal Amount, decimal Percent);
public sealed class MovementsViewModel : FinancePage
{
    private readonly MovementKind _kind;
    public string Title { get; }
    public string[] Categories { get; }
    public string[] FilterCategories { get; }
    public ObservableCollection<Movement> Rows { get; } = [];
    private Movement? _selected;
    public Movement? Selected { get => _selected; set { Set(ref _selected, value); if (value is not null) { EditAmount = value.Amount.ToString(CultureInfo.CurrentCulture); Category = value.Category; EditDate = value.Date.ToDateTime(TimeOnly.MinValue); Description = value.Description; NotifyFields(); } } }
    public string EditAmount { get; set; } = "";
    public string Category { get; set; }
    public DateTime? EditDate { get; set; } = DateTime.Today;
    public string Description { get; set; } = "";
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public string FilterCategory { get; set; } = "Todas";
    public string ListInfo => Rows.Count == 0 ? "No hay movimientos en este período." : $"{Rows.Count} movimientos · Total Bs {Rows.Sum(x => x.Amount):N2}";
    public ICommand FilterCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand NewCommand { get; }
    public MovementsViewModel(WorkspaceViewModel owner, MovementKind kind) : base(owner)
    {
        _kind = kind; Title = kind == MovementKind.Income ? "Ingresos" : "Gastos";
        Categories = kind == MovementKind.Income ? FinanceRules.IncomeCategories : FinanceRules.ExpenseCategories;
        FilterCategories = ["Todas", .. Categories]; Category = Categories[0];
        FilterCommand = Command(() => { Filter(); return Task.CompletedTask; });
        NewCommand = new RelayCommand(_ => Reset());
        SaveCommand = Command(async () => { await owner.Repository.SaveMovementAsync(owner.UserId, new(Selected?.Id ?? 0, _kind, Amount(EditAmount), Category, Date(EditDate), Description)); await owner.RefreshAsync(); owner.Status = "Movimiento guardado."; });
        DeleteCommand = Command(async () => { if (Selected is null) throw new ArgumentException("Selecciona un movimiento."); if (owner.Dialogs.Confirm("¿Eliminar el movimiento seleccionado?")) { await owner.Repository.DeleteMovementAsync(owner.UserId, Selected.Id); await owner.RefreshAsync(); } });
        Filter();
    }
    private void Reset() { _selected = null; EditAmount = ""; Category = Categories[0]; EditDate = DateTime.Today; Description = ""; Notify(nameof(Selected)); NotifyFields(); }
    private void NotifyFields() { Notify(nameof(EditAmount)); Notify(nameof(Category)); Notify(nameof(EditDate)); Notify(nameof(Description)); }
    private void Filter()
    {
        if (From > To) throw new ArgumentException("La fecha inicial debe ser anterior a la final.");
        Rows.Clear();
        foreach (var m in Owner.Data.Movements.Where(m => m.Kind == _kind && (From is null || m.Date >= Date(From)) && (To is null || m.Date <= Date(To)) && (FilterCategory == "Todas" || m.Category == FilterCategory))) Rows.Add(m);
        Notify(nameof(ListInfo));
    }
}
public sealed record BudgetRow(Budget Source, decimal Spent, decimal Available, decimal Percent, string Status)
{
    public string Month => Source.Month; public string Category => Source.Category; public decimal Amount => Source.Amount;
    public bool IsWarning => Status == "Atención al límite";
    public bool IsExceeded => Percent >= 100;
}
public sealed class BudgetsViewModel : FinancePage
{
    public string[] Categories { get; } = ["General", .. FinanceRules.ExpenseCategories];
    public IReadOnlyList<BudgetRow> Rows { get; }
    private BudgetRow? _selected;
    public BudgetRow? Selected { get => _selected; set { Set(ref _selected, value); if (value is not null) { Month = value.Month; Category = value.Category; EditAmount = value.Amount.ToString(CultureInfo.CurrentCulture); Notify(nameof(Month)); Notify(nameof(Category)); Notify(nameof(EditAmount)); } } }
    public string Month { get; set; } = DateTime.Today.ToString("yyyy-MM");
    public string Category { get; set; } = "General";
    public string EditAmount { get; set; } = "";
    public ICommand SaveCommand { get; }
    public ICommand NewCommand { get; }
    public ICommand DeleteCommand { get; }
    public BudgetsViewModel(WorkspaceViewModel owner) : base(owner)
    {
        Rows = owner.Data.Budgets.Select(b => { var usage = FinanceRules.Evaluate(b, owner.Data.Movements, owner.Warning); return new BudgetRow(b, usage.Spent, usage.Available, usage.Percent, usage.Status); }).ToArray();
        NewCommand = new RelayCommand(_ => { _selected = null; EditAmount = ""; Notify(nameof(Selected)); Notify(nameof(EditAmount)); });
        SaveCommand = Command(async () => { await owner.Repository.SaveBudgetAsync(owner.UserId, new(Selected?.Source.Id ?? 0, Month, Category, Amount(EditAmount))); await owner.RefreshAsync(); owner.Status = "Presupuesto guardado."; });
        DeleteCommand = Command(async () => { if (Selected is null) throw new ArgumentException("Selecciona un presupuesto."); if (owner.Dialogs.Confirm("¿Eliminar el presupuesto?")) { await owner.Repository.DeleteBudgetAsync(owner.UserId, Selected.Source.Id); await owner.RefreshAsync(); } });
    }
}
public sealed record GoalRow(SavingGoal Source, decimal Saved)
{
    public long Id => Source.Id; public string Name => Source.Name; public decimal Target => Source.Target;
    public DateOnly Deadline => Source.Deadline; public decimal Percent => Saved / Target * 100;
    public string Status => Saved >= Target ? "Cumplida" : Deadline < DateOnly.FromDateTime(DateTime.Today) ? "Vencida" : "En curso";
}
public sealed class GoalsViewModel : FinancePage
{
    public ICommand ProjectionCommand { get; }
    public IReadOnlyList<GoalRow> Rows { get; }
    private GoalRow? _selected;
    public GoalRow? Selected { get => _selected; set { Set(ref _selected, value); if (value is not null) { Name = value.Name; Description = value.Source.Description; EditAmount = value.Target.ToString(CultureInfo.CurrentCulture); Deadline = value.Deadline.ToDateTime(TimeOnly.MinValue); foreach (var p in new[] { nameof(Name), nameof(Description), nameof(EditAmount), nameof(Deadline), nameof(Contributions) }) Notify(p); SelectedContribution = null; } } }
    public IReadOnlyList<Contribution> Contributions => Owner.Data.Contributions.Where(c => c.GoalId == Selected?.Id).ToArray();
    private Contribution? _selectedContribution;
    public Contribution? SelectedContribution { get => _selectedContribution; set { Set(ref _selectedContribution, value); ContributionAmount = value?.Amount.ToString(CultureInfo.CurrentCulture) ?? ""; ContributionDate = value?.Date.ToDateTime(TimeOnly.MinValue) ?? DateTime.Today; Notify(nameof(ContributionAmount)); Notify(nameof(ContributionDate)); } }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string EditAmount { get; set; } = "";
    public DateTime? Deadline { get; set; } = DateTime.Today.AddMonths(6);
    public string ContributionAmount { get; set; } = "";
    public DateTime? ContributionDate { get; set; } = DateTime.Today;
    public string AvailableText => $"Saldo libre para reservar: Bs {FinanceRules.Summarize(Owner.Data, DateOnly.FromDateTime(DateTime.Today)).Available:N2}. Los aportes reservan saldo; no crean ingresos ni gastos.";
    public ICommand SaveCommand { get; }
    public ICommand NewCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand ContributeCommand { get; }
    public ICommand NewContributionCommand { get; }
    public ICommand DeleteContributionCommand { get; }
    public GoalsViewModel(WorkspaceViewModel owner) : base(owner)
    {
        ProjectionCommand = new RelayCommand(_ => owner.Section = "Proyecciones");
        Rows = owner.Data.Goals.Select(g => new GoalRow(g, owner.Data.Contributions.Where(c => c.GoalId == g.Id).Sum(c => c.Amount))).ToArray();
        NewCommand = new RelayCommand(_ => { owner.Section = "Dashboard"; owner.Section = "Metas de ahorro"; });
        SaveCommand = Command(async () => { await owner.Repository.SaveGoalAsync(owner.UserId, new(Selected?.Id ?? 0, Name, Description, Amount(EditAmount), Date(Deadline))); await owner.RefreshAsync(); owner.Status = "Meta guardada."; });
        DeleteCommand = Command(async () => { if (Selected is null) throw new ArgumentException("Selecciona una meta."); if (owner.Dialogs.Confirm("¿Eliminar la meta y liberar todos sus aportes?")) { await owner.Repository.DeleteGoalAsync(owner.UserId, Selected.Id); await owner.RefreshAsync(); } });
        NewContributionCommand = new RelayCommand(_ => SelectedContribution = null);
        ContributeCommand = Command(async () => { if (Selected is null) throw new ArgumentException("Selecciona una meta guardada."); await owner.Repository.SaveContributionAsync(owner.UserId, new(SelectedContribution?.Id ?? 0, Selected.Id, Amount(ContributionAmount), Date(ContributionDate))); await owner.RefreshAsync(); owner.Status = "Aporte guardado. Selecciona la meta para consultar sus aportes."; });
        DeleteContributionCommand = Command(async () => { if (SelectedContribution is null) throw new ArgumentException("Selecciona un aporte."); if (owner.Dialogs.Confirm("¿Eliminar el aporte y liberar el saldo reservado?")) { await owner.Repository.DeleteContributionAsync(owner.UserId, SelectedContribution.Id); await owner.RefreshAsync(); } });
    }
}
public sealed class ReportsViewModel : FinancePage
{
    public DateTime? From { get; set; } = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
    public DateTime? To { get; set; } = DateTime.Today;
    public ObservableCollection<Movement> Rows { get; } = [];
    public string Totals { get; private set; } = "";
    public ICommand FilterCommand { get; }
    public ICommand ExportCommand { get; }
    public ReportsViewModel(WorkspaceViewModel owner) : base(owner)
    {
        FilterCommand = Command(() => { Filter(); return Task.CompletedTask; });
        ExportCommand = Command(async () =>
        {
            Filter(); string? path = owner.Dialogs.CsvPath(); if (path is null) return;
            static string Cell(string s) { if (s.Length > 0 && "=+-@\t\r".Contains(s[0])) s = "'" + s; return "\"" + s.Replace("\"", "\"\"") + "\""; }
            var lines = new[] { "Fecha;Tipo;Categoria;Monto;Descripcion" }.Concat(Rows.Select(m => string.Join(";", Cell(m.Date.ToString("yyyy-MM-dd")), Cell(m.Kind == MovementKind.Income ? "Ingreso" : "Gasto"), Cell(m.Category), Cell(m.Amount.ToString(CultureInfo.InvariantCulture)), Cell(m.Description))));
            await System.IO.File.WriteAllLinesAsync(path, lines, new System.Text.UTF8Encoding(true)); owner.Status = "Reporte CSV exportado.";
        });
        Filter();
    }
    private void Filter()
    {
        var from = Date(From); var to = Date(To); if (from > to) throw new ArgumentException("El período no es válido.");
        Rows.Clear(); foreach (var m in Owner.Data.Movements.Where(m => m.Date >= from && m.Date <= to)) Rows.Add(m);
        decimal income = Rows.Where(m => m.Kind == MovementKind.Income).Sum(m => m.Amount), expense = Rows.Where(m => m.Kind == MovementKind.Expense).Sum(m => m.Amount);
        Totals = $"Ingresos Bs {income:N2} · Gastos Bs {expense:N2} · Ahorro Bs {income - expense:N2}"; Notify(nameof(Totals));
    }
}
