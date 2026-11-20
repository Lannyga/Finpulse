# FINPULSE

Una tarea universitaria para organizar el dinero de un estudiante. Tres pantallas:

- **Resumen:** cuánto tienes, cuánto recibiste y gastaste este mes, un gráfico de seis meses y un consejo según tus gastos.
- **Movimientos:** anotar, editar y eliminar ingresos y gastos por categoría.
- **Metas:** ahorrar para libros, materiales o una computadora, ver el avance y retirar lo guardado.

Sin presupuestos, proyecciones, CSV ni códigos de emergencia. La cuenta usa usuario y contraseña de 6 a 128 caracteres (también admite solo números); desde el nombre de usuario en la barra superior se puede cambiar la contraseña.

## Ejecutar

Requiere .NET 10 y Oracle con el esquema FINPULSE_WEB en `localhost:1521/FREEPDB1`.
Esta máquina ya tiene la conexión en ASP.NET Core User Secrets. No reinstales el esquema.

```powershell
dotnet restore FINPULSE.slnx
dotnet run --project src/Finpulse.Web
```

Abre https://localhost:7240 en Edge. HTTPS usa el certificado local de desarrollo de .NET.
En otra máquina configura `ConnectionStrings:Oracle` con User Secrets y una cuenta Oracle de privilegios mínimos, nunca SYS/SYSTEM.
El script [sql/esquema.sql](sql/esquema.sql) sirve únicamente para preparar un esquema vacío. Después ejecuta `dotnet run --project src/Finpulse.Web -- --seed-categories` una vez para crear las categorías.

## Cómo explicar el código

Un proyecto ASP.NET Core MVC con C#, Razor y Oracle mediante Entity Framework Core. CSS propio con cuadrícula Bento y tipografía Plus Jakarta Sans local; Chart.js local dibuja el gráfico. No hay cliente de escritorio ni SQLite.

```text
src/Finpulse.Web/
  Program.cs                       Inicia y configura la aplicación.
  Models.cs                        Datos y formularios.
  Data/PortalDb.cs                 Cuatro tablas y conexión Oracle.
  Controllers/AccountController.cs Registro, acceso y contraseña.
  Controllers/FinanceController.cs Formularios de movimientos y metas.
  Services/PortalFinance.cs        Guardado y cálculos de dinero.
  Views/         Tres pantallas y formularios de cuenta.
  wwwroot/       CSS, JavaScript, gráfico y marca.
tests/Finpulse.WebChecks/   Pruebas de cálculos y Oracle/HTTPS.
sql/esquema.sql            Instalación en un esquema nuevo.
```

Son **seis archivos C# principales**. Para exponer: empieza por `Models.cs` (qué guardamos), sigue por `PortalDb.cs` (dónde se guarda), `PortalFinance.cs` (qué calculamos) y los controladores (cómo llegan los formularios). Las vistas Razor muestran el resultado. Las clases relacionadas están juntas en el mismo archivo, con nombres y métodos separados para encontrarlas fácilmente.

El recorrido de un registro es: **formulario → controlador → PortalFinance → PortalDb → Oracle**. Después se vuelve a mostrar la pantalla con los datos guardados.

**Cálculos:** saldo = ingresos históricos − gastos históricos. Libre = saldo − dinero guardado en metas. Guardar en una meta reserva parte del saldo; no genera otro gasto. Retirar lo devuelve al saldo libre.

Las cuatro tablas activas son `USUARIOS`, `CATEGORIAS`, `MOVIMIENTOS` y `METAS_AHORRO`. En movimientos, `Kind` vale 0 para ingreso, 1 para gasto y 2 para dinero guardado en una meta. El campo `RequestKey` evita duplicados al reenviar y `Revision` detecta cambios simultáneos.

Se conservaron los datos y cuentas existentes. Las antiguas tablas de presupuestos y escenarios pueden seguir en Oracle, pero la aplicación ya no las consulta. `RecoveryHash` y `WarningPercent` son dos columnas obligatorias del esquema anterior; el modelo las conserva solo por compatibilidad, sin recuperación por código ni alertas de presupuesto. No se ejecutan cambios de esquema al iniciar.

Las contraseñas usan el hasher de ASP.NET Core. Se mantienen cookies seguras, protección CSRF, bloqueo tras intentos fallidos, consultas por propietario y transacciones para no guardar en metas más dinero del disponible.

## Comprobar

```powershell
dotnet build FINPULSE.slnx
dotnet run --project tests/Finpulse.WebChecks
dotnet run --project tests/Finpulse.WebChecks -- --oracle
```

La última prueba requiere `ConnectionStrings:OracleTest` en User Secrets y solo admite `FINPULSE_WEB_TEST` local. Crea dos cuentas sintéticas, verifica HTTP, persistencia, aislamiento y concurrencia, y elimina los datos que creó. Los archivos temporales quedan en `tests/.qa/`, excluidos de Git.
