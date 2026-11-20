using System.ComponentModel.DataAnnotations;

namespace Finpulse.Web.Models;

public sealed class Student
{
    public long Id { get; set; }
    public string Username { get; set; } = "";
    public string NormalizedName { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    // Compatibilidad con columnas obligatorias del esquema Oracle anterior.
    public string RecoveryHash { get; set; } = "disabled";
    public string SessionVersion { get; set; } = Guid.NewGuid().ToString("N");
    public int Failures { get; set; }
    public long Revision { get; set; }
    public DateTime? LockedUntil { get; set; }
    public decimal WarningPercent { get; set; } = 80;
}
public sealed class Category
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public int Kind { get; set; }
}
public abstract class OwnedRow
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public string RequestKey { get; set; } = Guid.NewGuid().ToString("N");
}
public sealed class Entry : OwnedRow
{
    public long? CategoryId { get; set; }
    public long? GoalId { get; set; }
    public int Kind { get; set; } // 0 ingreso, 1 gasto, 2 dinero guardado en una meta.
    public decimal Amount { get; set; }
    public DateTime Date { get; set; }
    public string Description { get; set; } = "";
}
public sealed class Goal : OwnedRow
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public decimal Target { get; set; }
    public DateTime Deadline { get; set; }
}

public sealed class Credentials
{
    [Required, RegularExpression("^[a-zA-Z0-9_.-]{3,40}$", ErrorMessage = "Usa de 3 a 40 letras, números, punto, guion o guion bajo.")]
    public string Username { get; set; } = "";
    [Required, StringLength(128, MinimumLength = 6, ErrorMessage = "Usa de 6 a 128 caracteres."), DataType(DataType.Password)]
    public string Password { get; set; } = "";
    [DataType(DataType.Password)] public string? Confirm { get; set; }
}
public sealed class FinanceForm
{
    public long Id { get; set; }
    public long? Revision { get; set; }
    public string RequestKey { get; set; } = Guid.NewGuid().ToString("N");
    [Range(0,1)] public int Kind { get; set; }
    public long? CategoryId { get; set; }
    public long? GoalId { get; set; }
    [Range(typeof(decimal), "0.01", "1000000000")] public decimal Amount { get; set; }
    [StringLength(120)] public string Name { get; set; } = "";
    [StringLength(500)] public string? Description { get; set; }
    public DateTime Date { get; set; } = DateTime.Today;
}

public enum MovementKind { Income, Expense }
public sealed record Movement(long Id, MovementKind Kind, decimal Amount, string Category, DateOnly Date, string Description);
public sealed record SavingGoal(long Id, string Name, string Description, decimal Target, DateOnly Deadline);
public sealed record Contribution(long Id, long GoalId, decimal Amount, DateOnly Date);
public sealed record FinanceData(IReadOnlyList<Movement> Movements, IReadOnlyList<SavingGoal> Goals, IReadOnlyList<Contribution> Contributions);
public sealed record FinancialSummary(decimal Balance, decimal Income, decimal Expense, decimal Available);
public sealed record MonthlyTotal(string Label, decimal Income, decimal Expense);
