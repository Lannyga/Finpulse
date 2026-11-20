# FINPULSE · Reestructuración web

Fecha: 19 de noviembre de 2026.

Actualización de integración: consultar [Integración Oracle Web](07_Integracion_Oracle_Web.md) para el inventario local, instalación segura, respaldo SQLite y pruebas actuales. El contenido siguiente documenta el estado original de la reestructuración.

## Resultado y límites

Portal ASP.NET Core MVC funcional sobre .NET 10, con SQLite provisional y modelo EF Core Oracle. La solución activa compila solo Finpulse.Web y Finpulse.WebChecks. No depende de WPF ni de las capas anteriores; no incorpora SPA, mediadores, repositorios genéricos ni una API duplicada.

**Oracle sigue pendiente de integración real.** Se generó DDL con su proveedor, sin conectarse a una base. No se declara revisión visual: Computer Use terminó porque no pudo identificar la URL de Edge con suficiente confianza. Se verificaron flujos HTTP y se generaron snapshots HTML con datos sintéticos para revisión posterior.

## Auditoría y respaldo

Antes había Domain, Application, Infrastructure y Desktop (WPF); SQLite financiero y Oracle limitado a FP_USUARIOS con correo. Smoke dependía de WPF. No había portal ni repositorio Git.

Se creó `backups/original-con-rutas.zip` con las fuentes y rutas antes de los cambios estructurales. El README anterior está en `docs/README_Anterior_WPF.md`. No se borraron bases personales ni los proyectos antiguos. Estos quedan fuera de FINPULSE.slnx hasta verificar Oracle y retirar el cliente anterior de forma definitiva.

| Código reutilizado | Destino y función |
|---|---|
| Domain/Finance.cs | Web/Models/Finance.cs: contratos financieros |
| Domain/MonthlyProjection.cs | Web/Services/MonthlyProjection.cs: mismo motor mensual decimal |
| Application/FinanceService.cs | Web/Services/FinanceRules.cs: validación, balance y presupuestos |
| Application/FinancialAnalysisService.cs | Web/Services/FinancialAnalysisService.cs: historial, promedio, metas y consejos |
| Infrastructure/Pbkdf2PasswordHasher.cs | Web/Services/LegacyPasswordHasher.cs: compatibilidad de hashes anteriores |
| Smoke/ProjectionChecks.cs | Incluido en WebChecks: 56 regresiones del motor |

Los algoritmos se trasladaron conservando su implementación. El código activo se mantiene en Web; las fuentes históricas no se compilan en la solución activa. No se trasladaron repositorios manuales, autenticación con correo, generador de demostración, vistas/controladores WPF, MVVM, conversores ni pruebas de ventanas. La proyección anual antigua se excluyó porque el motor mensual cubre sus horizontes. El pequeño contrato de hashing conserva únicamente lo necesario para el verificador legado.

## Arquitectura y dependencias

Controllers coordina HTTP; Models contiene entidades, contratos y formularios; Services mantiene reglas, transacciones, autenticación e importación; Data/PortalDb define el único modelo EF; Views contiene Razor; wwwroot contiene CSS, JavaScript y marca SVG.

Oracle.EntityFrameworkCore 10.23.26400 cubre todas las entidades Oracle; Microsoft.EntityFrameworkCore.Sqlite 10.0.14 permite desarrollo local; System.Security.Cryptography.Pkcs 8.0.2 corrige la dependencia transitiva vulnerable de ODP.NET. MVC, cookies, PasswordHasher, antiforgery, rate limiting y Data Protection forman parte del framework. Bootstrap 5.3.8 y Chart.js 4.5.1 se sirven localmente, sin CDN en ejecución ni Bootstrap JavaScript innecesario. No se añadió framework de pruebas.

## Modelo de seis tablas

| Tabla | Utilidad y garantías |
|---|---|
| USUARIOS | Usuario normalizado único; hashes, fallos/bloqueo, versión de sesión, revisión de concurrencia y umbral |
| CATEGORIAS | Catálogo de ingreso/gasto, único por nombre y tipo |
| MOVIMIENTOS | Ingreso, gasto o asignación a meta; propietario, importe positivo, fecha y clave de reenvío única |
| PRESUPUESTOS | Mes, categoría/general, límite y unicidad propietario/mes/alcance |
| METAS_AHORRO | Nombre, objetivo, plazo y propietario; FK compuesta protege aportes entre usuarios |
| ESCENARIOS | Supuestos y horizonte; resultados derivados no se almacenan |

Oracle usa NUMBER(19,2) para importes de movimientos, límites y metas, y NUMBER(28,8) para supuestos. SQLite guarda dinero en centavos enteros y supuestos en texto decimal. No se usa punto flotante para dinero. La categoría debe corresponder al tipo también por FK compuesta. Las asignaciones requieren meta propia y no llevan categoría. Descripciones permiten NULL en Oracle por su tratamiento de las cadenas vacías; la lectura las convierte a texto vacío.

Las asignaciones no alteran balance ni gastos: reducen el disponible. Escrituras usan transacciones serializables y revisión del propietario para evitar sobreasignación concurrente. Claves de solicitud evitan repetir altas. Ediciones comparan la revisión mostrada en el formulario. No se puede borrar un ingreso que respalda aportes ni una meta con aportes pendientes. Las lecturas financieras se agrupan en una transacción consistente.

## Autenticación sin correo

Usuario ASCII de 3 a 40 caracteres, contraseña de 12 a 128 y confirmación. Normalización invariante a mayúsculas e índice único; el esquema nuevo no tiene campo de correo. PasswordHasher estándar protege contraseñas y códigos. El código aleatorio de 192 bits se muestra una vez; recuperarse cambia contraseña, código y versión de sesión con control de concurrencia.

Cookie __Host-Finpulse: Secure, HttpOnly, SameSite=Lax y expiración deslizante de 30 minutos. El servidor verifica su versión en cada solicitud. Cambiar o recuperar contraseña revoca sesiones anteriores. Cinco fallos bloquean 15 minutos; acciones de cuenta tienen además límite por IP. No hay cuentas predeterminadas, SMTP ni acceso social. Se realiza hashing también ante usuarios desconocidos.

Todas las operaciones financieras exigen autenticación y obtienen identidad desde claims. Consultas, edición y borrado filtran propietario. Cambios solo por POST con antiforgery global. Formularios específicos no aceptan propietario ni hash. Razor escapa texto y EF parametriza consultas. CSV neutraliza fórmulas en texto. CSP solo permite recursos locales. Data Protection guarda claves excluidas de Git y protegidas con DPAPI en Windows; en otro sistema deben protegerse con las capacidades del entorno de despliegue.

## Importación anterior

La importación SQLite exige un mapa JSON revisado de ID a nuevo usuario, por ejemplo `{"7":"estudiante7"}`. El destino debe estar inicializado y sin cuentas. No se generan identidades desde correos.

```powershell
dotnet run --project src/Finpulse.Web -- --import-legacy RUTA_ORIGEN.db MAPA_USUARIOS.json
```

Configura primero el proveedor y cadena del destino nuevo. El origen se abre ReadOnly y se lee en una transacción. Usuarios, movimientos, presupuestos, metas y aportes se importan con mapeo de IDs en una transacción de destino. Nunca se selecciona la columna email. El hash PBKDF2 anterior se admite y actualiza a PasswordHasher tras el primer acceso válido. El usuario importado debe cambiar la contraseña para recibir su primer código de recuperación.

La prueba sintética verifica finanzas, acceso con clave anterior, actualización de hash y SHA-256 del archivo origen sin cambios. **No se ejecutó sobre datos personales.** Una importación real requiere respaldo y revisión de nombres, categorías, montos y fechas. La importación de FP_USUARIOS de Oracle sigue pendiente de herramienta específica.

## Interfaz

El sistema visual está en `design-system/finpulse/MASTER.md`: Bento, verdes pino/bosque/musgo/menta, marca de pulso SVG y tipografía local. Formularios con etiquetas, errores, foco, estados vacíos y botón guardando. Gráficos con leyenda y patrones de línea; tablas/CSV alternativos. CSS adaptable y reduced-motion.

Contrastes de tokens calculados: texto/superficie 14,85:1, secundario/superficie 7,93:1, menta/bosque 7,15:1 y texto oscuro/tarjeta menta 9,74:1. Esto no sustituye la revisión visual del navegador.

## Verificación

Se compiló la solución Release sin advertencias ni errores. Logs definitivos: `docs/qa/web-data-checks.log` y `docs/qa/web-http-checks.log`. Las pruebas cubren cálculos, inflación, rendimiento, horizontes, historial y recomendaciones; registro, duplicados, bloqueo, códigos; persistencia, reenvíos, presupuestos, reservas, rollback, concurrencia, ediciones obsoletas y acceso cruzado.

Resultado: 56 regresiones del motor, 33 comprobaciones de datos/seguridad, una prueba completa de importación y 55 comprobaciones HTTP con snapshots. También se verifica que las descripciones opcionales vacías no bloqueen el registro de gastos o metas.

La consulta final de vulnerabilidades NuGet, incluidas dependencias transitivas, terminó sin paquetes vulnerables conocidos en el índice consultado. Referencia de versión del proveedor: [Oracle.EntityFrameworkCore 10.23.26400](https://www.nuget.org/packages/Oracle.EntityFrameworkCore/10.23.26400).

HTTPS verifica Razor, ingresos/gastos, edición, metas/aportes, escenarios, exportación, preferencias, CSRF, cierre de sesión y recuperación con revocación de cookie. El script `scripts/validar-web.ps1` crea base y certificado sintéticos en tests/.qa, inicia y detiene un servidor propio. No instala confianza ni toca bases compartidas. Su cliente acepta el certificado solo en loopback para esas pruebas.

Windows bloqueó ejecuciones intermedias con Control de aplicaciones y el aislamiento impidió inicialmente TLS/SSPI. La ejecución final se realizó con autorización fuera del aislamiento, sin cambiar políticas de seguridad. No se presenta esto como reparación del sistema operativo.

## Pendientes

1. Probar DDL, CRUD, restricciones, transacciones y concurrencia en una instancia Oracle aislada; comprobar versión y permisos reales. Generar SQL no demuestra integración.
2. Revisar visualmente 375, 768, 1024 y 1440 px, teclado y gráficos cuando se disponga de control de navegador.
3. Revisar el mapa de identidades y ejecutar una importación real con respaldo; completar cuentas Oracle anteriores si son necesarias.
4. Retirar físicamente los proyectos anteriores después de estas verificaciones. Ya no forman parte de la solución activa.

No se desplegó el portal ni se ejecutaron scripts destructivos.
