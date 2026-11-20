using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Finpulse.Web.Data;
using Finpulse.Web.Models;
using Finpulse.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Oracle.ManagedDataAccess.Client;

internal static class OracleChecks
{
    public static async Task Run()
    {
        var config = new ConfigurationBuilder().AddUserSecrets("finpulse-web-local").AddEnvironmentVariables().Build();
        var connection = config.GetConnectionString("OracleTest") ?? throw new InvalidOperationException("Configura ConnectionStrings:OracleTest mediante entrada local segura.");
        var cs = new OracleConnectionStringBuilder(connection);
        if (cs.UserID != "FINPULSE_WEB_TEST" || cs.DataSource != "localhost:1521/FREEPDB1")
            throw new InvalidOperationException("Las pruebas solo admiten FINPULSE_WEB_TEST en localhost:1521/FREEPDB1.");
        var options = new DbContextOptionsBuilder<PortalDb>().UseOracle(connection, x => x.UseOracleSQLCompatibility(OracleSQLCompatibility.DatabaseVersion19)).Options;
        await using var db = new PortalDb(options);
        await OracleVerification.Verify(db, "FINPULSE_WEB_TEST");
        if (await db.Users.AnyAsync(x => !x.Username.StartsWith("qa_")))
            throw new InvalidOperationException("El esquema contiene cuentas ajenas a estas pruebas. No se modifico ningun dato.");
        var root = Directory.GetCurrentDirectory();
        var web = Path.Combine(root,"src","Finpulse.Web","bin","Release","net10.0","Finpulse.Web.dll");
        if (!File.Exists(web)) throw new InvalidOperationException("Compila Release desde la raiz del proyecto.");
        var artifacts = Path.Combine(root,"tests",".qa","oracle-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(artifacts);
        var certificate = Path.Combine(artifacts,"https.pfx");
        using (var rsa = RSA.Create(2048)) {
            var request = new CertificateRequest("CN=localhost",rsa,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1);
            var san = new SubjectAlternativeNameBuilder(); san.AddDnsName("localhost"); request.CertificateExtensions.Add(san.Build());
            using var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5),DateTimeOffset.UtcNow.AddDays(1));
            await File.WriteAllBytesAsync(certificate,cert.Export(X509ContentType.Pfx));
        }
        var listener = new TcpListener(IPAddress.Loopback,0); listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
        var origin = $"https://localhost:{port}";
        Process? server = null;
        async Task Stop() {
            if (server is null) return;
            if (!server.HasExited) { server.Kill(entireProcessTree:true); await server.WaitForExitAsync(); }
            server.Dispose(); server = null;
        }
        async Task Start() {
            var start = new ProcessStartInfo("dotnet") { WorkingDirectory = Path.Combine(root,"src","Finpulse.Web"), UseShellExecute=false, CreateNoWindow=true, RedirectStandardOutput=true, RedirectStandardError=true };
            start.ArgumentList.Add(web);
            start.Environment["ASPNETCORE_ENVIRONMENT"]="Development";
            start.Environment["ASPNETCORE_URLS"]=origin;
            start.Environment["Database__Provider"]="Oracle";
            start.Environment["ConnectionStrings__Oracle"]=connection;
            start.Environment["Oracle__ExpectedUser"]="FINPULSE_WEB_TEST";
            start.Environment["DataProtection__KeyDirectory"]=Path.Combine(artifacts,"keys");
            start.Environment["Kestrel__Certificates__Default__Path"]=certificate;
            // Local synthetic-test diagnostics; EF sensitive parameter logging remains disabled.
            server = Process.Start(start) ?? throw new InvalidOperationException("No inicio el servidor.");
            server.OutputDataReceived += (_,e) => { if(e.Data is not null) File.AppendAllText(Path.Combine(artifacts,"server.log"),e.Data+Environment.NewLine); };
            server.ErrorDataReceived += (_,e) => { if(e.Data is not null) File.AppendAllText(Path.Combine(artifacts,"error.log"),e.Data+Environment.NewLine); };
            server.BeginOutputReadLine(); server.BeginErrorReadLine();
            using var http = new HttpClient(new HttpClientHandler { ServerCertificateCustomValidationCallback=(_,_,_,_)=>true });
            for (var i=0;i<60;i++) {
                if (server.HasExited) throw new InvalidOperationException("El portal Oracle termino antes de iniciar.");
                try { if ((await http.GetAsync(origin+"/Account/Register")).StatusCode==HttpStatusCode.OK) return; }
                catch (HttpRequestException) { }
                await Task.Delay(500);
            }
            throw new TimeoutException("El portal no respondio por HTTPS.");
        }
        try {
            await Start();
            var name = await HttpChecks.Run(origin, async () => { await Stop(); await Start(); });
            var user = await db.Users.SingleAsync(x=>x.Username==name);
            var income = await db.Categories.FirstAsync(x=>x.Kind==0);
            var expense = await db.Categories.FirstAsync(x=>x.Kind==1);
            var entries = await db.Entries.AsNoTracking().Where(x=>x.UserId==user.Id).ToArrayAsync();
            if (entries.Where(x=>x.Kind==0).Sum(x=>x.Amount)!=1000 || entries.Where(x=>x.Kind==1).Sum(x=>x.Amount)!=250)
                throw new Exception("Importes HTTP no persistieron en Oracle como se esperaba.");
            async Task Reject(int expectedCode, Entry row) {
                await using var tx = await db.Database.BeginTransactionAsync();
                try {
                    db.Entries.Add(row);
                    try { await db.SaveChangesAsync(); }
                    catch (DbUpdateException ex) when (ex.InnerException is OracleException ora && ora.Number==expectedCode) {
                        Console.WriteLine($"ORACLE: restriccion verificada ORA-{expectedCode:D5}."); return;
                    }
                    throw new Exception("Oracle no rechazo el registro invalido esperado.");
                } finally { await tx.RollbackAsync(); db.ChangeTracker.Clear(); }
            }
            await Reject(2290,new() { UserId=user.Id,CategoryId=income.Id,Kind=0,Amount=-1,Date=DateTime.Today });
            await Reject(2291,new() { UserId=user.Id,CategoryId=expense.Id,Kind=0,Amount=1,Date=DateTime.Today });
            var other = await db.Users.SingleAsync(x=>x.Username==name+"b");
            var goal = await db.Goals.SingleAsync(x=>x.UserId==user.Id);
            await Reject(2291,new() { UserId=other.Id,GoalId=goal.Id,Kind=2,Amount=1,Date=DateTime.Today });
            var existing=entries.First(x=>x.Kind==0);
            await Reject(1,new() { UserId=user.Id,CategoryId=income.Id,Kind=0,Amount=1,Date=DateTime.Today,RequestKey=existing.RequestKey });
            // A valid write followed by rollback must not survive a fresh context.
            var key=Guid.NewGuid().ToString("N");
            await using (var tx=await db.Database.BeginTransactionAsync()) {
                db.Entries.Add(new() { UserId=user.Id,CategoryId=income.Id,Kind=0,Amount=1,Date=DateTime.Today,RequestKey=key });
                await db.SaveChangesAsync(); await tx.RollbackAsync();
            }
            await using var fresh=new PortalDb(options);
            if(await fresh.Entries.AnyAsync(x=>x.RequestKey==key)) throw new Exception("El rollback no revirtio la escritura.");
            async Task<bool> Allocate() {
                await using var context=new PortalDb(options);
                var finance=new PortalFinance(context,NullLogger<PortalFinance>.Instance);
                try {
                    await finance.Save(user.Id,"contributions",new() { GoalId=goal.Id,Amount=400,Date=PortalFinance.Today.ToDateTime(TimeOnly.MinValue) });
                    return true;
                } catch(ArgumentException) { return false; }
                catch(DbUpdateConcurrencyException) { return false; }
            }
            var allocations=await Task.WhenAll(Allocate(),Allocate());
            var reserved=await fresh.Entries.Where(x=>x.UserId==user.Id && x.Kind==2).SumAsync(x=>x.Amount);
            if(allocations.Count(x=>x)!=1 || reserved!=500) throw new Exception("Las aportaciones concurrentes no conservaron la disponibilidad esperada.");
            Console.WriteLine("ORACLE: dos aportaciones concurrentes de Bs 400; solo una aceptada, reserva total Bs 500 y disponible Bs 250.");
            Console.WriteLine("OK ORACLE: flujos HTTPS, dos cuentas sinteticas, Bs 1000 - Bs 250 = Bs 750, reinicio, restricciones y rollback verificados. Sin DDL ni borrado de datos.");
        } finally { await Stop(); OracleConnection.ClearAllPools(); }
    }
}
