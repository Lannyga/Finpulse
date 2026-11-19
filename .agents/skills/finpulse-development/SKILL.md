---
name: finpulse-development
description: Mantener, corregir y mejorar exclusivamente el portal web FINPULSE con ASP.NET Core MVC, C#, .NET 10 y Oracle; aplicar autenticación con nombre de usuario y contraseña, seguridad financiera, arquitectura mínima, reutilización y pruebas automatizadas. No aplicar a otros productos ni al desarrollo de interfaces de escritorio.
---

# Desarrollo de FINPULSE

## Alcance y estado del proyecto

Trabaja exclusivamente en el portal web FINPULSE. El objetivo tecnológico es ASP.NET Core MVC con vistas Razor, C#, .NET 10 y Oracle. No sustituyas estas decisiones por una SPA, otro lenguaje, otro motor de datos o una aplicación de escritorio salvo instrucción expresa del usuario.

Al crear esta skill, la carpeta FINPULSE_Entrega_01 contenía Domain, Application, Infrastructure y Desktop (WPF), pruebas Smoke y OracleIntegration, y scripts SQL. El README indicaba SQLite como proveedor predeterminado y soporte Oracle limitado a registro y autenticación. Es contexto histórico, no evidencia de que el portal MVC o los módulos financieros Oracle ya existan. Comprueba el estado real en cada tarea.

Crear o invocar esta skill no autoriza por sí solo una migración completa. Si una petición web requiere una base MVC inexistente, identifica el trabajo mínimo necesario y acota su implementación a lo solicitado. Reutiliza reglas y componentes compatibles; no traslades dependencias WPF al servidor ni elimines el cliente existente como efecto secundario.

## Reconocer y reutilizar antes de modificar

Lee las instrucciones AGENTS.md aplicables y examina los proyectos, rutas MVC, modelos, persistencia, SQL, autenticación y pruebas relacionados con el cambio. Usa README y docs para entender reglas, contrastándolos con código y esquema vigentes. No leas ni muestres secretos para hacer este inventario.

Localiza primero la implementación equivalente: cálculos, validaciones, consultas, servicios, vistas y pruebas. Extiéndela o refactorízala de forma localizada antes de crear una segunda versión. Conserva contratos, datos y funcionalidades existentes; no ocultes funciones incompletas tras resultados simulados. Separa hechos verificados de supuestos y pregunta solo cuando falte una decisión de negocio necesaria.

## Arquitectura mínima que preserve las garantías

- Conserva los proyectos y patrones útiles existentes. No impongas una reorganización completa para un cambio local.
- Usa controladores MVC para coordinar solicitudes y respuestas; coloca reglas financieras reutilizables fuera de controladores y vistas. Usa modelos de entrada específicos para impedir asignación de campos sensibles.
- Prefiere capacidades del framework y dependencias ya instaladas. Agrega un paquete, interfaz, capa, proyecto o tabla solo si resuelve una necesidad concreta que el código actual no cubre razonablemente; explica esa necesidad.
- No introduzcas por costumbre repositorios genéricos, CQRS, mediadores, microservicios o capas duplicadas. Tampoco elimines límites que protegen transacciones, autorización, invariantes o pruebas.
- Reutiliza el acceso Oracle y la biblioteca de persistencia existentes cuando sean adecuados. No añadas un ORM paralelo ni sustituyas seguridad mantenida por criptografía propia para reducir dependencias.
- Conserva restricciones, relaciones e historial necesarios. Una tabla nueva exige un dato persistente o relación real; la preferencia por pocas tablas no justifica perder integridad, trazabilidad ni funcionalidades.

## Identidad y seguridad del portal

Implementa acceso con nombre de usuario y contraseña. No conviertas el correo en el único identificador de acceso ni añadas acceso social obligatorio. Si el esquema actual solo admite correo, determina una evolución compatible para nombres de usuario únicos y normalizados, sin inventar identidades ni invalidar cuentas existentes.

Reutiliza el mecanismo seguro de hashing y verificación existente, comprobando su adecuación al servidor. Guarda hashes con sal usando componentes mantenidos; nunca contraseñas en claro o cifrado reversible. Migra hashes antiguos de forma compatible cuando sea necesario. No crees usuarios o contraseñas predeterminados.

Usa autenticación y cookies de ASP.NET Core con HTTPS, HttpOnly, Secure y SameSite adecuado al flujo; aplica expiración y cierre de sesión efectivo. Protege el inicio de sesión contra intentos repetidos con controles proporcionales y respuestas que no revelen la existencia de cuentas. Usa únicamente destinos locales validados para redirecciones posteriores al acceso.

Comprueba autorización en el servidor para cada consulta y modificación, incluida propiedad de los registros. Obtén la identidad del contexto autenticado, no de un identificador enviado por el navegador. Un usuario no debe leer ni modificar finanzas de otro alterando una URL o formulario. Ocultar botones no es autorización.

Protege operaciones con cambios de estado mediante antiforgery; evita mutaciones por GET. Valida entradas en el servidor y conserva el escape de Razor. Usa parámetros SQL y listas permitidas para identificadores dinámicos. Mantén secretos fuera del repositorio y de respuestas y registros; usa una cuenta Oracle de privilegios mínimos, nunca SYS o SYSTEM. Registra eventos de seguridad y cambios financieros relevantes sin exponer credenciales ni datos financieros completos.

## Integridad financiera y Oracle

- Usa decimal en C# y NUMBER con precisión y escala explícitas en Oracle para importes y tasas; evita float y double para dinero. Define moneda, unidades, signo, redondeo y límites conforme a reglas existentes. No inventes tasas ni fórmulas financieras.
- Conserva consistencia entre cálculo, almacenamiento, pantalla y exportación. Comprueba valores extremos, períodos, fechas y configuración regional. Las proyecciones deben distinguir supuestos de movimientos reales.
- Ejecuta cambios financieros relacionados dentro de una transacción. Evita dobles registros por reenvío y actualizaciones perdidas mediante los controles de idempotencia o concurrencia necesarios para la operación, reutilizando mecanismos existentes.
- Mantén claves, unicidad, referencias y restricciones de negocio también en la base cuando corresponda. No confíes solo en validaciones del navegador.
- Diseña scripts Oracle compatibles con la versión y convenciones reales del proyecto. Antes de cambiar el esquema revisa datos existentes, compatibilidad, índices necesarios y recuperación; contempla los commits implícitos de DDL de Oracle y no prometas rollback transaccional de esos cambios.
- No uses una prueba con SQLite como demostración de compatibilidad Oracle. No declares un módulo Oracle terminado si su persistencia aún solo funciona con SQLite.
- Prueba con datos sintéticos y una base aislada. Cambiar producción, borrar datos o ejecutar una migración destructiva requiere autorización específica; una solicitud ordinaria de desarrollo no la concede.

## Pruebas y entrega

Usa primero los proyectos y herramientas de prueba existentes; no añadas un framework por conveniencia. Para lógica, persistencia, autenticación o autorización modificadas, añade o adapta pruebas automatizadas de comportamiento y regresión proporcionales al riesgo. Una edición visual menor no exige crear una batería nueva.

Selecciona los casos pertinentes al cambio: cálculos y redondeo con resultados esperados independientes; transacciones y concurrencia; registro, acceso y cierre de sesión; entradas inválidas, antiforgery y acceso cruzado entre usuarios. Verifica Oracle con pruebas de integración aisladas cuando el cambio dependa de su SQL o esquema. Los dobles de prueba no sustituyen esa comprobación.

Ejecuta compilación y pruebas relevantes con los comandos reales del proyecto. Si existe MVC, comprueba además el flujo afectado por HTTP o navegador según corresponda; las pruebas WPF no validan el portal web. No accedas a una base compartida ni crees datos de demostración como efecto incidental de verificar.

Entrega un resumen de qué cambió, qué código se reutilizó y qué pruebas se ejecutaron con sus resultados. Expón fallos, dependencias externas y comprobaciones pendientes sin presentarlos como éxitos. Justifica cualquier dependencia, abstracción o tabla añadida. No despliegues ni amplíes la tarea sin autorización.
