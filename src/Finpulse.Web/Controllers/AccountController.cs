using System.Security.Claims;
using Finpulse.Web.Data;
using Finpulse.Web.Models;
using Finpulse.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace Finpulse.Web.Controllers;

[EnableRateLimiting("access")]
public sealed class AccountController(AccountService accounts, PortalDb db, IWebHostEnvironment environment, IConfiguration configuration) : Controller
{
    [HttpGet] public async Task<IActionResult> Login()
    {
        ViewBag.DemoAvailable = await DemoUser() is not null;
        return View("Access", new Credentials());
    }
    [HttpGet] public IActionResult Register() => View("Access", new Credentials());
    [HttpGet] public IActionResult Recover() => View("Access", new Credentials());
    [HttpPost] public async Task<IActionResult> Register(Credentials form)
    {
        if (ModelState.IsValid) try {
            var result = await accounts.Register(form); await SignIn(result.User);
            ViewBag.Code = result.Code; return View("RecoveryCode");
        } catch (ArgumentException e) { ModelState.AddModelError("Username", e.Message); }
        return View("Access", form);
    }
    [HttpPost] public async Task<IActionResult> Login(Credentials form, string? returnUrl)
    {
        if (ModelState.IsValid && await accounts.Verify(form) is Student user) {
            await SignIn(user); return Url.IsLocalUrl(returnUrl) ? LocalRedirect(returnUrl) : RedirectToAction("Index", "Finance");
        }
        ModelState.AddModelError("", "Credenciales incorrectas o acceso temporalmente restringido.");
        ViewBag.DemoAvailable = await DemoUser() is not null;
        return View("Access", form);
    }
    [HttpPost] public async Task<IActionResult> DemoLogin()
    {
        var demo = await DemoUser();
        if (demo is null) return NotFound();
        await SignIn(demo);
        return RedirectToAction("Index", "Finance");
    }
    [HttpPost] public async Task<IActionResult> Recover(Credentials form)
    {
        if (ModelState.IsValid && form.Password == form.Confirm && await accounts.Verify(form, true) is Student user) {
            try { ViewBag.Code = await accounts.Reset(user, form.Password); }
            catch (DbUpdateConcurrencyException) { return Conflict("La cuenta cambió. Intenta de nuevo."); }
            await HttpContext.SignOutAsync(); return View("RecoveryCode");
        }
        ModelState.AddModelError("", "No se pudo recuperar la cuenta. Revisa los datos o inténtalo más tarde."); return View("Access", form);
    }
    [Authorize, HttpGet] public async Task<IActionResult> Settings()
    {
        var user = await Current(); ViewBag.Warning = user.WarningPercent; return View();
    }
    [Authorize, HttpPost] public async Task<IActionResult> Settings(decimal warning)
    {
        if (warning is <= 0 or >= 100) return BadRequest("El umbral debe estar entre 0 y 100.");
        var user = await Current(); user.WarningPercent = warning; await db.SaveChangesAsync(); TempData["Message"] = "Preferencias guardadas."; return RedirectToAction(nameof(Settings));
    }
    [Authorize, HttpPost] public async Task<IActionResult> ChangePassword(string currentPassword, string password, string confirm)
    {
        var user = await Current();
        if (password is null || password.Length is < 12 or > 128 || password != confirm ||
            await accounts.Verify(new() { Username = user.Username, Password = currentPassword }) is null) {
            TempData["Message"] = "Revisa la contraseña actual y la nueva confirmación (12 a 128 caracteres)."; return RedirectToAction(nameof(Settings));
        }
        ViewBag.Code = await accounts.Reset(user, password); await SignIn(user); return View("RecoveryCode");
    }
    [Authorize, HttpPost] public async Task<IActionResult> Logout() { await HttpContext.SignOutAsync(); return RedirectToAction(nameof(Login)); }
    [HttpGet] public IActionResult Error() { Response.StatusCode = 500; return Content("No se pudo completar la operación. Revisa la configuración y el registro del servidor."); }
    private Task<Student> Current() => db.Users.SingleAsync(x => x.Id == long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!));
    private async Task<Student?> DemoUser()
    {
        // Explicit local demonstration only: no password in HTML, no arbitrary account ID, no production bypass.
        if (!environment.IsDevelopment() || !db.Database.IsSqlite() ||
            HttpContext.Connection.RemoteIpAddress is not { } address || !System.Net.IPAddress.IsLoopback(address)) return null;
        var username = configuration["Demo:Username"];
        if (string.IsNullOrEmpty(username) || !username.StartsWith("demo_6meses_", StringComparison.Ordinal)) return null;
        var normalized = AccountService.Normalize(username);
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.NormalizedName == normalized);
        if (user is null || user.LockedUntil > DateTime.UtcNow) return null;
        return await db.Entries.AnyAsync(x => x.UserId == user.Id && x.Description.StartsWith("[DEMO FICTICIA]")) ? user : null;
    }
    private Task SignIn(Student user) => HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
        new ClaimsPrincipal(new ClaimsIdentity([new(ClaimTypes.NameIdentifier, user.Id.ToString()), new(ClaimTypes.Name, user.Username), new("version", user.SessionVersion)], CookieAuthenticationDefaults.AuthenticationScheme)));
}
