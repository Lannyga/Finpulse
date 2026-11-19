using System.IO;
using Finpulse.Application;
using Finpulse.Domain;
using Finpulse.Infrastructure;

internal static class DemoAccountCommands
{
    public static async Task CreateLocalAsync()
    {
        var store = new StorageConfiguration().CreateLocal();
        var account = await store.CreateDemoAccountAsync(DateOnly.FromDateTime(DateTime.Today));
        var login = await new AuthService(store,new Pbkdf2PasswordHasher()).LoginAsync(account.Email,account.Password);
        if(!login.Exitoso || login.Usuario?.Id!=account.UserId)throw new InvalidOperationException("La cuenta se creó, pero no pasó la verificación de acceso.");
        Console.WriteLine("CUENTA DEMO CREADA Y ACCESO VERIFICADO - datos totalmente ficticios");
        Console.WriteLine("Base SQLite: "+store.DatabasePath);
        Console.WriteLine("Correo: "+account.Email);
        Console.WriteLine("Contraseña: "+account.Password);
        Console.WriteLine($"Historial: {account.From:yyyy-MM-dd} a {account.Through:yyyy-MM-dd}");
        Console.WriteLine($"Movimientos: {account.MovementCount}; presupuestos: {account.BudgetCount}; metas: {account.GoalCount}; aportes: {account.ContributionCount}");
        Console.WriteLine($"Saldo: Bs {account.Summary.Balance:N2}; reservado: Bs {account.Summary.Balance-account.Summary.Available:N2}; libre: Bs {account.Summary.Available:N2}");
        Console.WriteLine("Usa el proveedor SQLite e inicia sesión manualmente. No se activó acceso automático.");
    }
    public static async Task VerifyAsync()
    {
        string folder=Path.Combine(Path.GetTempPath(),"Finpulse-demo-qa-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        try
        {
            var store=new SqliteStore(Path.Combine(folder,"demo.db"));
            await store.CreateAsync("Usuario anterior", "anterior@example.test", new Pbkdf2PasswordHasher().HashPassword(Guid.NewGuid().ToString("N")));
            await store.SaveMovementAsync(1,new(0,MovementKind.Income,25,"Otros",new DateOnly(2026,5,1),"Dato anterior a la demostración"));
            var date=new DateOnly(2026,11,19); var demo=await store.CreateDemoAccountAsync(date);
            var data=await store.LoadAsync(demo.UserId);var analysis=new FinancialAnalysisService();
            if(demo.MovementCount!=229 || demo.BudgetCount!=14 || demo.GoalCount!=3 || demo.ContributionCount!=6)throw new InvalidOperationException("Demostración incompleta");
            if(demo.Summary.Balance!=2300 || demo.Summary.Available!=1200 || demo.Summary.Income!=1700 || demo.Summary.Expense!=630)throw new InvalidOperationException("Totales incorrectos en la demostración");
            if(data.Movements.Any(m=>m.Date>date)||data.Movements.Min(m=>m.Date)!=new DateOnly(2026,5,1))throw new InvalidOperationException("Fechas incorrectas en la demostración");
            var history=analysis.History(data,date,7);
            if(!history.Any(h=>h.Savings<0)||!history.Any(h=>h.Savings>0)||!analysis.Baseline(data,date).Sufficient)throw new InvalidOperationException("Faltan variaciones o historial suficiente");
            var login=await new AuthService(store,new Pbkdf2PasswordHasher()).LoginAsync(demo.Email,demo.Password);
            if(!login.Exitoso)throw new InvalidOperationException("Credenciales demo inválidas");
            var second=await store.CreateDemoAccountAsync(date);
            if(second.Email==demo.Email||second.Password==demo.Password||(await store.LoadAsync(demo.UserId)).Movements.Count!=229)throw new InvalidOperationException("Creación repetida no aislada");
            if((await new SqliteStore(store.DatabasePath).LoadAsync(second.UserId)).Movements.Count!=229)throw new InvalidOperationException("La demostración no persiste");
            var existing=await store.LoadAsync(1);
            if(existing.Movements.Count!=1||existing.Movements[0].Amount!=25||existing.Goals.Count!=0||existing.Budgets.Count!=0)throw new InvalidOperationException("La cuenta anterior fue modificada");
            Console.WriteLine("DEMO QA OK: credenciales, 229 movimientos, 6 meses completos y mes actual parcial, totales, variación, aislamiento y persistencia.");
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); Directory.Delete(folder,true); }
    }
}
