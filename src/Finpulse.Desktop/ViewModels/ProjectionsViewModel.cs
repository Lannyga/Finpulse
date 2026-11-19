using System.Collections;
using System.ComponentModel;
using System.Globalization;
using System.Windows.Input;
using Finpulse.Application;
using Finpulse.Domain;
using Finpulse.Desktop.Controls;

namespace Finpulse.Desktop.ViewModels;

public sealed class ScenarioInputViewModel(string name, Action changed) : Observable
{
    public string Name { get; } = name;
    private string _income = "0", _expense = "0", _inflation = "0", _yield = "0", _contribution = "0";
    public string IncomeChange { get => _income; set { if (Set(ref _income, value)) changed(); } }
    public string ExpenseChange { get => _expense; set { if (Set(ref _expense, value)) changed(); } }
    public string Inflation { get => _inflation; set { if (Set(ref _inflation, value)) changed(); } }
    public string AnnualReturn { get => _yield; set { if (Set(ref _yield, value)) changed(); } }
    public string ContributionChange { get => _contribution; set { if (Set(ref _contribution, value)) changed(); } }
    internal ProjectionAssumptions Read() => new(ProjectionsViewModel.Number(IncomeChange), ProjectionsViewModel.Number(ExpenseChange),
        ProjectionsViewModel.Number(Inflation), ProjectionsViewModel.Number(AnnualReturn), ProjectionsViewModel.Number(ContributionChange));
}
public sealed record ScenarioResult(string Name, decimal Nominal, decimal Real, decimal Savings, string Note);
public sealed class ProjectionsViewModel : FinancePage, INotifyDataErrorInfo
{
    private readonly FinancialAnalysisService _analysis = new();
    private bool _ready;
    private string _income = "0", _expense = "0", _capital = "0", _months = "60", _contribution = "", _reduction = "0";
    private string _selectedScenario = "Base";
    private bool _real, _annual;
    public string Income { get => _income; set { if (Set(ref _income, value)) Recalculate(); } }
    public string Expense { get => _expense; set { if (Set(ref _expense, value)) Recalculate(); } }
    public string InitialCapital { get => _capital; set { if (Set(ref _capital, value)) Recalculate(); } }
    public string Months { get => _months; set { if (Set(ref _months, value)) Recalculate(); } }
    public string MonthlyContribution { get => _contribution; set { if (Set(ref _contribution, value)) Recalculate(); } }
    public string ExpenseReduction { get => _reduction; set { if (Set(ref _reduction, value)) Recalculate(); } }
    public bool RealValues { get => _real; set { if (Set(ref _real, value)) Recalculate(); } }
    public bool AnnualView { get => _annual; set { if (Set(ref _annual, value)) Recalculate(); } }
    public string SelectedScenarioName { get => _selectedScenario; set { if (Set(ref _selectedScenario, value)) { Notify(nameof(SelectedAssumptions)); Recalculate(); } } }
    public string[] ScenarioNames { get; } = ["Conservador", "Base", "Optimista"];
    public IReadOnlyList<ScenarioInputViewModel> ScenarioInputs { get; }
    public ScenarioInputViewModel SelectedAssumptions => ScenarioInputs.First(s => s.Name == SelectedScenarioName);
    public IReadOnlyList<MonthlyProjectionPoint> Rows { get; private set; } = [];
    public IReadOnlyList<ScenarioResult> Results { get; private set; } = [];
    public ScenarioResult? ConservativeResult => Results.FirstOrDefault(r => r.Name == "Conservador");
    public ScenarioResult? BaseResult => Results.FirstOrDefault(r => r.Name == "Base");
    public ScenarioResult? OptimisticResult => Results.FirstOrDefault(r => r.Name == "Optimista");
    public IReadOnlyList<ChartSeries> Chart { get; private set; } = [];
    public IReadOnlyList<FinancialAdvice> Advice { get; private set; } = [];
    public IReadOnlyList<GoalEstimate> Goals { get; private set; } = [];
    public string BaselineMessage { get; }
    public string Assumptions { get; private set; } = "";
    public string MonthlySavings { get; private set; } = "";
    public string ValidationMessage { get; private set; } = "";
    public bool HasErrors => ValidationMessage.Length > 0;
    public event EventHandler<DataErrorsChangedEventArgs>? ErrorsChanged;
    public IEnumerable GetErrors(string? propertyName) => HasErrors ? new[] { ValidationMessage } : Array.Empty<string>();
    public ICommand CalculateCommand { get; }
    public ICommand HorizonCommand { get; }
    public ProjectionsViewModel(WorkspaceViewModel owner) : base(owner)
    {
        var today = DateOnly.FromDateTime(DateTime.Today); var baseline = _analysis.Baseline(owner.Data, today);
        BaselineMessage = baseline.Explanation;
        _income = baseline.Income.ToString(CultureInfo.CurrentCulture); _expense = baseline.Expense.ToString(CultureInfo.CurrentCulture);
        _capital = FinanceRules.Summarize(owner.Data, today).Balance.ToString(CultureInfo.CurrentCulture);
        ScenarioInputs = ScenarioNames.Select(n => new ScenarioInputViewModel(n, Recalculate)).ToArray();
        CalculateCommand = Command(() => { Recalculate(); return Task.CompletedTask; });
        HorizonCommand = new RelayCommand(p => Months = p?.ToString() ?? "60");
        _ready = true; Recalculate();
    }
    internal static decimal Number(string text)
    {
        if (!decimal.TryParse(text, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.CurrentCulture, out decimal value))
            throw new ArgumentException("Introduce números válidos sin separadores de miles; usa el separador decimal de tu configuración regional.");
        return value;
    }
    public void Recalculate()
    {
        if (!_ready) return;
        try
        {
            if (!int.TryParse(Months, out int months)) throw new ArgumentException("El plazo debe ser un número entero de meses (1 a 600).");
            decimal income = Number(Income), expense = Number(Expense), reduction = Number(ExpenseReduction);
            if (reduction < 0 || reduction > expense) throw new ArgumentException("La reducción de gasto debe estar entre cero y el gasto mensual.");
            decimal? contribution = string.IsNullOrWhiteSpace(MonthlyContribution) ? null : Number(MonthlyContribution);
            var request = new ProjectionRequest(DateOnly.FromDateTime(DateTime.Today), months, Number(InitialCapital), income, expense - reduction, new(), contribution);
            var calculated = ScenarioInputs.Select(s => (s.Name, Request: request with { Assumptions = s.Read() }))
                .Select(s => (s.Name, s.Request, Points: new MonthlyProjectionEngine().Calculate(s.Request))).ToArray();
            var selected = calculated.First(s => s.Name == SelectedScenarioName);
            var chartRows = calculated.Select(s => new ChartSeries(s.Name, s.Name == "Base" ? "AccentBrush" : s.Name == "Optimista" ? "BlueBrush" : "WarningBrush",
                s.Name == "Base" ? 0 : s.Name == "Optimista" ? 1 : 2,
                new[] { new ChartValue("Inicio", request.InitialCapital) }.Concat(s.Points.Where(p => !AnnualView || p.Month % 12 == 0 || p.Month == months)
                    .Select(p => new ChartValue(p.Date.ToString("MMM yy", CultureInfo.GetCultureInfo("es-BO")), RealValues ? p.RealBalance : p.Balance))).ToArray())).ToArray();
            var goals = Owner.Data.Goals.Select(g => _analysis.EstimateGoal(g, Owner.Data.Contributions.Where(c => c.GoalId == g.Id && c.Date <= request.Start).Sum(c => c.Amount), selected.Request)).ToArray();
            Results = calculated.Select(s => new ScenarioResult(s.Name, s.Points[^1].Balance, s.Points[^1].RealBalance, s.Points[0].Savings,
                s.Points.Any(p => p.RequestedContribution > Math.Max(0, p.Savings)) ? "Aporte limitado por el excedente disponible" : "Aporte financiado por el excedente")).ToArray();
            Rows = selected.Points.Where(p => !AnnualView || p.Month % 12 == 0 || p.Month == months).ToArray();
            Chart = chartRows; Goals = goals;
            Advice = _analysis.Recommendations(Owner.Data, request.Start, income, expense - reduction, goals);
            MonthlySavings = $"Bs {income - expense + reduction:N2}/mes · Bs {(income - expense + reduction) * 12:N2}/año inicial";
            Assumptions = $"{SelectedScenarioName} · {months} meses · gráfico en valores {(RealValues ? "ajustados por inflación" : "nominales")}. Reducción hipotética Bs {reduction:N2}/mes. Simular no modifica tus movimientos.";
            ValidationMessage = "";
        }
        catch (Exception ex) when (ex is ArgumentException or ArithmeticException)
        {
            Rows = []; Results = []; Chart = []; Goals = []; Advice = []; MonthlySavings = ""; Assumptions = "";
            ValidationMessage = ex is ArgumentException ? ex.Message : "El cálculo excede el rango numérico. Reduce montos, tasas o plazo.";
        }
        foreach (var property in new[] { nameof(Rows), nameof(Results), nameof(ConservativeResult), nameof(BaseResult), nameof(OptimisticResult), nameof(Chart), nameof(Goals), nameof(Advice), nameof(Assumptions), nameof(MonthlySavings), nameof(ValidationMessage), nameof(HasErrors) }) Notify(property);
        ErrorsChanged?.Invoke(this, new(string.Empty));
    }
}
