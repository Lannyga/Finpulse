using System.Security.Claims;
using System.Globalization;
using System.Threading.RateLimiting;
using Finpulse.Web.Controllers;
using Finpulse.Web.Data;
using Finpulse.Web.Models;
using Finpulse.Web.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

// HTML number inputs send a decimal point, regardless of the Windows language.
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
var protection = builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(builder.Configuration["DataProtection:KeyDirectory"]
        ?? Path.Combine(builder.Environment.ContentRootPath, "App_Data", "keys-windows")))
    .SetApplicationName("FINPULSE.Web");
if (OperatingSystem.IsWindows()) protection.ProtectKeysWithDpapi();

var connection = builder.Configuration.GetConnectionString("Oracle")
    ?? throw new InvalidOperationException("Configura ConnectionStrings:Oracle con dotnet user-secrets.");
var oracle = new Oracle.ManagedDataAccess.Client.OracleConnectionStringBuilder(connection);
if (new[] { "SYS", "SYSTEM" }.Contains(oracle.UserID.ToUpperInvariant()))
    throw new InvalidOperationException("Usa una cuenta Oracle con privilegios mínimos.");
builder.Services.AddDbContext<PortalDb>(o => o.UseOracle(connection,
    x => x.UseOracleSQLCompatibility(OracleSQLCompatibility.DatabaseVersion19)));
builder.Services.AddScoped<PortalFinance>();
builder.Services.AddScoped<AccountService>();
builder.Services.AddScoped<PasswordHasher<Student>>();
builder.Services.AddControllersWithViews(o => o.Filters.Add(new AutoValidateAntiforgeryTokenAttribute()));
builder.Services.AddAntiforgery(o => o.Cookie.SecurePolicy = CookieSecurePolicy.Always);
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(o => {
    o.LoginPath = "/Account/Login";
    o.Cookie.Name = "__Host-Finpulse";
    o.Cookie.HttpOnly = true;
    o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    o.Cookie.SameSite = SameSiteMode.Lax;
    o.ExpireTimeSpan = TimeSpan.FromMinutes(30);
    o.SlidingExpiration = true;
    o.Events.OnValidatePrincipal = async context => {
        var db = context.HttpContext.RequestServices.GetRequiredService<PortalDb>();
        var version = context.Principal?.FindFirstValue("version");
        if (!long.TryParse(context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ||
            !await db.Users.AnyAsync(x => x.Id == id && x.SessionVersion == version)) context.RejectPrincipal();
    };
});
builder.Services.AddAuthorization();
builder.Services.AddRateLimiter(o => {
    o.RejectionStatusCode = 429;
    o.AddPolicy("access", c => RateLimitPartition.GetFixedWindowLimiter(c.Connection.RemoteIpAddress?.ToString() ?? "local",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});

var app = builder.Build();
using (var scope = app.Services.CreateScope()) {
    var db = scope.ServiceProvider.GetRequiredService<PortalDb>();
    await db.Verify( builder.Configuration["Oracle:ExpectedUser"] ?? "FINPULSE_WEB");
    if (args.Contains("--seed-categories")) { await PortalFinance.SeedCategories(db); return; }
}
if (!app.Environment.IsDevelopment()) { app.UseExceptionHandler("/Account/Error"); app.UseHsts(); }
app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.Use(async (context, next) => {
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; font-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";
    context.Response.Headers["Referrer-Policy"] = "same-origin";
    context.Response.Headers.CacheControl = "no-store";
    await next();
});
app.MapControllerRoute("default", "{controller=Finance}/{action=Index}/{id?}");
app.Run();
