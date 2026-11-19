using System.Globalization;
using System.Windows.Input;
using Finpulse.Application;
using Finpulse.Domain;
using Finpulse.Desktop.Controls;

namespace Finpulse.Desktop.ViewModels;

public sealed class DashboardViewModel : FinancePage
{
    private readonly FinancialAnalysisService _analysis = new();
    private int _range = 12;
    private string _metric = "Saldo y ahorro";
    public int[] Ranges { get; } = [6, 12, 24];
    public string[] Metrics { get; } = ["Saldo y ahorro", "Ingresos y gastos", "Trabajo e ingresos"];
    public int Range { get => _range; set { if (Set(ref _range, value)) UpdateChart(); } }
    public string Metric { get => _metric; set { if (Set(ref _metric, value)) UpdateChart(); } }
    public FinancialSummary Summary { get; }
    public decimal Salary { get; }
    public string Period { get; } = DateTime.Today.ToString("MMMM yyyy", CultureInfo.GetCultureInfo("es-BO"));
    public IReadOnlyList<Movement> Recent { get; }
    public IReadOnlyList<CategoryRow> Categories { get; }
    public IReadOnlyList<HistoryMonth> History { get; private set; } = [];
    public IReadOnlyList<ChartSeries> Chart { get; private set; } = [];
    public IReadOnlyList<FinancialAdvice> Advice { get; }
    public string Trend { get; private set; } = "";
    public string EmptyMessage { get; }
    public ICommand AddCommand { get; }
    public ICommand ProjectCommand { get; }
    public DashboardViewModel(WorkspaceViewModel owner) : base(owner)
    {
        var today = DateOnly.FromDateTime(DateTime.Today); Summary = FinanceRules.Summarize(owner.Data, today);
        Recent = owner.Data.Movements.Where(m => m.Date <= today).Take(10).ToArray();
        Salary = owner.Data.Movements.Where(m => m.Date <= today && m.Date.Year == today.Year && m.Date.Month == today.Month && m.Kind == MovementKind.Income && m.Category == "Trabajo").Sum(m => m.Amount);
        var expenses = owner.Data.Movements.Where(m => m.Date <= today && m.Kind == MovementKind.Expense && m.Date.Year == today.Year && m.Date.Month == today.Month);
        Categories = expenses.GroupBy(m => m.Category).Select(g => new CategoryRow(g.Key, g.Sum(x => x.Amount), Summary.Expense == 0 ? 0 : g.Sum(x => x.Amount) / Summary.Expense * 100)).OrderByDescending(c => c.Amount).ToArray();
        EmptyMessage = Recent.Count == 0 ? "Todavía no hay movimientos. Registra tu primer ingreso para comenzar." : "Últimos movimientos registrados";
        AddCommand = new RelayCommand(_ => owner.Section = "Ingresos"); ProjectCommand = new RelayCommand(_ => owner.Section = "Proyecciones");
        Advice = _analysis.Recommendations(owner.Data, today, Summary.Income, Summary.Expense, []);
        UpdateChart();
    }
    private void UpdateChart()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        History = _analysis.History(Owner.Data, today, Range);
        ChartSeries Series(string name, string color, int style, Func<HistoryMonth, decimal> read) => new(name, color, style, History.Select(m => new ChartValue(m.Label, read(m))).ToArray());
        Chart = !Owner.Data.Movements.Any(m => m.Date <= today) ? [] : Metric switch
        {
            "Ingresos y gastos" => [Series("Ingresos", "AccentBrush", 0, m => m.Income), Series("Gastos", "BlueBrush", 1, m => m.Expense)],
            "Trabajo e ingresos" => [Series("Trabajo registrado", "BlueBrush", 0, m => m.Salary), Series("Ingresos totales", "AccentBrush", 1, m => m.Income)],
            _ => [Series("Saldo acumulado", "AccentBrush", 0, m => m.Balance), Series("Ahorro mensual", "BlueBrush", 1, m => m.Savings)]
        };
        var last = History[^2]; var previous = History[^3]; decimal delta = last.Savings - previous.Savings;
        bool hasComparison = Owner.Data.Movements.Any(m => m.Date.Year == last.Date.Year && m.Date.Month == last.Date.Month)
            && Owner.Data.Movements.Any(m => m.Date.Year == previous.Date.Year && m.Date.Month == previous.Date.Month);
        Trend = hasComparison ? $"Entre {previous.Label} y {last.Label}, el ahorro mensual {(delta >= 0 ? "mejoró" : "empeoró")} Bs {Math.Abs(delta):N2} (de Bs {previous.Savings:N2} a Bs {last.Savings:N2})." : "Aún no hay registros en ambos meses completos anteriores para comparar la evolución.";
        Trend += " El mes actual es parcial. Cero significa sin movimientos registrados; no confirma que no hubo actividad.";
        Notify(nameof(History)); Notify(nameof(Chart)); Notify(nameof(Trend));
    }
}
