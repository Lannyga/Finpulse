using System.Data;
using Finpulse.Web.Data;
using Finpulse.Web.Models;
using Microsoft.EntityFrameworkCore;
using Oracle.ManagedDataAccess.Client;

namespace Finpulse.Web.Services;

public sealed record PortalSnapshot(FinanceData Data, IReadOnlyList<Category> Categories, long Revision);

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
        var goals = await db.Goals.AsNoTracking().Where(x => x.UserId == userId).ToArrayAsync();
        var data = new FinanceData(entries.Where(x => x.Kind < 2).Select(x => new Movement(x.Id, (MovementKind)x.Kind, x.Amount, names[x.CategoryId!.Value], DateOnly.FromDateTime(x.Date), x.Description ?? "")).ToArray(),
            goals.Select(x => new SavingGoal(x.Id, x.Name, x.Description ?? "", x.Target, DateOnly.FromDateTime(x.Deadline))).ToArray(),
            entries.Where(x => x.Kind == 2).Select(x => new Contribution(x.Id, x.GoalId!.Value, x.Amount, DateOnly.FromDateTime(x.Date))).ToArray());
        var user = await db.Users.AsNoTracking().SingleAsync(x => x.Id == userId);
        if (read is not null) await read.CommitAsync();
        return new(data, categories, user.Revision);
    }
    public Task Save(long userId, string module, FinanceForm form) => WithSerializationRetry(() => SaveAttempt(userId, module, form));
    private async Task SaveAttempt(long userId, string module, FinanceForm f)
    {
        FinanceRules.ValidateAmount(f.Amount);
        FinanceRules.ValidateDate(DateOnly.FromDateTime(f.Date));
        if (!Guid.TryParseExact(f.RequestKey, "N", out _)) throw new ArgumentException("Solicitud inválida. Recarga el formulario.");
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var user = await db.Users.SingleAsync(x => x.Id == userId);
        bool duplicate = module switch {
            "movements" or "contributions" => await db.Entries.AnyAsync(x => x.UserId == userId && x.RequestKey == f.RequestKey),
            "goals" => await db.Goals.AnyAsync(x => x.UserId == userId && x.RequestKey == f.RequestKey),
            _ => throw new ArgumentException("Módulo inválido.")
        };
        if (duplicate && f.Id == 0) return;
        if (f.Id > 0 && f.Revision != user.Revision) throw new ArgumentException("Tus datos cambiaron desde que abriste el formulario. Recarga antes de editar.");
        var snapshot = await Load(userId);
        switch (module) {
            case "movements":
                if (f.Kind is < 0 or > 1 || !snapshot.Categories.Any(x => x.Id == f.CategoryId && x.Kind == f.Kind)) throw new ArgumentException("Selecciona una categoría correspondiente al tipo.");
                if (DateOnly.FromDateTime(f.Date) > Today) throw new ArgumentException("La fecha no puede ser posterior a hoy.");
                var entry = f.Id == 0 ? new Entry { UserId = userId, RequestKey = f.RequestKey } : await db.Entries.SingleOrDefaultAsync(x => x.UserId == userId && x.Id == f.Id && x.Kind < 2) ?? throw new ArgumentException("Movimiento no disponible.");
                entry.Kind = f.Kind;
                entry.CategoryId = f.CategoryId;
                entry.Amount = f.Amount;
                entry.Date = f.Date.Date;
                entry.Description = f.Description ?? "";
                if (f.Id == 0) db.Entries.Add(entry);
                break;
            case "contributions":
                if (f.Id != 0) throw new ArgumentException("Retira el dinero y vuelve a guardarlo.");
                if (!snapshot.Data.Goals.Any(x => x.Id == f.GoalId)) throw new ArgumentException("Meta inválida.");
                if (DateOnly.FromDateTime(f.Date) != Today) throw new ArgumentException("El dinero se guarda con la fecha de hoy.");
                if (FinanceRules.Summarize(snapshot.Data, Today).Available < f.Amount) throw new ArgumentException("No tienes suficiente dinero libre para guardar ese monto.");
                db.Entries.Add(new() { UserId = userId, RequestKey = f.RequestKey, Kind = 2, GoalId = f.GoalId, Amount = f.Amount, Date = f.Date.Date });
                break;
            case "goals":
                if (string.IsNullOrWhiteSpace(f.Name)) throw new ArgumentException("Introduce el nombre de tu meta.");
                var goal = f.Id == 0 ? new Goal { UserId = userId, RequestKey = f.RequestKey }
                    : await db.Goals.SingleOrDefaultAsync(x => x.UserId == userId && x.Id == f.Id) ?? throw new ArgumentException("Meta no disponible.");
                goal.Name = f.Name.Trim();
                goal.Description = f.Description ?? "";
                goal.Target = f.Amount;
                goal.Deadline = f.Date.Date;
                if (f.Id == 0) db.Goals.Add(goal);
                break;
        }
        // La revisión evita que dos sesiones reserven el mismo saldo al mismo tiempo.
        user.Revision++;
        await db.SaveChangesAsync();
        var after = await Load(userId);
        if (after.Data.Contributions.Count > 0 && FinanceRules.Summarize(after.Data, Today).Available < 0)
            throw new ArgumentException("Primero retira dinero de tus metas: este cambio dejaría tu saldo libre en negativo.");
        await tx.CommitAsync();
        log.LogInformation("Registro guardado en {Module} por usuario {UserId}", module, userId);
    }
    public Task Delete(long userId, string module, long id) => WithSerializationRetry(() => DeleteAttempt(userId, module, id));
    private async Task DeleteAttempt(long userId, string module, long id)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var user = await db.Users.SingleAsync(x => x.Id == userId);
        OwnedRow? row = module switch {
            "movements" => await db.Entries.SingleOrDefaultAsync(x => x.UserId == userId && x.Id == id && x.Kind < 2),
            "contributions" => await db.Entries.SingleOrDefaultAsync(x => x.UserId == userId && x.Id == id && x.Kind == 2),
            "goals" => await db.Goals.SingleOrDefaultAsync(x => x.UserId == userId && x.Id == id),
            _ => null
        };
        if (row is null) throw new ArgumentException("Registro no disponible.");
        if (row is Goal && await db.Entries.AnyAsync(x => x.UserId == userId && x.GoalId == id)) throw new ArgumentException("Retira lo guardado antes de eliminar la meta.");
        db.Remove(row);
        user.Revision++;
        await db.SaveChangesAsync();
        var snapshot = await Load(userId);
        if (snapshot.Data.Contributions.Count > 0 && FinanceRules.Summarize(snapshot.Data, Today).Available < 0)
            throw new ArgumentException("Primero retira dinero de tus metas: ese ingreso forma parte de lo guardado.");
        await tx.CommitAsync();
        log.LogInformation("Registro eliminado en {Module} por usuario {UserId}", module, userId);
    }
    private async Task WithSerializationRetry(Func<Task> operation)
    {
        for (var attempt = 0; ; attempt++) {
            try { await operation(); return; }
            catch (Exception ex) when (ex is OracleException { Number:8177 } || ex is DbUpdateException { InnerException: OracleException { Number:8177 } }) {
                // Oracle revirtió la transacción: repetir con datos actualizados.
                db.ChangeTracker.Clear();
                if (attempt >= 3) throw new DbUpdateException("Tus datos cambiaron. Recarga y vuelve a intentarlo.", ex);
                await Task.Delay(100 * (attempt + 1));
            }
        }
    }
}


public static class FinanceRules
{
    public static readonly string[] IncomeCategories = ["Mesada", "Beca", "Trabajo", "Emprendimiento", "Otros"];
    public static readonly string[] ExpenseCategories = ["Alimentación", "Transporte", "Educación", "Vivienda", "Entretenimiento", "Salud", "Compras", "Otros"];

    public static void ValidateAmount(decimal amount)
    {
        if (amount <= 0 || amount > 1_000_000_000m || decimal.Round(amount, 2) != amount)
            throw new ArgumentException("Escribe un monto mayor que cero, con hasta dos decimales y máximo 1.000.000.000.");
    }

    public static void ValidateDate(DateOnly date)
    {
        if (date.Year < 1900 || date.Year > 2200)
            throw new ArgumentException("La fecha debe estar entre 1900 y 2200.");
    }

    public static FinancialSummary Summarize(FinanceData data, DateOnly today)
    {
        var actual = data.Movements.Where(m => m.Date <= today).ToArray();
        var month = actual.Where(m => m.Date.Year == today.Year && m.Date.Month == today.Month).ToArray();
        var income = month.Where(m => m.Kind == MovementKind.Income).Sum(m => m.Amount);
        var expense = month.Where(m => m.Kind == MovementKind.Expense).Sum(m => m.Amount);
        var balance = actual.Sum(m => m.Kind == MovementKind.Income ? m.Amount : -m.Amount);
        return new(balance, income, expense, balance - data.Contributions.Sum(c => c.Amount));
    }

    public static MonthlyTotal[] History(FinanceData data, DateOnly today)
    {
        var start = new DateOnly(today.Year, today.Month, 1).AddMonths(-5);
        return Enumerable.Range(0, 6).Select(i => {
            var date = start.AddMonths(i);
            var entries = data.Movements.Where(m => m.Date.Year == date.Year && m.Date.Month == date.Month && m.Date <= today).ToArray();
            return new MonthlyTotal(date.ToString("MMM yy", System.Globalization.CultureInfo.GetCultureInfo("es-BO")),
                entries.Where(m => m.Kind == MovementKind.Income).Sum(m => m.Amount),
                entries.Where(m => m.Kind == MovementKind.Expense).Sum(m => m.Amount));
        }).ToArray();
    }
}
