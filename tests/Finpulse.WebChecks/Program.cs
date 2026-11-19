using Finpulse.Application;
using Finpulse.Web.Data;
using Finpulse.Web.Models;
using Finpulse.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

if(args.Length==2 && args[0]=="--certificate") {
    using var rsa=System.Security.Cryptography.RSA.Create(2048);
    var req=new System.Security.Cryptography.X509Certificates.CertificateRequest("CN=localhost",rsa,System.Security.Cryptography.HashAlgorithmName.SHA256,System.Security.Cryptography.RSASignaturePadding.Pkcs1);
    var san=new System.Security.Cryptography.X509Certificates.SubjectAlternativeNameBuilder();san.AddDnsName("localhost");req.CertificateExtensions.Add(san.Build());
    using var cert=req.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5),DateTimeOffset.UtcNow.AddDays(2));
    await File.WriteAllBytesAsync(args[1],cert.Export(System.Security.Cryptography.X509Certificates.X509ContentType.Pfx));
    Console.WriteLine("Certificado efímero de pruebas generado; no se instaló confianza en el sistema.");return;
}
if(args.Length==2 && args[0]=="--http") { await HttpChecks.Run(args[1]);return; }
ProjectionChecks.Run();
await ImportChecks.Run();
int checks = 0;
void Check(bool condition, string title) { if (!condition) throw new Exception(title); Console.WriteLine($"WEB {++checks}: {title}"); }
async Task Reject(Func<Task> action, string title) { try { await action(); } catch (ArgumentException) { Check(true,title); return; } throw new Exception(title); }
var path = Path.Combine(Path.GetTempPath(), $"finpulse-web-{Guid.NewGuid():N}.db");
var options = new DbContextOptionsBuilder<PortalDb>().UseSqlite($"Data Source={path};Foreign Keys=True;Pooling=False").Options;
try {
await using var db = new PortalDb(options);
await db.Database.EnsureCreatedAsync(); await PortalFinance.SeedCategories(db);
Check(db.Model.GetEntityTypes().Count() == 6, "exactamente seis tablas principales");
Check(!db.Model.GetEntityTypes().SelectMany(x=>x.GetProperties()).Any(x=>x.Name.Contains("email",StringComparison.OrdinalIgnoreCase)), "esquema sin correos");
var account = new AccountService(db, new PasswordHasher<Student>(), NullLogger<AccountService>.Instance);
var first = await account.Register(new(){Username="estudiante",Password="una-clave-segura-123",Confirm="una-clave-segura-123"});
Check(first.Code.Length == 48 && first.User.RecoveryHash != first.Code, "registro sin correo y recuperación protegida");
await Reject(()=>account.Register(new(){Username="ESTUDIANTE",Password="una-clave-segura-123",Confirm="una-clave-segura-123"}), "rechazo de usuario normalizado duplicado");
db.ChangeTracker.Clear();
var second = await account.Register(new(){Username="otro",Password="otra-clave-segura-123",Confirm="otra-clave-segura-123"});
Check(await account.Verify(new(){Username="ESTUDIANTE",Password="una-clave-segura-123"}) is not null, "acceso por nombre normalizado");
Check(await account.Verify(new(){Username="estudiante",Password="incorrecta"}) is null, "contraseña incorrecta rechazada");
var finance = new PortalFinance(db, NullLogger<PortalFinance>.Instance);
var cats = await db.Categories.ToArrayAsync(); var income = cats.First(x=>x.Kind==0); var expense = cats.First(x=>x.Kind==1);
var today = PortalFinance.Today.ToDateTime(TimeOnly.MinValue);
var input = new FinanceForm {Kind=0,CategoryId=income.Id,Amount=1200,Date=today};
await finance.Save(first.User.Id,"movements",input); await finance.Save(first.User.Id,"movements",input);
Check((await finance.Load(first.User.Id)).Data.Movements.Count == 1, "reenvío idempotente de movimiento");
await finance.Save(first.User.Id,"movements",new(){Kind=1,CategoryId=expense.Id,Amount=850,Date=today});
var data = (await finance.Load(first.User.Id)).Data;
Check(FinanceRules.Summarize(data,PortalFinance.Today).Balance == 350,"balance 1200 - 850 = 350 persistido");
Check((await finance.Load(second.User.Id)).Data.Movements.Count==0,"aislamiento de lectura");
await Reject(()=>finance.Delete(second.User.Id,"movements",data.Movements[0].Id),"eliminación cruzada rechazada"); db.ChangeTracker.Clear();
await Reject(()=>finance.Save(first.User.Id,"movements",new(){Kind=1,CategoryId=income.Id,Amount=1,Date=today}),"categoría incompatible rechazada"); db.ChangeTracker.Clear();
await finance.Save(first.User.Id,"budgets",new(){CategoryId=expense.Id,Amount=800,Month=PortalFinance.Today.ToString("yyyy-MM")});
Check(FinanceRules.Evaluate((await finance.Load(first.User.Id)).Data.Budgets.Single(),data.Movements).Spent==850,"presupuesto calcula gastos reales");
await Reject(()=>finance.Save(first.User.Id,"budgets",new(){CategoryId=expense.Id,Amount=900,Month=PortalFinance.Today.ToString("yyyy-MM")}),"presupuesto duplicado rechazado"); db.ChangeTracker.Clear();
await finance.Save(first.User.Id,"goals",new(){Name="Equipo",Amount=1000,Date=today.AddYears(1)});
var goal=(await finance.Load(first.User.Id)).Data.Goals.Single();
await finance.Save(first.User.Id,"contributions",new(){GoalId=goal.Id,Amount=100,Date=today});
var allocated=FinanceRules.Summarize((await finance.Load(first.User.Id)).Data,PortalFinance.Today);
Check(allocated.Balance==350 && allocated.Available==250 && allocated.Expense==850,"aportes no duplican gastos ni ingresos");
await Reject(()=>finance.Save(second.User.Id,"contributions",new(){GoalId=goal.Id,Amount=1,Date=today}),"aporte a meta ajena rechazado"); db.ChangeTracker.Clear();
await Reject(()=>finance.Save(first.User.Id,"contributions",new(){GoalId=goal.Id,Amount=251,Date=today}),"sobreasignación rechazada"); db.ChangeTracker.Clear();
await Reject(()=>finance.Delete(first.User.Id,"movements",data.Movements.Single(x=>x.Kind==Finpulse.Domain.MovementKind.Income).Id),"ingreso que respalda aportes protegido"); db.ChangeTracker.Clear();
Check((await finance.Load(first.User.Id)).Data.Movements.Count==2,"rollback preserva ingreso");
await Reject(()=>finance.Save(first.User.Id,"movements",new(){Kind=1,CategoryId=expense.Id,Amount=.001m,Date=today}),"fracciones de centavo rechazadas");
await finance.SaveScenario(first.User.Id,new(){Name="Base",Income=1200,Expense=850,Years=5});
var scenario=(await finance.Load(first.User.Id)).Scenarios.Single();
Check(new Finpulse.Domain.MonthlyProjectionEngine().Calculate(PortalFinance.Request(scenario))[^1].Balance==21000,"escenario persistido produce Bs 21000");
Check((await finance.Load(second.User.Id)).Scenarios.Count==0,"escenarios aislados por usuario");
var editable=(await finance.Load(first.User.Id));
var budgetToEdit=editable.Data.Budgets.Single();
await finance.Save(first.User.Id,"budgets",new(){Id=budgetToEdit.Id,Revision=editable.Revision,CategoryId=expense.Id,Amount=950,Month=budgetToEdit.Month});
Check((await finance.Load(first.User.Id)).Data.Budgets.Single().Amount==950,"edición de presupuesto conserva identidad");
await Reject(()=>finance.Save(first.User.Id,"budgets",new(){Id=budgetToEdit.Id,Revision=editable.Revision,CategoryId=expense.Id,Amount=900,Month=budgetToEdit.Month}),"edición obsoleta rechazada");db.ChangeTracker.Clear();
editable=await finance.Load(first.User.Id);
await finance.Save(first.User.Id,"goals",new(){Id=goal.Id,Revision=editable.Revision,Name="Equipo actualizado",Amount=1200,Date=today.AddYears(1)});
Check((await finance.Load(first.User.Id)).Data.Goals.Single().Name=="Equipo actualizado","edición de meta conserva aportes");
await Reject(()=>finance.Delete(first.User.Id,"goals",goal.Id),"meta con aportes protegida");db.ChangeTracker.Clear();
var contributionId=(await finance.Load(first.User.Id)).Data.Contributions.Single().Id;
await finance.Delete(first.User.Id,"contributions",contributionId);
Check(FinanceRules.Summarize((await finance.Load(first.User.Id)).Data,PortalFinance.Today).Available==350,"liberar aporte recupera disponibilidad sin cambiar balance");
await finance.Delete(first.User.Id,"goals",goal.Id);
Check((await finance.Load(first.User.Id)).Data.Goals.Count==0,"eliminación de meta sin aportes");
await Reject(()=>finance.SaveScenario(first.User.Id,new(){Name="Precisión",Income=1.000000001m,Years=1}),"supuestos con precisión no persistible rechazados");
var recoveryUser=await account.Verify(new(){Username="estudiante",RecoveryCode=first.Code},true);
Check(recoveryUser is not null,"código válido verifica recuperación");
var oldVersion=recoveryUser!.SessionVersion;
var newCode=await account.Reset(recoveryUser,"nueva-clave-segura-123");
Check(await account.Verify(new(){Username="estudiante",RecoveryCode=first.Code},true) is null,"código anterior invalidado");
Check(await account.Verify(new(){Username="estudiante",RecoveryCode=newCode},true) is not null && recoveryUser.SessionVersion!=oldVersion,"nuevo código y revocación de sesiones");
for(int i=0;i<5;i++) await account.Verify(new(){Username="otro",Password="incorrecta"});
Check(await account.Verify(new(){Username="otro",Password="otra-clave-segura-123"}) is null,"bloqueo tras cinco intentos fallidos");
// Independent contexts demonstrate optimistic concurrency on the shared owner revision.
await using(var a=new PortalDb(options)) await using(var b=new PortalDb(options)) {
 var ua=await a.Users.SingleAsync(x=>x.Id==first.User.Id); var ub=await b.Users.SingleAsync(x=>x.Id==first.User.Id);
 ua.Revision++; await a.SaveChangesAsync(); ub.Revision++;
 try { await b.SaveChangesAsync(); throw new Exception("conflicto no detectado"); } catch(DbUpdateConcurrencyException) { Check(true,"revisión de usuario detecta escrituras concurrentes"); }
}
var priorSnapshot=await finance.Load(first.User.Id);
var demo=await DemoAccount.Create(db,account,finance,PortalFinance.Today);
var demoData=(await finance.Load(demo.UserId)).Data;
var demoStart=new DateOnly(PortalFinance.Today.Year,PortalFinance.Today.Month,1).AddMonths(-6);
Check(demo.From==demoStart && demoData.Movements.Min(x=>x.Date)==demoStart,"demo tiene seis meses completos más el mes actual");
Check(demoData.Movements.All(x=>x.Date<=PortalFinance.Today&&x.Description.StartsWith("[DEMO FICTICIA]")),"demo identificada como ficticia y sin movimientos futuros");
Check(demoData.Budgets.Count==14&&demoData.Goals.Count==3&&demoData.Contributions.Count==6,"demo incluye presupuestos, metas y aportes");
Check(demoData.Goals.Any(g=>demoData.Contributions.Where(c=>c.GoalId==g.Id).Sum(c=>c.Amount)==g.Target),"demo incluye una meta cumplida");
Check(new FinancialAnalysisService().Baseline(demoData,PortalFinance.Today) is {Sufficient:true,Income:1633.33m,Expense:1293.33m},"promedio demo calculado desde tres meses completos");
Check(demo.Summary.Balance-demo.Summary.Available==1100&&demo.Summary.Available>=0,"demo reserva Bs 1100 sin duplicar gastos");
Check((await finance.Load(demo.UserId)).Scenarios.Select(x=>x.Years).Order().SequenceEqual(new[]{1,3,5,10}),"demo incluye cuatro horizontes persistidos");
Check(await account.Verify(new(){Username=demo.Username,Password=demo.Password}) is not null,"credenciales demo aleatorias permiten acceso");
var preserved=await finance.Load(first.User.Id);
Check(preserved.Data.Movements.SequenceEqual(priorSnapshot.Data.Movements)&&preserved.Revision==priorSnapshot.Revision,"crear demo no modifica cuentas existentes");
Console.WriteLine($"OK: {checks} comprobaciones web/datos, además de regresiones del motor.");
} finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if(File.Exists(path)) File.Delete(path); }
