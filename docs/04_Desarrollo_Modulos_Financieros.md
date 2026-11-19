# FINPULSE — Etapa 3: módulos financieros sin Oracle

Este documento conserva el alcance de etapa 3. La [etapa 4](05_Motor_Proyecciones_Financieras.md) reemplaza la interfaz de proyecciones por cálculo mensual, elimina tasas y diferencias de escenario prefijadas y corrige el saldo actual para excluir movimientos futuros. Consulta allí el estado de validación vigente.

Fecha: 18/11/2026. Implementación sobre los proyectos existentes; C#, .NET 10 y WPF.

## Arquitectura y alcance

| Capa | Responsabilidad |
| --- | --- |
| Domain | `Movement`, `Budget`, `SavingGoal`, `Contribution`, `FinanceData`, sesión y tipo de movimiento. Importes `decimal`. |
| Application | Contratos `IUserRepository`, `IPasswordHasher`, `IFinanceRepository`, `IProjectionService`; reglas financieras, validaciones, resúmenes, evaluación presupuestaria y proyección. Sin dependencias WPF ni SQLite. |
| Infrastructure | Autenticación/persistencia SQLite, configuración del proveedor y PBKDF2. Se conservan intactos `OracleUserRepository`, diagnósticos y scripts SQL/PowerShell de Oracle. |
| Desktop | Autenticación existente adaptada; ventana de trabajo, ocho secciones, comandos asincrónicos, vistas y ViewModels separados. Diálogos Windows detrás de `IDialogService`. |

Los servicios de cálculo no dependen del proveedor de datos ni de la interfaz. Las escrituras SQLite vuelven a validar las entradas y usan transacciones. La interfaz anuncia errores y conserva el formulario cuando una operación falla. El comando en curso queda deshabilitado.

## Proveedor temporal SQLite

Se incorpora el proveedor oficial [Microsoft.Data.Sqlite 10.0.14](https://www.nuget.org/packages/Microsoft.Data.Sqlite/10.0.14). Ubicación real: `%LOCALAPPDATA%\FINPULSE\finpulse.db`. No se guarda la base en el repositorio ni junto al ejecutable.

Configuración en `src/Finpulse.Desktop/appsettings.json` (copiado a la salida):

```json
{
  "Provider": "SQLite",
  "BudgetWarningPercent": 80
}
```

`FINPULSE_PROVIDER` tiene precedencia sobre el JSON. Valores admitidos: `SQLite` y `Oracle`. El umbral debe ser mayor que cero y menor que 100. Es necesario reiniciar tras cambiar la configuración. Con SQLite no se lee ni utiliza la cadena Oracle. Un proveedor desconocido o un archivo JSON inválido impide habilitar el acceso y muestra un mensaje de configuración; no se cambia silenciosamente de proveedor.

Oracle sigue utilizando `FINPULSE_ORACLE_CONNECTION`. Su autenticación se conserva, pero aún no existe repositorio Oracle para los nuevos módulos financieros: la sesión Oracle muestra esa limitación y no abre datos SQLite con identificadores Oracle.

### Esquema local

| Tabla | Columnas principales y restricciones |
| --- | --- |
| `users` | id, name, email único normalizado, hash, failures, locked_until. |
| `movements` | id, user_id, kind (ingreso/gasto), cents positivo, category, date ISO, description. Índice por usuario/fecha. |
| `budgets` | id, user_id, month AAAA-MM, category, cents positivo; único por usuario/mes/categoría. |
| `goals` | id, user_id, name, description, cents objetivo, deadline. |
| `contributions` | id, user_id, goal_id, cents positivo, date; clave foránea compuesta que impide aportar a una meta ajena. Borrado en cascada al eliminar la meta. |

Versión inicial: `PRAGMA user_version=1`. Claves foráneas activas y diario WAL. Cada conexión se cierra al terminar. Las lecturas completas usan una transacción para obtener un estado coherente; las escrituras usan transacciones inmediatas para serializar la comprobación del saldo reservado. Un error revierte la operación.

Los valores monetarios se almacenan como centavos enteros y se convierten a `decimal` al leer. No se usa `REAL` para dinero. Se admiten montos mayores que cero, hasta 1.000.000.000, con máximo dos decimales. Fechas entre 1900 y 2200; descripción máxima de 500 caracteres. Las categorías se validan por tipo de movimiento.

### Autenticación y privacidad

Registro real y acceso sin cuentas precargadas. PBKDF2-SHA256, 600.000 iteraciones, sal aleatoria de 16 bytes y comparación en tiempo constante; se reutiliza el componente existente. Cinco fallos bloquean el acceso durante 15 minutos. El bloqueo queda persistido. El trabajo de hashing se ejecuta fuera del hilo de interfaz.

Todas las lecturas y modificaciones financieras se limitan por `user_id`; se comprueba también la propiedad de las metas. El aislamiento es lógico dentro de la aplicación. La base SQLite no está cifrada: un usuario con acceso directo al archivo puede inspeccionarla. Esta entrega es de desarrollo local, sin backend remoto, recuperación de contraseña ni despliegue multiusuario de producción.

## Vistas y ViewModels

`MainWindow` conserva registro/acceso y abre `WorkspaceWindow` después de autenticar una cuenta SQLite. Cerrar la ventana de trabajo o pulsar Cerrar sesión devuelve al acceso y descarta el estado financiero de la sesión.

| Vista en `Desktop/Views` | ViewModel | Comportamiento |
| --- | --- | --- |
| DashboardView | DashboardViewModel | Indicadores, seis meses de ingresos/gastos, distribución de gastos por categoría, diez movimientos recientes y acceso al primer ingreso. |
| MovementsView | MovementsViewModel | Ingresos y gastos con alta, edición, eliminación confirmada, fecha, categoría, descripción, filtros inclusivos desde/hasta y categoría, total del resultado. |
| BudgetsView | BudgetsViewModel | Crear/editar/eliminar presupuestos generales o por categoría; asignado, gastado, disponible, porcentaje y estado. Avisos ámbar al umbral y rosa al alcanzar/superar 100 %, acompañados por texto. |
| GoalsView | GoalsViewModel | Crear/editar/eliminar metas; objetivo, fecha, acumulado, porcentaje y estado calculado (En curso, Vencida, Cumplida). Seleccionar una meta muestra sus aportes; se pueden crear, editar y eliminar. |
| ProjectionsView | ProjectionsViewModel | Horizonte 1/3/5/10 años, escenario, inflación y variaciones editables; tabla nominal y real. |
| ReportsView | ReportsViewModel | Consulta por período, totales y exportación CSV con diálogo de destino y confirmación de sobrescritura del sistema. |
| SettingsView | SettingsViewModel | Proveedor, ubicación, umbral vigente, instrucciones para cambiar configuración y privacidad local. |

Los ViewModels están en `Desktop/ViewModels`. `Observable` implementa notificaciones, `AsyncCommand` evita reejecución mientras trabaja y comunica errores, y `WorkspaceViewModel` controla sesión/navegación/recarga. El código detrás de las vistas únicamente inicializa controles; `MainWindow.xaml.cs` mantiene el flujo de autenticación original, sin lógica financiera.

Los estilos reutilizables viven en `App.xaml`: fondo #15171C, paneles oscuros, acentos verde/azul, Segoe UI del sistema (sin descargar fuentes), controles redondeados y foco de teclado. Se conserva el marco nativo de Windows. El manifiesto declara PerMonitorV2, asInvoker y rutas largas. No se incorporan animaciones continuas.

Las tablas no editan directamente: seleccionar una fila carga el formulario y Guardar confirma la edición. Nuevo/Limpiar evita reutilizar involuntariamente el identificador de la fila. Los filtros solo se aplican al pulsar Filtrar/Consultar. Guardar recarga la sección y restablece filtros/formulario. Si no hay movimientos el dashboard muestra un estado vacío; no se insertan cifras de demostración.

## Cálculos financieros

- Ingresos/gastos mensuales: suma por tipo, año y mes de la fecha registrada.
- Saldo total: ingresos históricos menos gastos históricos; incluye todos los movimientos registrados. No hay módulo de movimientos programados: registrar una fecha futura también modifica este saldo.
- Ahorro mensual: ingresos mensuales menos gastos mensuales.
- Porcentaje de ahorro: ahorro / ingresos × 100; cero si no hay ingresos. Puede ser negativo cuando los gastos superan los ingresos.
- Distribución de gastos: gasto de la categoría / gasto mensual × 100. Sin gastos no se generan porcentajes ficticios.
- Presupuesto: gastos del mes y categoría correspondiente. General incluye todas las categorías; los presupuestos individuales no se suman al general. Disponible = asignado − gastado; el porcentaje puede superar 100 %.
- Metas: acumulado = suma de aportes; progreso = acumulado / objetivo × 100. La meta cumplida tiene prioridad sobre el estado vencido.

### Regla para evitar doble contabilización

Un aporte **reserva saldo existente**. No crea ingreso ni gasto. Saldo libre = saldo total − todos los aportes. El almacén rechaza nuevos aportes y movimientos que dejarían reservas sin respaldo. También rechaza borrar/reducir ingresos si eso compromete las reservas. Para usar dinero reservado, primero reduce/elimina el aporte. Borrar una meta libera sus reservas, conservando intactos los movimientos. Los aportes no se asignan automáticamente a las metas a partir del ahorro mensual.

### Proyecciones

Valores iniciales: saldo total y entradas/salidas del mes actual, editables en el formulario. Son valores del mes completo registrado hasta ese momento, sin anualizar por días transcurridos ni calcular un promedio de meses anteriores.

Para cada año: saldo += (ingreso mensual − gasto mensual) × 12. Después se aplican variaciones anuales a ingreso y gasto para el siguiente año. Índice de precios *= (1 + inflación/100); saldo real = saldo nominal / índice de precios. Se redondea la salida a dos decimales, manteniendo precisión `decimal` durante el cálculo.

Base conserva las tasas introducidas. Conservador resta 2 puntos a la variación de ingresos y suma 2 a gastos; Optimista hace lo contrario. Se muestran las hipótesis aplicadas. Tasas efectivas admitidas entre −100 y 100 %, inflación entre 0 y 100 %. No hay intereses, recomendación financiera, IA ni predicción estadística.

El CSV incluye fecha ISO, tipo, categoría, monto con punto decimal y descripción; usa punto y coma, comillas y UTF-8 con BOM. Se neutralizan campos que comienzan por caracteres de fórmula para evitar ejecutarlos al abrirlos en una hoja de cálculo.

## Ejecutar desde VS Code

Windows 10/11, SDK .NET 10. Abre la carpeta actual y usa la terminal integrada:

```powershell
dotnet restore .\src\Finpulse.Desktop\Finpulse.Desktop.csproj
dotnet build .\src\Finpulse.Desktop\Finpulse.Desktop.csproj
dotnet run --project .\src\Finpulse.Desktop\Finpulse.Desktop.csproj
```

También se conserva `scripts/compilar-y-ejecutar.ps1`. No es necesario iniciar Oracle. Si el entorno ya contiene `FINPULSE_PROVIDER=Oracle`, cambia esa variable a SQLite para el proceso actual o elimínala antes de ejecutar. Crea una cuenta propia, inicia sesión y registra movimientos. No se traslada automáticamente una cuenta Oracle a SQLite.

## Pruebas y evidencia

```powershell
dotnet run --project .\tests\Finpulse.Smoke\Finpulse.Smoke.csproj
dotnet run --project .\tests\Finpulse.Smoke\Finpulse.Smoke.csproj -- --ui
dotnet build .\tests\Finpulse.Smoke\Finpulse.Smoke.csproj -c Release
dotnet list .\src\Finpulse.Infrastructure\Finpulse.Infrastructure.csproj package --vulnerable --include-transitive
```

Resultados observados durante esta etapa:

- 64 comprobaciones de persistencia/finanzas correctas y las 26 comprobaciones originales de reglas/hash correctas (90 en total).
- Incluye ingresos, gastos, saldo, ahorro, porcentajes, montos inválidos, fechas/categorías inválidas, CRUD, duplicados, presupuestos 80/100/superado, umbral configurable, aislamiento, saldos reservados, rollback, metas/aportes, proyecciones, hash persistido, bloqueo temporal e integridad SQLite.
- La persistencia se comprueba recreando el almacén y ejecutando otro proceso que vuelve a abrir el mismo archivo. No se insertan pruebas en la base del usuario.
- Ocho vistas renderizadas con WPF `RenderTargetBitmap`, sin errores de binding, con datos temporales explícitamente de prueba; capturas en `docs/qa`. Los archivos PNG son evidencia de QA, no datos del usuario.
- Comandos ViewModel comprobados contra SQLite temporal: guardar movimiento, filtrar categoría, guardar aporte, exportar CSV, cancelar eliminación y confirmar eliminación.
- Arranque interactivo comprobado con Computer Use: la ventana muestra SQLite como proveedor, habilita registro/acceso y no pide credenciales Oracle. No se automatizaron contraseñas ni se crearon cuentas de usuario desde la interfaz.
- NuGet: Infrastructure sin paquetes vulnerables reportados, incluidas dependencias transitivas, en los orígenes consultados.
- Compilación Release de Desktop y el proyecto de pruebas: cero advertencias y cero errores.

La auditoría estática nativa informa dos advertencias de virtualización en los gráficos del dashboard. Se revisaron: las colecciones están limitadas a seis meses y ocho categorías, por lo que su generación completa es acotada; los historiales usan DataGrid virtualizado. Informa además una recomendación de ReadyToRun que pertenece al futuro empaquetado. No son advertencias del compilador.

Límites de la verificación: no se ha recorrido registro/acceso completo mediante interacción manual, ni teclado de extremo a extremo, lectores de pantalla, temas de alto contraste, varios monitores/DPI reales, instalación/desinstalación o firma. Las capturas de vistas y las pruebas de comandos no sustituyen esas comprobaciones. No se ejecutó integración Oracle durante esta etapa.

## Archivos añadidos y modificados

- `Domain/Finance.cs`: entidades financieras.
- `Application/FinanceService.cs`: contratos, reglas, resúmenes, presupuestos y motor determinista.
- `Infrastructure/SqliteStore.cs`, `StorageConfiguration.cs` y su `.csproj`: SQLite/configuración/paquete oficial.
- `Desktop/App.xaml`, `App.xaml.cs`: estilos y cultura es-BO.
- `Desktop/MainWindow.xaml`, `MainWindow.xaml.cs`: tema/acceso local y transición a la ventana de trabajo.
- `Desktop/WorkspaceWindow.xaml`, `.xaml.cs`, `ViewModels/*`, `Views/*`: navegación y módulos MVVM.
- `Desktop/MovementKindConverter.cs`: etiquetas Ingreso/Gasto en español para reportes y movimientos recientes.
- `Desktop/appsettings.json`, `app.manifest`, `.csproj`: selección de proveedor, DPI y copia de configuración.
- `tests/Finpulse.Smoke/Program.cs`, `LocalFinanceChecks.cs`, `DesktopChecks.cs`, `.csproj`: pruebas financieras y WPF dentro del proyecto de pruebas existente; ahora requiere Windows.
- `README.md`, este documento y `docs/qa/*`: ejecución y evidencia.

No se modificaron los archivos de integración Oracle ni los scripts SQL.

## Estado y pendientes

**Funcionando en SQLite:** registro/acceso local, navegación, dashboard, CRUD de ingresos/gastos con filtros, presupuestos, metas y aportes, exportación CSV y proyección determinista.

**Parcial:** Configuración es de consulta e instrucciones; el proveedor/umbral se cambian por archivo o entorno, no mediante un formulario de ajustes. Reportes incluye tabla/CSV; no PDF ni impresión. Proyecciones contempla escenarios deterministas sin intereses ni modelos estadísticos.

**Pendiente para Oracle/producción:** esquema financiero definitivo, `IFinanceRepository` Oracle y sus pruebas, migración explícita de datos locales/cuentas si se decide ofrecerla, secretos/arquitectura remota, migraciones versionadas posteriores al esquema inicial, cifrado/backup asistido, recuperación de cuenta, paginación SQL para volúmenes grandes, auditoría de movimientos y empaquetado firmado.

La consulta carga el historial del usuario en memoria; es adecuada para desarrollo académico local, sin afirmar validación de grandes volúmenes. Los datos no se sincronizan ni se eliminan al cambiar el proveedor.
