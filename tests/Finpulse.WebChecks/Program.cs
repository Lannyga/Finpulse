using Finpulse.Web.Models;
using Finpulse.Web.Services;
using System.ComponentModel.DataAnnotations;

foreach (var (password, expected) in new[] { ("12345", false), ("123456", true), ("abc123", true), (new string('a', 128), true), (new string('a', 129), false) }) {
    var form = new Credentials { Username = "alumno", Password = password };
    Check(Validator.TryValidateObject(form, new ValidationContext(form), [], true) == expected,
        $"Valida contraseña de {password.Length} caracteres");
}

if (args.Contains("--oracle")) { await OracleChecks.Run(); return; }

var today = new DateOnly(2026, 11, 20);
var data = new FinanceData([
    new(1, MovementKind.Income, 100, "Mesada", today.AddMonths(-1), ""),
    new(2, MovementKind.Income, 1200, "Beca", today, ""),
    new(3, MovementKind.Expense, 850, "Educación", today, ""),
    new(4, MovementKind.Expense, 900, "Compras", today.AddDays(1), "")
], [], [new(5, 1, 100, today)]);
var summary = FinanceRules.Summarize(data, today);
Check(summary.Balance == 450 && summary.Income == 1200 && summary.Expense == 850, "Saldo histórico y totales del mes; excluye fechas futuras");
Check(summary.Available == 350, "Guardar en una meta no duplica gastos");
var history = FinanceRules.History(data, today);
Check(history.Length == 6 && history[^1].Income == 1200 && history[^1].Expense == 850 && history[^2].Income == 100, "Gráfico mensual con meses sin movimientos");
Check(FinanceRules.Summarize(new([], [], []), today) == new FinancialSummary(0, 0, 0, 0), "Resumen vacío sin errores");
foreach (var amount in new[] { 0m, -1m, .001m, 1_000_000_001m }) {
    try { FinanceRules.ValidateAmount(amount); throw new Exception("Monto inválido aceptado"); }
    catch (ArgumentException) { Check(true, $"Rechaza monto {amount}"); }
}
FinanceRules.ValidateAmount(.01m);
Check(true, "Acepta un centavo");
Console.WriteLine("Para verificar persistencia y HTTP en Oracle: dotnet run --project tests/Finpulse.WebChecks -- --oracle");

static void Check(bool valid, string message)
{
    if (!valid) throw new Exception(message);
    Console.WriteLine("OK: " + message);
}
