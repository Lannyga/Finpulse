using Microsoft.EntityFrameworkCore;
using Oracle.ManagedDataAccess.Client;

namespace Finpulse.Web.Data;

public static class OracleVerification
{
    public static readonly string[] Tables = ["CATEGORIAS", "ESCENARIOS", "METAS_AHORRO", "MOVIMIENTOS", "PRESUPUESTOS", "USUARIOS"];

    // Read-only authentication and schema verification through the application's actual connection.
    public static async Task Verify(PortalDb db, string expectedUser)
    {
        if (!db.Database.IsOracle()) throw new InvalidOperationException("El proveedor activo no es Oracle.");
        var connection = (OracleConnection)db.Database.GetDbConnection();
        await connection.OpenAsync();
        try
        {
            using var identity = connection.CreateCommand();
            identity.CommandText = "SELECT USER, SYS_CONTEXT('USERENV', 'CON_NAME') FROM DUAL";
            using (var reader = await identity.ExecuteReaderAsync())
            {
                await reader.ReadAsync();
                var user = reader.GetString(0);
                var container = reader.GetString(1);
                if (user != expectedUser || container != "FREEPDB1")
                    throw new InvalidOperationException("Usuario o contenedor Oracle distinto del esperado.");
                Console.WriteLine($"Oracle autenticado: {user}; contenedor: {container}.");
            }
            using var tables = connection.CreateCommand();
            tables.CommandText = "SELECT TABLE_NAME FROM USER_TABLES ORDER BY TABLE_NAME";
            var found = new List<string>();
            using (var reader = await tables.ExecuteReaderAsync())
                while (await reader.ReadAsync()) found.Add(reader.GetString(0));
            if (!found.SequenceEqual(Tables)) throw new InvalidOperationException("El esquema debe contener exactamente las seis tablas del portal.");
            using var segments = connection.CreateCommand();
            segments.CommandText = "SELECT COUNT(*) FROM USER_TABLES WHERE SEGMENT_CREATED <> 'YES'";
            if (Convert.ToInt32(await segments.ExecuteScalarAsync()) != 0)
                throw new InvalidOperationException("Materializa los segmentos de las seis tablas antes de usar transacciones serializables en Oracle.");
            using var constraints = connection.CreateCommand();
            constraints.CommandText = "SELECT COUNT(*) FROM USER_CONSTRAINTS WHERE STATUS <> 'ENABLED' OR VALIDATED <> 'VALIDATED'";
            if (Convert.ToInt32(await constraints.ExecuteScalarAsync()) != 0)
                throw new InvalidOperationException("Existen restricciones deshabilitadas o sin validar.");
            using var structure = connection.CreateCommand();
            structure.CommandText = "SELECT (SELECT COUNT(*) FROM USER_CONSTRAINTS WHERE CONSTRAINT_TYPE='P'), (SELECT COUNT(*) FROM USER_CONSTRAINTS WHERE CONSTRAINT_TYPE='R'), (SELECT COUNT(*) FROM USER_CONSTRAINTS WHERE CONSTRAINT_NAME LIKE 'CK_%'), (SELECT COUNT(*) FROM USER_INDEXES WHERE STATUS='VALID') FROM DUAL";
            using (var reader = await structure.ExecuteReaderAsync()) {
                await reader.ReadAsync();
                if (reader.GetInt32(0)!=6 || reader.GetInt32(1)!=7 || reader.GetInt32(2)!=8 || reader.GetInt32(3)!=19)
                    throw new InvalidOperationException("El esquema no tiene las seis PK, siete FK, ocho CHECK y diecinueve indices validos esperados. Revisa una posible instalacion parcial.");
            }
            // Exercise EF materialization, not only SQL authentication.
            _ = await db.Users.AsNoTracking().Take(1).ToArrayAsync();
            _ = await db.Categories.AsNoTracking().Take(1).ToArrayAsync();
            _ = await db.Entries.AsNoTracking().Take(1).ToArrayAsync();
            _ = await db.Budgets.AsNoTracking().Take(1).ToArrayAsync();
            _ = await db.Goals.AsNoTracking().Take(1).ToArrayAsync();
            _ = await db.Scenarios.AsNoTracking().Take(1).ToArrayAsync();
            Console.WriteLine("Seis tablas verificadas; restricciones habilitadas y validadas; lectura EF Core correcta.");
        }
        finally { await connection.CloseAsync(); }
    }
}
