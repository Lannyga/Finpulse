using Finpulse.Application;
using System.IO;
using Finpulse.Domain;
using Finpulse.Infrastructure;

internal static class LocalFinanceChecks
{
    public static async Task RunAsync()
    {
        int checks = 0;
        void Check(bool ok, string name) { if (!ok) throw new InvalidOperationException(name); Console.WriteLine($"LOCAL {++checks}: {name}"); }
        async Task Reject(Func<Task> action, string name)
        {
            bool rejected = false;
            try { await action(); } catch (ArgumentException) { rejected = true; } catch (Microsoft.Data.Sqlite.SqliteException) { rejected = true; }
            Check(rejected, name);
        }
        string folder = Path.Combine(Path.GetTempPath(), "Finpulse-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            string path = Path.Combine(folder, "test.db");
            var store = new SqliteStore(path); var hasher = new Pbkdf2PasswordHasher(); var auth = new AuthService(store, hasher);
            string password = Guid.NewGuid().ToString("N") + "!";
            Check((await auth.RegisterAsync("Usuario uno", "uno@example.test", password, password)).Exitoso, "registro SQLite");
            Check(!(await auth.RegisterAsync("Usuario uno", " UNO@example.test ", password, password)).Exitoso, "correo único normalizado");
            Check((await auth.RegisterAsync("Usuario dos", "dos@example.test", password, password)).Exitoso, "segundo usuario");
            var login = await auth.LoginAsync("uno@example.test", password);
            Check(login.Exitoso && login.Usuario is not null, "acceso SQLite");
            long u = login.Usuario?.Id ?? throw new InvalidOperationException("Sin sesión");
            long other = (await auth.LoginAsync("dos@example.test", password)).Usuario?.Id ?? throw new InvalidOperationException("Sin sesión");
            Check((await store.LoadAsync(u)).Movements.Count == 0, "sin datos de demostración");
            var date = DateOnly.FromDateTime(DateTime.Today);
            string budgetMonth = date.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture);
            await store.SaveMovementAsync(u, new(0, MovementKind.Income, 1000.10m, "Beca", date, "Ingreso"));
            await store.SaveMovementAsync(u, new(0, MovementKind.Income, 499.90m, "Trabajo", date, "Trabajo"));
            await store.SaveMovementAsync(u, new(0, MovementKind.Expense, 300m, "Alimentación", date, "Gasto"));
            await store.SaveMovementAsync(u, new(0, MovementKind.Expense, 100m, "Transporte", date, "Bus"));
            await store.SaveMovementAsync(u, new(0, MovementKind.Income, 50m, "Otros", date.AddMonths(-1), "Mes anterior"));
            var data = await store.LoadAsync(u); var summary = FinanceRules.Summarize(data, date);
            Check(summary.Income == 1500m, "ingresos del mes exactos");
            Check(summary.Expense == 400m, "gastos del mes"); Check(summary.Balance == 1150m, "saldo histórico");
            Check(summary.Savings == 1100m, "ahorro mensual"); Check(summary.SavingsRate == 1100m / 1500m * 100, "tasa de ahorro");
            Check(FinanceRules.Summarize(new([],[],[],[]), date).SavingsRate == 0, "tasa sin ingresos");
            Check(summary.Available == 1150m, "saldo sin reservas");
            Check((await store.LoadAsync(other)).Movements.Count == 0, "aislamiento de consulta");
            foreach (decimal invalid in new[] { 0m, -1m, 1.001m, 1_000_000_001m })
                await Reject(() => store.SaveMovementAsync(u, new(0, MovementKind.Expense, invalid, "Otros", date, "")), "monto inválido " + invalid);
            await Reject(() => store.SaveMovementAsync(u, new(0, MovementKind.Income, 1m, "Salud", date, "")), "categoría de otro tipo");
            await Reject(() => store.SaveMovementAsync(u, new(0, MovementKind.Income, 1m, "Otros", DateOnly.MinValue, "")), "fecha inválida");
            var income = data.Movements.Single(m => m.Amount == 1000.10m);
            await Reject(() => store.SaveMovementAsync(other, income with { Amount = 10 }), "aislamiento de edición");
            await Reject(() => store.DeleteMovementAsync(other, income.Id), "aislamiento de eliminación");
            await store.SaveMovementAsync(u, income with { Amount = 1000.11m });
            Check((await store.LoadAsync(u)).Movements.Single(m => m.Id == income.Id).Amount == 1000.11m, "edición con centavos");
            await store.SaveMovementAsync(u, income);
            var reopened = new SqliteStore(path);
            Check((await reopened.LoadAsync(u)).Movements.Count == 5, "persistencia tras recrear almacén y conexiones");
            Check((await new AuthService(reopened, hasher).LoginAsync("uno@example.test", password)).Exitoso, "cuenta persistente");
            await store.SaveBudgetAsync(u, new(0, budgetMonth, "General", 500));
            var budget = (await store.LoadAsync(u)).Budgets.Single(); var usage = FinanceRules.Evaluate(budget, data.Movements);
            Check(usage.Spent == 400 && usage.Available == 100 && usage.Percent == 80 && usage.Status == "Atención al límite", "aviso al 80 %");
            Check(FinanceRules.Evaluate(budget with { Amount = 400 }, data.Movements).Percent == 100, "límite al 100 %");
            Check(FinanceRules.Evaluate(budget with { Amount = 200 }, data.Movements).Available == -200, "presupuesto superado");
            Check(FinanceRules.Evaluate(budget with { Category = "Transporte" }, data.Movements).Spent == 100, "presupuesto por categoría");
            Check(FinanceRules.Evaluate(budget, data.Movements, 90).Status == "Dentro del presupuesto", "umbral configurable");
            await Reject(() => store.SaveBudgetAsync(u, new(0, budgetMonth, "General", 100)), "presupuesto duplicado");
            await Reject(() => store.SaveBudgetAsync(u, new(0, "2026-99", "General", 100)), "mes inválido");
            await Reject(() => store.SaveBudgetAsync(other, budget), "presupuesto ajeno");
            await store.SaveBudgetAsync(u, budget with { Amount = 600 });
            Check((await store.LoadAsync(u)).Budgets.Single().Amount == 600, "edición presupuesto");
            await store.SaveGoalAsync(u, new(0, "Computadora", "Para estudiar", 2000, date.AddYears(1)));
            var goal = (await store.LoadAsync(u)).Goals.Single();
            await store.SaveContributionAsync(u, new(0, goal.Id, 500, date));
            data = await store.LoadAsync(u); summary = FinanceRules.Summarize(data, date);
            Check(summary.Balance == 1150 && summary.Available == 650, "aporte reserva sin doble contabilización");
            Check(summary.Income == 1500 && summary.Expense == 400, "aporte no altera movimientos");
            var contribution = data.Contributions.Single();
            await Reject(() => store.SaveContributionAsync(other, new(0, goal.Id, 1, date)), "aporte a meta ajena");
            await Reject(() => store.SaveContributionAsync(u, new(0, goal.Id, 651, date)), "aporte sin saldo");
            Check((await store.LoadAsync(u)).Contributions.Count == 1, "rollback de aporte rechazado");
            await Reject(() => store.DeleteMovementAsync(u, income.Id), "protección de saldo reservado al borrar ingreso");
            await Reject(() => store.SaveMovementAsync(u, new(0, MovementKind.Expense, 651, "Otros", date, "")), "protección al gastar saldo reservado");
            Check((await store.LoadAsync(u)).Movements.Count == 5, "rollback de movimiento rechazado");
            await store.SaveContributionAsync(u, contribution with { Amount = 300 });
            Check(FinanceRules.Summarize(await store.LoadAsync(u), date).Available == 850, "editar aporte libera saldo");
            await Reject(() => store.DeleteContributionAsync(other, contribution.Id), "aislamiento de aportes");
            await store.SaveGoalAsync(u, goal with { Name = "Portátil" });
            Check((await store.LoadAsync(u)).Goals.Single().Name == "Portátil", "editar meta");
            await store.DeleteGoalAsync(u, goal.Id);
            Check((await store.LoadAsync(u)).Contributions.Count == 0, "eliminar meta libera aportes en cascada");
            await store.DeleteBudgetAsync(u, budget.Id); Check((await store.LoadAsync(u)).Budgets.Count == 0, "eliminar presupuesto");
            await store.DeleteMovementAsync(u, income.Id); Check((await store.LoadAsync(u)).Movements.Count == 4, "eliminar movimiento");
            var projection = new ProjectionService();
            foreach (int years in new[] {1,3,5,10})
            {
                var points = projection.Calculate(100, 1000, 700, 0, 0, 0, years);
                Check(points.Count == years && points[^1].Balance == 100 + 3600 * years, "proyección determinista " + years + " años");
            }
            Check(projection.Calculate(0, 100, 0, 10, 0, 0, 2)[^1].Balance == 2520, "variación anual de ingresos");
            Check(projection.Calculate(0, 0, 100, 0, 10, 0, 2)[^1].Balance == -2520, "variación anual de gastos");
            Check(projection.Calculate(0, 100, 0, 0, 0, 10, 1)[0].RealBalance == 1090.91m, "inflación descuenta saldo real");
            await Reject(() => { projection.Calculate(0, 1, 1, 0, 0, -1, 1); return Task.CompletedTask; }, "inflación inválida");
            for (int i = 0; i < 5; i++) Check(!(await auth.LoginAsync("dos@example.test", "incorrecta")).Exitoso, "fallo de acceso " + (i+1));
            Check(!(await auth.LoginAsync("dos@example.test", password)).Exitoso, "bloqueo temporal tras cinco fallos");
            using (var db = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=" + path))
            {
                db.Open(); using var cmd = db.CreateCommand(); cmd.CommandText = "SELECT hash FROM users WHERE id=" + u;
                Check(cmd.ExecuteScalar() is string hash && hash.StartsWith("PBKDF2-SHA256$") && !hash.Contains(password), "contraseña almacenada como hash");
                cmd.CommandText = "PRAGMA integrity_check"; Check((string?)cmd.ExecuteScalar() == "ok", "integridad SQLite");
            }
            var info = new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath ?? throw new InvalidOperationException()) { UseShellExecute = false };
            if (Path.GetFileNameWithoutExtension(info.FileName).Equals("dotnet", StringComparison.OrdinalIgnoreCase)) info.ArgumentList.Add(typeof(LocalFinanceChecks).Assembly.Location);
            info.ArgumentList.Add("--verify-local-reopen"); info.ArgumentList.Add(path); info.ArgumentList.Add(u.ToString());
            using var child = System.Diagnostics.Process.Start(info) ?? throw new InvalidOperationException("Proceso no iniciado");
            using var reopenTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            try { await child.WaitForExitAsync(reopenTimeout.Token); }
            catch (OperationCanceledException) { child.Kill(entireProcessTree: true); throw new TimeoutException("El proceso de persistencia no terminó en 20 segundos."); }
            Check(child.ExitCode == 0, "persistencia verificada desde otro proceso");
            Console.WriteLine($"OK: {checks} comprobaciones locales y financieras.");
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); Directory.Delete(folder, recursive: true); }
    }
}
