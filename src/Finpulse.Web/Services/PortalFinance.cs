using System.Data;
using Finpulse.Application;
using Finpulse.Domain;
using Finpulse.Web.Data;
using Finpulse.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace Finpulse.Web.Services;

public sealed record PortalSnapshot(FinanceData Data, IReadOnlyList<Category> Categories, IReadOnlyList<Scenario> Scenarios, decimal Warning, long Revision);

public sealed class PortalFinance(PortalDb db, ILogger<PortalFinance> log)
{
    public static DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTime.UtcNow, "America/La_Paz"));
    public static async Task SeedCategories(PortalDb db)
    {
        if (await db.Categories.AnyAsync()) return;
        db.Categories.AddRange(FinanceRules.IncomeCategories.Select(x => new Category { Name = x, Kind = 0 }));
        db.Categories.AddRange(FinanceRules.ExpenseCategories.Select(x => new Category { Name = x, Kind = 1 }));
        await db.SaveChangesAsync();
    }
    public async Task<PortalSnapshot> Load(long userId)
    {
        await using var read = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable) : null;
        var categories = await db.Categories.AsNoTracking().ToArrayAsync();
        var names = categories.ToDictionary(x => x.Id, x => x.Name);
        var entries = await db.Entries.AsNoTracking().Where(x => x.UserId == userId).OrderByDescending(x => x.Date).ThenByDescending(x => x.Id).ToArrayAsync();
        var budgets = await db.Budgets.AsNoTracking().Where(x => x.UserId == userId).ToArrayAsync();
        var goals = await db.Goals.AsNoTracking().Where(x => x.UserId == userId).ToArrayAsync();
        var scenarios = await db.Scenarios.AsNoTracking().Where(x => x.UserId == userId).OrderByDescending(x => x.Id).ToArrayAsync();
        var data = new FinanceData(entries.Where(x => x.Kind < 2).Select(x => new Movement(x.Id, (MovementKind)x.Kind, x.Amount, names[x.CategoryId!.Value], DateOnly.FromDateTime(x.Date), x.Description ?? "")).ToArray(),
            budgets.Select(x => new Budget(x.Id, x.Month, x.Scope, x.Amount)).ToArray(),
            goals.Select(x => new SavingGoal(x.Id, x.Name, x.Description ?? "", x.Target, DateOnly.FromDateTime(x.Deadline))).ToArray(),
            entries.Where(x => x.Kind == 2).Select(x => new Contribution(x.Id, x.GoalId!.Value, x.Amount, DateOnly.FromDateTime(x.Date))).ToArray());
        var user = await db.Users.AsNoTracking().SingleAsync(x => x.Id == userId);
        if(read is not null) await read.CommitAsync();
        return new(data, categories, scenarios, user.WarningPercent, user.Revision);
    }
    public async Task Save(long userId, string module, FinanceForm f)
    {
        FinanceRules.ValidateAmount(f.Amount); FinanceRules.ValidateDate(DateOnly.FromDateTime(f.Date));
        if (!Guid.TryParseExact(f.RequestKey, "N", out _)) throw new ArgumentException("Solicitud inválida. Recarga el formulario.");
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var user = await db.Users.SingleAsync(x => x.Id == userId);
        bool duplicate = module switch {
            "movements" or "contributions" => await db.Entries.AnyAsync(x => x.UserId == userId && x.RequestKey == f.RequestKey),
            "budgets" => await db.Budgets.AnyAsync(x => x.UserId == userId && x.RequestKey == f.RequestKey),
            "goals" => await db.Goals.AnyAsync(x => x.UserId == userId && x.RequestKey == f.RequestKey),
            _ => throw new ArgumentException("Módulo inválido.")
        };
        if (duplicate && f.Id == 0) return;
        if (f.Id > 0 && f.Revision != user.Revision) throw new ArgumentException("Tus datos cambiaron desde que abriste el formulario. Recarga antes de editar.");
        var snapshot = await Load(userId);
        switch (module) {
            case "movements":
                if (f.Kind is < 0 or > 1 || !snapshot.Categories.Any(x => x.Id == f.CategoryId && x.Kind == f.Kind)) throw new ArgumentException("Selecciona una categoría correspondiente al tipo.");
                if (DateOnly.FromDateTime(f.Date) > Today) throw new ArgumentException("Registra movimientos reales hasta hoy; usa escenarios para el futuro.");
                var entry = f.Id == 0 ? new Entry { UserId = userId, RequestKey = f.RequestKey } : await db.Entries.SingleOrDefaultAsync(x => x.UserId == userId && x.Id == f.Id && x.Kind < 2) ?? throw new ArgumentException("Movimiento no disponible.");
                entry.Kind=f.Kind;entry.CategoryId=f.CategoryId;entry.Amount=f.Amount;entry.Date=f.Date.Date;entry.Description=f.Description ?? "";
                if(f.Id==0) db.Entries.Add(entry); break;
            case "contributions":
                if(f.Id != 0) throw new ArgumentException("Libera el aporte y vuelve a asignarlo.");
                if (!snapshot.Data.Goals.Any(x => x.Id == f.GoalId)) throw new ArgumentException("Meta inválida.");
                if (DateOnly.FromDateTime(f.Date) != Today) throw new ArgumentException("Los aportes se asignan con la fecha de hoy.");
                if (FinanceRules.Summarize(snapshot.Data, Today).Available < f.Amount) throw new ArgumentException("No tienes saldo disponible suficiente para asignar ese aporte.");
                db.Entries.Add(new() { UserId = userId, RequestKey = f.RequestKey, Kind = 2, GoalId = f.GoalId, Amount = f.Amount, Date = f.Date.Date }); break;
            case "budgets":
                if (!DateOnly.TryParseExact(f.Month + "-01", "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var month) || month.Year is < 1900 or > 2200) throw new ArgumentException("Mes inválido.");
                string scope = f.CategoryId is null ? "General" : snapshot.Categories.SingleOrDefault(x => x.Id == f.CategoryId && x.Kind == 1)?.Name ?? throw new ArgumentException("Categoría inválida.");
                if (snapshot.Data.Budgets.Any(x => x.Id != f.Id && x.Month == f.Month && x.Category == scope)) throw new ArgumentException("Ya existe un presupuesto para esa categoría y mes.");
                var budget=f.Id==0?new SpendingPlan{UserId=userId,RequestKey=f.RequestKey}:await db.Budgets.SingleOrDefaultAsync(x=>x.UserId==userId&&x.Id==f.Id)??throw new ArgumentException("Presupuesto no disponible.");
                budget.Month=f.Month;budget.CategoryId=f.CategoryId;budget.Scope=scope;budget.Amount=f.Amount;
                if(f.Id==0)db.Budgets.Add(budget);break;
            case "goals":
                if (string.IsNullOrWhiteSpace(f.Name)) throw new ArgumentException("Introduce el nombre de tu meta.");
                var goal=f.Id==0?new Goal{UserId=userId,RequestKey=f.RequestKey}:await db.Goals.SingleOrDefaultAsync(x=>x.UserId==userId&&x.Id==f.Id)??throw new ArgumentException("Meta no disponible.");
                goal.Name=f.Name.Trim();goal.Description=f.Description ?? "";goal.Target=f.Amount;goal.Deadline=f.Date.Date;
                if(f.Id==0)db.Goals.Add(goal);break;
        }
        // Updating a shared owner revision protects availability checks against concurrent allocations/expenses.
        user.Revision++;
        await db.SaveChangesAsync();
        var after = await Load(userId);
        if (after.Data.Contributions.Count > 0 && FinanceRules.Summarize(after.Data, Today).Available < 0) throw new ArgumentException("La operación dejaría aportes sin respaldo. Libera primero una asignación a metas.");
        await tx.CommitAsync(); log.LogInformation("Cambio financiero {Module} de usuario {UserId}", module, userId);
    }
    public async Task Delete(long userId, string module, long id)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var user = await db.Users.SingleAsync(x => x.Id == userId);
        OwnedRow? row = module switch {
            "movements" or "contributions" => await db.Entries.SingleOrDefaultAsync(x => x.UserId == userId && x.Id == id),
            "budgets" => await db.Budgets.SingleOrDefaultAsync(x => x.UserId == userId && x.Id == id),
            "goals" => await db.Goals.SingleOrDefaultAsync(x => x.UserId == userId && x.Id == id),
            "scenarios" => await db.Scenarios.SingleOrDefaultAsync(x => x.UserId == userId && x.Id == id),
            _ => null
        };
        if (row is null) throw new ArgumentException("Registro no disponible.");
        if (row is Goal && await db.Entries.AnyAsync(x => x.UserId == userId && x.GoalId == id)) throw new ArgumentException("Libera los aportes antes de eliminar la meta.");
        db.Remove(row); user.Revision++; await db.SaveChangesAsync();
        var snapshot = await Load(userId);
        if (snapshot.Data.Contributions.Count > 0 && FinanceRules.Summarize(snapshot.Data, Today).Available < 0) throw new ArgumentException("Libera primero aportes: el ingreso respalda tus metas.");
        await tx.CommitAsync(); log.LogInformation("Eliminación financiera {Module} de usuario {UserId}", module, userId);
    }
    public static ProjectionRequest Request(Scenario s) => new(DateOnly.FromDateTime(s.Start), s.Years * 12, s.Capital, s.Income, s.Expense,
        new(s.IncomeChange, s.ExpenseChange, s.Inflation, s.AnnualReturn, s.ContributionChange), s.Contribution);
    public async Task SaveScenario(long userId, ScenarioForm f)
    {
        if (!new[] { 1,3,5,10 }.Contains(f.Years) || !Guid.TryParseExact(f.RequestKey, "N", out _)) throw new ArgumentException("Horizonte o solicitud inválidos.");
        var s = new Scenario { UserId = userId, RequestKey = f.RequestKey, Name = f.Name, Start = Today.ToDateTime(TimeOnly.MinValue), Years = f.Years,
            Capital = f.Capital, Income = f.Income, Expense = f.Expense, IncomeChange = f.IncomeChange, ExpenseChange = f.ExpenseChange,
            Inflation = f.Inflation, AnnualReturn = f.AnnualReturn, Contribution = f.Contribution, ContributionChange = f.ContributionChange };
        _ = new MonthlyProjectionEngine().Calculate(Request(s));
        if(new decimal?[]{s.Capital,s.Income,s.Expense,s.IncomeChange,s.ExpenseChange,s.Inflation,s.AnnualReturn,s.Contribution,s.ContributionChange}.Any(x=>x.HasValue&&decimal.Round(x.Value,8)!=x.Value)) throw new ArgumentException("Los supuestos admiten hasta ocho decimales para conservar el mismo cálculo al guardarse.");
        if (await db.Scenarios.AnyAsync(x => x.UserId == userId && x.RequestKey == f.RequestKey)) return;
        db.Scenarios.Add(s); await db.SaveChangesAsync();
    }
}
