# FINPULSE — Reporte de correcciones y estabilización

> Actualización del 17/11/2026: Smoke supera 26 comprobaciones y la nueva
> integración Oracle supera 29. El esquema local y la conexión ya están
> configurados. Ver [validación actual](03_Validacion_Actual.md).
> El contenido siguiente conserva los resultados históricos del 16/11.

Fecha: 16/11/2026 (America/La_Paz). Etapa: primera entrega, módulo de usuarios.

## Resultado

Desktop y Smoke compilan con **0 errores y 0 advertencias** usando SDK .NET
10.0.404, MSBuild 18.9.15 y Windows x64; runtime instalado 10.0.15.
NuGet no detecta paquetes vulnerables en Infrastructure tras la corrección.
**No se puede declarar cumplido el criterio completo de finalización:** Windows
bloqueó la ejecución de Smoke antes de sus comprobaciones y no fue posible
confirmar visualmente la ventana WPF. No hay conexión Oracle configurada.

## Errores originales y cambios

- CS0118: `Application` en `App.xaml.cs` podía resolverse como el espacio de
  nombres `Finpulse.Application`. Se sustituyó el contenido por la herencia
  explícita `global::System.Windows.Application` solicitada.
- CS0108: el campo generado para `RegisterName` ocultaba el método heredado
  `FrameworkElement.RegisterName`. Se renombró a `RegisterFullNameTextBox` en
  XAML y en `Register_Click`, sin usar `new`. La búsqueda en los archivos fuente
  confirma que ya no hay referencias al nombre anterior.
- Se conservaron `x:Class="Finpulse.Desktop.App"` y
  `StartupUri="MainWindow.xaml"` en `App.xaml`.
- Se corrigió el contador de Smoke: el archivo original contenía 12
  comprobaciones pero anunciaba 11. Ahora cuenta y enumera cada comprobación
  ejecutada, y contiene 26 comprobaciones, incluidas validaciones adicionales
  de entradas y hashes. El mensaje final solo se imprime si todas pasan.
- README actualizado para trabajar desde VS Code y reflejar la validación real.

## Archivos modificados o creados

1. `src/Finpulse.Desktop/App.xaml.cs`
2. `src/Finpulse.Desktop/MainWindow.xaml`
3. `src/Finpulse.Desktop/MainWindow.xaml.cs`
4. `src/Finpulse.Infrastructure/Finpulse.Infrastructure.csproj`
5. `tests/Finpulse.Smoke/Program.cs`
6. `README.md`
7. `docs/02_Reporte_Correcciones.md` (nuevo)

La restauración y compilación también regeneran archivos de salida en `bin/` y
`obj/`; no son cambios manuales del código fuente.

## Vulnerabilidades y solución

Se ejecutaron, antes de corregir las dependencias:

```powershell
dotnet list .\src\Finpulse.Infrastructure\Finpulse.Infrastructure.csproj package --include-transitive
dotnet list .\src\Finpulse.Infrastructure\Finpulse.Infrastructure.csproj package --vulnerable --include-transitive
```

La salida confirmó la dependencia que introduce el paquete afectado:

```text
Oracle.ManagedDataAccess.Core 23.26.400
  -> System.Security.Cryptography.Pkcs 8.0.1
```

Advertencias identificadas:

| Advertencia | Aviso | Gravedad | Estado |
| --- | --- | --- | --- |
| NU1902 | GHSA-2fmv-rv2c-59x7 / CVE-2026-70162 | Moderada | Corregida con PKCS 8.0.2 |
| NU1903 | GHSA-846f-f764-76mx / CVE-2026-84008 | Alta | Corregida con PKCS 8.0.2 |

Se consultó primero la posibilidad de actualizar Oracle. La versión estable más
reciente publicada consultada es la misma 23.26.400, y su grupo net8.0 admite
PKCS >= 8.0.1. Se conservó Oracle y se añadió una referencia directa a PKCS
8.0.2, parche oficial de ambas vulnerabilidades dentro de la misma rama 8.x.
La resolución efectiva, la restauración y la compilación bajo net10.0 confirman
la compatibilidad de compilación. La compatibilidad de ejecución con una base
Oracle concreta sigue pendiente de integración.

Después de restaurar se repitió la auditoría con acceso a NuGet:

```text
El proyecto "Finpulse.Infrastructure" especificado no tiene paquetes vulnerables en los orígenes actuales.
```

El listado final resuelve Oracle 23.26.400 y PKCS 8.0.2. No se desactivó
`NuGetAudit`, no se agregó `NoWarn` ni se ocultaron advertencias.

Fuentes consultadas:

- [Oracle en NuGet: versiones y dependencias](https://www.nuget.org/packages/Oracle.ManagedDataAccess.Core/23.26.400).
- [Aviso Microsoft CVE-2026-70162 y versiones corregidas](https://github.com/advisories/GHSA-2fmv-rv2c-59x7).
- [Aviso Microsoft CVE-2026-84008 y versiones corregidas](https://github.com/advisories/GHSA-846f-f764-76mx).
- [Paquete PKCS 8.0.2](https://www.nuget.org/packages/System.Security.Cryptography.Pkcs/8.0.2).

## Arquitectura y seguridad conservadas

| Proyecto | Framework | Referencias a proyectos |
| --- | --- | --- |
| Domain | net10.0 | Ninguna |
| Application | net10.0 | Domain |
| Infrastructure | net10.0 | Domain, Application |
| Desktop | net10.0-windows, WPF | Application, Domain, Infrastructure |

La compilación de Desktop incluye los cuatro proyectos y sus referencias son
coherentes, sin ciclos. Se conservaron los contratos de Application, el repositorio
Oracle, las consultas SQL parametrizadas con `BindByName`, el manejo de
duplicados ORA-00001 y la lógica de bloqueo tras cinco intentos fallidos.

PBKDF2-SHA256 conserva 600.000 iteraciones, sal aleatoria de 16 bytes, derivado
de 32 bytes y comparación con `CryptographicOperations.FixedTimeEquals`.
No se añadieron credenciales. La conexión sigue leyendo exclusivamente
`FINPULSE_ORACLE_CONNECTION`. No se modificaron scripts SQL ni se ejecutaron
operaciones sobre la base de datos. No se añadieron módulos financieros.

## Comandos y resultados reales

```powershell
dotnet restore .\src\Finpulse.Desktop\Finpulse.Desktop.csproj
dotnet build .\src\Finpulse.Desktop\Finpulse.Desktop.csproj
dotnet run --project .\tests\Finpulse.Smoke\Finpulse.Smoke.csproj
dotnet build .\tests\Finpulse.Smoke\Finpulse.Smoke.csproj --no-restore
dotnet run --project .\src\Finpulse.Desktop\Finpulse.Desktop.csproj --no-build
```

| Verificación | Resultado observado |
| --- | --- |
| Restore Desktop | Correcto, salida 0 |
| Build Desktop y sus tres dependencias | Correcto, salida 0; 0 errores, 0 advertencias |
| Build Smoke | Correcto, salida 0; 0 errores, 0 advertencias |
| Run Smoke | Falló, salida 1, antes de ejecutar las comprobaciones |
| Auditoría Infrastructure con transitivos | Sin paquetes vulnerables detectados |
| Inicio Desktop | Procesos iniciados; sin confirmación de ventana principal |

Smoke produjo el mismo error dentro y fuera del aislamiento:

```text
System.IO.FileLoadException: Could not load file or assembly '...Finpulse.Smoke.dll'.
Una directiva de Control de aplicaciones bloqueó este archivo. (0x800711C7)
```

Se verificó el evento 3077 en
`Microsoft-Windows-CodeIntegrity/Operational`: la carga de `Finpulse.Smoke.dll`
no cumple los requisitos de firma Enterprise o viola la política de integridad
de código. También aparece la excepción en el registro Application/.NET Runtime.
Es un bloqueo de ejecución del equipo; no es un fallo de aserción ni de
compilación. No se cambiaron ni eludieron las políticas de seguridad.

## Cobertura y límites de la validación

Las 26 comprobaciones Smoke preparadas cubren sales aleatorias, verificación de
contraseña correcta, rechazo de contraseña incorrecta, hashes malformados,
Base64 inválido, límites de iteraciones, normalización y formato del correo,
nombre corto/vacío/largo, correo demasiado largo, límites de longitud de
contraseña, confirmación, registro, duplicados (incluidas variantes con espacios
y mayúsculas), login correcto/incorrecto, usuario inexistente, entradas inválidas
y datos de sesión. Usan el repositorio en memoria existente; **no se declara
ninguna de estas comprobaciones como superada en esta ejecución**, porque Windows
bloqueó el ensamblado antes de llegar a ellas.

La revisión del código confirmó que permanecen el registro, validaciones,
prevención de duplicados, login, verificación segura, cierre de sesión,
navegación login/registro/bienvenida y mensajes de error de conexión. El cierre
de sesión limpia contraseñas y vuelve a login. Estas conclusiones son de revisión
estática, no equivalen a pruebas de interacción.

Se intentó usar Computer Use para observar WPF; el servicio devolvió
`Computer Use native pipe is unavailable` (os error 2). Se iniciaron procesos
Desktop por ejecutable y con `dotnet run --no-build`: respondían, pero su
`MainWindowHandle` era 0 y su título estaba vacío. Por tanto, no se afirma que
`MainWindow` se haya mostrado ni que el inicio visual esté validado.

## Pendientes y reproducción en VS Code

1. Resolver mediante el administrador del equipo la autorización/firma exigida
   por la política de integridad de código para los binarios de desarrollo y
   repetir Smoke. No se requiere cambiar la lógica del proyecto para ocultar
   este error.
2. Confirmar visualmente el arranque y navegación WPF en una sesión interactiva.
3. Configurar `FINPULSE_ORACLE_CONNECTION`: no estaba definida ni en el proceso
   ni en el entorno de usuario consultados. No se comprobó disponibilidad de una
   instancia Oracle ni se simuló una conexión exitosa.
4. Con Oracle funcional, validar registro persistente, correo duplicado, login,
   bloqueo/reactivación, actualización de último acceso, cierre de sesión y
   recuperación ante errores de conexión. Las pruebas en memoria no sustituyen
   estas verificaciones.

Desde la raíz del proyecto, en la terminal integrada de Visual Studio Code:

```powershell
dotnet run --project .\src\Finpulse.Desktop\Finpulse.Desktop.csproj
```

Para repetir las comprobaciones disponibles:

```powershell
dotnet run --project .\tests\Finpulse.Smoke\Finpulse.Smoke.csproj
```
