# FINPULSE · Portal financiero estudiantil

ASP.NET Core MVC, Razor y C# sobre .NET 10. Interfaz oscura Bento con identidad verde bosque, Bootstrap y Chart.js locales. `FINPULSE.slnx` contiene un proyecto web y un ejecutable independiente de pruebas.

**Estado:** conectado y verificado con Oracle en FREEPDB1. FINPULSE_WEB es el esquema operativo y FINPULSE_WEB_TEST contiene las pruebas sintéticas. Pasaron 74 comprobaciones HTTPS con persistencia tras reinicio, aislamiento y concurrencia, además de restricciones y rollback SQL. Consulta el [informe de integración real](docs/07_Integracion_Oracle_Web.md). SQLite y sus datos anteriores están respaldados y conservados.

## Iniciar desde VS Code

```powershell
dotnet restore FINPULSE.slnx
dotnet run --project src/Finpulse.Web
```

Abre https://localhost:7240. El portal local ya tiene Oracle configurado mediante ASP.NET Core User Secrets y HTTPS verificado. Al arrancar comprueba la conexión real a FINPULSE_WEB. En otra máquina se deben preparar credenciales propias; no están incluidas en Git. No repitas la instalación de los esquemas existentes.

Regístrate con usuario y contraseña de 12 a 128 caracteres. Guarda el código de recuperación mostrado una sola vez. El portal no solicita ni almacena correo.

La inicialización operativa solo agregó categorías. Las cuentas y los datos sintéticos de verificación están en el esquema separado FINPULSE_WEB_TEST.

## Funciones

- Dashboard, historial y recomendaciones justificadas con tus datos.
- Ingresos, gastos, presupuestos y metas: registro, edición y eliminación.
- Aportes y liberación de reservas para metas, sin duplicar gastos.
- Escenarios persistentes a 1, 3, 5 y 10 años con motor mensual, inflación, rendimiento y cambios de flujos; tablas y CSV.
- Reportes, preferencias de alertas y cambio/recuperación de contraseña con revocación de sesiones.
- Aislamiento de datos, CSRF, bloqueo de acceso, idempotencia y concurrencia.

## Pruebas

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/validar-oracle-http.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/validar-restricciones-oracle.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/verificar-reanudacion-oracle.ps1
```

El primer comando utiliza la conexión local segura de FINPULSE_WEB_TEST, genera un certificado efímero sin instalar confianza, verifica el portal por HTTPS y reinicia su proceso para comprobar persistencia. El segundo comprueba restricciones con rollback; el tercero confirma que la reanudación conserva los objetos. Los resultados están en `docs/qa/web-oracle-*`. Datos temporales en `tests/.qa/`, excluidos de Git. La excepción de certificado se limita al cliente de pruebas local.

## Oracle y migración

El procedimiento revisado está en [Integración Oracle Web](docs/07_Integracion_Oracle_Web.md). `scripts/configurar-oracle-web.ps1 -CrearEsquemas -AutorizarDDL` prepara, tras autorización, FINPULSE_WEB y FINPULSE_WEB_TEST, solicita las contraseñas localmente con entrada oculta y configura ASP.NET Core User Secrets. Conserva FINPULSE_APP y SQLite. El portal no ejecuta DDL Oracle al arrancar.

La configuración local usa `Database:Provider=Oracle` y `ConnectionStrings:Oracle` fuera del repositorio, con privilegios mínimos y sin SYS/SYSTEM para la aplicación. El instalador verifica identidad, contenedor y seis tablas antes de inicializar categorías. `scripts/verificar-conexion-oracle.ps1 -Modo Web` verifica la conexión real; `scripts/validar-oracle-http.ps1` verifica los flujos HTTPS en FINPULSE_WEB_TEST con reinicio y concurrencia; `scripts/validar-restricciones-oracle.ps1` comprueba restricciones y rollback. El auxiliar C# `scripts/validar-oracle-web.ps1` quedó bloqueado por la política local de Windows; sus intentos fallidos están documentados. Estas pruebas requieren haber autorizado los datos sintéticos en ese esquema.

Consulta [reestructuración, importación y pendientes](docs/06_Reestructuracion_Web.md). La importación SQLite anterior exige mapeo revisado de IDs a usuarios y un destino vacío, no copia correos y no modifica el origen. Las cuentas Oracle anteriores requieren una importación específica pendiente.

Los proyectos WPF anteriores permanecen fuera de la solución activa, con respaldo en `backups/original-con-rutas.zip`. El portal es independiente y contiene los algoritmos financieros reutilizados. El [README anterior](docs/README_Anterior_WPF.md) conserva el contexto histórico.
