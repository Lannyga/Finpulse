# Cuenta de demostración — seis meses de uso

## Portal MVC vigente · 19/11/2026

El acceso ahora ofrece **Entrar a la cuenta demo** con un clic. Está vinculado al usuario demo configurado en `appsettings.Development.json`, sin contraseña en HTML ni campos que permitan escoger otra cuenta. Solo funciona con Development, SQLite y conexión de loopback; exige POST y antiforgery. Si falta la cuenta, está bloqueada o el entorno no corresponde, el acceso no se ofrece y el endpoint devuelve 404. No crea usuarios automáticamente. Cerrar sesión está en la cabecera, también en móvil, y devuelve al formulario de acceso. Se reutilizan las cookies y la acción Logout existentes.

Verificación del acceso rápido: 13 comprobaciones HTTP aprobadas en Development y seis en Production, incluyendo cierre efectivo, reentrada, CSRF, rechazo de GET, ausencia de contraseña en HTML e imposibilidad de escoger otro usuario. Logs: `docs/qa/access-demo-checks.log` y `docs/qa/access-production-checks.log`. La revisión visual por navegador sigue pendiente.

Se creó una cuenta nueva, identificada por el prefijo `demo_6meses_`, en la base local del portal (`src/Finpulse.Web/finpulse-web.db`). Usuario y contraseña aleatorios se entregaron en la conversación, sin persistirlos en esta documentación. No se solicita correo y no se agrega una cuenta al arrancar.

Incluye seis meses completos (mayo a octubre) y noviembre hasta el 19/11/2026: 229 movimientos ficticios, 14 presupuestos, tres metas, seis aportes y cuatro escenarios base a 1, 3, 5 y 10 años sin rendimiento. Saldo: Bs 2.300; reservado: Bs 1.100; disponible: Bs 1.200. Ingresos del mes: Bs 1.700; gastos: Bs 630; ahorro: Bs 1.070. Las estadísticas las calcula el motor habitual a partir de los registros, no son cifras fijas insertadas en las vistas. El historial es sintético: no acredita que la cuenta haya existido realmente seis meses.

Se adaptó el patrón del generador anterior al modelo MVC y a PasswordHasher. La creación es transaccional, inserta un propietario nuevo y no modifica otras cuentas. El comando explícito `dotnet run --project src/Finpulse.Web -- --create-demo` solo se permite en Development y SQLite local. Cada ejecución crea otra cuenta con credenciales distintas; nunca una contraseña predeterminada.

Verificación: compilación Release sin advertencias ni errores; ejecución del generador real sobre SQLite temporal y comprobación independiente de fechas, cantidades, sumas, promedio histórico, reservas y horizontes. Se añadieron nueve regresiones al ejecutable WebChecks, pero Windows bloqueó su ejecución con Control de aplicaciones (0x800711C7); no se presentan como aprobadas. No se cambió esa protección.

Se comprobó además el acceso HTTPS de la cuenta creada y su dashboard: HTTP 200, página Resumen, saldo `2.300,00`, disponible `1.200,00`, ingresos del mes `1.700,00`, gastos `630,00` y ahorro `1.070,00`. La primera aserción HTTP esperaba separadores estadounidenses y se corrigió al confirmar el formato es-BO de la vista; no fue un fallo del saldo ni del acceso. La sesión de Edge no se sustituyó.

## Contexto histórico WPF (anterior a la migración)

Se incorporó un generador explícito de datos ficticios. No se ejecuta al abrir FINPULSE, no activa acceso automático y no introduce una contraseña fija en el código. Cada ejecución crea una cuenta nueva, con correo y contraseña aleatorios; conserva las cuentas existentes. Las contraseñas siguen almacenándose como PBKDF2-SHA256 con sal aleatoria.

## Demostración creada el 19/11/2026

- Proveedor: SQLite.
- Nombre visible: `DEMO - Estudiante (historial ficticio)`.
- Archivo: `%LOCALAPPDATA%\FINPULSE\finpulse.db`, en el perfil del usuario de Windows que ejecuta el comando.
- Fechas: 01/05/2026 a 19/11/2026; seis meses completos y el mes actual parcial.
- 229 movimientos, 14 presupuestos, tres metas y seis aportes.
- Saldo histórico al corte: Bs 2.300; reservado en metas: Bs 1.100; libre: Bs 1.200.
- Ingresos del mes actual: Bs 1.700; gastos hasta el corte: Bs 630; ahorro registrado: Bs 1.070.
- Se comprobó el inicio de sesión usando las credenciales generadas. Se entregaron en la conversación y no se guardan en esta documentación.

Todos los movimientos llevan la marca `[DEMO FICTICIA]`; las metas y el nombre de la cuenta también están identificados como demostración. No se importaron datos personales ni movimientos reales.

## Historia representada

| Mes completo | Ingresos Bs | Gastos Bs | Ahorro Bs |
| --- | ---: | ---: | ---: |
| Mayo | 1.400 | 1.120 | 280 |
| Junio | 1.400 | 1.280 | 120 |
| Julio | 1.500 | 1.690 | -190 |
| Agosto | 1.550 | 1.460 | 90 |
| Septiembre | 1.650 | 1.240 | 410 |
| Octubre | 1.700 | 1.180 | 520 |

El estudiante combina mesada, beca y trabajo de medio tiempo. Los gastos se distribuyen por semana y categoría. Hay deterioro, un mes con déficit y recuperación posterior para que el gráfico muestre cambios, no una línea siempre ascendente.

Las metas son una computadora de Bs 4.500 con Bs 500 reservados, un fondo de emergencia de Bs 1.200 con Bs 300 y libros de Bs 300 ya completos. Los aportes reservan dinero existente y no agregan ingresos ni gastos. Los presupuestos históricos incluyen meses excedidos y otros dentro del límite.

## Cómo recorrerla

1. En FINPULSE, usa SQLite e inicia sesión con las credenciales entregadas. No necesitas registrar otra vez esa cuenta.
2. En Dashboard, selecciona 12 meses y alterna `Saldo y ahorro`, `Ingresos y gastos` y `Trabajo e ingresos`.
3. En Ingresos/Gastos, filtra desde mayo para consultar el detalle semanal y por categoría.
4. En Presupuestos, compara los meses excedidos con la recuperación de septiembre/octubre.
5. En Metas, selecciona cada fila para ver sus aportes y estados.
6. En Proyecciones, el historial ya es suficiente para cargar el promedio de los tres meses completos anteriores. Modifica los supuestos de cada escenario y compara plazos de metas.
7. En Reportes, elige 01/05/2026–19/11/2026 para consultar/exportar el período de demostración.

## Generar otra cuenta

Desde la raíz del proyecto, en la terminal de tu usuario de Windows:

```powershell
dotnet build .\tests\Finpulse.Smoke\Finpulse.Smoke.csproj -c Release
dotnet run --project .\tests\Finpulse.Smoke\Finpulse.Smoke.csproj -c Release --no-build -- --verify-demo
dotnet run --project .\tests\Finpulse.Smoke\Finpulse.Smoke.csproj -c Release --no-build -- --create-demo
```

`--verify-demo` solo trabaja con una base temporal. `--create-demo` es una acción explícita que escribe una nueva cuenta en el SQLite local del usuario de Windows y muestra sus credenciales; no debe usarse en registros públicos porque la salida incluye la contraseña recién generada. Ejecutarlo desde otra cuenta de Windows crea los datos en el perfil de esa otra cuenta.

La inserción completa ocurre en una sola transacción. Si falla, no queda una cuenta de demostración incompleta. No se actualizan ni borran usuarios existentes. Los movimientos del mes actual se limitan a la fecha de corte, sin insertar ingresos o gastos futuros. Al generarla en otra fecha, cambian el período y la cantidad de movimientos del mes parcial.

## Archivos y pruebas

- `Infrastructure/SqliteStore.Demo.cs`: generación transaccional, credenciales aleatorias, movimientos, presupuestos, metas y aportes.
- `Infrastructure/SqliteStore.cs`: declaración parcial para separar la utilidad de demostración de los métodos habituales.
- `tests/Finpulse.Smoke/DemoAccountCommands.cs`: comando de creación y verificación aislada.
- `tests/Finpulse.Smoke/Program.cs`: argumentos explícitos y prueba de demostración incorporada a la suite normal.

La prueba aislada verificó los totales anteriores, ausencia de movimientos futuros, existencia de ahorro positivo/negativo, promedio histórico disponible, acceso, persistencia, credenciales diferentes al repetir y conservación de una cuenta anterior con sus movimientos. La compilación Release terminó sin errores ni advertencias, y la creación real en `%LOCALAPPDATA%\FINPULSE` terminó con código 0.

El Control de aplicaciones bloqueó una de las compilaciones Debug nuevas durante la preparación. La compilación y verificación Release utilizadas para crear la cuenta sí se ejecutaron correctamente, sin cambiar ninguna política de seguridad. La existencia de la cuenta no evita un eventual bloqueo de Windows al abrir los binarios de la aplicación.
