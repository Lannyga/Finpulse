using System.Security.Claims;
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
    [HttpGet] public async Task<IActionResult> Goals() => View(await finance.Load(UserId));

    [HttpGet]
    public async Task<IActionResult> Edit(string module, long id)
    {
        var snapshot = await finance.Load(UserId);
        var form = new FinanceForm { Id = id, Revision = snapshot.Revision };
        if (module == "movements" && snapshot.Data.Movements.SingleOrDefault(x => x.Id == id) is Movement movement) {
            form.Kind = (int)movement.Kind;
            form.Amount = movement.Amount;
            form.CategoryId = snapshot.Categories.Single(x => x.Kind == form.Kind && x.Name == movement.Category).Id;
            form.Date = movement.Date.ToDateTime(TimeOnly.MinValue);
            form.Description = movement.Description;
        } else if (module == "goals" && snapshot.Data.Goals.SingleOrDefault(x => x.Id == id) is SavingGoal goal) {
            form.Name = goal.Name;
            form.Description = goal.Description;
            form.Amount = goal.Target;
            form.Date = goal.Deadline.ToDateTime(TimeOnly.MinValue);
        } else return NotFound();
        ViewBag.Form = form;
        return View(ActionFor(module), snapshot);
    }

    [HttpPost]
    public async Task<IActionResult> Save(string module, FinanceForm form)
    {
        if (ActionFor(module) is null) return NotFound();
        if (ModelState.IsValid) {
            try {
                await finance.Save(UserId, module, form);
                TempData["Message"] = "Guardado. Tu resumen ya está actualizado.";
                return RedirectToAction(ActionFor(module));
            } catch (ArgumentException e) { ModelState.AddModelError("", e.Message); }
            catch (DbUpdateException) { ModelState.AddModelError("", "Tus datos cambiaron o este registro ya existe. Recarga e inténtalo otra vez."); }
        }
        ViewBag.Form = form;
        return View(ActionFor(module), await finance.Load(UserId));
    }

    [HttpPost]
    public async Task<IActionResult> Delete(string module, long id)
    {
        if (ActionFor(module) is null) return NotFound();
        try { await finance.Delete(UserId, module, id); TempData["Message"] = "Registro eliminado."; }
        catch (ArgumentException e) { TempData["Message"] = e.Message; }
        catch (DbUpdateException) { TempData["Message"] = "Tus datos cambiaron. Recarga e inténtalo otra vez."; }
        return RedirectToAction(ActionFor(module));
    }

    private static string? ActionFor(string module) => module switch {
        "movements" => "Movements", "goals" or "contributions" => "Goals", _ => null
    };
}
