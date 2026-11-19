using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Finpulse.Application;
using Finpulse.Web.Data;
using Finpulse.Web.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Finpulse.Application { public interface IPasswordHasher { string HashPassword(string password); bool VerifyPassword(string password, string storedHash); } }

namespace Finpulse.Web.Services {
/// <summary>Explicit, all-or-nothing import. The source is opened read-only; names are assigned by a reviewed ID mapping.</summary>
public static class LegacyImport
{
    public static async Task Run(PortalDb db, string sourcePath, string mappingPath)
    {
        var map = JsonSerializer.Deserialize<Dictionary<long,string>>(await File.ReadAllTextAsync(mappingPath)) ?? throw new ArgumentException("Mapa vacío.");
        if (map.Values.Any(x => !Regex.IsMatch(x,"^[a-zA-Z0-9_.-]{3,40}$")) || map.Values.Select(AccountService.Normalize).Distinct().Count()!=map.Count)
            throw new ArgumentException("El mapa requiere nombres válidos y únicos por ID anterior.");
        await using var source = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.GetFullPath(sourcePath), Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        await source.OpenAsync();
        await using var sourceTx = await source.BeginTransactionAsync();
        await using var tx = await db.Database.BeginTransactionAsync();
        if (await db.Users.AnyAsync()) throw new ArgumentException("Importa únicamente a un esquema nuevo sin cuentas. No se sobrescribe ningún usuario.");
        var owners = new Dictionary<long,long>(); var goalIds = new Dictionary<long,long>();
        SqliteCommand Query(string sql) { var c = source.CreateCommand(); c.Transaction = (SqliteTransaction)sourceTx; c.CommandText = sql; return c; }
        // Intentionally never selects or copies the legacy email column.
        using (var cmd=Query("SELECT id,hash,failures,locked_until FROM users ORDER BY id"))
        using (var r=await cmd.ExecuteReaderAsync()) while(await r.ReadAsync()) {
            long old=r.GetInt64(0); if(!map.TryGetValue(old,out var name)) throw new ArgumentException($"Falta nombre revisado para ID {old}.");
            var u=new Student { Username=name,NormalizedName=AccountService.Normalize(name),PasswordHash=r.GetString(1),Failures=r.GetInt32(2),
                LockedUntil=r.IsDBNull(3)?null:DateTimeOffset.Parse(r.GetString(3),CultureInfo.InvariantCulture).UtcDateTime };
            // Existing password stays usable; recovery code is first issued when the owner changes that password.
            u.RecoveryHash=new Microsoft.AspNetCore.Identity.PasswordHasher<Student>().HashPassword(u,AccountService.NewCode());
            db.Users.Add(u); await db.SaveChangesAsync(); owners.Add(old,u.Id);
        }
        var cats=await db.Categories.ToArrayAsync();
        using(var cmd=Query("SELECT id,user_id,name,description,cents,deadline FROM goals"))
        using(var r=await cmd.ExecuteReaderAsync()) while(await r.ReadAsync()) {
            var g=new Goal { UserId=owners[r.GetInt64(1)],Name=r.GetString(2),Description=r.GetString(3),Target=r.GetInt64(4)/100m,Deadline=DateTime.Parse(r.GetString(5),CultureInfo.InvariantCulture) };
            FinanceRules.ValidateAmount(g.Target); if(g.Name.Length>120) throw new ArgumentException("Meta fuera del límite.");
            db.Goals.Add(g);await db.SaveChangesAsync();goalIds.Add(r.GetInt64(0),g.Id);
        }
        using(var cmd=Query("SELECT user_id,kind,cents,category,date,description FROM movements"))
        using(var r=await cmd.ExecuteReaderAsync()) while(await r.ReadAsync()) {
            int kind=r.GetInt32(1);string category=r.GetString(3);decimal amount=r.GetInt64(2)/100m;FinanceRules.ValidateAmount(amount);
            db.Entries.Add(new(){UserId=owners[r.GetInt64(0)],Kind=kind,Amount=amount,CategoryId=cats.Single(x=>x.Kind==kind&&x.Name==category).Id,Date=DateTime.Parse(r.GetString(4),CultureInfo.InvariantCulture),Description=r.GetString(5)});
        }
        using(var cmd=Query("SELECT user_id,month,category,cents FROM budgets"))
        using(var r=await cmd.ExecuteReaderAsync()) while(await r.ReadAsync()) {
            string scope=r.GetString(2);decimal amount=r.GetInt64(3)/100m;FinanceRules.ValidateAmount(amount);
            db.Budgets.Add(new(){UserId=owners[r.GetInt64(0)],Month=r.GetString(1),Scope=scope,CategoryId=scope=="General"?null:cats.Single(x=>x.Kind==1&&x.Name==scope).Id,Amount=amount});
        }
        using(var cmd=Query("SELECT user_id,goal_id,cents,date FROM contributions"))
        using(var r=await cmd.ExecuteReaderAsync()) while(await r.ReadAsync()) {
            decimal amount=r.GetInt64(2)/100m;FinanceRules.ValidateAmount(amount);
            db.Entries.Add(new(){UserId=owners[r.GetInt64(0)],GoalId=goalIds[r.GetInt64(1)],Kind=2,Amount=amount,Date=DateTime.Parse(r.GetString(3),CultureInfo.InvariantCulture)});
        }
        await db.SaveChangesAsync(); await tx.CommitAsync();
    }
}
}
