# FINPULSE — revisión de calidad y regresiones

Validación: 19/11/2026. Se aplicó el flujo `testing-qa` a los riesgos reales del proyecto WPF existente, manteniendo las suites de consola y las bases temporales. No se instalaron frameworks web, no se usó Computer Use y no se capturó el monitor.

## Estado verificado

| Control | Resultado observado |
| --- | --- |
| Compilación Debug estricta (`-warnaserror`) | 0 errores, 0 advertencias |
| Suite normal Debug | 163 comprobaciones correctas: 56 de motor/análisis, 17 nuevas de fiabilidad, 64 SQLite/finanzas y 26 de autenticación/hash |
| Vistas y comandos WPF Debug | 8 vistas renderizadas, 0 errores de binding, 14 comprobaciones dinámicas y 6 operaciones MVVM correctas; proceso con código 0 |
| Estabilidad del cierre WPF | 3 ejecuciones consecutivas del EXE Debug con `--ui`, todas con código 0; además pasó el control completo Debug posterior |
| Consulta NuGet, paquetes directos y transitivos de Infrastructure | No se reportaron vulnerabilidades en el origen consultado |
| Auditoría estática Windows | 0 errores y 0 advertencias; 1 información sobre ReadyToRun para empaquetado |
| Compilación Release estricta | 0 errores, 0 advertencias |
| Suite normal Release | 163 comprobaciones correctas |
| Vistas WPF Release | Bloqueadas por Windows al cargar `Finpulse.Desktop.dll`, `FileLoadException` / `0x800711C7`; código 1. Se confirmó en una segunda ejecución del mismo binario |

El comando de calidad Debug completo terminó con código 0, incluida la revisión de vulnerabilidades. El intento Release se detuvo ante el bloqueo y **no** se declaró aprobado. Las comprobaciones correctas no constituyen una garantía de ausencia absoluta de defectos, seguridad universal ni aprobación para producción.

## Problemas corregidos

### 1. Una meta podía quedar corta por centavos

Objetivo Bs 100, tres meses y rendimiento cero: el redondeo más cercano proponía Bs 33,33 mensuales, que solo reúne Bs 99,99. El aporte constante requerido ahora se redondea **hacia arriba** a centavos: Bs 33,34.

### 2. El plazo objetivo se acortaba silenciosamente

Una fecha objetivo más allá de 600 meses se trataba como si tuviera exactamente 600 meses. Ahora el aporte requerido queda sin estimar para ese plazo y se informa la limitación mediante `RequiredNote`; no se inventa un plazo distinto. La simulación sigue usando el horizonte seleccionado.

### 3. Una meta alcanzada podía invalidarse décadas después

El servicio calculaba el horizonte completo antes de buscar la fecha de cumplimiento. Con rendimientos y horizontes extremos, el capital podía exceder el rango mucho después de haber alcanzado la meta y hacer fallar esa estimación. Se añadió `CalculateUntilTarget`, que detiene el cálculo al alcanzar el objetivo. La regresión verifica Bs 500 millones iniciales hacia Bs 1.000 millones con rendimiento anual efectivo del 100 %: la meta se alcanza en 12 meses, sin seguir capitalizando durante 50 años.

### 4. El saldo mostrado podía adelantar el cumplimiento

Se buscaba el objetivo usando el saldo ya redondeado a centavos. Ahora `TargetReached` se calcula a partir del capital interno. Se usa una tolerancia numérica de Bs `0,00000001` para el residuo de la raíz decimal de tasas efectivas, muy inferior a un centavo. No se usa el redondeo visible a dos decimales para decidir el cumplimiento. La prueba Bs 0,005/mes hacia Bs 0,01 exige dos meses, aunque el primer saldo visible se redondee a Bs 0,01.

### 5. Entradas extremas o nulas podían provocar excepciones inadecuadas

Se rechazan solicitudes/supuestos nulos, objetivos no positivos, acumulados negativos y tasas tan próximas a −100 % que no puedan representarse en la precisión decimal disponible. Se evita una división por cero dentro de Newton y se mantienen los errores de validación explícitos.

### 6. Una base de una versión futura podía perder su identificación

El constructor ejecutaba `PRAGMA user_version=1` sin comprobar la versión existente. Ahora rechaza versiones superiores antes de ejecutar DDL o cambiar el diario. La creación del esquema conocido ocurre dentro de una transacción. Las pruebas verifican rechazo, versión 9 conservada, dato marcador intacto y ausencia de tablas de versión antigua añadidas a esa base temporal.

No se abrió, eliminó, migró ni reinicializó la base financiera real del usuario.

### 7. Las pruebas podían fallar por fechas o rutas de desarrollo

Las pruebas locales ya no dependen del mes fijo noviembre de 2026: capturan la fecha actual y derivan el mes presupuestario. Los artefactos WPF se localizan desde `AppContext.BaseDirectory` y la raíz del proyecto, sin depender del directorio desde el que se inició el EXE. Si el binario está fuera del árbol fuente, se crea un directorio temporal exclusivo para artefactos.

Los procesos hijos tienen un máximo de 20 segundos y las pruebas WPF un máximo de dos minutos. Un bloqueo produce un error explícito; no deja la validación esperando indefinidamente.

### 8. Los fallos del runner no tenían una salida controlada

La entrada Smoke conserva y muestra el `Exception.ToString()` completo en stderr y devuelve código 1 ante un fallo. No hay `catch` vacío ni conversión de errores en éxito. Un argumento desconocido también termina con código 1: no ejecuta otra suite silenciosamente. Esto se comprueba desde un proceso hijo, leyendo su salida y su código real.

Un bloqueo que impida al CLR cargar el propio ensamblado de entrada puede producirse antes de ese manejador; no se afirma que un `try/catch` solucione Control de aplicaciones.

### 9. El cierre de las pruebas WPF podía fallar después de imprimir «OK»

El control de calidad detectó un proceso que había completado las aserciones pero terminó con un error del CLR:

```text
Attempt to execute managed code after the .NET runtime thread state has been destroyed.
ceemain.cpp:1721
```

Las pruebas mezclaban un `Dispatcher.Run` manual, `Application.Shutdown` y un `InvokeShutdown` forzado adicional. Se cambió a un ciclo normal de `Application.Run`/`Shutdown`. Los recursos compartidos se extrajeron a `Desktop/Themes/Theme.xaml`: producción los carga desde `App.xaml` y las pruebas los cargan sin iniciar el formulario real ni acceder a la base del usuario. Los errores de arranque/limpieza se propagan al resultado y al código de salida. Se comprobó el proceso completo, no solo su texto de salida.

## Estrategia y alcance

- Reglas puras: importes, porcentajes, escenarios, inflación, rendimiento, límites y metas.
- Integración: SQLite temporal, claves foráneas, aislamiento de usuarios, rollback, reservas y persistencia en otro proceso.
- Componentes WPF: carga real de recursos, construcción y renderizado de las ocho vistas, binding y comandos MVVM con base temporal.
- Fiabilidad del ejecutable: salida 0/1, stderr con excepción completa, argumentos inválidos, tiempo máximo de procesos y cierre repetido.
- Rendimiento acotado: tres series de 600 meses, con resultados numéricos comprobados; en las ejecuciones observadas tardaron aproximadamente 2,5–4 ms. Es una medición local, no una garantía para cualquier equipo, historial o número de metas.
- Seguridad revisada: consultas parametrizadas, conservación de datos, aislamiento ya probado, hashing y consulta NuGet. No sustituye una auditoría de seguridad independiente ni garantiza ausencia de vulnerabilidades no publicadas.

No se midió un porcentaje de cobertura de líneas. No se afirma cobertura superior al 80 %, recorrido manual E2E, validación de lector de pantalla, DPI físico múltiple ni integración financiera Oracle completa. Las métricas de la habilidad son guías; se registran las comprobaciones que realmente se ejecutaron.

## Comando reproducible de calidad

Desde la raíz, con Windows y SDK .NET 10:

```powershell
.\scripts\validar-calidad.ps1 -Configuration Debug -RevisarVulnerabilidades
.\scripts\validar-calidad.ps1 -Configuration Release -RevisarVulnerabilidades
```

El script restaura, compila con advertencias como errores, ejecuta las suites normales y WPF secuencialmente y consulta NuGet cuando se solicita. Además de comprobar el código de salida, analiza el JSON de NuGet y falla si hay paquetes vulnerables reportados. No envía resultados a un servicio de CI ni crea ejecutores remotos. Puede integrarse posteriormente en un agente Windows autorizado.

`-SinVistas` omite la suite WPF de forma explícita y no equivale a la validación completa. Si la política de PowerShell impide ejecutar el archivo, no se modifica esa política: se pueden revisar los comandos del script y ejecutar los comandos de desarrollo permitidos por el administrador.

Registros finales separados por configuración: `docs/qa/07-Debug-*.log`, `07-Debug-dependencies.json`. El intento Release anterior quedó en `07-build.log`, `07-tests.log`, `07-ui.log`; `07-Release-ui-recheck.log` conserva la segunda comprobación del bloqueo. `07-ui-stability-1.log` a `3.log` registran los tres cierres WPF correctos. Los `06-*` siguen siendo evidencia histórica de la corrección anterior.

## Archivos creados y modificados

- `src/Finpulse.Domain/MonthlyProjection.cs`: validaciones, precisión y detención al alcanzar objetivo.
- `src/Finpulse.Application/FinancialAnalysisService.cs`: redondeo requerido, límites de fecha y estimación de metas.
- `src/Finpulse.Infrastructure/SqliteStore.cs`: protección de versión futura y creación transaccional del esquema.
- `src/Finpulse.Desktop/App.xaml` y `Themes/Theme.xaml`: recursos compartidos, sin cambio intencional del diseño.
- `tests/Finpulse.Smoke/ReliabilityChecks.cs`: 17 nuevas comprobaciones de fiabilidad y medición acotada.
- `tests/Finpulse.Smoke/Program.cs`: integración de regresiones, validación de argumentos, traza completa y salida de error.
- `tests/Finpulse.Smoke/LocalFinanceChecks.cs`: fechas independientes del calendario fijo y límite del proceso hijo.
- `tests/Finpulse.Smoke/DesktopChecks.cs`: ciclo WPF normal, rutas de artefactos, limpieza y límite de espera.
- `scripts/validar-calidad.ps1`: control de calidad local, registros y comprobación de vulnerabilidades.
- Documentación y artefactos de QA.

Se conservaron todas las pruebas anteriores y los módulos existentes.

## Pendiente externo

El sistema no puede declararse libre de fallos ni plenamente aprobado en Release mientras Windows bloquee su DLL. El error vigente es `System.IO.FileLoadException`, `0x800711C7`, al cargar `tests/Finpulse.Smoke/bin/Release/net10.0-windows/Finpulse.Desktop.dll`. Requiere que el propietario/administrador del equipo resuelva la confianza/firma según la política aplicada. No se cambió el nombre ni la ubicación de los binarios para evadirla, y no se desactivó ninguna protección. Las pruebas Debug correctas no eliminan ni ocultan esa limitación de Release.
