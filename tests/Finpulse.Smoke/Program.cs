using Finpulse.Application;
using Finpulse.Domain;
using Finpulse.Infrastructure;

try
{
    await RunChecksAsync(args);
}
catch (Exception ex)
{
    Console.Error.WriteLine("FALLO DE FINPULSE.SMOKE: la ejecución no superó las comprobaciones.");
    Console.Error.WriteLine(ex.ToString());
    Environment.ExitCode = 1;
}

static async Task RunChecksAsync(string[] args)
{
if (!(args.Length == 0 || args is ["--ui"] or ["--qa-edge-cases"] or ["--create-demo"] or ["--verify-demo"] or ["--oracle-connectivity"] or ["--oracle-diagnostic-errors"]
    || args is ["--verify-local-reopen", _, _]))
    throw new ArgumentException("Argumentos no reconocidos. Usa ningún argumento, --ui, --qa-edge-cases, --create-demo, --verify-demo o un diagnóstico Oracle explícito.");

if(args is ["--create-demo"]) { await DemoAccountCommands.CreateLocalAsync(); return; }
if(args is ["--verify-demo"]) { await DemoAccountCommands.VerifyAsync(); return; }


if (args.Contains("--qa-edge-cases"))
{
    await ReliabilityChecks.RunAsync();
    return;
}

if (args.Contains("--ui"))
{
    DesktopChecks.Run();
    return;
}

if (args is ["--verify-local-reopen", var database, var userId])
{
    var data = await new SqliteStore(database).LoadAsync(long.Parse(userId));
    Environment.ExitCode = data.Movements.Count == 4 ? 0 : 1;
    return;
}

if (args.Contains("--oracle-connectivity"))
{
    string? cs = Environment.GetEnvironmentVariable("FINPULSE_ORACLE_DIAGNOSTIC_CONNECTION")
        ?? Environment.GetEnvironmentVariable("FINPULSE_ORACLE_CONNECTION");
    string expectedUser = Environment.GetEnvironmentVariable("FINPULSE_ORACLE_EXPECTED_USER") ?? "FINPULSE_APP";
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
    var result = await OracleConnectionDiagnostics.CheckAsync(cs, expectedUser, timeout.Token);
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(result));
    Environment.ExitCode = result.Success ? 0 : 1;
    return;
}

if (args.Contains("--oracle-diagnostic-errors"))
{
    var missing = await OracleConnectionDiagnostics.CheckAsync(null, "SYSTEM");
    var invalid = await OracleConnectionDiagnostics.CheckAsync("Connection Timeout=no-es-numero", "SYSTEM");
    if (missing.Success || missing.Status != "MissingConfiguration" ||
        invalid.Success || invalid.Status != "InvalidConfiguration")
        throw new InvalidOperationException($"Falló el diagnóstico: ausente={missing.Status}, inválida={invalid.Status}, código={invalid.OracleError}.");
    Console.WriteLine("OK: configuración ausente e inválida rechazadas sin mostrar secretos ni intentar autenticación.");
    return;
}

int checks = 0;
ProjectionChecks.Run();
await ReliabilityChecks.RunAsync();
await LocalFinanceChecks.RunAsync();
await DemoAccountCommands.VerifyAsync();
var passwordHasher = new Pbkdf2PasswordHasher();
string pass = "Una frase de acceso larga y unica 2026!";
string h1 = passwordHasher.HashPassword(pass);
string h2 = passwordHasher.HashPassword(pass);
Check(h1 != h2, "La sal debe ser aleatoria");
Check(passwordHasher.VerifyPassword(pass, h1), "La contraseña correcta debe verificarse");
Check(!passwordHasher.VerifyPassword("incorrecta", h1), "La contraseña errónea debe rechazarse");
Check(!passwordHasher.VerifyPassword(pass, "PBKDF2-SHA256$malformado"), "Hash corrupto rechazado");
Check(AuthService.NormalizeEmail(" Estudiante@Correo.COM ") == "estudiante@correo.com", "Normalización correo");
Check(!AuthService.IsValidEmail("correo_sin_arroba"), "Rechazar correo incorrecto");

var repository = new MemoryUserRepo();
var service = new AuthService(repository, passwordHasher);
Check(!(await service.RegisterAsync("Yo", "a@b.com", pass, pass)).Exitoso, "Nombre demasiado corto");
Check(!(await service.RegisterAsync("María Pérez", "maria@uni.bo", pass, "no coincide")).Exitoso,
    "Confirmación inválida");
Check((await service.RegisterAsync("María Pérez", " MARIA@UNI.BO ", pass, pass)).Exitoso, "Alta válida");
Check(!(await service.RegisterAsync("María Pérez", "maria@uni.bo", pass, pass)).Exitoso,
    "Correo duplicado");
Check(!(await service.LoginAsync("maria@uni.bo", "contraseña errónea")).Exitoso,
    "Login incorrecto");
Check((await service.LoginAsync("maria@uni.bo", pass)).Exitoso, "Login correcto");
Check(!(await service.RegisterAsync("   ", "otra@uni.bo", pass, pass)).Exitoso, "Nombre vacío");
Check(!(await service.RegisterAsync(new string('a', 121), "otra@uni.bo", pass, pass)).Exitoso,
    "Nombre demasiado largo");
Check(!(await service.RegisterAsync("Otro estudiante", "sin-arroba", pass, pass)).Exitoso,
    "Registro con correo inválido");
Check(!(await service.RegisterAsync("Otro estudiante", new string('a', 250) + "@uni.bo", pass, pass)).Exitoso,
    "Correo demasiado largo");
Check(!(await service.RegisterAsync("Otro estudiante", "otra@uni.bo", "corta", "corta")).Exitoso,
    "Contraseña demasiado corta");
Check(!(await service.RegisterAsync("Otro estudiante", "otra@uni.bo", new string('a', 129), new string('a', 129))).Exitoso,
    "Contraseña demasiado larga");
Check(!(await service.RegisterAsync("María Pérez", " MARIA@UNI.BO ", pass, pass)).Exitoso,
    "Duplicado con mayúsculas y espacios");
Check(!(await service.LoginAsync("desconocido@uni.bo", pass)).Exitoso, "Usuario inexistente");
Check(!(await service.LoginAsync("sin-arroba", pass)).Exitoso, "Login con correo inválido");
Check(!(await service.LoginAsync("maria@uni.bo", "")).Exitoso, "Login sin contraseña");
var normalizedLogin = await service.LoginAsync(" MARIA@UNI.BO ", pass);
Check(normalizedLogin.Exitoso && normalizedLogin.Usuario is { NombreCompleto: "María Pérez", Correo: "maria@uni.bo" },
    "Login normalizado y datos de sesión");
Check(!passwordHasher.VerifyPassword(pass, "PBKDF2-SHA256$600000$!!!$!!!"), "Base64 inválido rechazado");
Check(!passwordHasher.VerifyPassword(pass, h1.Replace("$600000$", "$1$")), "Iteraciones inseguras rechazadas");
Check(!passwordHasher.VerifyPassword(pass, h1.Replace("$600000$", "$1200001$")), "Iteraciones excesivas rechazadas");
Console.WriteLine($"OK: {checks} comprobaciones de reglas y hash (sin conexión a Oracle)");

void Check(bool condition, string detail)
{
    if (!condition) throw new InvalidOperationException($"ERROR: {detail}");
    checks++;
    Console.WriteLine($"OK {checks}: {detail}");
}

}

sealed class MemoryUserRepo : IUserRepository
{
    private readonly Dictionary<string, (string Name, string Hash)> _records = new();

    public Task<bool> CreateAsync(string nombre, string correoNormalizado, string hash,
        CancellationToken ct = default)
    {
        if (_records.ContainsKey(correoNormalizado)) return Task.FromResult(false);
        _records[correoNormalizado] = (nombre, hash);
        return Task.FromResult(true);
    }

    public Task<AuthResult> AuthenticateAsync(string correoNormalizado, string password,
        IPasswordHasher hasher, CancellationToken ct = default)
    {
        if (!_records.TryGetValue(correoNormalizado, out var record) ||
            !hasher.VerifyPassword(password, record.Hash))
            return Task.FromResult(new AuthResult(false, "Credenciales incorrectas"));
        return Task.FromResult(new AuthResult(true, "OK", new UserSession(1, record.Name, correoNormalizado)));
    }
}
