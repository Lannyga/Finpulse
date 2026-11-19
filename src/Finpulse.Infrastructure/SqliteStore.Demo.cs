using System.Globalization;
using System.Security.Cryptography;
using Finpulse.Application;
using Finpulse.Domain;

namespace Finpulse.Infrastructure;

public sealed record DemoAccountResult(string Email, string Password, long UserId, DateOnly From, DateOnly Through,
    int MovementCount, int BudgetCount, int GoalCount, int ContributionCount, FinancialSummary Summary);

public sealed partial class SqliteStore
{
    /// <summary>Explicit development action only. Inserts a new synthetic account atomically; never updates an existing account.</summary>
    public Task<DemoAccountResult> CreateDemoAccountAsync(DateOnly asOf) => Task.Run(() =>
    {
        FinanceRules.ValidateDate(asOf);
        var first = new DateOnly(asOf.Year, asOf.Month, 1).AddMonths(-6);
        string email = $"demo.6meses.{Convert.ToHexString(RandomNumberGenerator.GetBytes(4)).ToLowerInvariant()}@finpulse.test";
        string password = "Demo!" + Convert.ToHexString(RandomNumberGenerator.GetBytes(10));
        string hash = new Pbkdf2PasswordHasher().HashPassword(password);
        using var db = Open(); using var tx = db.BeginTransaction();
        using var user = Command(db, tx, "INSERT INTO users(name,email,hash) VALUES(@p0,@p1,@p2) RETURNING id", "DEMO - Estudiante (historial ficticio)", email, hash);
        long userId = Convert.ToInt64(user.ExecuteScalar(), CultureInfo.InvariantCulture);

        int movementCount = 0, budgetCount = 0, goalCount = 0, contributionCount = 0;
        string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        void Movement(MovementKind kind, decimal amount, string category, DateOnly date, string description)
        {
            if (date > asOf) return;
            FinanceRules.ValidateMovement(new(0,kind,amount,category,date,description));
            using var cmd = Command(db, tx, "INSERT INTO movements(user_id,kind,cents,category,date,description) VALUES(@p0,@p1,@p2,@p3,@p4,@p5)",
                userId,(int)kind,Cents(amount),category,Iso(date),"[DEMO FICTICIA] " + description);
            cmd.ExecuteNonQuery(); movementCount++;
        }
        void Budget(DateOnly month, string category, decimal amount)
        {
            using var cmd = Command(db, tx, "INSERT INTO budgets(user_id,month,category,cents) VALUES(@p0,@p1,@p2,@p3)",
                userId,month.ToString("yyyy-MM",CultureInfo.InvariantCulture),category,Cents(amount));
            cmd.ExecuteNonQuery(); budgetCount++;
        }
        decimal[] incomes = [1400,1400,1500,1550,1650,1700,1700];
        decimal[] expenses = [1120,1280,1690,1460,1240,1180,1260];
        decimal[] budgets = [1200,1200,1300,1400,1400,1400,1300];
        int[] shares = [36,12,16,10,10,6,6,4];
        for (int monthIndex = 0; monthIndex < 7; monthIndex++)
        {
            var month = first.AddMonths(monthIndex);
            Movement(MovementKind.Income,150,"Mesada",month,"Apoyo familiar del mes");
            Movement(MovementKind.Income,250,"Beca",month.AddDays(1),"Beca de apoyo universitario");
            Movement(MovementKind.Income,incomes[monthIndex]-400,"Trabajo",month.AddDays(4),"Trabajo de medio tiempo");
            long totalCents = Cents(expenses[monthIndex]), allocated = 0;
            for(int categoryIndex=0;categoryIndex<shares.Length;categoryIndex++)
            {
                long categoryCents = categoryIndex == shares.Length-1 ? totalCents-allocated : totalCents*shares[categoryIndex]/100;
                allocated += categoryCents;
                for(int week=0;week<4;week++)
                {
                    long payment = week==3 ? categoryCents-3*(categoryCents/4) : categoryCents/4;
                    int day = Math.Min(DateTime.DaysInMonth(month.Year,month.Month),6+week*7+categoryIndex%7);
                    Movement(MovementKind.Expense,payment/100m,FinanceRules.ExpenseCategories[categoryIndex],new(month.Year,month.Month,day),
                        $"{FinanceRules.ExpenseCategories[categoryIndex]} - semana {week+1}");
                }
            }
            Budget(month,"General",budgets[monthIndex]); Budget(month,"Alimentación",budgets[monthIndex]*.35m);
        }
        long Goal(string name, string description, decimal target, DateOnly date)
        {
            FinanceRules.ValidateDate(date);
            using var cmd = Command(db,tx,"INSERT INTO goals(user_id,name,description,cents,deadline) VALUES(@p0,@p1,@p2,@p3,@p4) RETURNING id",
                userId,name,"[DEMO FICTICIA] "+description,Cents(target),Iso(date));
            goalCount++; return Convert.ToInt64(cmd.ExecuteScalar(),CultureInfo.InvariantCulture);
        }
        long laptop = Goal("DEMO - Computadora para estudiar","Meta exigente para comparar el plazo con la capacidad de ahorro.",4500,asOf.AddMonths(6));
        long emergency = Goal("DEMO - Fondo de emergencia","Reserva que progresa con los aportes de los últimos meses.",1200,asOf.AddMonths(3));
        long books = Goal("DEMO - Libros del semestre","Ejemplo de una meta ya cumplida.",300,asOf.AddMonths(-1));
        void Contribution(long goalId, decimal amount, DateOnly date)
        {
            using var cmd = Command(db,tx,"INSERT INTO contributions(user_id,goal_id,cents,date) VALUES(@p0,@p1,@p2,@p3)",userId,goalId,Cents(amount),Iso(date));
            cmd.ExecuteNonQuery(); contributionCount++;
        }
        var current = first.AddMonths(6);
        Contribution(laptop,200,current.AddMonths(-2).AddDays(24)); Contribution(laptop,300,current.AddMonths(-1).AddDays(24));
        Contribution(emergency,150,current.AddMonths(-2).AddDays(24)); Contribution(emergency,150,current.AddMonths(-1).AddDays(24));
        Contribution(books,100,current.AddMonths(-2).AddDays(24)); Contribution(books,200,current.AddMonths(-1).AddDays(24));
        var data = Load(db,tx,userId); var summary = FinanceRules.Summarize(data,asOf);
        if(summary.Available < 0 || data.Movements.Any(m=>m.Date>asOf)) throw new InvalidOperationException("La demostración no tiene un saldo coherente.");
        tx.Commit();
        return new DemoAccountResult(email,password,userId,first,asOf,movementCount,budgetCount,goalCount,contributionCount,summary);
    });
}
