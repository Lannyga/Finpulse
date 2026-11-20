using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
using Finpulse.Web.Data;
using Finpulse.Web.Models;
using Finpulse.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

internal static class OracleChecks
{
    public static async Task Run()
    {
        var config = new ConfigurationBuilder().AddUserSecrets("finpulse-web-local").AddEnvironmentVariables().Build();
        var connection = config.GetConnectionString("OracleTest") ?? throw new InvalidOperationException("Configura ConnectionStrings:OracleTest.");
        var settings = new Oracle.ManagedDataAccess.Client.OracleConnectionStringBuilder(connection);
        if (settings.UserID != "FINPULSE_WEB_TEST" || settings.DataSource != "localhost:1521/FREEPDB1")
            throw new InvalidOperationException("Solo se permiten pruebas en FINPULSE_WEB_TEST local.");
        var options = new DbContextOptionsBuilder<PortalDb>().UseOracle(connection,
            x => x.UseOracleSQLCompatibility(OracleSQLCompatibility.DatabaseVersion19)).Options;
        await using var db = new PortalDb(options);
        await db.Verify( "FINPULSE_WEB_TEST");
        var root = Directory.GetCurrentDirectory();
        var artifacts = Path.Combine(root, "tests", ".qa", "simple");
        Directory.CreateDirectory(artifacts);
        var certificate = Path.Combine(artifacts, "https.pfx");
        using (var rsa = RSA.Create(2048)) {
            var request = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            var san = new SubjectAlternativeNameBuilder(); san.AddDnsName("localhost"); request.CertificateExtensions.Add(san.Build());
            using var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(1));
            await File.WriteAllBytesAsync(certificate, cert.Export(X509ContentType.Pfx));
        }
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
        var origin = new Uri($"https://localhost:{port}");
        var web = Path.Combine(root, "src", "Finpulse.Web", "bin", "Debug", "net10.0", "Finpulse.Web.dll");
        Process? server = null;
        var output = new List<string>();
        async Task Start()
        {
            var start = new ProcessStartInfo("dotnet") {
                WorkingDirectory = Path.Combine(root, "src", "Finpulse.Web"), UseShellExecute = false,
                CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
            };
            start.ArgumentList.Add(web);
            start.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
            start.Environment["ASPNETCORE_URLS"] = origin.ToString().TrimEnd('/');
            start.Environment["ConnectionStrings__Oracle"] = connection;
            start.Environment["Oracle__ExpectedUser"] = "FINPULSE_WEB_TEST";
            start.Environment["DataProtection__KeyDirectory"] = Path.Combine(artifacts, "keys");
            start.Environment["Kestrel__Certificates__Default__Path"] = certificate;
            server = Process.Start(start) ?? throw new Exception("No inició el portal");
            server.OutputDataReceived += (_, e) => { if (e.Data is not null) lock (output) output.Add(e.Data); };
            server.ErrorDataReceived += (_, e) => { if (e.Data is not null) lock (output) output.Add(e.Data); };
            server.BeginOutputReadLine(); server.BeginErrorReadLine();
            using var client = Client();
            for (int i = 0; i < 60; i++) {
                if (server.HasExited) throw new Exception("El portal terminó antes de iniciar");
                try { if ((await client.GetAsync("/Account/Login")).StatusCode == HttpStatusCode.OK) return; }
                catch (HttpRequestException) { }
                await Task.Delay(500);
            }
            throw new TimeoutException("El portal no respondió");
        }
        async Task Stop()
        {
            if (server is null) return;
            if (!server.HasExited) { server.Kill(true); await server.WaitForExitAsync(); }
            server.Dispose(); server = null;
        }
        HttpClient Client() => new(new HttpClientHandler {
            AllowAutoRedirect = false, CookieContainer = new CookieContainer(),
            // Only the isolated local server uses this temporary certificate.
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true
        }) { BaseAddress = origin };
        int count = 0;
        void Check(bool valid, string title) { if (!valid) throw new Exception(title); Console.WriteLine($"ORACLE {++count}: {title}"); }
        string Field(string html, string name) => WebUtility.HtmlDecode(Regex.Match(html, $"name=\"{Regex.Escape(name)}\"[^>]*value=\"([^\"]*)\"").Groups[1].Value);
        async Task<string> Get(HttpClient client, string path) {
            using var response = await client.GetAsync(path);
            Check(response.StatusCode == HttpStatusCode.OK, "GET " + path);
            return await response.Content.ReadAsStringAsync();
        }
        async Task<HttpResponseMessage> Post(HttpClient client, string path, string html, Dictionary<string, string> fields) {
            fields["__RequestVerificationToken"] = Field(html, "__RequestVerificationToken");
            return await client.PostAsync(path, new FormUrlEncodedContent(fields));
        }
        var name = "qa_simple_" + Guid.NewGuid().ToString("N")[..10];
        const string password = "123456";
        try {
            await Start();
            using var first = Client(); using var second = Client();
            Check((await first.GetAsync("/Finance/Index")).StatusCode == HttpStatusCode.Redirect, "Protege el resumen anónimo");
            Check((await first.PostAsync("/Account/Login", new FormUrlEncodedContent([]))).StatusCode == HttpStatusCode.BadRequest, "Rechaza POST sin CSRF");
            var shortForm = await Get(first, "/Account/Register");
            Check(shortForm.Contains("minlength=\"6\""), "Formulario pide mínimo seis caracteres");
            Check((await Post(first, "/Account/Register", shortForm, new() { ["Username"] = name, ["Password"] = "12345", ["Confirm"] = "12345" })).StatusCode == HttpStatusCode.OK, "Servidor rechaza contraseña de cinco caracteres");
            foreach (var client in new[] { first, second }) {
                var html = await Get(client, "/Account/Register");
                using var result = await Post(client, "/Account/Register", html, new() {
                    ["Username"] = client == first ? name : name + "b", ["Password"] = password, ["Confirm"] = password
                });
                Check(result.StatusCode == HttpStatusCode.Redirect, "Registro directo sin código de emergencia");
                Check(result.Headers.GetValues("Set-Cookie").Any(x => x.StartsWith("__Host-Finpulse=") && x.Contains("secure", StringComparison.OrdinalIgnoreCase) && x.Contains("httponly", StringComparison.OrdinalIgnoreCase)), "Cookie segura");
            }
            var page = await Get(first, "/Finance/Movements");
            var income = Regex.Match(page, "<option value=\"(\\d+)\" data-kind=\"0\"").Groups[1].Value;
            var expense = Regex.Match(page, "<option value=\"(\\d+)\" data-kind=\"1\"").Groups[1].Value;
            var today = PortalFinance.Today.ToString("yyyy-MM-dd");
            var fields = new Dictionary<string, string> { ["module"] = "movements", ["RequestKey"] = Field(page, "RequestKey"), ["Kind"] = "0", ["CategoryId"] = income, ["Amount"] = "1000.25", ["Date"] = today, ["Description"] = "Ingreso prueba simple" };
            Check((await Post(first, "/Finance/Save", page, fields)).StatusCode == HttpStatusCode.Redirect, "Guarda ingreso en Oracle");
            Check((await Post(first, "/Finance/Save", page, fields)).StatusCode == HttpStatusCode.Redirect, "Reenvío tolerado");
            page = await Get(first, "/Finance/Movements");
            Check(Regex.Matches(page, "Ingreso prueba simple").Count == 1, "Reenvío no duplica ingresos");
            var movementId = Field(page, "id");
            Check((await second.GetAsync("/Finance/Edit?module=movements&id=" + movementId)).StatusCode == HttpStatusCode.NotFound, "No edita movimientos ajenos");
            Check(!(await Get(second, "/Finance/Movements")).Contains("Ingreso prueba simple"), "Lecturas aisladas");
            var otherPage = await Get(second, "/Finance/Movements");
            await Post(second, "/Finance/Delete", otherPage, new() { ["module"] = "movements", ["id"] = movementId });
            Check((await Get(first, "/Finance/Movements")).Contains("Ingreso prueba simple"), "No elimina movimientos ajenos");
            fields = new() { ["module"] = "movements", ["RequestKey"] = Field(page, "RequestKey"), ["Kind"] = "1", ["CategoryId"] = expense, ["Amount"] = "250.25", ["Date"] = today };
            Check((await Post(first, "/Finance/Save", page, fields)).StatusCode == HttpStatusCode.Redirect, "Guarda gasto con nota vacía");
            var edit = await Get(first, "/Finance/Edit?module=movements&id=" + movementId);
            fields = new() { ["module"] = "movements", ["Id"] = movementId, ["Revision"] = Field(edit, "Revision"), ["RequestKey"] = Field(edit, "RequestKey"), ["Kind"] = "0", ["CategoryId"] = income, ["Amount"] = "1000.25", ["Date"] = today, ["Description"] = "Ingreso editado" };
            Check((await Post(first, "/Finance/Save", edit, fields)).StatusCode == HttpStatusCode.Redirect, "Edita movimiento propio");
            Check((await Post(first, "/Finance/Save", edit, fields)).StatusCode == HttpStatusCode.OK, "Rechaza edición obsoleta");
            page = await Get(first, "/Finance/Goals");
            Check((await Post(first, "/Finance/Save", page, new() { ["module"] = "goals", ["RequestKey"] = Field(page, "RequestKey"), ["Name"] = "Computadora", ["Amount"] = "1200", ["Date"] = PortalFinance.Today.AddMonths(3).ToString("yyyy-MM-dd") })).StatusCode == HttpStatusCode.Redirect, "Crea meta");
            page = await Get(first, "/Finance/Goals");
            var goalId = Field(page, "GoalId");
            var contributionKey = Field(page[page.IndexOf("value=\"contributions\"", StringComparison.Ordinal)..], "RequestKey");
            Check((await Post(first, "/Finance/Save", page, new() { ["module"] = "contributions", ["RequestKey"] = contributionKey, ["GoalId"] = goalId, ["Amount"] = "100", ["Date"] = today })).StatusCode == HttpStatusCode.Redirect, "Guarda Bs 100 en meta");
            var user = await db.Users.SingleAsync(x => x.Username == name);
            var finance = new PortalFinance(db, Microsoft.Extensions.Logging.Abstractions.NullLogger<PortalFinance>.Instance);
            var summary = FinanceRules.Summarize((await finance.Load(user.Id)).Data, PortalFinance.Today);
            Check(summary.Balance == 750 && summary.Available == 650 && summary.Expense == 250.25m, "Saldo 750, libre 650; ahorro no duplica gasto");
            page = await Get(first, "/Finance/Index");
            await File.WriteAllTextAsync(Path.Combine(artifacts, "summary.html"), page);
            await File.WriteAllTextAsync(Path.Combine(artifacts, "movements.html"), await Get(first, "/Finance/Movements"));
            await File.WriteAllTextAsync(Path.Combine(artifacts, "goals.html"), await Get(first, "/Finance/Goals"));
            var nav = Regex.Match(page, "<nav.*?</nav>", RegexOptions.Singleline).Value;
            Check(Regex.Matches(nav, "<a ").Count == 3, "Exactamente tres pestañas laterales");
            foreach (var path in new[] { "/Finance/Budgets", "/Finance/Projections", "/Finance/Reports", "/Finance/Export", "/Account/Recover" })
                Check((await first.GetAsync(path)).StatusCode == HttpStatusCode.NotFound, "Ruta eliminada " + path);
            await Stop(); await Start();
            Check((await Get(first, "/Finance/Goals")).Contains("Computadora"), "Sesión y meta persisten tras reinicio");
            page = await Get(first, "/Finance/Goals");
            var attempts = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => Post(first, "/Finance/Save", page, new() { ["module"] = "contributions", ["RequestKey"] = Guid.NewGuid().ToString("N"), ["GoalId"] = goalId, ["Amount"] = "400", ["Date"] = today })));
            Check(attempts.Count(x => x.StatusCode == HttpStatusCode.Redirect) == 1 && attempts.Count(x => x.StatusCode == HttpStatusCode.OK) == 1, "Dos ahorros simultáneos no superan saldo libre");
            db.ChangeTracker.Clear();
            summary = FinanceRules.Summarize((await finance.Load(user.Id)).Data, PortalFinance.Today);
            Check(summary.Balance == 750 && summary.Available == 250, "Oracle conserva el saldo tras concurrencia");
            page = await Get(first, "/Account/Settings");
            await Post(first, "/Account/ChangePassword", page, new() { ["currentPassword"] = password, ["password"] = "12345", ["confirm"] = "12345" });
            Check((await Post(first, "/Account/ChangePassword", page, new() { ["currentPassword"] = password, ["password"] = "654321", ["confirm"] = "654321" })).StatusCode == HttpStatusCode.Redirect, "Cambiar contraseña acepta seis dígitos; el intento de cinco no cambió la clave");
            await Post(first, "/Account/Logout", await Get(first, "/Account/Settings"), new());
            Check((await first.GetAsync("/Finance/Index")).StatusCode == HttpStatusCode.Redirect, "Cerrar sesión elimina acceso");
            page = await Get(first, "/Account/Login");
            Check((await Post(first, "/Account/Login", page, new() { ["Username"] = name, ["Password"] = password })).StatusCode == HttpStatusCode.OK, "Contraseña anterior inválida");
            Check((await Post(first, "/Account/Login", page, new() { ["Username"] = name, ["Password"] = "654321" })).StatusCode == HttpStatusCode.Redirect, "Nueva contraseña de seis dígitos funciona");
            Console.WriteLine($"OK: {count} comprobaciones sobre Oracle real y HTTPS.");
        } finally {
            await Stop();
            lock (output) File.WriteAllLines(Path.Combine(artifacts, "server.log"), output);
            // Remove only the two accounts generated by this run and their dependent rows.
            var ids = await db.Users.Where(x => x.Username == name || x.Username == name + "b").Select(x => x.Id).ToArrayAsync();
            await db.Entries.Where(x => ids.Contains(x.UserId)).ExecuteDeleteAsync();
            await db.Goals.Where(x => ids.Contains(x.UserId)).ExecuteDeleteAsync();
            await db.Users.Where(x => ids.Contains(x.Id)).ExecuteDeleteAsync();
        }
    }
}
