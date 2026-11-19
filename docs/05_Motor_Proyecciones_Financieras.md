# FINPULSE — Etapa 4: motor mensual y análisis financiero

> La [revisión QA posterior](07_Revision_QA.md) actualiza las reglas de metas: aporte constante redondeado hacia arriba a centavos, detención al alcanzar el objetivo con precisión interna y ausencia de estimación del aporte para fechas más allá de 600 meses. Las validaciones y el estado de ejecución actuales están en ese informe.

> Actualización del 19/11/2026: el bloqueo descrito más abajo conserva el resultado histórico de la etapa. En la sesión posterior se reprodujo y corrigió un error XAML `RowHeight="Auto"`; las 14 comprobaciones WPF adicionales y la ejecución directa Smoke Debug/Release ya pasaron. No se modificó la seguridad de Windows. El estado vigente, los comandos y las evidencias están en [Corrección de Smoke](06_Correccion_Error_Smoke.md).

## Estado de la entrega y verificación

Se implementó el flujo de datos → historial/supuestos → cálculo mensual → tres escenarios → gráficos/tablas → análisis de metas y recomendaciones, dentro de los cuatro proyectos existentes. SQLite sigue siendo predeterminado; los archivos de Oracle no se sustituyeron.

**Compilación comprobada:** el proyecto Desktop y el proyecto Smoke compilaron con cero advertencias y cero errores. **Pruebas ejecutadas:** 56 comprobaciones nuevas de motor/análisis, 64 de almacenamiento/finanzas y 26 de autenticación/hash, todas correctas (146). El caso obligatorio dio Bs 350 por mes y Bs 21.000 en 60 meses.

Se renderizaron las ocho vistas con una primera versión de la interfaz de etapa 4, sin errores de binding, y se repitieron las seis operaciones MVVM de la etapa 3. Después se mejoró la distribución visual y se añadieron 14 comprobaciones WPF adicionales. **La ejecución de esta última revisión quedó bloqueada por Windows con `FileLoadException`, `0x800711C7`; no se declaran esas 14 comprobaciones superadas.** Tampoco se declara comprobado el arranque interactivo final.

El registro `Microsoft-Windows-CodeIntegrity/Operational` confirmó eventos 3077/3033: `Finpulse.Smoke.dll` no cumplió los requisitos de firma/nivel de confianza de la política `{0283ac0f-fff1-49ae-ada1-8a933130cad6}`. También se había observado un bloqueo de `Finpulse.Infrastructure.dll` en Release. Restaurar y recompilar completamente fuera del entorno restringido no eliminó el bloqueo. No se desactivó ni modificó ninguna política de seguridad. La validación final requiere que el propietario/administrador permita los binarios de desarrollo mediante su procedimiento de confianza/firma; no se recomienda desactivar la protección. Referencia: [Microsoft: App Control y .NET](https://learn.microsoft.com/en-us/windows/security/application-security/application-control/app-control-for-business/design/appcontrol-and-dotnet).

Compilación final completa Release: cero errores y cero advertencias. SHA-256 para identificar los artefactos concretos al revisar su confianza (volver a compilar puede cambiar estos hashes):

- `tests/Finpulse.Smoke/bin/Release/net10.0-windows/Finpulse.Smoke.dll`: `176260D0C3D4C4439F0A9E56500F81F4AE66BE0F36D5CAAA5B7A019D096BC346`.
- `src/Finpulse.Desktop/bin/Release/net10.0-windows/Finpulse.Desktop.dll`: `492DCCC0ED2E3871331EB77142ED8EB858BA4A082D392D206FF9FFABDB614085`.

Se intentó abrir Desktop sin capturar el monitor: el proceso se inició, pero no llegó a exponer una ventana. Un proceso existente sin ventana no se contabilizó como arranque satisfactorio.

## Auditoría de la etapa 3

- Se reutilizaron entidades, repositorio, autenticación PBKDF2, comandos y navegación.
- Los 64 controles existentes volvieron a comprobar registro/consulta/edición/borrado, presupuestos, reservas, bloqueo de cuenta, aislamiento, rollback, integridad SQLite y reapertura desde otro proceso.
- No hay cuentas, ingresos ni gastos de prueba precargados en producción. Los importes de `tests/` y las capturas son exclusivamente fixtures de QA.
- Se corrigió el saldo actual: ahora excluye movimientos con fecha posterior a la fecha de corte. El mes actual muestra lo registrado hasta hoy. Los históricos incluyen meses anteriores completos.
- Se eliminó de la interfaz la inflación inicial fija del 3 % y los desplazamientos automáticos de ±2 puntos entre escenarios. Los escenarios nuevos empiezan con tasas cero y deben configurarse expresamente.
- Se conservaron `IProjectionService`/`ProjectionService` anuales de etapa 3 para compatibilidad con sus pruebas. Las nuevas vistas usan exclusivamente el motor mensual. No se presentan ambos como motores equivalentes con intereses.
- La auditoría estática Windows terminó sin errores ni advertencias; conserva una observación informativa sobre ReadyToRun, pendiente del empaquetado.

## Arquitectura y archivos

| Proyecto/archivo | Responsabilidad |
| --- | --- |
| `Domain/MonthlyProjection.cs` | `ProjectionRequest`, `ProjectionAssumptions`, `MonthlyProjectionPoint`, `IMonthlyProjectionEngine`, `MonthlyProjectionEngine`. Sin referencias WPF/SQLite. |
| `Application/FinancialAnalysisService.cs` | Promedio histórico, serie mensual de ingresos/trabajo/gastos/ahorro/saldo, reglas de recomendaciones y estimaciones de metas. |
| `Application/FinanceService.cs` | Corrección del resumen a fecha de corte; se mantienen contratos de persistencia y motor anterior. |
| `Desktop/ViewModels/DashboardViewModel.cs` | Resumen del usuario, rango 6/12/24 meses, selector de series, tendencia textual y recomendaciones. |
| `Desktop/ViewModels/ProjectionsViewModel.cs` | Parámetros con notificaciones, errores `INotifyDataErrorInfo`, tres escenarios independientes, recálculo inmediato, tarjetas/tablas/gráficos y metas. Incluye `ScenarioInputViewModel`. |
| `Desktop/ViewModels/WorkspaceViewModel.cs` | Conserva sesión/navegación/CRUD; se extraen los dos ViewModels anteriores a sus archivos. Desde Metas se accede a Proyecciones. |
| `Desktop/Controls/FinancialLineChart.cs` | Gráfico vectorial WPF redimensionable, con eje temporal, escala Bs, leyenda, líneas diferenciadas y consulta de puntos por ratón o flechas/Inicio/Fin. |
| `Desktop/Views/DashboardView.xaml` | Tarjetas, gráfico histórico, tabla mensual, categorías, movimientos recientes y observaciones. |
| `Desktop/Views/ProjectionsView.xaml` | Panel de parámetros separado del panel de resultados, tres tarjetas comparativas, gráfico, tabla nominal/real, metas y fundamentos numéricos. |
| `Desktop/Views/GoalsView.xaml` | Acceso al análisis de plazo y ahorro requerido. |
| `Desktop/App.xaml` | Estilo oscuro de CheckBox para controles de inflación/agregación. |
| `tests/Finpulse.Smoke/ProjectionChecks.cs` | 56 comprobaciones nuevas con resultados numéricos conocidos. |
| `tests/Finpulse.Smoke/Program.cs` | Integra las pruebas del nuevo motor con la suite existente. |
| `tests/Finpulse.Smoke/DesktopChecks.cs` | Amplía renderizado/pruebas de comandos con 14 verificaciones nuevas de actualización, escenarios, gráfico e aislamiento. Última ampliación bloqueada por Windows. |
| `README.md`, este documento, `docs/qa/README.md` | Ejecución, alcance y evidencia. |

Los ViewModels no realizan consultas ajenas a la sesión: reciben el `FinanceData` cargado por `WorkspaceViewModel` con `Repository.LoadAsync(UserId)`. El cálculo no escribe en las tablas y no recibe una conexión de base de datos. Se mantiene `decimal` en todos los importes y tasas; solo se convierte a `double` al dibujar coordenadas de pantalla.

## Historial y valores iniciales

Se usan los **tres meses naturales completos anteriores**, excluyendo el mes actual parcial. Se considera suficiente si cada uno contiene al menos un movimiento y en el conjunto existen ingresos y gastos. Ingreso/gasto mensual de partida = suma del tipo / 3, redondeada a dos decimales. Esto no demuestra que el usuario haya registrado todas sus operaciones; la interfaz pide confirmar la completitud.

Con historial insuficiente, ingresos y gastos empiezan en cero con una advertencia visible: no se interpretan como una estimación de las finanzas del usuario. Se pueden introducir valores manuales. El saldo inicial sí procede del saldo real registrado hasta hoy y también es editable como supuesto de simulación.

El dashboard distingue «Trabajo registrado» de ingresos totales: solo incluye categorías `Trabajo`, sin asumir que becas, mesadas o emprendimientos sean salarios. El saldo acumulado incorpora movimientos anteriores al inicio del rango visible. Un mes sin movimientos tiene cero flujo registrado, no actividad económica comprobada. La comparación de mejora/empeoramiento utiliza los dos últimos meses completos, si ambos tienen registros, y muestra la diferencia absoluta del ahorro.

## Convenciones y fórmulas del motor

Sea `I0` el ingreso mensual, `G0` el gasto mensual, `C0` el capital inicial, `N` el horizonte en meses y `m` el número de mes (1…N). El primer resultado corresponde a `fechaInicial.AddMonths(1)`; el motor avanza por aniversarios mensuales usando la regla `DateOnly.AddMonths` para días de fin de mes.

### Flujos y capacidad de ahorro

```text
y = floor((m - 1) / 12)
I(m) = I0 × (1 + variaciónIngresoAnual / 100)^y
G(m) = G0 × (1 + variaciónGastoAnual / 100)^y
S(m) = I(m) - G(m)
```

Los incrementos de flujos se aplican en meses 13, 25, 37…; no se aplican al primer año. La reducción mensual de gasto que introduce el usuario se resta de `G0` antes de proyectar y no modifica las transacciones reales.

### Aportes y prevención de dinero ficticio

El campo Aporte puede quedar vacío. En ese caso, el aporte deseado parte del excedente positivo de cada mes. Si se introduce un monto, se usa ese monto como base. En ambos casos se aplica la variación anual del aporte:

```text
Deseado(m) = (aporteManual ?? max(S(m),0)) × (1 + variaciónAporteAnual/100)^y
Si S(m) >= 0: A(m) = min(S(m), Deseado(m))
Si S(m) < 0:  A(m) = S(m)
```

Así, un aporte nunca inventa un ingreso externo. Si se desea aportar más que el excedente, se limita y la tarjeta lo indica. Un déficit se descuenta completo, aunque el aporte deseado sea cero. El excedente no aportado **queda fuera del capital modelado**: no se considera gastado ni se remunera. El gráfico representa el capital destinado a esta simulación; solo representa todo el saldo futuro si se aporta todo el excedente.

### Rendimiento compuesto

`rA` es rendimiento efectivo anual expresado como fracción (por ejemplo, 12 % = 0,12):

```text
factorMensual = (1 + rA)^(1/12)
rMensual = factorMensual - 1
R(m) = max(C(m-1), 0) × rMensual
C(m) = C(m-1) + R(m) + A(m)
```

Los aportes son **vencidos**: entran al final de cada mes y empiezan a rendir el siguiente. No se generan intereses sobre capital negativo; un déficit no constituye aquí un préstamo modelado. Un rendimiento negativo es admisible como supuesto de pérdida del capital positivo.

La raíz duodécima se calcula con Newton usando `decimal` (`xNuevo = (11x + valor/x^11)/12`), tolerancia `1e-26` y límite de 200 iteraciones. No se usa `Math.Pow(double)` para importes financieros.

### Inflación y poder adquisitivo

```text
factorPreciosMensual = (1 + inflaciónEfectivaAnual/100)^(1/12)
P(0) = 1
P(m) = P(m-1) × factorPreciosMensual
CapitalReal(m) = C(m) / P(m)
```

Se admiten inflación y deflación como supuestos. La inflación no modifica automáticamente el gasto: la variación de gastos es un parámetro separado, lo que evita contar dos veces el mismo incremento de precios. El resultado real se expresa en bolivianos de poder adquisitivo de la fecha inicial; no es una segunda cuenta bancaria.

El motor conserva precisión `decimal` internamente y redondea los campos de salida a centavos con `AwayFromZero`. No capitaliza a partir de los valores ya redondeados de la tabla. Al sumar campos mensuales mostrados pueden existir diferencias de centavos frente al capital interno final.

### Validaciones

- Horizonte personalizado: 1…600 meses (hasta 50 años), además de accesos 1/3/5/10 años.
- Fecha inicial entre 1900 y 2150.
- Capital inicial entre −1.000.000.000 y 1.000.000.000; ingreso/gasto/aporte inicial entre 0 y 1.000.000.000.
- Variaciones de ingreso/gasto/aporte: −100…100 % anual.
- Inflación/rendimiento efectivos: mayores que −100 % y hasta 100 % anual.
- Reducción de gastos: entre cero y el gasto inicial.
- Se rechazan resultados fuera del rango del simulador (capital absoluto superior a 1e18, índice de precios extremadamente pequeño o desbordamiento decimal).
- Entradas incompletas o inválidas limpian resultados anteriores y muestran el error junto al formulario; no se mantiene una simulación obsoleta como si correspondiera al valor nuevo.

## Escenarios e interfaz

Conservador, Base y Optimista tienen cada uno cinco tasas propias: variación de ingreso, gasto y aporte, inflación y rendimiento. **Todos empiezan en cero**; sus nombres no imponen que uno sea mejor o peor. Las diferencias deben introducirse explícitamente. Se comparan simultáneamente con el mismo saldo/ingreso/gasto/plazo inicial y se muestran los capitales finales nominales y reales.

El panel izquierdo permite editar los parámetros y el escenario seleccionado. El panel derecho conserva visibles los resultados y contiene:

1. Capacidad mensual/anual inicial y supuestos activos.
2. Tres tarjetas con capital final nominal y real.
3. Gráfico de líneas de los tres escenarios, con selector nominal/real y vista mensual/anual.
4. Tabla del escenario seleccionado: fecha, ingreso, gasto, capacidad, aporte neto, rendimiento, capital nominal y real.
5. Tabla de metas y observaciones con fundamento numérico.

Los resultados se recalculan al editar propiedades; no hace falta reiniciar ni pulsar Recalcular (el botón permanece como alternativa explícita). La vista anual toma los meses 12, 24… y añade el último mes si el horizonte no es múltiplo de 12. No suma saldos de distintos meses.

El gráfico es un control WPF vectorial propio, sin dependencias de navegador ni paquetes de gráficos. Base usa línea continua, Optimista guiones y Conservador puntos; el significado no depende solo del color. Flechas, Inicio y Fin consultan puntos; ratón y tooltip muestran los mismos valores. La tabla conserva una alternativa textual completa. Los ejes muestran Bs y fechas; magnitudes muy grandes usan notación científica en el eje, con valores completos en consulta/tabla. No hay bandas de confianza, movimientos continuos ni porcentajes de probabilidad.

## Metas

La simulación de cada meta comienza con **sus aportes ya registrados**, no con el saldo inicial global; así no vuelve a asignar el mismo saldo a todas las metas. Cada estimación supone dedicar a esa meta todo el aporte neto futuro del escenario seleccionado. Son comparaciones individuales alternativas, no un plan simultáneo que reparta automáticamente el dinero entre metas.

Meses necesarios/fecha estimada: primer punto cuya evolución alcanza el objetivo, dentro del horizonte seleccionado. Si no lo alcanza, se indica «No alcanzada en N meses simulados», sin afirmar imposibilidad para siempre. Una meta ya cumplida informa cero meses. Una meta vencida no divide entre cero y muestra la situación.

Para el ahorro mensual constante requerido se cuentan los aniversarios mensuales disponibles hasta la fecha objetivo. Si son `n`, con `q` como factor mensual del rendimiento:

```text
FactorAportes = Σ(q^k), k = 0…n-1
RequeridoMensual = max(0, (Objetivo - Acumulado × q^n) / FactorAportes)
Brecha = RequeridoMensual - primerAporteNetoProyectado
```

Con rendimiento cero se reduce a `(Objetivo - Acumulado)/n`. Este aporte requerido es una anualidad constante de referencia; puede diferir del calendario real con aportes variables. La fecha objetivo y el monto objetivo son nominales. No se crea ninguna transacción ni ningún aporte al calcular.

## Recomendaciones deterministas

- Déficit: gasto supuesto mayor que ingreso; muestra ambos montos y la capacidad negativa.
- Presupuesto excedido: gasto real del mes mayor que el límite; muestra exceso y porcentaje.
- Concentración: categoría con al menos 30 % de los gastos registrados del mes. La regla es explícita, no una norma financiera oficial.
- Efecto de reducir gastos: para categorías concentradas muestra una reducción **ilustrativa del 10 %** y su equivalente a 12 meses iguales; no afirma que el mes actual parcial represente el año.
- Meta fuera de plazo/no alcanzada: acumulado, objetivo, aporte constante requerido y brecha frente al aporte simulado.

Sin datos ni supuestos que activen una regla, no se generan consejos personales ficticios. No hay IA, entrenamiento, predicción estadística, garantías ni recomendación de un producto de inversión.

## Pruebas y comandos reproducibles

```powershell
dotnet restore .\tests\Finpulse.Smoke\Finpulse.Smoke.csproj
dotnet build .\tests\Finpulse.Smoke\Finpulse.Smoke.csproj --no-restore
dotnet run --project .\tests\Finpulse.Smoke\Finpulse.Smoke.csproj --no-build
dotnet run --project .\tests\Finpulse.Smoke\Finpulse.Smoke.csproj --no-build -- --ui
dotnet run --project .\src\Finpulse.Desktop\Finpulse.Desktop.csproj
```

Ejecutar desde la raíz en VS Code, con Windows y SDK .NET 10. Si Windows informa `0x800711C7`, no es un fallo de validación de datos ni de NuGet: la ejecución queda impedida por la política observada; debe resolverse la confianza/firma mediante el administrador antes de repetir las pruebas. Un `build` exitoso no prueba que Windows vaya a permitir la carga de los ensamblados.

La suite nueva comprueba el ejemplo obligatorio, ahorro negativo, saldo inicial cero, flujos iguales, meses personalizados, 1/3/5/10 años, anualidad compuesta con resultado analítico independiente, inflación/deflación, rendimiento negativo, incrementos en mes 13, aportes limitados, déficits, tasas/montos/plazos extremos, tiempo/plazo/requerido de metas, promedio histórico, exclusión de ingresos futuros, salario, saldo acumulado y recomendaciones numéricas. Las suites existentes comprueban usuarios aislados y persistencia.

Las 14 verificaciones WPF nuevas comprueban recálculo inmediato 1200/850/21000, separación de escenarios, reducción de gasto, agregación anual, inflación real, invalidación/recuperación del formulario, consulta de puntos, usuario sin datos ajenos, simulación sin escrituras, cambio de rango/series, historial y carga automática de promedios, además de binding. **Están implementadas y compilan; su ejecución final está pendiente por el bloqueo descrito.**

Las capturas existentes `docs/qa/Dashboard.png` y `Proyecciones.png` corresponden al primer renderizado de etapa 4, anterior a la redistribución final de paneles. No se presentan como capturas de la revisión final. Los nuevos archivos `Etapa4-*` solo se producirán al ejecutar satisfactoriamente la ampliación WPF.

## Limitaciones e integración con Oracle

- Las tasas no se consultan en fuentes externas: todos los valores son supuestos editables del usuario. Cero es el punto de partida neutral, no una tasa oficial.
- No se modelan impuestos, comisiones, riesgo probabilístico, estacionalidad, eventos extraordinarios, préstamos ni calendario de pagos por día.
- Los parámetros viven en la vista actual; cambiar de sección reinicia la simulación. No se han añadido tablas de simulaciones guardadas.
- El historial considera movimientos registrados, no garantiza su completitud. Los registros futuros quedan excluidos del saldo actual, pero no constituyen un módulo de pagos programados.
- El gráfico representa capital aportado según la convención descrita; excedente sin aportar no se remunera ni se añade a esa serie.
- Las metas se estiman individualmente. No hay optimizador de reparto entre metas simultáneas.
- La última distribución visual y las 14 pruebas adicionales necesitan ejecución una vez autorizado el binario por la política de Windows; accesibilidad con lector real, varios monitores/DPI y recorrido manual completo siguen pendientes. No se usó Computer Use ni captura del monitor.
- Oracle conserva autenticación/esquema original, sin repositorio financiero nuevo. Para integrarlo: crear tablas financieras y restricciones, implementar `IFinanceRepository` con filtros obligatorios por usuario, respetar `decimal`/centavos y fechas, probar transacciones/aislamiento/reservas y habilitar las mismas vistas con ese repositorio. El motor de dominio y el análisis no requieren cambiar fórmulas.
- El empaquetado/firma con un publicador reconocido y autorizado debe resolverse antes de distribuir la aplicación. La compilación no genera por sí sola confianza de Windows.
