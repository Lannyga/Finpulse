using Finpulse.Application;
using Finpulse.Domain;
using Oracle.ManagedDataAccess.Client;

namespace Finpulse.Infrastructure;

public sealed class OracleUserRepository : IUserRepository
{
    private const string LoginError = "Credenciales incorrectas o acceso temporalmente restringido.";
    private readonly string _connectionString;

    public OracleUserRepository(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("Falta la cadena de conexión Oracle.", nameof(connectionString));
        _connectionString = connectionString;
    }

    public async Task<bool> CreateAsync(string nombre, string correoNormalizado, string hash,
        CancellationToken ct = default)
    {
        using var connection = new OracleConnection(_connectionString);
        await connection.OpenAsync(ct);
        using var cmd = connection.CreateCommand();
        cmd.BindByName = true;
        cmd.CommandText = @"INSERT INTO FP_USUARIOS (NOMBRE_COMPLETO, CORREO, CLAVE_HASH)
                             VALUES (:nombre, :correo, :hash)";
        cmd.Parameters.Add(new OracleParameter { ParameterName = "nombre", OracleDbType = OracleDbType.Varchar2, Value = nombre });
        cmd.Parameters.Add(new OracleParameter { ParameterName = "correo", OracleDbType = OracleDbType.Varchar2, Value = correoNormalizado });
        cmd.Parameters.Add(new OracleParameter { ParameterName = "hash", OracleDbType = OracleDbType.Varchar2, Value = hash });
        try
        {
            return await cmd.ExecuteNonQueryAsync(ct) == 1;
        }
        catch (OracleException ex) when (ex.Number == 1) // ORA-00001 UNIQUE
        {
            return false;
        }
    }

    public async Task<AuthResult> AuthenticateAsync(string correoNormalizado, string password,
        IPasswordHasher hasher, CancellationToken ct = default)
    {
        using var connection = new OracleConnection(_connectionString);
        await connection.OpenAsync(ct);
        using var tx = connection.BeginTransaction(System.Data.IsolationLevel.ReadCommitted);
        // FOR UPDATE serializa los cambios de intentos fallidos para una cuenta.
        using var query = connection.CreateCommand();
        query.BindByName = true;
        query.Transaction = tx;
        query.CommandText = @"SELECT USUARIO_ID, NOMBRE_COMPLETO, CORREO, CLAVE_HASH,
                                     INTENTOS_FALLIDOS, ACTIVO, BLOQUEADO_HASTA_UTC
                              FROM FP_USUARIOS WHERE CORREO = :correo FOR UPDATE";
        query.Parameters.Add(new OracleParameter { ParameterName = "correo", OracleDbType = OracleDbType.Varchar2, Value = correoNormalizado });

        long id;
        string nombre, correo, hash;
        int intentos, activo;
        DateTime? bloqueo;
        using (var reader = await query.ExecuteReaderAsync(ct))
        {
            if (!await reader.ReadAsync(ct)) return new(false, LoginError);
            id = reader.GetInt64(0);
            nombre = reader.GetString(1);
            correo = reader.GetString(2);
            hash = reader.GetString(3);
            intentos = reader.GetInt32(4);
            activo = reader.GetInt32(5);
            bloqueo = reader.IsDBNull(6) ? null : DateTime.SpecifyKind(reader.GetDateTime(6), DateTimeKind.Utc);
        }
        if (activo != 1 || (bloqueo.HasValue && bloqueo.Value > DateTime.UtcNow))
            return new(false, LoginError);

        bool correcto = hasher.VerifyPassword(password, hash);
        using var update = connection.CreateCommand();
        update.BindByName = true;
        update.Transaction = tx;
        if (!correcto)
        {
            int siguientes = bloqueo.HasValue && bloqueo.Value <= DateTime.UtcNow ? 1 : intentos + 1;
            DateTime? nuevoBloqueo = siguientes >= 5 ? DateTime.UtcNow.AddMinutes(15) : null;
            update.CommandText = @"UPDATE FP_USUARIOS
                SET INTENTOS_FALLIDOS = :intentos, BLOQUEADO_HASTA_UTC = :bloqueo
                WHERE USUARIO_ID = :id";
            update.Parameters.Add(new OracleParameter { ParameterName = "intentos", OracleDbType = OracleDbType.Int32, Value = siguientes });
            update.Parameters.Add(new OracleParameter { ParameterName = "bloqueo", OracleDbType = OracleDbType.TimeStamp, Value = nuevoBloqueo.HasValue ? (object)nuevoBloqueo.Value : DBNull.Value });
            update.Parameters.Add(new OracleParameter { ParameterName = "id", OracleDbType = OracleDbType.Int64, Value = id });
            await update.ExecuteNonQueryAsync(ct);
            tx.Commit();
            return new(false, LoginError);
        }

        update.CommandText = @"UPDATE FP_USUARIOS
            SET INTENTOS_FALLIDOS = 0, BLOQUEADO_HASTA_UTC = NULL,
                ULTIMO_ACCESO_UTC = SYS_EXTRACT_UTC(SYSTIMESTAMP)
            WHERE USUARIO_ID = :id";
        update.Parameters.Add(new OracleParameter { ParameterName = "id", OracleDbType = OracleDbType.Int64, Value = id });
        await update.ExecuteNonQueryAsync(ct);
        tx.Commit();
        return new(true, "Acceso concedido.", new UserSession(id, nombre, correo));
    }
}
