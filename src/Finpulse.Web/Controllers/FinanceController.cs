using System.Globalization;
using System.Security.Claims;
using System.Text;
using Finpulse.Application;
using Finpulse.Domain;
using Finpulse.Web.Models;
using Finpulse.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Finpulse.Web.Controllers;

[Authorize]
public sealed class FinanceController(PortalFinance finance) : Controller
{
    private long UserId => long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    [HttpGet] public async Task<IActionResult> Index() => View(await finance.Load(UserId));
    [HttpGet] public async Task<IActionResult> Movements() => View(await finance.Load(UserId));
    [HttpGet] public async Task<IActionResult> Budgets() => View(await finance.Load(UserId));
    [HttpGet] public async Task<IActionResult> Goals() => View(await finance.Load(UserId));
    [HttpGet] public async Task<IActionResult> Projections() => View(await finance.Load(UserId));
    [HttpGet] public async Task<IActionResult> Reports() => View(await finance.Load(UserId));
    [HttpGet] public async Task<IActionResult> Edit(string module, long id)
    {
        var snapshot=await finance.Load(UserId);
        var f=new FinanceForm{Id=id,Revision=snapshot.Revision};
        if(module=="movements" && snapshot.Data.Movements.SingleOrDefault(x=>x.Id==id) is Movement m) {
            f.Kind=(int)m.Kind;f.Amount=m.Amount;f.CategoryId=snapshot.Categories.Single(x=>x.Kind==f.Kind&&x.Name==m.Category).Id;f.Date=m.Date.ToDateTime(TimeOnly.MinValue);f.Description=m.Description;
        } else if(module=="budgets" && snapshot.Data.Budgets.SingleOrDefault(x=>x.Id==id) is Budget b) {
            f.Month=b.Month;f.Amount=b.Amount;f.CategoryId=b.Category=="General"?null:snapshot.Categories.Single(x=>x.Kind==1&&x.Name==b.Category).Id;
        } else if(module=="goals" && snapshot.Data.Goals.SingleOrDefault(x=>x.Id==id) is SavingGoal g) {
            f.Name=g.Name;f.Description=g.Description;f.Amount=g.Target;f.Date=g.Deadline.ToDateTime(TimeOnly.MinValue);
        } else return NotFound();
        ViewBag.Form=f;return View(ActionFor(module),snapshot);
    }
    [HttpPost] public async Task<IActionResult> Save(string module, FinanceForm form)
    {
        if (!ModelState.IsValid) return await Invalid(module, form);
        try { await finance.Save(UserId, module, form); TempData["Message"] = "Registro guardado."; }
        catch (ArgumentException e) { ModelState.AddModelError("", e.Message); return await Invalid(module, form); }
        catch (DbUpdateException) { ModelState.AddModelError("", "No se guardó el registro. Puede haberse enviado ya o cambiado en otra sesión. Recarga y revisa tus datos."); return await Invalid(module, form); }
        return RedirectToAction(ActionFor(module));
    }
    [HttpPost] public async Task<IActionResult> Delete(string module, long id)
    {
        try { await finance.Delete(UserId, module, id); TempData["Message"] = "Registro eliminado."; }
        catch (ArgumentException e) { TempData["Message"] = e.Message; }
        catch (DbUpdateException) { TempData["Message"] = "La operación no pudo completarse. Recarga y revisa tus datos."; }
        return RedirectToAction(ActionFor(module));
    }
    [HttpPost] public async Task<IActionResult> Scenario(ScenarioForm form)
    {
        if (ModelState.IsValid) try { await finance.SaveScenario(UserId, form); return RedirectToAction(nameof(Projections)); }
        catch (ArgumentException e) { ModelState.AddModelError("", e.Message); }
        catch (DbUpdateException) { ModelState.AddModelError("", "No se pudo guardar el escenario. Revisa si ya existe."); }
        ViewBag.Form = form; return View("Projections", await finance.Load(UserId));
    }
    [HttpGet] public async Task<IActionResult> Export(long? scenarioId)
    {
        var snapshot = await finance.Load(UserId); var csv = new StringBuilder();
        static string Cell(string s) => "\"" + ((s.Length > 0 && "=+-@\t\r".Contains(s[0])) ? "'" : "") + s.Replace("\"", "\"\"") + "\"";
        if (scenarioId is long id) {
            var scenario = snapshot.Scenarios.SingleOrDefault(x => x.Id == id); if (scenario is null) return NotFound();
            csv.AppendLine("Mes,Fecha,Ingreso,Gasto,Aporte,Rendimiento,Saldo nominal,Saldo real");
            foreach (var p in new MonthlyProjectionEngine().Calculate(PortalFinance.Request(scenario)))
                csv.AppendLine(FormattableString.Invariant($"{p.Month},{p.Date:yyyy-MM-dd},{p.Income},{p.Expense},{p.Contribution},{p.Return},{p.Balance},{p.RealBalance}"));
        } else {
            csv.AppendLine("Fecha,Tipo,Categoria,Monto BOB,Descripcion");
            foreach (var m in snapshot.Data.Movements) csv.AppendLine($"{m.Date:yyyy-MM-dd},{(m.Kind == MovementKind.Income ? "Ingreso" : "Gasto")},{Cell(m.Category)},{m.Amount.ToString(CultureInfo.InvariantCulture)},{Cell(m.Description)}");
        }
        return File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray(), "text/csv; charset=utf-8", scenarioId.HasValue ? "finpulse-proyeccion.csv" : "finpulse-movimientos.csv");
    }
    private async Task<IActionResult> Invalid(string module, FinanceForm form) { ViewBag.Form = form; return View(ActionFor(module), await finance.Load(UserId)); }
    private static string ActionFor(string module) => module switch { "movements" => "Movements", "budgets" => "Budgets", "goals" or "contributions" => "Goals", "scenarios" => "Projections", _ => "Index" };
}
