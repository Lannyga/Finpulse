using Finpulse.Domain;

namespace Finpulse.Application;

public sealed record HistoryMonth(DateOnly Date, decimal Income, decimal Salary, decimal Expense, decimal Savings, decimal Balance)
{
    public string Label => Date.ToString("MMM yy", System.Globalization.CultureInfo.GetCultureInfo("es-BO"));
}
public sealed record HistoryBaseline(bool Sufficient, decimal Income, decimal Expense, string Explanation);
public sealed record FinancialAdvice(string Title, string Detail);
public sealed record GoalEstimate(long GoalId, string Name, decimal Saved, decimal Target, decimal? RequiredMonthly,
    decimal? Gap, int? MonthsNeeded, DateOnly? EstimatedDate, string Status, string RequiredNote = "");

public sealed class FinancialAnalysisService
{
    public IReadOnlyList<HistoryMonth> History(FinanceData data, DateOnly asOf, int months)
    {
        if (months is < 1 or > 120) throw new ArgumentException("Consulta entre 1 y 120 meses.");
        var first = new DateOnly(asOf.Year, asOf.Month, 1).AddMonths(1 - months);
        var actual = data.Movements.Where(m => m.Date <= asOf).ToArray();
        decimal balance = actual.Where(m => m.Date < first).Sum(Signed);
        List<HistoryMonth> rows = [];
        for (int i = 0; i < months; i++)
        {
            var date = first.AddMonths(i); var current = actual.Where(m => m.Date.Year == date.Year && m.Date.Month == date.Month).ToArray();
            decimal income = current.Where(m => m.Kind == MovementKind.Income).Sum(m => m.Amount);
            decimal expense = current.Where(m => m.Kind == MovementKind.Expense).Sum(m => m.Amount);
            balance += income - expense;
            rows.Add(new(date, income, current.Where(m => m.Kind == MovementKind.Income && m.Category == "Trabajo").Sum(m => m.Amount), expense, income - expense, balance));
        }
        return rows;
    }
    private static decimal Signed(Movement m) => m.Kind == MovementKind.Income ? m.Amount : -m.Amount;
    public HistoryBaseline Baseline(FinanceData data, DateOnly asOf)
    {
        var start = new DateOnly(asOf.Year, asOf.Month, 1).AddMonths(-3);
        var end = new DateOnly(asOf.Year, asOf.Month, 1);
        var previous = data.Movements.Where(m => m.Date >= start && m.Date < end).ToArray();
        bool enough = Enumerable.Range(0, 3).All(i => previous.Any(m => m.Date.Year == start.AddMonths(i).Year && m.Date.Month == start.AddMonths(i).Month))
            && previous.Any(m => m.Kind == MovementKind.Income) && previous.Any(m => m.Kind == MovementKind.Expense);
        return enough
            ? new(true, decimal.Round(previous.Where(m => m.Kind == MovementKind.Income).Sum(m => m.Amount) / 3, 2),
                decimal.Round(previous.Where(m => m.Kind == MovementKind.Expense).Sum(m => m.Amount) / 3, 2),
                $"Promedio de los tres meses completos anteriores ({start:yyyy-MM} a {end.AddMonths(-1):yyyy-MM}); confirma que registraste todos tus movimientos.")
            : new(false, 0, 0, "Historial insuficiente: se requieren movimientos en tres meses completos consecutivos, con ingresos y gastos. Introduce supuestos manuales; los ceros iniciales no son una estimación de tus finanzas.");
    }
    public GoalEstimate EstimateGoal(SavingGoal goal, decimal saved, ProjectionRequest request)
    {
        MonthlyProjectionEngine.Validate(request);
        ArgumentNullException.ThrowIfNull(goal);
        FinanceRules.ValidateAmount(goal.Target);
        FinanceRules.ValidateDate(goal.Deadline);
        if (saved < 0) throw new ArgumentException("El acumulado de la meta no puede ser negativo.");
        if (saved >= goal.Target) return new(goal.Id, goal.Name, saved, goal.Target, 0, 0, 0, request.Start, "Cumplida");
        int dueMonths = 0;
        while (dueMonths < 600 && request.Start.AddMonths(dueMonths + 1) <= goal.Deadline) dueMonths++;
        decimal? required = null;
        bool supportedDeadline = request.Start.AddMonths(601) > goal.Deadline;
        string requiredNote = !supportedDeadline ? "El plazo objetivo supera los 600 meses admitidos." : dueMonths == 0 ? "No hay aniversarios mensuales disponibles antes de la fecha objetivo." : "Aporte mensual constante, redondeado hacia arriba a centavos.";
        if (dueMonths > 0 && supportedDeadline)
        {
            decimal factor = MonthlyProjectionEngine.MonthlyFactor(request.Assumptions.AnnualReturn), accumulated = 1, annuity = 0;
            for (int i = 0; i < dueMonths; i++) { annuity += accumulated; accumulated *= factor; }
            required = decimal.Ceiling(Math.Max(0, (goal.Target - saved * accumulated) / annuity) * 100) / 100;
        }
        var rows = new MonthlyProjectionEngine().CalculateUntilTarget(request with { InitialCapital = saved }, goal.Target);
        var reached = rows.FirstOrDefault(p => p.TargetReached);
        decimal initialContribution = rows[0].Contribution;
        string status = reached is null ? $"No alcanzada en {request.Months} meses simulados" : reached.Date > goal.Deadline ? "Fuera del plazo" : "Dentro del plazo";
        if (goal.Deadline < request.Start) status = "Plazo vencido";
        return new(goal.Id, goal.Name, saved, goal.Target, required, required - initialContribution, reached?.Month, reached?.Date, status, requiredNote);
    }
    public IReadOnlyList<FinancialAdvice> Recommendations(FinanceData data, DateOnly asOf, decimal income, decimal expense, IEnumerable<GoalEstimate> goals)
    {
        List<FinancialAdvice> advice = [];
        if (expense > income) advice.Add(new("Déficit mensual", $"Gastas Bs {expense:N2} frente a ingresos de Bs {income:N2}: tu capacidad de ahorro es Bs {income - expense:N2} al mes. El déficit reduce el capital proyectado."));
        var actual = data.Movements.Where(m => m.Date <= asOf).ToArray();
        foreach (var budget in data.Budgets.Where(b => b.Month == asOf.ToString("yyyy-MM")))
        {
            var usage = FinanceRules.Evaluate(budget, actual);
            if (usage.Percent > 100) advice.Add(new("Presupuesto excedido", $"{budget.Category}: gastaste Bs {usage.Spent:N2} de Bs {budget.Amount:N2}; exceso Bs {-usage.Available:N2} ({usage.Percent:N1} % utilizado)."));
        }
        var current = actual.Where(m => m.Kind == MovementKind.Expense && m.Date.Year == asOf.Year && m.Date.Month == asOf.Month).ToArray();
        decimal total = current.Sum(m => m.Amount);
        foreach (var group in current.GroupBy(m => m.Category).OrderByDescending(g => g.Sum(m => m.Amount)))
        {
            decimal amount = group.Sum(m => m.Amount);
            if (total > 0 && amount / total >= 0.30m)
                advice.Add(new("Concentración de gastos", $"{group.Key}: Bs {amount:N2}, {amount / total * 100:N1} % del gasto del mes (regla: al menos 30 %). Si redujeras esta categoría un 10 % —supuesto ilustrativo— liberarías Bs {amount * .1m:N2} por mes comparable, Bs {amount * 1.2m:N2} en 12 meses iguales."));
        }
        foreach (var goal in goals.Where(g => g.Status != "Cumplida" && g.Status != "Dentro del plazo"))
            advice.Add(new("Revisar plazo de meta", $"{goal.Name}: {goal.Status}. Acumulado Bs {goal.Saved:N2} de Bs {goal.Target:N2}; aporte constante requerido al plazo: {(goal.RequiredMonthly is decimal required ? $"Bs {required:N2}" : goal.RequiredNote)}. Diferencia frente al primer aporte proyectado: {(goal.Gap is decimal gap ? $"Bs {gap:N2}/mes" : "no aplicable")}."));
        return advice;
    }
}
