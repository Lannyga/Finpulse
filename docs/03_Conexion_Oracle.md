# FINPULSE — Configuración y conexión con Oracle

Fecha: 18/11/2026, America/La_Paz.

## Configuración

| Parámetro | Valor |
| --- | --- |
| Conexión administrativa en SQL Developer | FINPULSE_ADMIN |
| Usuario administrativo, solo diagnóstico | SYSTEM |
| Tipo | Basic |
| Host | localhost |
| Puerto | 1521 |
| Service Name | FREEPDB1 |
| Usuario permanente de la aplicación | FINPULSE_APP |
| Configuración de la aplicación | FINPULSE_ORACLE_CONNECTION |
| Proveedor .NET instalado | Oracle.ManagedDataAccess.Core 23.26.400 |

La contraseña administrativa debe introducirse localmente. No se sustituye por
una contraseña inventada ni se guarda SYSTEM en la configuración de FINPULSE.

## Disponibilidad comprobada

Consulta PowerShell del estado, sin iniciar ni detener servicios:

| Servicio | Estado observado |
| --- | --- |
| OracleServiceFREE | Running |
| OracleOraDB23Home1TNSListener | Running |

`Test-NetConnection localhost -Port 1521` devolvió
`TcpTestSucceeded: True`. La prueba ODP.NET posterior confirma además que el
listener acepta una conexión real a FREEPDB1; una prueba TCP por sí sola no
demuestra autenticación ni disponibilidad de ese servicio de base de datos.

## Resultado real de C#

Se ejecutó el modo `--oracle-connectivity` del proyecto Smoke existente, cargando
FINPULSE_ORACLE_CONNECTION del perfil real del usuario de Windows, sin mostrarla.
El proveedor abrió una conexión física nueva (pooling desactivado en diagnóstico)
y ejecutó:

```sql
SELECT USER AS USUARIO,
       SYS_CONTEXT('USERENV', 'CON_NAME') AS BASE_DATOS
FROM DUAL;
```

Resultado observado, salida del proceso 0:

```text
USUARIO        BASE_DATOS
FINPULSE_APP   FREEPDB1
```

Las consultas SELECT parametrizadas adicionales confirmaron:

- FINPULSE no existe con ese nombre literal.
- FINPULSE_APP sí existe.
- FP_USUARIOS existe en FINPULSE_APP y es visible para esa cuenta.

El nombre previsto por el proyecto y sus scripts es FINPULSE_APP; se conserva
ese esquema existente. No hace falta duplicarlo como FINPULSE. No se crearon,
eliminaron ni modificaron usuarios, tablas o datos durante esta etapa.

**SYSTEM: pendiente de evidencia.** Se pidió al usuario ejecutar el script de
diagnóstico con entrada oculta. Aunque indicó haberlo ejecutado, al comprobarlo
todavía no se encontraba `docs/oracle-diagnostico-admin.json`. No se declara
obtenido el resultado `SYSTEM / FREEPDB1` sin ese resultado real.

## Arquitectura y archivos

Se conserva Domain sin dependencias, Application con referencia a Domain,
Infrastructure con referencias a Domain/Application y ODP.NET, y Desktop WPF
con referencias a esas capas. No se creó otro proyecto ni otra base de datos.

La conexión operativa sigue en OracleUserRepository: crea y dispone conexiones
por operación, usa BindByName y parámetros; autenticación usa transacción y
libera comandos y lectores con using. MainWindow sigue leyendo exclusivamente
FINPULSE_ORACLE_CONNECTION. No se modificaron ventanas ni módulos financieros.

Archivos C# nuevos/modificados:

- `src/Finpulse.Infrastructure/OracleConnectionDiagnostics.cs`: diagnóstico
  reutilizable de solo lectura, validación de identidad y FREEPDB1, tiempos
  máximos de conexión/consulta, cancelación y resultados sin cadena de conexión.
- `tests/Finpulse.Smoke/Program.cs`: modos optativos de diagnóstico; la ruta
  original de las 26 comprobaciones queda conservada.

Archivos adicionales:

- `scripts/verificar-conexion-oracle.ps1`: solicita la contraseña mediante
  Read-Host -AsSecureString, la transmite solo al proceso hijo por una variable
  temporal y restaura el entorno en finally. No persiste la clave administrativa.
  El JSON de resultado incluye únicamente campos permitidos del diagnóstico.
- `sql/04_diagnostico_conexion.sql`: consultas manuales de solo lectura.
- Este informe y referencia de acceso en README.

Los scripts originales de creación del usuario y esquema ya existen en
`sql/01_crear_usuario_oracle.sql` y `sql/02_esquema_usuarios.sql`. Se revisaron y
no se ejecutaron. No ejecutar tampoco `preparar-oracle-local.ps1` sobre la
instalación actual; ya existe el esquema.

## Compilación y problemas observados

`dotnet build .\src\Finpulse.Desktop\Finpulse.Desktop.csproj` completó con
**0 errores y 0 advertencias** en el contexto normal de Windows.

Dentro del aislamiento aparecieron dos restricciones de entorno:

- NuGet no pudo consultar su índice y emitió NU1900. La compilación autorizada
  fuera del aislamiento pudo restaurar sin esas advertencias.
- WPF recibió MC1000 por acceso denegado al archivo generado MainWindow.g.cs.
  La misma compilación fuera del aislamiento terminó correctamente; no se
  cambiaron permisos ni políticas de seguridad.

El diagnóstico de configuración ausente se ejecutó, pero la comprobación de
configuración inválida falló inicialmente. Se amplió el tratamiento de formato
inválido; su nueva ejecución quedó bloqueada antes de las comprobaciones por
Control de aplicaciones de Windows: FileLoadException, `0x800711C7`, también
fuera del aislamiento. Por ello esa corrección y los casos negativos no se
declaran superados. La prueba positiva con FINPULSE_APP sí se ejecutó antes de
ese bloqueo y queda diferenciada de los resultados pendientes.

El código maneja errores Oracle con su número exacto y mensajes controlados
(incluidos 1017, 28000, 28001, 12514, 12541, 12154 y 12170), sin imprimir el
mensaje arbitrario del proveedor ni la conexión. No se provocaron intentos de
autenticación con contraseñas inventadas ni se detuvo el servidor para probar.
Conexiones, comandos y lectores se liberan mediante using en éxito y excepción;
la cancelación y el formato inválido tienen resultados específicos. La liberación
de recursos en todos los fallos se revisó estáticamente, sin afirmar pruebas
integrales de todas las rutas.

## Guardar FINPULSE_ADMIN en SQL Developer

No fue posible acceder a VS Code mediante Computer Use (`native pipe is
unavailable`, os error 2). No se inventaron archivos internos de la extensión.

1. Abre Oracle SQL Developer en la barra lateral de VS Code y crea una conexión.
2. Nombre FINPULSE_ADMIN, usuario SYSTEM, tipo Basic, host localhost, puerto 1521,
   y selecciona **Service Name: FREEPDB1** (no SID).
3. Introduce la contraseña de instalación localmente; deja desactivado guardar
   contraseña si no deseas persistirla. Pulsa Test y guarda la conexión.
4. Ejecuta `sql/04_diagnostico_conexion.sql`. Para la conexión administrativa,
   la primera consulta debe devolver SYSTEM y FREEPDB1. Ese resultado es esperado,
   no un resultado ya observado en esta etapa.

## Pasos pendientes que requieren intervención

Para completar la comprobación administrativa sin compartir la contraseña:

```powershell
.\scripts\verificar-conexion-oracle.ps1 -Modo Admin
```

Si termina correctamente, deja un JSON sin contraseña que permite confirmar
SYSTEM y FREEPDB1. Si Windows bloquea el binario con `0x800711C7`, el administrador
del equipo debe revisar la autorización o firma exigida por Control de aplicaciones.
No se debe desactivar ni eludir esa política. También sigue pendiente guardar
manualmente FINPULSE_ADMIN en la extensión.

Para repetir solo el diagnóstico de la aplicación, sin tocar datos:

```powershell
.\scripts\verificar-conexion-oracle.ps1 -Modo App
```

El script toma la variable existente del proceso o del perfil de Windows; si falta,
solicita la contraseña de FINPULSE_APP localmente. La aplicación WPF sigue leyendo
la variable de su proceso, por lo que una terminal antigua puede necesitar cargarla:

```powershell
$env:FINPULSE_ORACLE_CONNECTION = [Environment]::GetEnvironmentVariable('FINPULSE_ORACLE_CONNECTION', 'User')
```

No imprimir esa variable. Su presencia no sustituye el diagnóstico SELECT.
