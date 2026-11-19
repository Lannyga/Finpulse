# FINPULSE · Portal financiero estudiantil

ASP.NET Core MVC, Razor y C# sobre .NET 10. Interfaz oscura Bento con identidad verde bosque, Bootstrap y Chart.js locales. `FINPULSE.slnx` contiene un proyecto web y un ejecutable independiente de pruebas.

**Estado:** portal verificado con SQLite provisional. Proveedor EF Core Oracle y esquema de seis tablas implementados; falta probarlos contra una instancia Oracle aislada. La revisión visual de navegador quedó bloqueada por Computer Use. No se importaron datos personales automáticamente.

## Iniciar desde VS Code

```powershell
dotnet restore FINPULSE.slnx
dotnet dev-certs https --trust
dotnet run --project src/Finpulse.Web -- --init-local
dotnet run --project src/Finpulse.Web
```

Abre https://localhost:7240. El certificado requiere tu decisión de confianza en Windows; las cookies siempre necesitan HTTPS. `--init-local` crea únicamente esquema y categorías, sin usuarios ni movimientos. No actualiza esquemas anteriores. La base está en `src/Finpulse.Web/finpulse-web.db`, separada de WPF.

Regístrate con usuario y contraseña de 12 a 128 caracteres. Guarda el código de recuperación mostrado una sola vez. El portal no solicita ni almacena correo.

Para recrear el historial de demostración en una instalación nueva:

```powershell
dotnet run --project src/Finpulse.Web -- --create-demo
# Copia el usuario generado (no su contraseña) para habilitar el botón local:
$env:Demo__Username = 'USUARIO_DEMO_GENERADO'
dotnet run --project src/Finpulse.Web
```

El generador crea seis meses completos más el mes actual, con credenciales aleatorias. Solo funciona en Development con SQLite. La cuenta y sus datos no se incluyen en Git: se generan localmente. La configuración de demostración puede apuntar a un usuario anterior hasta sustituirla por el nuevo usuario generado.

## Funciones

- Dashboard, historial y recomendaciones justificadas con tus datos.
- Ingresos, gastos, presupuestos y metas: registro, edición y eliminación.
- Aportes y liberación de reservas para metas, sin duplicar gastos.
- Escenarios persistentes a 1, 3, 5 y 10 años con motor mensual, inflación, rendimiento y cambios de flujos; tablas y CSV.
- Reportes, preferencias de alertas y cambio/recuperación de contraseña con revocación de sesiones.
- Aislamiento de datos, CSRF, bloqueo de acceso, idempotencia y concurrencia.

## Pruebas

```powershell
dotnet run --project tests/Finpulse.WebChecks -c Release
powershell -NoProfile -File scripts/validar-web.ps1 -Snapshots
```

El segundo comando compila, crea una base sintética y certificado efímero sin instalar confianza, prueba HTTPS en localhost y detiene su servidor. Logs en `docs/qa/web-*-checks.log`. Datos temporales en `tests/.qa/`, excluidos de Git. La excepción de certificado se limita al cliente de pruebas local.

## Oracle y migración

Revisa `sql/05_esquema_web_oracle.sql` y aplícalo en **un esquema nuevo aislado**. No contiene borrados ni transforma tablas anteriores. Oracle confirma DDL implícitamente; requiere respaldo y revisión. El portal no ejecuta DDL Oracle al arrancar.

Configura `Database__Provider=Oracle` y `ConnectionStrings__Oracle` fuera del repositorio, usando privilegios mínimos. No uses SYS ni SYSTEM. Ejecuta `dotnet run --project src/Finpulse.Web -- --seed-categories` una vez instalado el esquema. Se configuró compatibilidad SQL Oracle 19 o superior; la instancia real debe validarse.

Consulta [reestructuración, importación y pendientes](docs/06_Reestructuracion_Web.md). La importación SQLite anterior exige mapeo revisado de IDs a usuarios y un destino vacío, no copia correos y no modifica el origen. Las cuentas Oracle anteriores requieren una importación específica pendiente.

Los proyectos WPF anteriores permanecen fuera de la solución activa, con respaldo en `backups/original-con-rutas.zip`. El portal es independiente y contiene los algoritmos financieros reutilizados. El [README anterior](docs/README_Anterior_WPF.md) conserva el contexto histórico.
