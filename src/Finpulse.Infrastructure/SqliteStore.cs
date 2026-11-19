using System.Globalization;
using Finpulse.Application;
using Finpulse.Domain;
using Microsoft.Data.Sqlite;

namespace Finpulse.Infrastructure;

/// <summary>Connections are short lived; all writes use an immediate transaction.</summary>
public sealed partial class SqliteStore : IUserRepository, IFinanceRepository
{
    private readonly string _connectionString;
    public string DatabasePath { get; }
    public SqliteStore(string path)
    {
        DatabasePath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath) ?? throw new ArgumentException("Ruta inválida."));
        _connectionString = new SqliteConnectionStringBuilder { DataSource = DatabasePath, ForeignKeys = true, DefaultTimeout = 10, Pooling = false }.ToString();
        using var db = Open();
        using (var version = db.CreateCommand())
        {
            version.CommandText = "PRAGMA user_version";
            if (Convert.ToInt64(version.ExecuteScalar(), CultureInfo.InvariantCulture) > 1)
                throw new InvalidOperationException("La base local pertenece a una versión más reciente de FINPULSE. Actualiza la aplicación; no se modificó su esquema.");
        }
        using (var journal = db.CreateCommand()) { journal.CommandText = "PRAGMA journal_mode=WAL"; journal.ExecuteNonQuery(); }
        using var schemaTransaction = db.BeginTransaction();
        using var cmd = db.CreateCommand();
        cmd.Transaction = schemaTransaction;
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS users(id INTEGER PRIMARY KEY, name TEXT NOT NULL, email TEXT NOT NULL UNIQUE,
                hash TEXT NOT NULL, failures INTEGER NOT NULL DEFAULT 0, locked_until TEXT);
            CREATE TABLE IF NOT EXISTS movements(id INTEGER PRIMARY KEY, user_id INTEGER NOT NULL REFERENCES users(id),
                kind INTEGER NOT NULL CHECK(kind IN (0,1)), cents INTEGER NOT NULL CHECK(cents>0), category TEXT NOT NULL, date TEXT NOT NULL, description TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS ix_movements_user_date ON movements(user_id,date);
            CREATE TABLE IF NOT EXISTS budgets(id INTEGER PRIMARY KEY, user_id INTEGER NOT NULL REFERENCES users(id),
                month TEXT NOT NULL, category TEXT NOT NULL, cents INTEGER NOT NULL CHECK(cents>0), UNIQUE(user_id,month,category));
            CREATE TABLE IF NOT EXISTS goals(id INTEGER PRIMARY KEY, user_id INTEGER NOT NULL REFERENCES users(id),
                name TEXT NOT NULL, description TEXT NOT NULL, cents INTEGER NOT NULL CHECK(cents>0), deadline TEXT NOT NULL, UNIQUE(id,user_id));
            CREATE TABLE IF NOT EXISTS contributions(id INTEGER PRIMARY KEY, user_id INTEGER NOT NULL REFERENCES users(id),
                goal_id INTEGER NOT NULL, cents INTEGER NOT NULL CHECK(cents>0), date TEXT NOT NULL,
                FOREIGN KEY(goal_id,user_id) REFERENCES goals(id,user_id) ON DELETE CASCADE);
            PRAGMA user_version=1;
            """;
        cmd.ExecuteNonQuery();
        schemaTransaction.Commit();
    }
    private SqliteConnection Open() { var db = new SqliteConnection(_connectionString); db.Open(); return db; }
    private static SqliteCommand Command(SqliteConnection db, SqliteTransaction? tx, string sql, params object[] values)
    {
        var cmd = db.CreateCommand(); cmd.Transaction = tx; cmd.CommandText = sql;
        for (int i = 0; i < values.Length; i++) cmd.Parameters.AddWithValue("@p" + i, values[i]);
        return cmd;
    }
    private static long Cents(decimal amount) { FinanceRules.ValidateAmount(amount); return checked((long)(amount * 100)); }
    public Task<bool> CreateAsync(string nombre, string correoNormalizado, string hash, CancellationToken ct = default) => Task.Run(() =>
    {
        ct.ThrowIfCancellationRequested(); using var db = Open();
        using var cmd = Command(db, null, "INSERT INTO users(name,email,hash) VALUES(@p0,@p1,@p2) ON CONFLICT(email) DO NOTHING", nombre, correoNormalizado, hash);
        return cmd.ExecuteNonQuery() == 1;
    }, ct);
    public Task<AuthResult> AuthenticateAsync(string correoNormalizado, string password, IPasswordHasher hasher, CancellationToken ct = default) => Task.Run(() =>
    {
        ct.ThrowIfCancellationRequested(); using var db = Open(); using var tx = db.BeginTransaction();
        using var cmd = Command(db, tx, "SELECT id,name,hash,failures,locked_until FROM users WHERE email=@p0", correoNormalizado);
        using var reader = cmd.ExecuteReader();
        const string denied = "Credenciales incorrectas o acceso temporalmente restringido.";
        if (!reader.Read()) return new AuthResult(false, denied);
        long id = reader.GetInt64(0); string name = reader.GetString(1), hash = reader.GetString(2); int failures = reader.GetInt32(3);
        bool locked = !reader.IsDBNull(4) && DateTimeOffset.Parse(reader.GetString(4), CultureInfo.InvariantCulture) > DateTimeOffset.UtcNow;
        reader.Close();
        if (locked) return new AuthResult(false, denied);
        bool valid = hasher.VerifyPassword(password, hash);
        failures = valid ? 0 : failures + 1;
        object until = failures >= 5 ? DateTimeOffset.UtcNow.AddMinutes(15).ToString("O") : DBNull.Value;
        using var update = Command(db, tx, "UPDATE users SET failures=@p0,locked_until=@p1 WHERE id=@p2", failures >= 5 ? 0 : failures, until, id);
        update.ExecuteNonQuery(); tx.Commit();
        return valid ? new AuthResult(true, "Sesión iniciada.", new(id, name, correoNormalizado)) : new AuthResult(false, denied);
    }, ct);
    public Task<FinanceData> LoadAsync(long userId) => Task.Run(() =>
    {
        using var db = Open(); using var tx = db.BeginTransaction(deferred: true);
        var data = Load(db, tx, userId); tx.Commit(); return data;
    });
    private static FinanceData Load(SqliteConnection db, SqliteTransaction tx, long user)
    {
        List<Movement> movements = []; List<Budget> budgets = []; List<SavingGoal> goals = []; List<Contribution> contributions = [];
        using (var cmd = Command(db, tx, "SELECT id,kind,cents,category,date,description FROM movements WHERE user_id=@p0 ORDER BY date DESC,id DESC", user))
        using (var r = cmd.ExecuteReader()) while (r.Read()) movements.Add(new(r.GetInt64(0), (MovementKind)r.GetInt32(1), r.GetInt64(2) / 100m, r.GetString(3), DateOnly.Parse(r.GetString(4), CultureInfo.InvariantCulture), r.GetString(5)));
        using (var cmd = Command(db, tx, "SELECT id,month,category,cents FROM budgets WHERE user_id=@p0 ORDER BY month DESC", user))
        using (var r = cmd.ExecuteReader()) while (r.Read()) budgets.Add(new(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetInt64(3) / 100m));
        using (var cmd = Command(db, tx, "SELECT id,name,description,cents,deadline FROM goals WHERE user_id=@p0 ORDER BY deadline", user))
        using (var r = cmd.ExecuteReader()) while (r.Read()) goals.Add(new(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetInt64(3) / 100m, DateOnly.Parse(r.GetString(4), CultureInfo.InvariantCulture)));
        using (var cmd = Command(db, tx, "SELECT id,goal_id,cents,date FROM contributions WHERE user_id=@p0 ORDER BY date DESC,id DESC", user))
        using (var r = cmd.ExecuteReader()) while (r.Read()) contributions.Add(new(r.GetInt64(0), r.GetInt64(1), r.GetInt64(2) / 100m, DateOnly.Parse(r.GetString(3), CultureInfo.InvariantCulture)));
        return new(movements, budgets, goals, contributions);
    }
    private Task Write(long user, string sql, object[] values, bool checkReserve = false) => Task.Run(() =>
    {
        using var db = Open(); using var tx = db.BeginTransaction();
        using var cmd = Command(db, tx, sql, values);
        if (cmd.ExecuteNonQuery() != 1) throw new ArgumentException("El registro no existe o pertenece a otro usuario.");
        if (checkReserve)
        {
            var data = Load(db, tx, user);
            if (data.Contributions.Count > 0 && FinanceRules.Summarize(data, DateOnly.FromDateTime(DateTime.Today)).Available < 0)
                throw new ArgumentException("Saldo insuficiente: reduce o elimina aportes antes de utilizar el dinero reservado.");
        }
        tx.Commit();
    });
    public Task SaveMovementAsync(long u, Movement m)
    {
        FinanceRules.ValidateMovement(m);
        return m.Id == 0 ? Write(u, "INSERT INTO movements(user_id,kind,cents,category,date,description) VALUES(@p0,@p1,@p2,@p3,@p4,@p5)", [u,(int)m.Kind,Cents(m.Amount),m.Category,m.Date.ToString("yyyy-MM-dd"),m.Description], true)
            : Write(u, "UPDATE movements SET kind=@p1,cents=@p2,category=@p3,date=@p4,description=@p5 WHERE user_id=@p0 AND id=@p6", [u,(int)m.Kind,Cents(m.Amount),m.Category,m.Date.ToString("yyyy-MM-dd"),m.Description,m.Id], true);
    }
    public Task DeleteMovementAsync(long u, long id) => Write(u, "DELETE FROM movements WHERE user_id=@p0 AND id=@p1", [u,id], true);
    public Task SaveBudgetAsync(long u, Budget b)
    {
        if (!DateOnly.TryParseExact(b.Month + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var month)) throw new ArgumentException("Usa el mes en formato AAAA-MM.");
        FinanceRules.ValidateDate(month);
        if (b.Category != "General" && !FinanceRules.ExpenseCategories.Contains(b.Category)) throw new ArgumentException("Categoría inválida.");
        return b.Id == 0 ? Write(u, "INSERT INTO budgets(user_id,month,category,cents) VALUES(@p0,@p1,@p2,@p3)", [u,b.Month,b.Category,Cents(b.Amount)])
            : Write(u, "UPDATE budgets SET month=@p1,category=@p2,cents=@p3 WHERE user_id=@p0 AND id=@p4", [u,b.Month,b.Category,Cents(b.Amount),b.Id]);
    }
    public Task DeleteBudgetAsync(long u, long id) => Write(u, "DELETE FROM budgets WHERE user_id=@p0 AND id=@p1", [u,id]);
    public Task SaveGoalAsync(long u, SavingGoal g)
    {
        FinanceRules.ValidateDate(g.Deadline);
        if (string.IsNullOrWhiteSpace(g.Name) || g.Name.Length > 120 || g.Description.Length > 500) throw new ArgumentException("Nombre obligatorio de hasta 120 caracteres; descripción de hasta 500.");
        return g.Id == 0 ? Write(u, "INSERT INTO goals(user_id,name,description,cents,deadline) VALUES(@p0,@p1,@p2,@p3,@p4)", [u,g.Name.Trim(),g.Description,Cents(g.Target),g.Deadline.ToString("yyyy-MM-dd")])
            : Write(u, "UPDATE goals SET name=@p1,description=@p2,cents=@p3,deadline=@p4 WHERE user_id=@p0 AND id=@p5", [u,g.Name.Trim(),g.Description,Cents(g.Target),g.Deadline.ToString("yyyy-MM-dd"),g.Id]);
    }
    public Task DeleteGoalAsync(long u, long id) => Write(u, "DELETE FROM goals WHERE user_id=@p0 AND id=@p1", [u,id]);
    public Task SaveContributionAsync(long u, Contribution c)
    {
        FinanceRules.ValidateDate(c.Date);
        return c.Id == 0 ? Write(u, "INSERT INTO contributions(user_id,goal_id,cents,date) VALUES(@p0,@p1,@p2,@p3)", [u,c.GoalId,Cents(c.Amount),c.Date.ToString("yyyy-MM-dd")], true)
            : Write(u, "UPDATE contributions SET goal_id=@p1,cents=@p2,date=@p3 WHERE user_id=@p0 AND id=@p4", [u,c.GoalId,Cents(c.Amount),c.Date.ToString("yyyy-MM-dd"),c.Id], true);
    }
    public Task DeleteContributionAsync(long u, long id) => Write(u, "DELETE FROM contributions WHERE user_id=@p0 AND id=@p1", [u,id]);
}
