# FINPULSE conectado a Oracle

Estado final comprobado el 19 de noviembre de 2026, Bolivia: **Oracle es el proveedor operativo del portal** en https://localhost:7240. El servidor respondió HTTP 200 usando la validación normal del certificado HTTPS de Windows y se solicitó su apertura en Microsoft Edge. No se realizó una auditoría visual adicional del navegador.

## Configuración real

| Parámetro | Valor |
|---|---|
| Host / puerto / servicio | localhost / 1521 / FREEPDB1 |
| Cuenta operativa | FINPULSE_WEB |
| Cuenta de pruebas aisladas | FINPULSE_WEB_TEST |
| Database:Provider | Oracle |
| ConnectionStrings:Oracle | User Secrets local, sin publicar su contenido |
| ConnectionStrings:OracleTest | User Secrets local, sin publicar su contenido |
| UserSecretsId | finpulse-web-local |
| URL HTTPS del portal | https://localhost:7240 |

No hay contraseñas en Git, appsettings.json, argumentos de procesos ni informes. Se introdujeron localmente con entrada oculta. ASP.NET Core carga User Secrets en Development; una instalación distinta requiere sus propias credenciales seguras. User Secrets es almacenamiento de desarrollo fuera del repositorio, no una bóveda cifrada.

El proveedor predeterminado de appsettings.json y del código es Oracle. El arranque comprueba identidad, contenedor, tablas, restricciones, índices y segmentos por C# antes de servir solicitudes. Ante un error Oracle se detiene con un diagnóstico, sin volver automáticamente a SQLite.

## Esquemas y permisos verificados

Ambos esquemas contienen exactamente USUARIOS, CATEGORIAS, MOVIMIENTOS, PRESUPUESTOS, METAS_AHORRO y ESCENARIOS. Cada uno tiene 6 claves primarias, 2 claves alternativas, 7 claves foráneas, 8 CHECK de negocio y 19 índices válidos; restricciones habilitadas y validadas. Sus segmentos están materializados.

Las cuentas conservan únicamente CREATE SESSION como permiso de sistema. CREATE TABLE y CREATE SEQUENCE se concedieron temporalmente para instalar y se retiraron al finalizar. La cuota de cada esquema es 100 MB en USERS. El portal no usa SYS/SYSTEM; la autenticación local AS SYSDBA se utilizó únicamente para tareas administrativas autorizadas. FINPULSE_APP y FP_USUARIOS permanecen intactos.

El esquema operativo se inicializó solo con 13 categorías. El inventario de cierre confirma cero usuarios y cero movimientos operativos: las cuentas y finanzas sintéticas están exclusivamente en FINPULSE_WEB_TEST. Para empezar a utilizar Oracle, registrar una cuenta desde el portal.

Las relaciones y restricciones conservan las garantías del modelo existente: propietario en cada fila financiera, categoría compatible con el tipo mediante FK compuesta, aportación solo a una meta propia mediante FK meta/usuario, importes NUMBER(19,2) positivos, supuestos NUMBER(28,8), claves de reenvío únicas y presupuestos únicos por propietario/mes/alcance. No se añadieron tablas, paquetes NuGet ni módulos duplicados.

## Pruebas reales completadas

1. C# con ODP.NET y EF Core autenticó FINPULSE_WEB y FINPULSE_WEB_TEST en FREEPDB1; materializó entidades de las seis tablas.
2. El portal real pasó **74 comprobaciones HTTPS** usando FINPULSE_WEB_TEST: dos registros, acceso sin correo, cookies Secure/HttpOnly, CSRF, inicio/cierre de sesión, recuperación/revocación, dashboard, ingresos/gastos, edición, presupuestos, metas/aportes, escenarios, reportes y CSV.
3. Ingreso Bs 1.000 y gasto Bs 250 producen saldo Bs 750. El proceso se detuvo y reinició: saldo, sesión, meta y escenario siguieron disponibles desde Oracle.
4. Proyección persistida: Bs 1.000 de ingreso y Bs 250 de gasto mensuales durante cinco años, sin otras tasas, produce Bs 45.000.
5. Las solicitudes de lectura, edición y eliminación de datos ajenos no dieron acceso a otro estudiante.
6. Dos aportaciones simultáneas de Bs 400 sobre Bs 650 disponibles aceptaron una sola: reserva total Bs 500, disponible Bs 250, saldo Bs 750.
7. SQL directo como FINPULSE_WEB_TEST confirmó CHECK (ORA-02290), categoría incompatible y meta ajena (ORA-02291), unicidad de solicitud (ORA-00001), persistencia de preferencias y rollback de una inserción válida. Las inserciones de esta comprobación se revirtieron; no hubo cambios persistentes.
8. PLAN y APLICAR del SQL de reanudación recorrieron ambos esquemas completos conservando todos los objetos, sin nuevas concesiones ni DDL.
9. El servidor operativo autenticó FINPULSE_WEB y respondió HTTPS 200 con certificado aceptado por Windows.

Evidencias sin secretos:

- docs/qa/web-oracle-http-checks.log: 74 comprobaciones.
- docs/qa/web-oracle-http-result.json: resultado, usuario sintético, importes y reinicio.
- docs/qa/web-oracle-constraints.log: restricciones y rollback.
- docs/qa/web-oracle-idempotence.log: conservación de objetos al repetir el recorrido.
- docs/qa/oracle-integration-final.json: inventario y estado final.

El ejecutable auxiliar Finpulse.WebChecks.dll fue bloqueado por una política de Control de aplicaciones de Windows (0x800711C7; evento 3077). No se modificó esa política ni se cargó el archivo bloqueado. Las comprobaciones completas del portal se realizaron con un cliente HTTPS independiente en Node y las restricciones mediante SQLPlus, contra el mismo servidor C# y Oracle real. No se presentan los intentos fallidos del ejecutable auxiliar como pruebas exitosas.

## Errores encontrados y correcciones

- ORA-01031: faltaba CREATE SEQUENCE al crear la identidad de CATEGORIAS. Se concedió temporalmente y se completaron los esquemas; después se retiró junto con CREATE TABLE.
- Contraseña olvidada de FINPULSE_WEB: se introdujo una nueva con entrada oculta y se restableció únicamente después de comprobar que el esquema existente estaba vacío. No se reemplazó la cuenta ni se borraron objetos.
- ORA-08177 durante escrituras serializables: PortalFinance ahora reintenta de forma limitada la transacción completa, después de rollback y limpieza del seguimiento, recargando propiedad, revisión, idempotencia y saldo disponible. La prueba concurrente confirmó que se conserva el saldo. No se rebajó el aislamiento.
- Se verificó que los segmentos de las doce tablas estuvieran materializados. El DDL de nuevas instalaciones declara SEGMENT CREATION IMMEDIATE para evitar materialización diferida durante transacciones serializables.
- Se corrigieron los terminadores de los once índices para SQLPlus.
- El script de arranque ya no intenta inicializar SQLite en cada ejecución.
- La lectura de la cadena segura en PowerShell utiliza el setter real de DbConnectionStringBuilder, evitando que el adaptador de diccionario la trate como una clave.

Referencias oficiales: [ORA-08177 y reintento de transacciones](https://docs.oracle.com/en/error-help/db/ora-08177/), [restricciones de creación diferida de segmentos](https://docs.oracle.com/en/database/oracle/oracle-database/26/sqlrf/CREATE-TABLE.html), [materialización de segmentos](https://docs.oracle.com/en/database/oracle/oracle-database/26/arpls/DBMS_SPACE_ADMIN.html).

## Arranque y verificación reproducibles

```powershell
dotnet run --project src/Finpulse.Web
```

Abrir https://localhost:7240. No volver a ejecutar la instalación completa: los esquemas ya existen.

```powershell
# Solo lectura: conexión C# del proveedor operativo.
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/verificar-conexion-oracle.ps1 -Modo Web

# Flujos HTTPS sintéticos en el esquema TEST, con reinicio y concurrencia.
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/validar-oracle-http.ps1

# Restricciones SQL y rollback, usando la última cuenta sintética del informe HTTPS.
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/validar-restricciones-oracle.ps1

# Recorrido sin cambios de ambos esquemas ya completos.
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/verificar-reanudacion-oracle.ps1
```

El cliente HTTPS de pruebas genera un certificado local efímero nuevo bajo tests/.qa en cada ejecución; esos certificados no se instalan como confianza de Windows. La excepción de validación está limitada al cliente de pruebas contra localhost. El portal operativo utiliza el certificado normal de desarrollo, válido y aceptado por Windows.

Las pruebas conservan sus cuentas y datos sintéticos en FINPULSE_WEB_TEST para revisión; no escriben finanzas en FINPULSE_WEB. No compartir el contenido de User Secrets ni ejecutar su listado en informes.

## Código modificado y reutilización

- Program.cs: proveedor Oracle predeterminado, verificación previa al arranque, diagnóstico explícito y respaldo SQLite consistente.
- Data/OracleVerification.cs: identidad, contenedor, seis tablas, restricciones, índices, segmentos y materialización EF Core.
- Services/PortalFinance.cs: reintento acotado de transacciones completas ante ORA-08177; conserva reglas, concurrencia, revisión e idempotencia existentes.
- Finpulse.Web.csproj: UserSecretsId local.
- WebChecks/HttpChecks.cs, OracleChecks.cs y Program.cs: casos Oracle de persistencia, reinicio, concurrencia y restricciones. La ejecución del auxiliar quedó limitada por Windows como se documenta arriba.
- Scripts SQL/PowerShell/Node: instalación y reanudación seguras, privilegios mínimos, diagnóstico, pruebas HTTP independientes y rollback.

Se reutilizaron PortalDb, AccountService, las reglas y el motor financiero, los controladores y las vistas existentes. No se cambió el diseño web.

## SQLite y datos anteriores

Se conserva src/Finpulse.Web/finpulse-web.db y el respaldo consistente backups/finpulse-web-antes-oracle-20261119.db, validado con PRAGMA integrity_check=ok. No se sobrescribieron datos ni se migraron cuentas o movimientos automáticamente. El respaldo incluye datos confirmados de WAL mediante la API de SQLite, con el origen abierto en modo de solo lectura.

Una migración personal posterior requiere mapeo de propietarios, revisión del destino y autorización específica. El uso explícito de SQLite para desarrollo requiere Database__Provider=Sqlite; no es el proveedor operativo ni una recuperación silenciosa de Oracle.
