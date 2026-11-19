using System.Data;
using System.Security.Cryptography;
using Finpulse.Application;
using Finpulse.Domain;
using Finpulse.Web.Data;
using Finpulse.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace Finpulse.Web.Services;

public sealed record DemoAccountResult(string Username, string Password, string RecoveryCode, long UserId,
    DateOnly From, DateOnly Through, int Movements, int Budgets, int Goals, int Contributions, FinancialSummary Summary);

/// <summary>Explicit local demo only. Creates a new account and synthetic history atomically; never changes other accounts.</summary>
public static class DemoAccount
{
    public static async Task<DemoAccountResult> Create(PortalDb db, AccountService accounts, PortalFinance finance, DateOnly asOf)
    {
        if (!db.Database.IsSqlite()) throw new InvalidOperationException("La demostración solo puede crearse en SQLite local.");
        FinanceRules.ValidateDate(asOf);
        var first = new DateOnly(asOf.Year, asOf.Month, 1).AddMonths(-6);
        var current = first.AddMonths(6);
        string username = "demo_6meses_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(4)).ToLowerInvariant();
        string password = "Demo!" + Convert.ToHexString(RandomNumberGenerator.GetBytes(10));
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var registered = await accounts.Register(new() { Username = username, Password = password, Confirm = password });
        long owner = registered.User.Id;
        var categories = await db.Categories.ToArrayAsync();
        void Movement(int kind, decimal amount, string category, DateOnly date, string description)
        {
            if (date > asOf) return;
            FinanceRules.ValidateMovement(new(0, (MovementKind)kind, amount, category, date, description));
            db.Entries.Add(new() { UserId = owner, Kind = kind, Amount = amount,
                CategoryId = categories.Single(x => x.Kind == kind && x.Name == category).Id,
                Date = date.ToDateTime(TimeOnly.MinValue), Description = "[DEMO FICTICIA] " + description });
        }
        void Budget(DateOnly month, string category, decimal amount)
        {
            db.Budgets.Add(new() { UserId = owner, Month = month.ToString("yyyy-MM"), Scope = category, Amount = amount,
                CategoryId = category == "General" ? null : categories.Single(x => x.Kind == 1 && x.Name == category).Id });
        }
        // Same seven-month pattern as the original desktop demo: six complete months plus the current month to date.
        decimal[] incomes = [1400,1400,1500,1550,1650,1700,1700];
        decimal[] expenses = [1120,1280,1690,1460,1240,1180,1260];
        decimal[] limits = [1200,1200,1300,1400,1400,1400,1300];
        int[] shares = [36,12,16,10,10,6,6,4];
        for (int index = 0; index < 7; index++)
        {
            var month = first.AddMonths(index);
            Movement(0,150,"Mesada",month,"Apoyo familiar del mes");
            Movement(0,250,"Beca",month.AddDays(1),"Beca de apoyo universitario");
            Movement(0,incomes[index]-400,"Trabajo",month.AddDays(4),"Trabajo de medio tiempo");
            long total = checked((long)(expenses[index] * 100)), assigned = 0;
            for (int categoryIndex = 0; categoryIndex < shares.Length; categoryIndex++)
            {
                long cents = categoryIndex == shares.Length - 1 ? total - assigned : total * shares[categoryIndex] / 100;
                assigned += cents;
                for (int week = 0; week < 4; week++)
                {
                    long payment = week == 3 ? cents - 3 * (cents / 4) : cents / 4;
                    int day = Math.Min(DateTime.DaysInMonth(month.Year,month.Month),6+week*7+categoryIndex%7);
                    Movement(1,payment/100m,FinanceRules.ExpenseCategories[categoryIndex],new(month.Year,month.Month,day),
                        $"{FinanceRules.ExpenseCategories[categoryIndex]} · semana {week+1}");
                }
            }
            Budget(month,"General",limits[index]);
            Budget(month,"Alimentación",limits[index]*.35m);
        }
        Goal Goal(string name, string description, decimal target, DateOnly deadline)
        {
            var goal = new Goal { UserId = owner, Name = "DEMO · " + name, Description = "[DEMO FICTICIA] " + description,
                Target = target, Deadline = deadline.ToDateTime(TimeOnly.MinValue) };
            db.Goals.Add(goal); return goal;
        }
        var laptop = Goal("Computadora para estudiar","Meta exigente para comparar plazo y capacidad de ahorro.",4500,asOf.AddMonths(6));
        var emergency = Goal("Fondo de emergencia","Reserva con aportes de los últimos meses.",1200,asOf.AddMonths(3));
        var books = Goal("Libros del semestre","Ejemplo de una meta cumplida.",300,asOf.AddMonths(-1));
        await db.SaveChangesAsync();
        void Contribution(Goal goal, decimal amount, DateOnly date) => db.Entries.Add(new() {
            UserId = owner, GoalId = goal.Id, Kind = 2, Amount = amount, Date = date.ToDateTime(TimeOnly.MinValue),
            Description = "[DEMO FICTICIA] Asignación de ahorro; no es un gasto." });
        Contribution(laptop,200,current.AddMonths(-2).AddDays(24));
        Contribution(laptop,300,current.AddMonths(-1).AddDays(24));
        Contribution(emergency,150,current.AddMonths(-2).AddDays(24));
        Contribution(emergency,150,current.AddMonths(-1).AddDays(24));
        Contribution(books,100,current.AddMonths(-2).AddDays(24));
        Contribution(books,200,current.AddMonths(-1).AddDays(24));
        await db.SaveChangesAsync();
        var snapshot = await finance.Load(owner);
        var summary = FinanceRules.Summarize(snapshot.Data,asOf);
        var baseline = new FinancialAnalysisService().Baseline(snapshot.Data,asOf);
        if (!baseline.Sufficient || summary.Available < 0 || snapshot.Data.Movements.Any(x=>x.Date>asOf))
            throw new InvalidOperationException("El historial de demostración no es coherente.");
        foreach (int years in new[] {1,3,5,10})
            await finance.SaveScenario(owner,new() { Name = $"DEMO · Base {years} año(s) · sin rendimiento", Years = years,
                Capital = summary.Balance, Income = baseline.Income, Expense = baseline.Expense });
        await tx.CommitAsync();
        return new(username,password,registered.Code,owner,first,asOf,snapshot.Data.Movements.Count,
            snapshot.Data.Budgets.Count,snapshot.Data.Goals.Count,snapshot.Data.Contributions.Count,summary);
    }
}
