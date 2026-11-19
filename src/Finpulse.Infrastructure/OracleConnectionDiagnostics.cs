using Oracle.ManagedDataAccess.Client;

namespace Finpulse.Infrastructure;

public sealed record OracleConnectionDiagnostic(
    bool Success, string Status, string Message, string? User = null,
    string? Container = null, int? OracleError = null,
    bool? FinpulseExists = null, bool? FinpulseAppExists = null,
    bool? UsersTableExists = null);

/// <summary>Diagnóstico de solo lectura. No devuelve la cadena ni mensajes del proveedor con secretos.</summary>
public static class OracleConnectionDiagnostics
{
    public static async Task<OracleConnectionDiagnostic> CheckAsync(
        string? connectionString, string expectedUser, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            return new(false, "MissingConfiguration", "Falta la conexión Oracle en la variable de entorno.");
        if (string.IsNullOrWhiteSpace(expectedUser))
            return new(false, "InvalidConfiguration", "Indica el usuario esperado para verificar la identidad.");
        try
        {
            // Sin pooling: cada comprobación abre una conexión física nueva.
            var settings = new OracleConnectionStringBuilder(connectionString)
            {
                Pooling = false,
                ConnectionTimeout = 10
            };
            using var connection = new OracleConnection(settings.ConnectionString);
            await connection.OpenAsync(ct);
            string user, container;
            using (var query = connection.CreateCommand())
            {
                query.CommandTimeout = 10;
                query.CommandText = "SELECT USER AS USUARIO, SYS_CONTEXT('USERENV', 'CON_NAME') AS BASE_DATOS FROM DUAL";
                using var reader = await query.ExecuteReaderAsync(ct);
                if (!await reader.ReadAsync(ct))
                    return new(false, "UnexpectedResult", "DUAL no devolvió una fila.");
                user = reader.GetString(0);
                container = reader.GetString(1);
            }
            if (!user.Equals(expectedUser, StringComparison.OrdinalIgnoreCase) || container != "FREEPDB1")
                return new(false, "UnexpectedIdentity", "La identidad o el contenedor no coincide con lo esperado.", user, container);

            // ALL_USERS permite comprobar nombres de esquema sin operaciones administrativas.
            bool finpulse = await Exists("SELECT COUNT(*) FROM ALL_USERS WHERE USERNAME = :nombre", "FINPULSE");
            bool finpulseApp = await Exists("SELECT COUNT(*) FROM ALL_USERS WHERE USERNAME = :nombre", "FINPULSE_APP");
            bool table = await Exists("SELECT COUNT(*) FROM ALL_TABLES WHERE OWNER = :nombre AND TABLE_NAME = 'FP_USUARIOS'", "FINPULSE_APP");
            return new(true, "Connected", "Conexión física y SELECT sobre DUAL verificados.", user, container,
                FinpulseExists: finpulse, FinpulseAppExists: finpulseApp, UsersTableExists: table);

            async Task<bool> Exists(string sql, string name)
            {
                using var query = connection.CreateCommand();
                query.BindByName = true;
                query.CommandTimeout = 10;
                query.CommandText = sql;
                query.Parameters.Add(new OracleParameter("nombre", OracleDbType.Varchar2) { Value = name });
                return Convert.ToInt64(await query.ExecuteScalarAsync(ct)) > 0;
            }
        }
        catch (OracleException ex)
        {
            string message = ex.Number switch
            {
                1017 => "Autenticación rechazada. Comprueba localmente el usuario y la contraseña.",
                28000 => "La cuenta Oracle está bloqueada; requiere revisión del administrador.",
                28001 => "La contraseña Oracle ha expirado; requiere intervención local.",
                12514 => "El listener no reconoce el servicio solicitado. Comprueba FREEPDB1.",
                12541 => "No se encuentra un listener en el destino configurado.",
                12154 => "No se pudo resolver el destino Oracle configurado.",
                12170 => "Se agotó el tiempo de conexión con Oracle.",
                _ => "Oracle rechazó la conexión o la consulta. Revisa el código ORA indicado."
            };
            return new(false, "OracleError", message, OracleError: ex.Number);
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or OverflowException)
        {
            return new(false, "InvalidConfiguration", "La cadena de conexión no tiene un formato válido.");
        }
        catch (OperationCanceledException)
        {
            return new(false, "Cancelled", "La comprobación fue cancelada o excedió su plazo.");
        }
        catch (Exception)
        {
            // No propagar mensajes que puedan incluir cadenas de conexión o credenciales.
            return new(false, "UnexpectedError", "No se pudo completar la comprobación de Oracle.");
        }
    }
}
