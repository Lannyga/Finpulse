using Finpulse.Application;
using Finpulse.Infrastructure;
using Oracle.ManagedDataAccess.Client;

string cs = Environment.GetEnvironmentVariable("FINPULSE_ORACLE_CONNECTION")
    ?? throw new InvalidOperationException("Configura FINPULSE_ORACLE_CONNECTION para una base local de pruebas.");
var builder = new OracleConnectionStringBuilder(cs);
if (builder.DataSource != "localhost:1521/FREEPDB1" ||
    !builder.UserID.Equals("FINPULSE_APP", StringComparison.OrdinalIgnoreCase))
    throw new InvalidOperationException("Estas pruebas solo admiten FINPULSE_APP en localhost:1521/FREEPDB1.");

using var connection = new OracleConnection(cs);
await connection.OpenAsync();
var hasher = new Pbkdf2PasswordHasher();
var auth = new AuthService(new OracleUserRepository(cs), hasher);
string email = $"qa-{Guid.NewGuid():N}@finpulse.test";
string password = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(20));
int checks = 0;

try
{
    Check(!(await auth.RegisterAsync("Prueba Oracle", email, password, "distinta")).Exitoso,
        "Confirmación incorrecta rechazada");
    Check(await Number("SELECT COUNT(*) FROM FP_USUARIOS WHERE CORREO = :correo") == 0,
        "Validación no inserta filas");
    Check((await auth.RegisterAsync("Prueba Oracle", email.ToUpperInvariant(), password, password)).Exitoso,
        "Registro persistente en Oracle");
    Check(!(await auth.RegisterAsync("Duplicado", " " + email.ToUpperInvariant() + " ", password, password)).Exitoso,
        "Duplicado normalizado rechazado por Oracle");
    Check(await Number("SELECT COUNT(*) FROM FP_USUARIOS WHERE CORREO = :correo") == 1,
        "Solo una fila por correo");
    using (var cmd = Command("SELECT CLAVE_HASH FROM FP_USUARIOS WHERE CORREO = :correo"))
    {
        string hash = Convert.ToString(await cmd.ExecuteScalarAsync())!;
        Check(hash != password && hasher.VerifyPassword(password, hash), "Hash persistido verificable sin texto plano");
    }
    var login = await auth.LoginAsync(" " + email.ToUpperInvariant() + " ", password);
    Check(login.Exitoso && login.Usuario?.Correo == email && login.Usuario.NombreCompleto == "Prueba Oracle",
        "Login real devuelve identidad persistida");
    Check(await Number("SELECT COUNT(*) FROM FP_USUARIOS WHERE CORREO = :correo AND ULTIMO_ACCESO_UTC IS NOT NULL") == 1,
        "Último acceso actualizado");
    Check(!(await auth.LoginAsync($"ausente-{Guid.NewGuid():N}@finpulse.test", password)).Exitoso,
        "Usuario inexistente rechazado");
    for (int i = 1; i <= 5; i++)
    {
        Check(!(await auth.LoginAsync(email, "clave incorrecta")).Exitoso, $"Intento erróneo {i} rechazado");
        Check(await Number("SELECT INTENTOS_FALLIDOS FROM FP_USUARIOS WHERE CORREO = :correo") == i,
            $"Contador persistente {i}");
    }
    Check(await Number("SELECT COUNT(*) FROM FP_USUARIOS WHERE CORREO = :correo AND BLOQUEADO_HASTA_UTC BETWEEN SYS_EXTRACT_UTC(SYSTIMESTAMP) + INTERVAL '14' MINUTE AND SYS_EXTRACT_UTC(SYSTIMESTAMP) + INTERVAL '16' MINUTE") == 1,
        "Bloqueo cercano a 15 minutos");
    Check(!(await auth.LoginAsync(email, password)).Exitoso, "Clave válida rechazada durante bloqueo");
    await Execute("UPDATE FP_USUARIOS SET BLOQUEADO_HASTA_UTC = SYS_EXTRACT_UTC(SYSTIMESTAMP) - INTERVAL '1' MINUTE WHERE CORREO = :correo");
    Check(!(await auth.LoginAsync(email, "otra incorrecta")).Exitoso &&
        await Number("SELECT INTENTOS_FALLIDOS FROM FP_USUARIOS WHERE CORREO = :correo") == 1,
        "Primer fallo tras expiración reinicia contador");
    Check((await auth.LoginAsync(email, password)).Exitoso, "Acceso válido después de expiración");
    Check(await Number("SELECT COUNT(*) FROM FP_USUARIOS WHERE CORREO = :correo AND INTENTOS_FALLIDOS = 0 AND BLOQUEADO_HASTA_UTC IS NULL") == 1,
        "Éxito limpia intentos y bloqueo");
    await Execute("UPDATE FP_USUARIOS SET ACTIVO = 0 WHERE CORREO = :correo");
    Check(!(await auth.LoginAsync(email, password)).Exitoso, "Cuenta inactiva rechazada");
    await Execute("UPDATE FP_USUARIOS SET ACTIVO = 1 WHERE CORREO = :correo");
    Check((await auth.LoginAsync(email, password)).Exitoso, "Cuenta reactivada permite acceso");

    if (args.Contains("--create-demo"))
    {
        string demoPassword = Environment.GetEnvironmentVariable("FINPULSE_DEMO_PASSWORD")
            ?? throw new InvalidOperationException("Falta FINPULSE_DEMO_PASSWORD; no se genera una clave implícita.");
        // No restablecer ni reemplazar una cuenta existente.
        Check((await auth.RegisterAsync("Estudiante Demo", "estudiante@finpulse.local", demoPassword, demoPassword)).Exitoso,
            "Cuenta de ingreso creada");
        Check((await auth.LoginAsync("estudiante@finpulse.local", demoPassword)).Exitoso,
            "Credenciales de ingreso verificadas con Oracle");
    }
}
finally
{
    await Execute("DELETE FROM FP_USUARIOS WHERE CORREO = :correo");
}
Check(await Number("SELECT COUNT(*) FROM FP_USUARIOS WHERE CORREO = :correo") == 0,
    "Cuenta temporal de QA eliminada");
Console.WriteLine($"OK: {checks} comprobaciones con Oracle real. La interfaz WPF no forma parte de esta prueba.");

OracleCommand Command(string sql)
{
    var cmd = connection.CreateCommand();
    cmd.BindByName = true;
    cmd.CommandText = sql;
    cmd.Parameters.Add(new OracleParameter("correo", OracleDbType.Varchar2) { Value = email });
    return cmd;
}
async Task<long> Number(string sql)
{
    using var cmd = Command(sql);
    return Convert.ToInt64(await cmd.ExecuteScalarAsync());
}
async Task Execute(string sql)
{
    using var cmd = Command(sql);
    await cmd.ExecuteNonQueryAsync();
}
void Check(bool success, string label)
{
    if (!success) throw new InvalidOperationException("ERROR: " + label);
    Console.WriteLine($"OK {++checks}: {label}");
}
