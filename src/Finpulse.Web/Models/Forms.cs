using System.ComponentModel.DataAnnotations;

namespace Finpulse.Web.Models;

public sealed class Credentials
{
    [Required, RegularExpression("^[a-zA-Z0-9_.-]{3,40}$", ErrorMessage = "Usa de 3 a 40 letras, números, punto, guion o guion bajo.")]
    public string Username { get; set; } = "";
    [Required, StringLength(128, MinimumLength = 12), DataType(DataType.Password)]
    public string Password { get; set; } = "";
    [DataType(DataType.Password)] public string? Confirm { get; set; }
    public string? RecoveryCode { get; set; }
}
public sealed class FinanceForm
{
    public long Id { get; set; }
    public long? Revision { get; set; }
    public string RequestKey { get; set; } = Guid.NewGuid().ToString("N");
    [Range(0,2)] public int Kind { get; set; }
    public long? CategoryId { get; set; }
    public long? GoalId { get; set; }
    [Range(typeof(decimal), "0.01", "1000000000")] public decimal Amount { get; set; }
    [StringLength(120)] public string Name { get; set; } = "";
    [StringLength(500)] public string? Description { get; set; }
    public DateTime Date { get; set; } = DateTime.Today;
    [RegularExpression("^[0-9]{4}-(0[1-9]|1[0-2])$")] public string Month { get; set; } = DateTime.Today.ToString("yyyy-MM");
}
public sealed class ScenarioForm
{
    public string RequestKey { get; set; } = Guid.NewGuid().ToString("N");
    [Required, StringLength(100)] public string Name { get; set; } = "Mi escenario";
    public int Years { get; set; } = 5;
    public decimal Capital { get; set; }
    public decimal Income { get; set; }
    public decimal Expense { get; set; }
    public decimal IncomeChange { get; set; }
    public decimal ExpenseChange { get; set; }
    public decimal Inflation { get; set; }
    public decimal AnnualReturn { get; set; }
    public decimal? Contribution { get; set; }
    public decimal ContributionChange { get; set; }
}
