using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using System.Security.Claims;
using Finpulse.Web.Data;
using Finpulse.Web.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace Finpulse.Web.Controllers;

[EnableRateLimiting("access")]
public sealed class AccountController(AccountService accounts, PortalDb db) : Controller
{
    [HttpGet] public IActionResult Login() => View("Access", new Credentials());
    [HttpGet] public IActionResult Register() => View("Access", new Credentials());

    [HttpPost]
    public async Task<IActionResult> Register(Credentials form)
    {
        if (ModelState.IsValid) {
            try {
                await SignIn(await accounts.Register(form));
                return RedirectToAction("Index", "Finance");
            } catch (ArgumentException e) { ModelState.AddModelError("Username", e.Message); }
        }
        return View("Access", form);
    }

    [HttpPost]
    public async Task<IActionResult> Login(Credentials form, string? returnUrl)
    {
        if (ModelState.IsValid && await accounts.Verify(form) is Student user) {
            await SignIn(user);
            return Url.IsLocalUrl(returnUrl) ? LocalRedirect(returnUrl) : RedirectToAction("Index", "Finance");
        }
        ModelState.AddModelError("", "Usuario o contraseña incorrectos, o acceso temporalmente bloqueado.");
        return View("Access", form);
    }

    [Authorize, HttpGet] public IActionResult Settings() => View();

    [Authorize, HttpPost]
    public async Task<IActionResult> ChangePassword(string currentPassword, string password, string confirm)
    {
        var user = await db.Users.SingleAsync(x => x.Id == long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!));
        if (password is null || password.Length is < 6 or > 128 || password != confirm ||
            await accounts.Verify(new() { Username = user.Username, Password = currentPassword ?? "" }) is null) {
            TempData["Message"] = "Revisa tu contraseña actual y confirma la nueva (6 a 128 caracteres).";
        } else {
            try {
                await accounts.ChangePassword(user, password);
                await SignIn(user);
                TempData["Message"] = "Contraseña actualizada.";
            } catch (DbUpdateConcurrencyException) { TempData["Message"] = "La cuenta cambió. Inténtalo otra vez."; }
        }
        return RedirectToAction(nameof(Settings));
    }

    [Authorize, HttpPost]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync();
        return RedirectToAction(nameof(Login));
    }

    [HttpGet] public IActionResult Error() { Response.StatusCode = 500; return Content("No se pudo completar la operación. Inténtalo más tarde."); }

    private Task SignIn(Student user) => HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
        new ClaimsPrincipal(new ClaimsIdentity([
            new(ClaimTypes.NameIdentifier, user.Id.ToString()), new(ClaimTypes.Name, user.Username), new("version", user.SessionVersion)
        ], CookieAuthenticationDefaults.AuthenticationScheme)));
}


public sealed class AccountService(PortalDb db, PasswordHasher<Student> hasher, ILogger<AccountService> log)
{
    public static string Normalize(string name) => name.Trim().ToUpperInvariant();

    public async Task<Student> Register(Credentials form)
    {
        if (form.Password != form.Confirm) throw new ArgumentException("La confirmación no coincide.");
        var user = new Student { Username = form.Username.Trim(), NormalizedName = Normalize(form.Username) };
        user.PasswordHash = hasher.HashPassword(user, form.Password);
        db.Users.Add(user);
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException) { throw new ArgumentException("No se pudo registrar ese nombre de usuario."); }
        log.LogInformation("Cuenta registrada {UserId}", user.Id);
        return user;
    }

    public async Task<Student?> Verify(Credentials form)
    {
        var name = Normalize(form.Username);
        var user = await db.Users.SingleOrDefaultAsync(x => x.NormalizedName == name);
        var candidate = user ?? new Student();
        // Unknown accounts also perform password hashing.
        var hash = user?.PasswordHash ?? hasher.HashPassword(candidate, Convert.ToHexString(RandomNumberGenerator.GetBytes(24)));
        var result = hasher.VerifyHashedPassword(candidate, hash, form.Password);
        if (user is null || user.LockedUntil > DateTime.UtcNow) return null;
        if (result == PasswordVerificationResult.Failed) {
            log.LogWarning("Acceso rechazado para usuario {UserId}", user.Id);
            user.Failures++;
            if (user.Failures >= 5) { user.Failures = 0; user.LockedUntil = DateTime.UtcNow.AddMinutes(15); }
        } else {
            user.Failures = 0;
            user.LockedUntil = null;
            if (result == PasswordVerificationResult.SuccessRehashNeeded)
                user.PasswordHash = hasher.HashPassword(user, form.Password);
        }
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateConcurrencyException) { return null; }
        return result != PasswordVerificationResult.Failed ? user : null;
    }

    public async Task ChangePassword(Student user, string password)
    {
        user.PasswordHash = hasher.HashPassword(user, password);
        user.SessionVersion = Guid.NewGuid().ToString("N");
        await db.SaveChangesAsync();
        log.LogInformation("Contraseña actualizada para usuario {UserId}", user.Id);
    }
}
