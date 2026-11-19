# FINPULSE — Motor financiero mensual (etapa 4)

Hay una [cuenta de demostración con seis meses de historial ficticio](docs/08_Cuenta_Demostracion.md), creada expresamente a petición del usuario. El generador es opcional, crea credenciales aleatorias y no precarga cuentas al iniciar la aplicación.

**Revisión QA vigente:** se añadieron 17 regresiones (163 comprobaciones normales en total), se protegió el esquema SQLite y se corrigieron redondeo/plazo de metas y cierre WPF. El control completo Debug pasó; Release compila y pasa las pruebas normales, pero su carga visual vuelve a estar bloqueada por Control de aplicaciones de Windows. Consulta [resultados y límites actuales](docs/07_Revision_QA.md).

```powershell
.\scripts\validar-calidad.ps1 -Configuration Debug -RevisarVulnerabilidades
```

La etapa 4 incorpora proyecciones mes a mes con rendimiento compuesto opcional, escenarios editables, análisis de metas y un dashboard con gráfico de líneas del historial de ingresos, trabajo, gastos, ahorro y saldo. Consulta [arquitectura, fórmulas, pruebas y estado de la validación](docs/05_Motor_Proyecciones_Financieras.md).

**Validación del 19/11/2026:** corregido el fallo XAML al ejecutar Smoke con `--ui`. Pasan 146 comprobaciones normales, ocho vistas sin errores de binding, las 14 comprobaciones adicionales y seis operaciones MVVM. Smoke y Desktop compilan sin errores ni advertencias; el ejecutable Smoke Release se comprobó directamente con código 0. El bloqueo histórico de Windows no se reprodujo en estas ejecuciones. Consulta el [diagnóstico y los registros](docs/06_Correccion_Error_Smoke.md).

FINPULSE funciona por defecto con **SQLite, sin conexión a Oracle**. Se conservan C#, .NET 10, WPF y los cuatro proyectos originales.

## Ejecutar desde VS Code

Abre esta carpeta y ejecuta en la terminal integrada:

```powershell
dotnet restore .\src\Finpulse.Desktop\Finpulse.Desktop.csproj
dotnet run --project .\src\Finpulse.Desktop\Finpulse.Desktop.csproj
```

Crea tu propia cuenta (contraseña de 12 a 128 caracteres). No existen usuarios ni contraseñas de demostración. La base se crea en `%LOCALAPPDATA%\FINPULSE\finpulse.db` y conserva los datos al cerrar el programa.

Funcionan dashboard, ingresos, gastos, presupuestos, metas con aportes, proyecciones deterministas y reportes CSV. La configuración permite elegir el proveedor y el umbral de aviso presupuestario. Consulta la [documentación de la etapa 3](docs/04_Desarrollo_Modulos_Financieros.md) para alcance, fórmulas, pruebas y limitaciones.

```powershell
dotnet run --project .\tests\Finpulse.Smoke\Finpulse.Smoke.csproj
dotnet run --project .\tests\Finpulse.Smoke\Finpulse.Smoke.csproj -- --ui
```

La segunda orden renderiza las ocho vistas y comprueba comandos MVVM con una base temporal; guarda capturas en `docs/qa`. Las pruebas requieren Windows.

Para Oracle, cambia `Provider` a `Oracle` en `src/Finpulse.Desktop/appsettings.json`, o establece `FINPULSE_PROVIDER=Oracle` en el entorno del proceso y configura la cadena de conexión como se explica abajo. **Oracle conserva registro y autenticación; los nuevos módulos financieros todavía solo tienen implementación SQLite.** No se mezclan datos ni sesiones entre proveedores. Reinicia el programa después de cambiar el proveedor.

**Proyecto académico:** sistema de escritorio para gestión y proyección financiera estudiantil.

## Infraestructura original conservada
- Documentación de análisis, alcance, arquitectura y casos de uso en `docs/`.
- Cuatro proyectos C# (`Domain`, `Application`, `Infrastructure`, `Desktop`).
- Ventanas WPF para registro, acceso, sesión de bienvenida y cierre de sesión.
- Registro y autenticación en Oracle mediante ODP.NET Core.
- Hash de contraseñas PBKDF2-SHA256 (600.000 iteraciones, sal aleatoria de 16 bytes).
- Pruebas de reglas y hashing sin Oracle en `tests/Finpulse.Smoke` (para ejecutar con el SDK).
- Consultas parametrizadas, correo único y bloqueo temporal de 15 minutos tras 5 fallos.
- Scripts SQL para crear el esquema de la primera etapa.

Los módulos financieros de la etapa 3 usan SQLite. La bienvenida del proveedor Oracle confirma autenticación y muestra expresamente que sus módulos financieros siguen pendientes.

## Requisitos
1. Windows 10/11 x64.
2. SDK .NET 10 y Visual Studio Code con la terminal `dotnet`.
3. Solo para el proveedor Oracle: Oracle AI Database Free 26ai, servicio `FREEPDB1`, o una instancia compatible, y SQL*Plus/SQLcl.

## Configuración opcional de Oracle

Estado de la conexión y diagnóstico de solo lectura: [Conexión Oracle, 18/11/2026](docs/03_Conexion_Oracle.md).

1. Instala e inicia Oracle. Comprueba servicio `FREEPDB1` en `localhost:1521`.
2. Conéctate **como `SYSTEM` a `FREEPDB1`**, no a la raíz `FREE`:
   ```powershell
   sqlplus system@localhost:1521/FREEPDB1
   ```
3. Desde SQL*Plus, ejecuta `@ruta_absoluta\sql\01_crear_usuario_oracle.sql`. Introduce contraseña fuerte cuando se solicite.
4. Sal de SQL*Plus y conéctate con el usuario nuevo:
   ```powershell
   sqlplus FINPULSE_APP@localhost:1521/FREEPDB1
   ```
5. Ejecuta `@ruta_absoluta\sql\02_esquema_usuarios.sql`. Si quieres, verifica con `03_verificacion.sql`.
6. En PowerShell, en carpeta del proyecto, ejecuta:
   ```powershell
   .\scripts\configurar-conexion.ps1
   ```
   Si la política impide ejecutarlo, puedes abrirlo para revisar su contenido; no debilites la política del equipo sin necesidad.
7. Reinicia Visual Studio Code/terminal para que lea la variable. Ejecuta:
   ```powershell
   dotnet restore .\src\Finpulse.Desktop\Finpulse.Desktop.csproj
   dotnet run --project .\src\Finpulse.Desktop\Finpulse.Desktop.csproj
   ```
8. Crea una cuenta (contraseña de 12 a 128 caracteres), ingresa y cierra sesión.

### ¿Dónde editar el código?

En Visual Studio Code abre la carpeta raíz `FINPULSE_Entrega_01` y usa su terminal integrada.
El proyecto de entrada es `src/Finpulse.Desktop/Finpulse.Desktop.csproj`;
sus referencias incluyen los otros tres proyectos.

## Pruebas aisladas sin Oracle

```powershell
dotnet run --project .\tests\Finpulse.Smoke\Finpulse.Smoke.csproj
```

Estas comprobaciones incluyen reglas, hash y persistencia financiera local; no sustituyen la prueba de integración real con Oracle.

## Seguridad y limitaciones

- No use la cuenta `SYS`/`SYSTEM` en la aplicación.
- Las contraseñas de usuarios se guardan como hashes; nunca se almacenan en claro.
- La cadena de conexión en una variable de entorno de usuario NO constituye almacenamiento secreto resistente a usuarios con acceso al equipo.
- **Esta versión usa credenciales de un usuario de BD compartido por una instalación local.** Es apta para demostración académica local, NO para despliegue multiusuario remoto o distribución a terceros; para ello hacen falta un backend de autenticación/identidad, gestión adecuada de secretos, TLS, políticas de acceso y controles de privacidad.
- El resultado vigente de la etapa 3 está en [Desarrollo de módulos financieros](docs/04_Desarrollo_Modulos_Financieros.md). Los documentos de etapas anteriores conservan los resultados históricos de Oracle; esa integración no se vuelve a ejecutar al validar el modo SQLite.

## Referencias oficiales
- Microsoft WPF con .NET 10: https://learn.microsoft.com/en-us/visualstudio/get-started/csharp/tutorial-wpf?view=vs-2022
- Paquete ODP.NET Core: https://www.nuget.org/packages/Oracle.ManagedDataAccess.Core
- Oracle AI Database Free: https://www.oracle.com/database/free/get-started/
- Conexión en Windows: https://docs.oracle.com/en/database/oracle/oracle-database/26/xeinw/connecting-oracle-database-xe.html
