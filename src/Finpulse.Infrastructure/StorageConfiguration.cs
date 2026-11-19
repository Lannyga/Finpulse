using System.Text.Json;
using Finpulse.Application;

namespace Finpulse.Infrastructure;

public sealed record StorageConfiguration(string Provider = "SQLite", decimal BudgetWarningPercent = 80m)
{
    public static StorageConfiguration Load(string path)
    {
        var config = File.Exists(path) ? JsonSerializer.Deserialize<StorageConfiguration>(File.ReadAllText(path)) ?? new() : new();
        string provider = Environment.GetEnvironmentVariable("FINPULSE_PROVIDER") ?? config.Provider;
        if (provider is not ("SQLite" or "Oracle")) throw new ArgumentException("Proveedor inválido. Usa SQLite u Oracle.");
        if (config.BudgetWarningPercent is <= 0 or >= 100) throw new ArgumentException("BudgetWarningPercent debe estar entre 0 y 100.");
        return config with { Provider = provider };
    }
    public IUserRepository CreateUsers() => Provider == "SQLite" ? CreateLocal() : new OracleUserRepository(
        Environment.GetEnvironmentVariable("FINPULSE_ORACLE_CONNECTION") ?? throw new ArgumentException("Configura FINPULSE_ORACLE_CONNECTION para utilizar Oracle."));
    public SqliteStore CreateLocal() => new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FINPULSE", "finpulse.db"));
}
