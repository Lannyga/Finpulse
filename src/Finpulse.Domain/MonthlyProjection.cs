namespace Finpulse.Domain;

public sealed record ProjectionAssumptions(decimal AnnualIncomeChange = 0, decimal AnnualExpenseChange = 0,
    decimal AnnualInflation = 0, decimal AnnualReturn = 0, decimal AnnualContributionChange = 0);
public sealed record ProjectionRequest(DateOnly Start, int Months, decimal InitialCapital, decimal MonthlyIncome,
    decimal MonthlyExpense, ProjectionAssumptions Assumptions, decimal? MonthlyContribution = null);
public sealed record MonthlyProjectionPoint(int Month, DateOnly Date, decimal Income, decimal Expense,
    decimal Savings, decimal Contribution, decimal RequestedContribution, decimal Return, decimal Balance, decimal RealBalance,
    bool TargetReached = false);
public interface IMonthlyProjectionEngine
{
    IReadOnlyList<MonthlyProjectionPoint> Calculate(ProjectionRequest request);
}

/// <summary>End-of-month contributions; annual effective return/inflation, annual stepped cash-flow changes.</summary>
public sealed class MonthlyProjectionEngine : IMonthlyProjectionEngine
{
    public IReadOnlyList<MonthlyProjectionPoint> Calculate(ProjectionRequest request)
        => CalculateCore(request, null);

    public IReadOnlyList<MonthlyProjectionPoint> CalculateUntilTarget(ProjectionRequest request, decimal target)
    {
        if (target is <= 0 or > 1_000_000_000m) throw new ArgumentException("El objetivo debe ser positivo y no superar 1.000.000.000.");
        return CalculateCore(request, target);
    }

    private static IReadOnlyList<MonthlyProjectionPoint> CalculateCore(ProjectionRequest request, decimal? target)
    {
        Validate(request);
        var a = request.Assumptions;
        decimal interestFactor = MonthlyFactor(a.AnnualReturn), inflationFactor = MonthlyFactor(a.AnnualInflation);
        decimal capital = request.InitialCapital, income = request.MonthlyIncome, expense = request.MonthlyExpense, prices = 1;
        decimal? contributionTarget = request.MonthlyContribution;
        decimal contributionGrowth = 1;
        List<MonthlyProjectionPoint> rows = [];
        for (int m = 1; m <= request.Months; m++)
        {
            if (m > 1 && (m - 1) % 12 == 0)
            {
                income *= 1 + a.AnnualIncomeChange / 100;
                expense *= 1 + a.AnnualExpenseChange / 100;
                contributionGrowth *= 1 + a.AnnualContributionChange / 100;
            }
            decimal savings = income - expense;
            decimal requested = (contributionTarget ?? Math.Max(0, savings)) * contributionGrowth;
            // Contributions allocate earned surplus, never invent another income stream.
            decimal contribution = savings < 0 ? savings : Math.Min(savings, requested);
            decimal interest = Math.Max(0, capital) * (interestFactor - 1);
            capital += interest + contribution;
            prices *= inflationFactor;
            if (Math.Abs(capital) > 1_000_000_000_000_000_000m || prices < 0.000000000000000001m)
                throw new ArgumentException("El resultado excede el rango del simulador; reduce tasas, montos o plazo.");
            static decimal Money(decimal amount) => decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
            // Sub-cent numerical tolerance for decimal root conversion, never the displayed cent rounding.
            bool reached = target is decimal threshold && (capital >= threshold || threshold - capital <= .00000001m);
            rows.Add(new(m, request.Start.AddMonths(m), Money(income), Money(expense), Money(savings),
                Money(contribution), Money(requested), Money(interest), Money(capital), Money(capital / prices), reached));
            if (reached) break;
        }
        return rows;
    }
    public static void Validate(ProjectionRequest r)
    {
        ArgumentNullException.ThrowIfNull(r);
        ArgumentNullException.ThrowIfNull(r.Assumptions);
        if (r.Months is < 1 or > 600 || r.Start.Year < 1900 || r.Start.Year > 2150)
            throw new ArgumentException("El plazo admite de 1 a 600 meses y la fecha inicial debe estar entre 1900 y 2150.");
        if (r.InitialCapital is < -1_000_000_000m or > 1_000_000_000m || r.MonthlyIncome is < 0 or > 1_000_000_000m ||
            r.MonthlyExpense is < 0 or > 1_000_000_000m || r.MonthlyContribution is < 0 or > 1_000_000_000m)
            throw new ArgumentException("Montos iniciales de hasta 1.000.000.000; ingreso, gasto y aporte no pueden ser negativos.");
        var a = r.Assumptions;
        if (a.AnnualIncomeChange is < -100 or > 100 || a.AnnualExpenseChange is < -100 or > 100 ||
            a.AnnualContributionChange is < -100 or > 100 || a.AnnualInflation is <= -100 or > 100 || a.AnnualReturn is <= -100 or > 100)
            throw new ArgumentException("Variaciones entre -100 y 100 %. Inflación y rendimiento deben ser mayores que -100 % y hasta 100 %.");
    }
    public static decimal MonthlyFactor(decimal annualPercent)
    {
        if (annualPercent is <= -100 or > 100) throw new ArgumentException("Tasa efectiva anual fuera del rango admitido.");
        decimal value = 1 + annualPercent / 100, root = 1;
        if (value <= 0) throw new ArgumentException("La tasa está demasiado cerca de -100 % para representarse con precisión decimal.");
        for (int i = 0; i < 200; i++)
        {
            decimal previous = root;
            decimal divisor = Power(root, 11);
            if (divisor == 0) throw new ArgumentException("La tasa excede la precisión admitida.");
            root = (11 * root + value / divisor) / 12;
            if (Math.Abs(root - previous) < 0.00000000000000000000000001m) return root;
        }
        throw new ArgumentException("La tasa no pudo convertirse con la precisión requerida.");
    }
    public static decimal Power(decimal value, int exponent)
    {
        decimal result = 1;
        for (int i = 0; i < exponent; i++) result *= value;
        return result;
    }
}
