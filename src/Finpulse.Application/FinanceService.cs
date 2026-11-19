using Finpulse.Domain;

namespace Finpulse.Application;

public interface IFinanceRepository
{
    Task<FinanceData> LoadAsync(long userId);
    Task SaveMovementAsync(long userId, Movement movement);
    Task DeleteMovementAsync(long userId, long id);
    Task SaveBudgetAsync(long userId, Budget budget);
    Task DeleteBudgetAsync(long userId, long id);
    Task SaveGoalAsync(long userId, SavingGoal goal);
    Task DeleteGoalAsync(long userId, long id);
    Task SaveContributionAsync(long userId, Contribution contribution);
    Task DeleteContributionAsync(long userId, long id);
}

public static class FinanceRules
{
    public static readonly string[] IncomeCategories = ["Mesada", "Beca", "Trabajo", "Emprendimiento", "Otros"];
    public static readonly string[] ExpenseCategories = ["Alimentación", "Transporte", "Educación", "Vivienda", "Entretenimiento", "Salud", "Compras", "Otros"];
    public static void ValidateAmount(decimal amount)
    {
        if (amount <= 0 || amount > 1_000_000_000m || decimal.Round(amount, 2) != amount)
            throw new ArgumentException("El monto debe ser mayor que cero, con hasta dos decimales y máximo 1.000.000.000.");
    }
    public static void ValidateMovement(Movement movement)
    {
        ValidateAmount(movement.Amount);
        if (!Enum.IsDefined(movement.Kind) || !(movement.Kind == MovementKind.Income ? IncomeCategories : ExpenseCategories).Contains(movement.Category))
            throw new ArgumentException("Selecciona una categoría válida.");
        ValidateDate(movement.Date);
        if (movement.Description.Length > 500) throw new ArgumentException("La descripción admite hasta 500 caracteres.");
    }
    public static void ValidateDate(DateOnly date)
    {
        if (date.Year < 1900 || date.Year > 2200) throw new ArgumentException("La fecha debe estar entre 1900 y 2200.");
    }
    public static FinancialSummary Summarize(FinanceData data, DateOnly month)
    {
        var actual = data.Movements.Where(m => m.Date <= month).ToArray();
        var current = actual.Where(m => m.Date.Year == month.Year && m.Date.Month == month.Month);
        var income = current.Where(m => m.Kind == MovementKind.Income).Sum(m => m.Amount);
        var expense = current.Where(m => m.Kind == MovementKind.Expense).Sum(m => m.Amount);
        var balance = actual.Where(m => m.Kind == MovementKind.Income).Sum(m => m.Amount)
            - actual.Where(m => m.Kind == MovementKind.Expense).Sum(m => m.Amount);
        return new(balance, income, expense, income - expense, income == 0 ? 0 : (income - expense) / income * 100,
            balance - data.Contributions.Sum(c => c.Amount));
    }
    public static BudgetUsage Evaluate(Budget budget, IEnumerable<Movement> movements, decimal warning = 80m)
    {
        if (warning is <= 0 or >= 100) throw new ArgumentException("El umbral debe estar entre 0 y 100.");
        var spent = movements.Where(m => m.Kind == MovementKind.Expense && m.Date.ToString("yyyy-MM") == budget.Month
            && (budget.Category == "General" || m.Category == budget.Category)).Sum(m => m.Amount);
        var percent = spent / budget.Amount * 100;
        return new(spent, budget.Amount - spent, percent, percent >= 100 ? "Límite alcanzado o superado" : percent >= warning ? "Atención al límite" : "Dentro del presupuesto");
    }
}
public sealed record FinancialSummary(decimal Balance, decimal Income, decimal Expense, decimal Savings, decimal SavingsRate, decimal Available);
public sealed record BudgetUsage(decimal Spent, decimal Available, decimal Percent, string Status);

public sealed record ProjectionPoint(int Year, decimal Balance, decimal RealBalance);
public interface IProjectionService
{
    IReadOnlyList<ProjectionPoint> Calculate(decimal balance, decimal income, decimal expense, decimal incomeChange, decimal expenseChange, decimal inflation, int years);
}
public sealed class ProjectionService : IProjectionService
{
    public IReadOnlyList<ProjectionPoint> Calculate(decimal balance, decimal income, decimal expense, decimal incomeChange, decimal expenseChange, decimal inflation, int years)
    {
        if (years is < 1 or > 10 || income < 0 || expense < 0 || incomeChange is < -100 or > 100 || expenseChange is < -100 or > 100 || inflation is < 0 or > 100)
            throw new ArgumentException("Horizonte de 1 a 10 años, tasas entre -100 y 100 e inflación entre 0 y 100.");
        List<ProjectionPoint> result = [];
        decimal price = 1;
        for (int y = 1; y <= years; y++)
        {
            balance += (income - expense) * 12;
            price *= 1 + inflation / 100;
            result.Add(new(y, decimal.Round(balance, 2), decimal.Round(balance / price, 2)));
            income *= 1 + incomeChange / 100;
            expense *= 1 + expenseChange / 100;
        }
        return result;
    }
}
