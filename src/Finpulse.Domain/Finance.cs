namespace Finpulse.Domain;

public enum MovementKind { Income, Expense }
public sealed record Movement(long Id, MovementKind Kind, decimal Amount, string Category, DateOnly Date, string Description);
public sealed record Budget(long Id, string Month, string Category, decimal Amount);
public sealed record SavingGoal(long Id, string Name, string Description, decimal Target, DateOnly Deadline);
public sealed record Contribution(long Id, long GoalId, decimal Amount, DateOnly Date);
public sealed record FinanceData(IReadOnlyList<Movement> Movements, IReadOnlyList<Budget> Budgets,
    IReadOnlyList<SavingGoal> Goals, IReadOnlyList<Contribution> Contributions);
