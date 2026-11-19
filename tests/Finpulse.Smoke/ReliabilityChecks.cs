using System.IO;
using Finpulse.Application;
using Finpulse.Domain;
using Finpulse.Infrastructure;
using Microsoft.Data.Sqlite;

internal static class ReliabilityChecks
{
    public static async Task RunAsync()
    {
        int checks = 0; List<string> failures = [];
        void Check(bool result, string label)
        {
            checks++; Console.WriteLine($"QA {(result ? "OK" : "FALLO")}: {label}");
            if (!result) failures.Add(label);
        }
        void Reject(Action action, string label)
        {
            try { action(); Check(false, label); }
            catch (ArgumentException) { Check(true, label); }
            catch (Exception ex) { Check(false, label + " — " + ex.GetType().Name); }
        }
        var analysis = new FinancialAnalysisService();
        var start = new DateOnly(2026, 11, 19);
        var request = new ProjectionRequest(start, 120, 0, 100, 0, new());
        var goal = new SavingGoal(1, "Equipo", "", 100, start.AddMonths(3));
        Check(analysis.EstimateGoal(goal, 0, request).RequiredMonthly == 33.34m, "El aporte requerido redondea hacia arriba para no dejar una meta corta por centavos");
        Check(analysis.EstimateGoal(goal with { Deadline = start.AddMonths(601) }, 0, request).RequiredMonthly is null, "No se sustituye un plazo mayor de 600 meses por uno de 600");
        Reject(() => MonthlyProjectionEngine.MonthlyFactor(-99.99999999999999999999999999m), "Tasas no representables fallan con validación, no división por cero");
        Reject(() => MonthlyProjectionEngine.Validate(null!), "Solicitud nula se valida sin NullReferenceException");
        Reject(() => MonthlyProjectionEngine.Validate(request with { Assumptions = null! }), "Supuestos nulos se validan sin NullReferenceException");
        Reject(() => analysis.EstimateGoal(goal with { Target = 0 }, 0, request), "Objetivo cero rechazado");
        Reject(() => analysis.EstimateGoal(goal, -1, request), "Acumulado negativo rechazado");
        try
        {
            var reached = analysis.EstimateGoal(goal with { Target = 1_000_000_000m, Deadline = start.AddYears(2) }, 500_000_000m,
                request with { Months = 600, MonthlyIncome = 0, Assumptions = new(AnnualReturn: 100) });
            Check(reached.MonthsNeeded == 12, "La meta alcanzada no se invalida por un desbordamiento décadas después");
        }
        catch (Exception ex) { Check(false, "Estimación de meta a largo plazo — " + ex.GetType().Name); }
        var small = analysis.EstimateGoal(goal with { Target = .01m }, 0, request with { MonthlyIncome = .005m });
        Check(small.MonthsNeeded == 2, "No se declara una meta cumplida solo por redondear el saldo visible");

        string folder = Path.Combine(Path.GetTempPath(), "Finpulse-QA-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            string path = Path.Combine(folder, "future.db");
            using (var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString()))
            {
                db.Open(); using var cmd = db.CreateCommand();
                cmd.CommandText = "CREATE TABLE future_marker(value TEXT); INSERT INTO future_marker VALUES('preservar'); PRAGMA user_version=9;"; cmd.ExecuteNonQuery();
            }
            bool refused = false;
            try { _ = new SqliteStore(path); } catch (InvalidOperationException) { refused = true; }
            Check(refused, "Se rechaza un esquema SQLite de versión posterior");
            using (var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString()))
            {
                db.Open(); using var cmd = db.CreateCommand(); cmd.CommandText = "PRAGMA user_version";
                Check(Convert.ToInt64(cmd.ExecuteScalar()) == 9, "La versión futura se conserva intacta");
                cmd.CommandText = "SELECT value FROM future_marker"; Check((string?)cmd.ExecuteScalar() == "preservar", "Los datos de la versión futura no se modifican");
                cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='users'";
                Check(Convert.ToInt64(cmd.ExecuteScalar()) == 0, "No se crean tablas de una versión vieja sobre una base futura");
            }
            var store = new SqliteStore(Path.Combine(folder, "local.db"));
            await store.CreateAsync("Usuario QA", "qa@example.test", new Pbkdf2PasswordHasher().HashPassword(Guid.NewGuid().ToString("N")));
            await store.SaveMovementAsync(1,new(0,MovementKind.Income,10,"Otros",DateOnly.FromDateTime(DateTime.Today),"QA"));
            Check((await new SqliteStore(store.DatabasePath).LoadAsync(1)).Movements.Count == 1,"Inicialización repetida conserva movimientos");
        }
        finally { SqliteConnection.ClearAllPools(); Directory.Delete(folder, true); }
        var timer = System.Diagnostics.Stopwatch.StartNew();
        var runs = Enumerable.Range(0, 3).Select(_ => new MonthlyProjectionEngine().Calculate(request with { Months = 600, MonthlyIncome = 1200, MonthlyExpense = 850 })).ToArray();
        timer.Stop();
        Check(runs.All(r => r.Count == 600 && r[^1].Balance == 210000), "Horizonte máximo: tres series de 600 meses correctas");
        Console.WriteLine($"PERF: tres proyecciones de 600 meses en {timer.Elapsed.TotalMilliseconds:F2} ms; medición local, no garantía de rendimiento universal.");
        var command = new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath ?? throw new InvalidOperationException("Ruta del proceso no disponible."))
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
        if (Path.GetFileNameWithoutExtension(command.FileName).Equals("dotnet", StringComparison.OrdinalIgnoreCase)) command.ArgumentList.Add(typeof(ReliabilityChecks).Assembly.Location);
        command.ArgumentList.Add("--opcion-qa-inexistente");
        using (var child = System.Diagnostics.Process.Start(command) ?? throw new InvalidOperationException("No se inició el proceso de comprobación."))
        {
            var error = child.StandardError.ReadToEndAsync(); var output = child.StandardOutput.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            try { await child.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException) { child.Kill(entireProcessTree: true); throw new TimeoutException("El proceso de diagnóstico no terminó en 20 segundos."); }
            string detail = await error; await output;
            Check(child.ExitCode == 1, "Un argumento de prueba desconocido produce código 1, no éxito engañoso");
            Check(detail.Contains("ArgumentException") && detail.Contains("Argumentos no reconocidos"), "Los fallos del runner muestran excepción completa en stderr sin ocultarla");
        }
        Console.WriteLine($"QA: {checks} comprobaciones, {failures.Count} fallos.");
        if(failures.Count>0)throw new InvalidOperationException("Regresiones QA:\n"+string.Join("\n",failures));
    }
}
