# Evidencia de QA — etapa 3

La revisión vigente es [QA y fiabilidad](../07_Revision_QA.md): 163 comprobaciones normales y control WPF Debug completo correctos, tres cierres repetidos con código 0, consulta NuGet sin vulnerabilidades reportadas y bloqueo externo de carga visual Release `0x800711C7`. Los registros `07-Debug-*` y `07-Release-ui-recheck.log` documentan esos resultados; las notas siguientes conservan resultados históricos.

Validación vigente, 19/11/2026: corregido el error de carga XAML en Dashboard/Proyecciones. Se regeneraron las capturas de etapa 4; pasaron las 146 comprobaciones normales, ocho vistas sin fallos de binding, 14 verificaciones adicionales y seis operaciones MVVM, incluida la ejecución directa del EXE Release. Los registros `06-*.log` y [el informe de corrección](../06_Correccion_Error_Smoke.md) sustituyen el estado pendiente indicado en el historial siguiente.

Actualización etapa 4: se ejecutaron 56 comprobaciones adicionales del motor (146 junto con las anteriores) y un renderizado inicial de las ocho vistas de etapa 4 sin errores de binding. Las capturas Dashboard/Proyecciones existentes corresponden a esa primera revisión, antes de reorganizar la interfaz. La última ampliación WPF de 14 comprobaciones y las capturas `Etapa4-*` quedaron pendientes porque Windows bloqueó `Finpulse.Smoke.dll` con `0x800711C7`. El detalle vigente está en [el informe de etapa 4](../05_Motor_Proyecciones_Financieras.md). No se declara verificada la interfaz final por esas imágenes anteriores.

Las ocho capturas PNG se generan con `dotnet run --project tests/Finpulse.Smoke/Finpulse.Smoke.csproj -- --ui` en Windows. Usan WPF real, recursos de la aplicación y una base SQLite temporal eliminada al terminar. Los importes y el usuario «Prueba visual» son datos exclusivos de esas pruebas; no se precargan en FINPULSE.

Resultado: ocho vistas renderizadas, sin errores de binding; seis operaciones de ViewModel comprobadas (registro de movimiento, filtro, aporte, CSV con neutralización de fórmula, cancelación de borrado y borrado confirmado). La proyección también se ejecuta antes de capturar su tabla.

La prueba estándar del mismo proyecto pasó 64 comprobaciones de almacenamiento/finanzas y 26 comprobaciones originales de autenticación/hash. La compilación Release de todos sus proyectos terminó sin errores ni advertencias. La consulta NuGet no reportó dependencias vulnerables en Infrastructure.

El arranque interactivo también se observó mediante Computer Use: proveedor SQLite, formularios habilitados y sin credenciales Oracle. Las capturas no son evidencia de una sesión manual completa ni de pruebas físicas de múltiples monitores o accesibilidad.
