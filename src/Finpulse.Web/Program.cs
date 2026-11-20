using System.Security.Claims;
using System.Threading.RateLimiting;
using Finpulse.Web.Data;
using Finpulse.Web.Models;
using Finpulse.Web.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders(); builder.Logging.AddConsole();
var protection = builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(builder.Configuration["DataProtection:KeyDirectory"] ?? Path.Combine(builder.Environment.ContentRootPath,"App_Data","keys"))).SetApplicationName("FINPULSE.Web");
if (OperatingSystem.IsWindows()) protection.ProtectKeysWithDpapi();
builder.Services.AddControllersWithViews(o => o.Filters.Add(new AutoValidateAntiforgeryTokenAttribute()));
builder.Services.AddAntiforgery(o=>o.Cookie.SecurePolicy=CookieSecurePolicy.Always);
builder.Services.AddDbContext<PortalDb>(o => {
    var provider = builder.Configuration["Database:Provider"] ?? "Oracle";
    if (provider == "Oracle") {
        var connection = builder.Configuration.GetConnectionString("Oracle") ?? throw new InvalidOperationException("Configura ConnectionStrings__Oracle fuera del repositorio.");
        var cs = new Oracle.ManagedDataAccess.Client.OracleConnectionStringBuilder(connection);
        if (new[] { "SYS", "SYSTEM" }.Contains(cs.UserID.ToUpperInvariant())) throw new InvalidOperationException("Usa una cuenta Oracle de privilegios mÃ­nimos.");
        o.UseOracle(connection, x => x.UseOracleSQLCompatibility(OracleSQLCompatibility.DatabaseVersion19));
    } else if (provider == "Sqlite") o.UseSqlite(builder.Configuration.GetConnectionString("Sqlite") ?? "Data Source=finpulse-web.db;Foreign Keys=True;Default Timeout=15");
    else throw new InvalidOperationException("Proveedor invÃ¡lido: Oracle o Sqlite.");
});
builder.Services.AddScoped<PortalFinance>();
builder.Services.AddScoped<AccountService>();
builder.Services.AddScoped<PasswordHasher<Student>>();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(o => {
    o.LoginPath = "/Account/Login";
    o.Cookie.Name = "__Host-Finpulse";
    o.Cookie.HttpOnly = true; o.Cookie.SecurePolicy = CookieSecurePolicy.Always; o.Cookie.SameSite = SameSiteMode.Lax;
    o.ExpireTimeSpan = TimeSpan.FromMinutes(30); o.SlidingExpiration = true;
    o.Events.OnValidatePrincipal = async context => {
        var db = context.HttpContext.RequestServices.GetRequiredService<PortalDb>();
        if (!long.TryParse(context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ||
            !await db.Users.AnyAsync(x => x.Id == id && x.SessionVersion == context.Principal!.FindFirstValue("version"))) context.RejectPrincipal();
    };
});
builder.Services.AddAuthorization();
builder.Services.AddRateLimiter(o => {
    o.RejectionStatusCode = 429;
    o.AddPolicy("access", c => RateLimitPartition.GetFixedWindowLimiter(c.Connection.RemoteIpAddress?.ToString() ?? "local",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
var app = builder.Build();
if (args.Contains("--verify-oracle")) {
    using var scope = app.Services.CreateScope();
    try {
        await OracleVerification.Verify(scope.ServiceProvider.GetRequiredService<PortalDb>(), builder.Configuration["Oracle:ExpectedUser"] ?? "FINPULSE_WEB");
    } catch (Oracle.ManagedDataAccess.Client.OracleException ex) {
        Console.Error.WriteLine($"No se verificó Oracle (ORA-{ex.Number:D5}). Revisa servicio, credenciales y esquema; no se usará SQLite.");
        Environment.ExitCode = 1;
    }
    return;
}
if (args.Length == 2 && args[0] == "--backup-sqlite") {
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<PortalDb>();
    if (!db.Database.IsSqlite()) throw new InvalidOperationException("El respaldo exige seleccionar SQLite explícitamente.");
    if (File.Exists(args[1])) throw new InvalidOperationException("El respaldo ya existe; no se sobrescribirá.");
    var source = (Microsoft.Data.Sqlite.SqliteConnection)db.Database.GetDbConnection();
    // Mode=ReadOnly prevents accidental creation of a missing source and includes committed WAL data.
    var sourceOptions = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder(source.ConnectionString) { Mode = Microsoft.Data.Sqlite.SqliteOpenMode.ReadOnly };
    using var input = new Microsoft.Data.Sqlite.SqliteConnection(sourceOptions.ConnectionString);
    using var output = new Microsoft.Data.Sqlite.SqliteConnection(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = args[1] }.ConnectionString);
    input.Open(); output.Open(); input.BackupDatabase(output);
    using var check = output.CreateCommand(); check.CommandText = "PRAGMA integrity_check";
    if ((string?)check.ExecuteScalar() != "ok") throw new InvalidOperationException("El respaldo no superó la comprobación de integridad.");
    Console.WriteLine("Respaldo SQLite consistente verificado; origen sin modificaciones."); return;
}
if(args.Contains("--create-demo")) {
    if(!app.Environment.IsDevelopment()) throw new InvalidOperationException("La creación de demo requiere el entorno Development.");
    using var scope=app.Services.CreateScope();
    var demo=await DemoAccount.Create(scope.ServiceProvider.GetRequiredService<PortalDb>(),scope.ServiceProvider.GetRequiredService<AccountService>(),scope.ServiceProvider.GetRequiredService<PortalFinance>(),PortalFinance.Today);
    // Credentials are emitted only by this explicit local command, never on startup or via an HTTP endpoint.
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(demo)); return;
}
if(args.Contains("--seed-categories")) {
    using var scope=app.Services.CreateScope();await PortalFinance.SeedCategories(scope.ServiceProvider.GetRequiredService<PortalDb>());
    Console.WriteLine("Catálogo de categorías listo.");return;
}
if (args.Contains("--import-legacy")) {
    int offset = Array.IndexOf(args, "--import-legacy");
    if (args.Length < offset + 3) throw new ArgumentException("Usa --import-legacy ruta-origen.db mapa-usuarios.json");
    using var scope = app.Services.CreateScope();
    await LegacyImport.Run(scope.ServiceProvider.GetRequiredService<PortalDb>(), args[offset+1], args[offset+2]);
    Console.WriteLine("Importación completada; origen sin modificaciones."); return;
}
if (args.Contains("--schema")) {
    using var scope = app.Services.CreateScope();
    Console.WriteLine(scope.ServiceProvider.GetRequiredService<PortalDb>().Database.GenerateCreateScript()); return;
}
// Creating an isolated local database is explicit. Oracle schema is always reviewed and installed separately.
if (args.Contains("--init-local")) {
    using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<PortalDb>();
    if (!db.Database.IsSqlite()) throw new InvalidOperationException("--init-local solo admite SQLite.");
    await db.Database.EnsureCreatedAsync(); await PortalFinance.SeedCategories(db);
    Console.WriteLine("Base local lista; no se crearon usuarios."); return;
}
using (var scope = app.Services.CreateScope()) {
    var db = scope.ServiceProvider.GetRequiredService<PortalDb>();
    if (db.Database.IsOracle()) {
        try { await OracleVerification.Verify(db, builder.Configuration["Oracle:ExpectedUser"] ?? "FINPULSE_WEB"); }
        catch (Oracle.ManagedDataAccess.Client.OracleException ex) {
            throw new InvalidOperationException($"No se puede iniciar FINPULSE: Oracle no disponible (ORA-{ex.Number:D5}). Revisa la conexion configurada.");
        }
    }
}
if (!app.Environment.IsDevelopment()) { app.UseExceptionHandler("/Account/Error"); app.UseHsts(); }
app.UseHttpsRedirection(); app.UseStaticFiles(); app.UseRouting(); app.UseRateLimiter(); app.UseAuthentication(); app.UseAuthorization();
app.Use(async (c,next) => {
    c.Response.Headers["X-Content-Type-Options"] = "nosniff";
    c.Response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; font-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";
    c.Response.Headers["Referrer-Policy"] = "same-origin";
    c.Response.Headers.CacheControl = "no-store";
    await next();
});
app.MapControllerRoute("default", "{controller=Finance}/{action=Index}/{id?}");
app.Run();
public partial class Program;

