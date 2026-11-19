# Validación actual de FINPULSE

Fecha: 17/11/2026, America/La_Paz.

## Resultado comprobado

- Compilación Desktop y sus dependencias: **0 errores, 0 advertencias**.
- Smoke: **26/26 comprobaciones superadas**, salida 0. El bloqueo de ejecución
  documentado el 16/11 no se reprodujo en esta ejecución.
- Nuevo proyecto OracleIntegration: compilación con **0 errores y 0 advertencias**.
- Integración Oracle real: **29/29 comprobaciones superadas**, salida 0,
  incluida la creación y autenticación de la cuenta de ingreso.
- Auditoría NuGet de Infrastructure con dependencias transitivas: sin paquetes
  vulnerables detectados por los orígenes consultados.

Esto acredita las reglas y la integración del módulo de usuarios. No equivale
a una prueba visual completa de WPF ni a una certificación de seguridad.

## Instalación local completada

Se verificó FREEPDB1 abierta en READ WRITE. FINPULSE_APP no existía antes de
la intervención. Se creó el usuario de base de datos y FP_USUARIOS, y se guardó
FINPULSE_ORACLE_CONNECTION en el entorno del usuario de Windows. La contraseña
de base de datos se generó aleatoriamente y no se incorporó al repositorio.

La primera creación de tabla falló con ORA-01031. Se confirmó el esquema vacío;
se añadió CREATE SEQUENCE a los permisos de instalación requeridos para IDENTITY
y se completó correctamente la creación. El script SQL original y el nuevo
script PowerShell incluyen este permiso.

Cuenta de aplicación persistente creada: **estudiante@finpulse.local**.
Su contraseña se entrega en la conversación, no se guarda en este documento.
La cuenta temporal con correo aleatorio usada para QA se eliminó al finalizar;
la prueba confirmó que no quedó ninguna fila de esa cuenta.

## Cobertura de integración

Se ejercitaron AuthService, OracleUserRepository y Pbkdf2PasswordHasher con
conexiones reales a localhost:1521/FREEPDB1:

- Confirmación incorrecta sin insertar filas, registro persistente y correo único
  normalizado con mayúsculas y espacios.
- Hash persistido distinto de la contraseña y verificable.
- Login válido con identidad, actualización de último acceso y usuario inexistente.
- Cinco contraseñas erróneas y contador persistente tras cada fallo.
- Bloqueo de aproximadamente quince minutos y rechazo de clave válida durante él.
- Expiración simulada cambiando únicamente la fecha de la cuenta temporal;
  primer fallo tras expiración, acceso posterior y limpieza del contador/bloqueo.
- Cuenta inactiva rechazada y cuenta reactivada aceptada.
- Creación y login de la cuenta de ingreso; limpieza de la cuenta temporal.

No se detuvo Oracle ni se alteraron otras cuentas para probar fallos.

## Límites y observaciones pendientes

- Computer Use devolvió `native pipe is unavailable` (os error 2), tanto en
  el runtime de la habilidad como en la herramienta CUA. No se pudo interactuar
  visualmente con login, registro, bienvenida ni cierre de sesión.
- Se inició Desktop con la conexión de usuario cargada explícitamente. El proceso
  respondía, pero MainWindowHandle era 0 y el título estaba vacío; por tanto no
  se afirma que la ventana se haya mostrado ni que el arranque visual funcione.
- El mensaje de la pantalla «Conexión configurada» indica presencia de la variable;
  no realiza por sí mismo una consulta de salud a Oracle.
- Revisión estática: si una operación lanza una excepción, la limpieza de las
  contraseñas situada después del await no llega a ejecutarse. Los enlaces de
  navegación tampoco se bloquean mientras hay una operación pendiente. Conviene
  mejorar ambos comportamientos y verificarlos con pruebas visuales.
- El cierre de sesión cambia la pantalla y limpia contraseñas, pero mantiene
  nombre y correo en controles ocultos. No se validó mediante interacción.
- El alcance actual es registro, acceso y bienvenida. Ingresos, gastos, ahorro,
  proyecciones e informes todavía no están implementados.

## Reproducción

En una terminal nueva que herede FINPULSE_ORACLE_CONNECTION:

```powershell
dotnet build .\src\Finpulse.Desktop\Finpulse.Desktop.csproj --no-restore
dotnet run --project .\tests\Finpulse.Smoke\Finpulse.Smoke.csproj --no-restore
dotnet run --project .\tests\Finpulse.OracleIntegration\Finpulse.OracleIntegration.csproj
dotnet list .\src\Finpulse.Infrastructure\Finpulse.Infrastructure.csproj package --vulnerable --include-transitive
dotnet run --project .\src\Finpulse.Desktop\Finpulse.Desktop.csproj --no-build
```

OracleIntegration solo admite el usuario FINPULSE_APP en el servicio local
especificado. Sin `--create-demo` ejecuta 27 comprobaciones y no modifica la
cuenta de ingreso existente. La ejecución inicial añadió dos comprobaciones
para crear y verificar dicha cuenta (29 en total). La opción `--create-demo`
requiere FINPULSE_DEMO_PASSWORD y rechaza una cuenta demo ya existente;
no restablece contraseñas.

`scripts/preparar-oracle-local.ps1` es de primera instalación: no volver a
ejecutarlo sobre esta instalación completada. `-ResumeEmptySchema` solo sirve
para recuperar una instalación fallida vacía y renueva la clave del esquema;
nunca se debe usar sobre otro esquema que se quiera conservar.
