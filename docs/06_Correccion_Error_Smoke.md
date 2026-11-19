# FINPULSE — diagnóstico y corrección de Smoke

Fecha de validación: 19/11/2026.

## Resultado actual

El fallo WPF reproducido se corrigió y se verificó ejecutando el programa real. `Finpulse.Smoke.exe` Release terminó con código 0 tanto sin argumentos como con `--ui`. Las pruebas WPF Debug también terminaron con código 0. Las compilaciones Smoke y Desktop terminaron con cero advertencias y cero errores.

Pasaron 146 comprobaciones normales (56 del motor, 64 de almacenamiento/finanzas y 26 de autenticación/hash), el renderizado de las ocho secciones sin errores de binding, las 14 comprobaciones adicionales de proyecciones/gráficos y las seis operaciones MVVM. No quedaron pruebas fallidas en las ejecuciones finales.

No se utilizó Computer Use, no se capturó el monitor, no se modificó la seguridad de Windows y no se accedió ni se reinicializó la base financiera del usuario.

## Excepciones identificadas

`0xE0434352` identifica una excepción CLR; por sí solo no determina si el origen es SQLite, WPF, un archivo o una política de Windows. Hay que examinar el tipo, mensaje, excepciones internas y stack trace. Referencia: [Microsoft: CLR Exception E0434352](https://learn.microsoft.com/en-us/shows/inside/e0434352).

Se distinguieron dos evidencias:

1. **Registro histórico:** el visor de eventos `.NET Runtime` conserva cierres anteriores de `Finpulse.Smoke.exe` por `System.IO.FileLoadException`, con `0x800711C7`, al cargar `Finpulse.Smoke.dll`. Ese fallo corresponde al bloqueo de Control de aplicaciones descrito en etapa 4. No volvió a reproducirse en las ejecuciones finales de esta corrección y no se modificó la política para conseguirlo. No se afirma haber reparado una política de Windows mediante código XAML.
2. **Fallo de código reproducido ahora:** la suite normal pasaba, pero `--ui` fallaba durante la carga/medición de las vistas con una excepción XAML no controlada. Esta segunda causa sí se corrigió en los archivos del proyecto.

Traza relevante capturada desde terminal antes de corregir:

```text
System.InvalidOperationException: Validación WPF fallida
 ---> System.Windows.Markup.XamlParseException:
 'Se produjo una excepción al proporcionar un valor en
 System.Windows.Baml2006.TypeConverterMarkupExtension.'
 (número de línea: '96'; posición de línea: '54').
 ---> System.ArgumentException:
 Auto is not a valid value for Double. (Parameter 'value')
 ---> System.FormatException:
 The input string 'Auto' was not in a correct format.
 at System.Double.Parse(...)
 at System.ComponentModel.DoubleConverter.FromString(...)
 at MS.Internal.Xaml.Runtime.ClrObjectRuntime.CallProvideValue(...)
 at System.Windows.FrameworkElement.MeasureCore(...)
 at DesktopChecks...MoveNext() in DesktopChecks.cs:line 51
 at DesktopChecks.Run() in DesktopChecks.cs:line 150
 at Program.<Main>$(String[] args) in Program.cs:line 7
```

En las tablas de recomendaciones de Dashboard y Proyecciones, `RowHeight="Auto"` se estaba procesando mediante `DoubleConverter` al materializar las plantillas BAML y fallaba. Que el proyecto compilara no garantizaba que esa conversión diferida funcionara al abrir la vista.

## Corrección aplicada

En ambas vistas se declaró:

```xml
xmlns:sys="clr-namespace:System;assembly=System.Runtime"
```

Y se reemplazó el valor problemático por una constante `double` explícita:

```xml
RowHeight="{x:Static sys:Double.NaN}"
MinRowHeight="64"
```

Esto conserva filas ajustables al contenido y el mínimo de altura sin convertir el texto `Auto`. `Double.NaN` es el valor de altura automática documentado para esta propiedad: [Microsoft: DataGrid.RowHeight](https://learn.microsoft.com/en-us/dotnet/api/system.windows.controls.datagrid.rowheight?view=windowsdesktop-10.0).

Archivos de código corregidos:

- `src/Finpulse.Desktop/Views/DashboardView.xaml`.
- `src/Finpulse.Desktop/Views/ProjectionsView.xaml`.

No se cambiaron el cuerpo de las pruebas, las aserciones, la configuración de proveedor, dependencias, servicios de autenticación ni repositorios. No se añadieron bloques `catch` para silenciar el fallo. Se conservaron todas las funciones de etapas anteriores.

## Revisión de dependencias, SQLite y rutas

- Smoke mantiene sus referencias a Desktop, Application e Infrastructure, y el objetivo `net10.0-windows` con WPF.
- La restauración y compilación resolvieron Microsoft.Data.Sqlite 10.0.14 y las dependencias existentes de Oracle sin errores de referencia.
- Las comprobaciones locales crean su propio archivo `test.db` en `%TEMP%\Finpulse-tests-<GUID>\`. Las WPF crean `ui.db` en `%TEMP%\Finpulse-ui-<GUID>\`.
- Esos directorios son exclusivos de cada ejecución y se limpian al finalizar; no corresponden a `%LOCALAPPDATA%\FINPULSE\finpulse.db`.
- Las pruebas normales crean usuarios de prueba, validan hashing/bloqueo, prueban operaciones financieras e aislamiento y reabren la base desde un proceso hijo. Todas esas comprobaciones pasaron.
- La suite normal y `--ui` no necesitan una instancia Oracle ni `FINPULSE_ORACLE_CONNECTION`. Los diagnósticos Oracle solo se invocan con argumentos explícitos específicos.
- `--ui` guarda PNG de QA en `docs/qa`, relativo al directorio de trabajo. Los comandos se ejecutaron desde la raíz del repositorio. Esa ruta de artefactos no es la ruta de almacenamiento financiero.

## Comandos ejecutados y evidencia

Se ejecutaron secuencialmente las compilaciones y las pruebas para evitar que un proceso Smoke mantuviera DLL abiertas durante otra compilación. Durante el diagnóstico hubo un intento de compilación simultáneo a la prueba que fallaba; produjo errores MSB3021/MSB3027 de archivos en uso. Al terminar ese proceso y ejecutar de forma secuencial desaparecieron, sin cambiar dependencias ni desactivar controles.

| Comando | Resultado final | Registro |
| --- | --- | --- |
| `dotnet build .\tests\Finpulse.Smoke\Finpulse.Smoke.csproj` | Código 0; 0 advertencias, 0 errores | `docs/qa/06-build-smoke.log` |
| `dotnet run --project .\tests\Finpulse.Smoke\Finpulse.Smoke.csproj` | Código 0; 146 comprobaciones correctas | `docs/qa/06-run-smoke.log` |
| `dotnet build .\src\Finpulse.Desktop\Finpulse.Desktop.csproj` | Código 0; 0 advertencias, 0 errores | `docs/qa/06-build-desktop.log` |
| `Finpulse.Smoke.exe --ui` (Debug) | Código 0; 8 vistas, 0 fallos de binding, 14 comprobaciones nuevas y 6 operaciones MVVM | `docs/qa/06-smoke-exe-ui.log` |
| `dotnet build .\tests\Finpulse.Smoke\Finpulse.Smoke.csproj -c Release` | Código 0; 0 advertencias, 0 errores | `docs/qa/06-build-release.log` |
| `Finpulse.Smoke.exe` (Release) | Código 0; 146 comprobaciones correctas | `docs/qa/06-smoke-exe-release.log` |
| `Finpulse.Smoke.exe --ui` (Release) | Código 0; 8 vistas, 0 fallos de binding, 14 comprobaciones nuevas y 6 operaciones MVVM | `docs/qa/06-smoke-exe-release-ui.log` |

Las rutas completas de los ejecutables comprobados son:

```text
tests/Finpulse.Smoke/bin/Debug/net10.0-windows/Finpulse.Smoke.exe
tests/Finpulse.Smoke/bin/Release/net10.0-windows/Finpulse.Smoke.exe
```

Los PNG de las ocho secciones y `Etapa4-*` se regeneraron correctamente con WPF real fuera de pantalla. Son datos de prueba, no capturas del escritorio ni información financiera del usuario.

## Alcance de la verificación

Se verificó la ejecución real de las pruebas y la compilación de la aplicación. No se necesita control de Windows para este arreglo. La prueba de vistas utiliza `WorkspaceWindow` con repositorio SQLite temporal; no equivale a un recorrido manual completo de registro/login ni a pruebas físicas de múltiples monitores/DPI. La integración financiera Oracle y el empaquetado siguen con el alcance documentado en las etapas anteriores.

Los bloqueos históricos de seguridad deben distinguirse de los fallos del programa: en esta sesión las ejecuciones finales no mostraron `0x800711C7`, pero eso no garantiza confianza/firma en otros equipos ni permite afirmar que una política de Windows se haya cambiado o reparado.
