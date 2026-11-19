namespace Finpulse.Web.Models;

public sealed class Student
{
    public long Id { get; set; }
    public string Username { get; set; } = "";
    public string NormalizedName { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string RecoveryHash { get; set; } = "";
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
    public int Kind { get; set; } // 0 income, 1 expense, 2 allocation; allocations never change balance.
    public decimal Amount { get; set; }
    public DateTime Date { get; set; }
    public string Description { get; set; } = "";
}
public sealed class SpendingPlan : OwnedRow
{
    public string Month { get; set; } = "";
    public long? CategoryId { get; set; } // null = General
    public string Scope { get; set; } = "General";
    public decimal Amount { get; set; }
}
public sealed class Goal : OwnedRow
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public decimal Target { get; set; }
    public DateTime Deadline { get; set; }
}
public sealed class Scenario : OwnedRow
{
    public string Name { get; set; } = "";
    public DateTime Start { get; set; }
    public int Years { get; set; }
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
