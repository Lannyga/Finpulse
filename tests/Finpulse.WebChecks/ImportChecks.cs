using System.Text.Json;
using Finpulse.Web.Data;
using Finpulse.Web.Models;
using Finpulse.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

internal static class ImportChecks
{
    public static async Task Run()
    {
        var folder=Path.Combine(Path.GetTempPath(),"finpulse-import-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        var sourcePath=Path.Combine(folder,"old.db");var targetPath=Path.Combine(folder,"new.db");var mapPath=Path.Combine(folder,"map.json");
        try {
            var hash=new Finpulse.Infrastructure.Pbkdf2PasswordHasher().HashPassword("clave-anterior-segura-123");
            await using(var old=new SqliteConnection($"Data Source={sourcePath};Pooling=False")) {
                await old.OpenAsync();using var cmd=old.CreateCommand();cmd.CommandText="""
                CREATE TABLE users(id INTEGER PRIMARY KEY,email TEXT,hash TEXT,failures INTEGER,locked_until TEXT);
                CREATE TABLE movements(user_id INTEGER,kind INTEGER,cents INTEGER,category TEXT,date TEXT,description TEXT);
                CREATE TABLE budgets(user_id INTEGER,month TEXT,category TEXT,cents INTEGER);
                CREATE TABLE goals(id INTEGER,user_id INTEGER,name TEXT,description TEXT,cents INTEGER,deadline TEXT);
                CREATE TABLE contributions(user_id INTEGER,goal_id INTEGER,cents INTEGER,date TEXT);
                INSERT INTO users VALUES(7,'legacy@example.test',$hash,0,NULL);
                INSERT INTO movements VALUES(7,0,120000,'Trabajo','2026-10-01','Historial');
                INSERT INTO movements VALUES(7,1,85000,'Alimentación','2026-10-02','Gasto');
                INSERT INTO budgets VALUES(7,'2026-10','General',90000);
                INSERT INTO goals VALUES(9,7,'Equipo','Meta',100000,'2027-10-01');
                INSERT INTO contributions VALUES(7,9,10000,'2026-10-03');
                """;cmd.Parameters.AddWithValue("$hash",hash);await cmd.ExecuteNonQueryAsync();
            }
            await File.WriteAllTextAsync(mapPath,JsonSerializer.Serialize(new Dictionary<long,string>{{7,"migrado"}}));
            var sourceBytes=await File.ReadAllBytesAsync(sourcePath);
            var before=System.Security.Cryptography.SHA256.HashData(sourceBytes);
            await using var db=new PortalDb(new DbContextOptionsBuilder<PortalDb>().UseSqlite($"Data Source={targetPath};Foreign Keys=True;Pooling=False").Options);
            await db.Database.EnsureCreatedAsync();await PortalFinance.SeedCategories(db);await LegacyImport.Run(db,sourcePath,mapPath);
            var account=new AccountService(db,new PasswordHasher<Student>(),NullLogger<AccountService>.Instance);
            var user=await account.Verify(new(){Username="migrado",Password="clave-anterior-segura-123"});
            if(user is null || user.PasswordHash.StartsWith("PBKDF2-SHA256$"))throw new Exception("No se actualizó el hash anterior.");
            var data=(await new PortalFinance(db,NullLogger<PortalFinance>.Instance).Load(user.Id)).Data;
            if(data.Movements.Count!=2 || data.Budgets.Count!=1 || data.Goals.Count!=1 || data.Contributions.Single().Amount!=100 || Finpulse.Application.FinanceRules.Summarize(data,PortalFinance.Today).Available!=250)throw new Exception("Migración financiera incompleta.");
            var afterBytes=await File.ReadAllBytesAsync(sourcePath);
            if(!before.SequenceEqual(System.Security.Cryptography.SHA256.HashData(afterBytes)))throw new Exception("Se modificó el origen.");
            Console.WriteLine("IMPORT OK: identidad por mapa, hash compatible y actualizado, finanzas y aportes preservados, origen intacto.");
        } finally {SqliteConnection.ClearAllPools();Directory.Delete(folder,true);}
    }
}
