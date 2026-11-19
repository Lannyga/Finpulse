using System.Security.Cryptography;
using Finpulse.Web.Data;
using Finpulse.Web.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Finpulse.Web.Services;

public sealed class AccountService(PortalDb db, PasswordHasher<Student> hasher, ILogger<AccountService> log)
{
    public static string Normalize(string name) => name.Trim().ToUpperInvariant();
    public static string NewCode() => Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
    public async Task<(Student User, string Code)> Register(Credentials form)
    {
        if (form.Password != form.Confirm) throw new ArgumentException("La confirmación no coincide.");
        var user = new Student { Username = form.Username.Trim(), NormalizedName = Normalize(form.Username) };
        user.PasswordHash = hasher.HashPassword(user, form.Password);
        var code = NewCode(); user.RecoveryHash = hasher.HashPassword(user, code);
        db.Users.Add(user);
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException) { throw new ArgumentException("No se pudo registrar ese nombre de usuario."); }
        log.LogInformation("Registro de cuenta {UserId}", user.Id);
        return (user, code);
    }
    public async Task<Student?> Verify(Credentials form, bool recovery = false)
    {
        var name = Normalize(form.Username);
        var user = await db.Users.SingleOrDefaultAsync(x => x.NormalizedName == name);
        var dummy = user ?? new Student();
        // Equal-cost hashing for unknown accounts avoids a cheap account-existence oracle.
        var hash = user is null ? hasher.HashPassword(dummy, NewCode()) : recovery ? user.RecoveryHash : user.PasswordHash;
        bool legacy = !recovery && hash.StartsWith("PBKDF2-SHA256$", StringComparison.Ordinal);
        bool valid = legacy ? new Finpulse.Infrastructure.Pbkdf2PasswordHasher().VerifyPassword(form.Password, hash)
            : hasher.VerifyHashedPassword(dummy, hash, recovery ? form.RecoveryCode ?? "" : form.Password) != PasswordVerificationResult.Failed;
        if (user is null || user.LockedUntil > DateTime.UtcNow) return null;
        if (!valid) {
            user.Failures++;
            if (user.Failures >= 5) { user.Failures = 0; user.LockedUntil = DateTime.UtcNow.AddMinutes(15); }
            log.LogWarning("Intento de acceso rechazado {UserId}", user.Id);
        } else { user.Failures = 0; user.LockedUntil = null; if (legacy) user.PasswordHash = hasher.HashPassword(user, form.Password); }
        try { await db.SaveChangesAsync(); } catch (DbUpdateConcurrencyException) { return null; }
        return valid ? user : null;
    }
    public async Task<string> Reset(Student user, string password)
    {
        var code = NewCode(); user.PasswordHash = hasher.HashPassword(user, password);
        user.RecoveryHash = hasher.HashPassword(user, code); user.SessionVersion = Guid.NewGuid().ToString("N");
        user.Failures = 0; user.LockedUntil = null;
        await db.SaveChangesAsync(); log.LogInformation("Credenciales renovadas {UserId}", user.Id); return code;
    }
}
