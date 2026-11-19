using Finpulse.Application;
using Finpulse.Domain;

internal static class ProjectionChecks
{
    public static void Run()
    {
        int count = 0;
        void Check(bool condition, string label) { if (!condition) throw new InvalidOperationException(label); Console.WriteLine($"MOTOR {++count}: {label}"); }
        void Invalid(ProjectionRequest request, string label)
        {
            bool rejected = false; try { new MonthlyProjectionEngine().Calculate(request); } catch (ArgumentException) { rejected = true; }
            Check(rejected, label);
        }
        var engine = new MonthlyProjectionEngine(); var analysis = new FinancialAnalysisService();
        var start = new DateOnly(2026, 11, 18);
        var baseline = new ProjectionRequest(start, 60, 0, 1200, 850, new());
        var rows = engine.Calculate(baseline);
        Check(rows.Count == 60, "sesenta resultados mensuales");
        Check(rows.All(p => p.Savings == 350 && p.Contribution == 350), "caso requerido: 1200 - 850 = 350");
        Check(rows[^1].Balance == 21000 && rows[^1].RealBalance == 21000, "cinco años: Bs 21.000 nominales y reales");
        foreach (var months in new[] { 1, 12, 36, 60, 120, 17 })
            Check(engine.Calculate(baseline with { Months = months })[^1].Balance == months * 350, $"plazo de {months} meses sin intereses");
        Check(engine.Calculate(baseline with { MonthlyIncome = 850 })[^1].Balance == 0, "ingreso igual a gasto y saldo inicial cero");
        Check(engine.Calculate(baseline with { MonthlyIncome = 700, Months = 12 })[^1].Balance == -1800, "ahorro negativo reduce capital");
        Check(engine.Calculate(baseline with { InitialCapital = 1000 })[^1].Balance == 22000, "saldo inicial se suma una sola vez");
        Check(engine.Calculate(baseline with { MonthlyContribution = 100 })[^1].Balance == 6000, "aporte parcial no crea ingresos");
        Check(engine.Calculate(baseline with { MonthlyContribution = 1000 })[^1].Balance == 21000, "aporte limitado a excedente");
        Check(engine.Calculate(baseline with { MonthlyContribution = 0 })[^1].Balance == 0, "aporte cero deja excedente fuera del capital modelado");
        Check(engine.Calculate(baseline with { MonthlyContribution = 0, MonthlyExpense = 1300, Months = 1 })[0].Balance == -100, "déficit se descuenta aun con aporte cero");
        var invested = engine.Calculate(baseline with { InitialCapital = 1000, MonthlyIncome = 0, MonthlyExpense = 0, Months = 12, Assumptions = new(AnnualReturn: 12) });
        Check(invested[^1].Balance == 1120, "rendimiento efectivo anual 12 %: Bs 1120 al año");
        decimal factor = MonthlyProjectionEngine.MonthlyFactor(12);
        decimal expected = 1000 * MonthlyProjectionEngine.Power(factor, 12) + 350 * (MonthlyProjectionEngine.Power(factor, 12) - 1) / (factor - 1);
        var annuity = engine.Calculate(baseline with { Months = 12, InitialCapital = 1000, Assumptions = new(AnnualReturn: 12) });
        Check(annuity[^1].Balance == decimal.Round(expected, 2, MidpointRounding.AwayFromZero), "anualidad vencida con capitalización compuesta");
        var inflation = engine.Calculate(baseline with { Months = 12, Assumptions = new(AnnualInflation: 10) });
        Check(inflation[^1].Balance == 4200 && inflation[^1].RealBalance == 3818.18m, "inflación efectiva del 10 %");
        Check(engine.Calculate(baseline with { Months = 12, InitialCapital = 1000, MonthlyIncome = 0, MonthlyExpense = 0, Assumptions = new(AnnualReturn: -10) })[^1].Balance == 900, "rendimiento negativo");
        Check(engine.Calculate(baseline with { Months = 12, Assumptions = new(AnnualInflation: -10) })[^1].RealBalance == 4666.67m, "deflación configurable");
        var growth = engine.Calculate(baseline with { Months = 13, Assumptions = new(AnnualIncomeChange: 10, AnnualExpenseChange: 5) });
        Check(growth[11].Income == 1200 && growth[12].Income == 1320 && growth[12].Expense == 892.5m, "incrementos de flujos en mes 13");
        var contribution = engine.Calculate(baseline with { Months = 13, MonthlyContribution = 100, Assumptions = new(AnnualContributionChange: 10) });
        Check(contribution[11].Contribution == 100 && contribution[12].Contribution == 110, "variación anual de aporte");
        Check(engine.Calculate(baseline with { Months = 13, Assumptions = new(AnnualIncomeChange: -100) })[12].Income == 0, "ingreso puede disminuir 100 %");
        var optimistic = engine.Calculate(baseline with { Assumptions = new(AnnualIncomeChange: 10) });
        var conservative = engine.Calculate(baseline with { Assumptions = new(AnnualExpenseChange: 10) });
        Check(optimistic[^1].Balance > rows[^1].Balance && conservative[^1].Balance < rows[^1].Balance, "escenarios configurados producen resultados diferentes");
        foreach (int invalid in new[] { 0, -1, 601 }) Invalid(baseline with { Months = invalid }, "plazo inválido " + invalid);
        Invalid(baseline with { InitialCapital = decimal.MinValue }, "capital extremo inválido");
        Invalid(baseline with { MonthlyIncome = -1 }, "ingreso negativo inválido");
        Invalid(baseline with { MonthlyExpense = -1 }, "gasto negativo inválido");
        Invalid(baseline with { MonthlyContribution = -1 }, "aporte negativo inválido");
        Invalid(baseline with { MonthlyIncome = 1_000_000_001m }, "límite monetario");
        Invalid(baseline with { Assumptions = new(AnnualReturn: -100) }, "rendimiento -100 no convertible");
        Invalid(baseline with { Assumptions = new(AnnualInflation: -100) }, "inflación -100 no convertible");
        Invalid(baseline with { Assumptions = new(AnnualIncomeChange: 101) }, "crecimiento excesivo");
        Invalid(baseline with { Months = 600, MonthlyIncome = 1_000_000_000, Assumptions = new(AnnualIncomeChange: 100) }, "resultado extremo rechazado");
        var goal = new SavingGoal(1, "Equipo", "", 2100, start.AddMonths(6));
        var estimate = analysis.EstimateGoal(goal, 0, baseline);
        Check(estimate.MonthsNeeded == 6 && estimate.EstimatedDate == start.AddMonths(6), "fecha y meses para alcanzar meta");
        Check(estimate.RequiredMonthly == 350 && estimate.Gap == 0 && estimate.Status == "Dentro del plazo", "aporte requerido y brecha de meta");
        Check(analysis.EstimateGoal(goal, 700, baseline).MonthsNeeded == 4, "aporte existente no se duplica");
        Check(analysis.EstimateGoal(goal, 2100, baseline).MonthsNeeded == 0, "meta ya cumplida");
        Check(analysis.EstimateGoal(goal with { Deadline = start.AddMonths(3) }, 0, baseline).Status == "Fuera del plazo", "meta fuera de plazo");
        Check(analysis.EstimateGoal(goal with { Deadline = start.AddDays(-1) }, 0, baseline).RequiredMonthly is null, "meta vencida sin división entre cero");
        Check(analysis.EstimateGoal(goal, 0, baseline with { MonthlyIncome = 850 }).MonthsNeeded is null, "meta sin capacidad de ahorro");
        Check(analysis.EstimateGoal(goal, 0, baseline with { MonthlyIncome = 1400 }).MonthsNeeded == 4, "menor gasto o mayor ingreso acelera meta");
        Check(analysis.EstimateGoal(goal, 0, baseline with { InitialCapital = 9000 }).MonthsNeeded == 6, "saldo global no se asigna otra vez a cada meta");
        var movements = Enumerable.Range(1, 3).SelectMany(i => new[] {
            new Movement(i*2, MovementKind.Income, 1200, "Trabajo", start.AddMonths(-i), ""),
            new Movement(i*2+1, MovementKind.Expense, 850, "Alimentación", start.AddMonths(-i), "") }).ToArray();
        var data = new FinanceData(movements, [], [], []);
        var history = analysis.Baseline(data, start);
        Check(history.Sufficient && history.Income == 1200 && history.Expense == 850, "promedio de tres meses completos");
        Check(!analysis.Baseline(new([],[],[],[]), start).Sufficient, "historial vacío requiere supuestos manuales");
        Check(!analysis.Baseline(data with { Movements = movements.Take(2).ToArray() }, start).Sufficient, "historial corto requiere supuestos manuales");
        var future = new Movement(99, MovementKind.Income, 9999, "Beca", start.AddMonths(1), "futuro");
        Check(FinanceRules.Summarize(data with { Movements = [.. movements, future] }, start).Balance == 1050, "saldo actual excluye ingresos futuros");
        var timeline = analysis.History(data, start, 6);
        Check(timeline[^1].Balance == 1050 && timeline[^2].Savings == 350 && timeline[^2].Salary == 1200, "historial de ahorro, salario y saldo acumulado");
        var currentExpense = new Movement(30, MovementKind.Expense, 900, "Alimentación", start, "");
        var advice = analysis.Recommendations(data with { Movements = [.. movements, currentExpense], Budgets = [new(1,start.ToString("yyyy-MM"),"General",800)] }, start, 700, 900, [estimate with { Status = "Fuera del plazo" }]);
        Check(advice.Any(a => a.Title == "Déficit mensual" && a.Detail.Contains("200")), "recomendación de déficit con fundamento numérico");
        Check(advice.Any(a => a.Title == "Presupuesto excedido" && a.Detail.Contains("100")), "recomendación presupuestaria numérica");
        Check(advice.Any(a => a.Title == "Concentración de gastos" && a.Detail.Contains("90")), "efecto hipotético de reducir categoría 10 %");
        Check(advice.Any(a => a.Title == "Revisar plazo de meta"), "recomendación de meta");
        Check(analysis.Recommendations(new([],[],[],[]), start, 0, 0, []).Count == 0, "sin recomendaciones personales ficticias");
        Console.WriteLine($"OK: {count} comprobaciones del motor mensual y análisis.");
    }
}
