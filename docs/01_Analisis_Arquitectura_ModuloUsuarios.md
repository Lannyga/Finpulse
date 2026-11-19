# FINPULSE — Documento de análisis, diseño y entrega 01

**Título:** Sistema de Gestión y Proyección Financiera Estudiantil  
**Plataforma:** Windows (WPF) | **Lenguaje:** C# (.NET 10) | **Base de datos:** Oracle AI Database Free 26ai  
**Versión documental:** 1.0 | **Fecha:** 15 de noviembre de 2026  
**Estado:** diseño general aprobado para implementación; módulo 1 preparado para prueba de integración.

## 1. Planteamiento del problema
Los estudiantes suelen administrar recursos limitados provenientes de becas, mesadas, trabajo eventual u otras fuentes. El registro fragmentado de gastos, la falta de seguimiento presupuestario y la ausencia de proyecciones dificultan establecer metas realistas de ahorro. Una herramienta específica debe permitir conocer la situación actual y simular posibles resultados futuros sin confundir estimaciones con garantías.

## 2. Objetivo general
Desarrollar un sistema de escritorio que permita a estudiantes gestionar sus ingresos, gastos, presupuestos y metas de ahorro mediante herramientas de análisis y proyección financiera, para apoyar su planificación económica y la toma informada de decisiones.

### Objetivos específicos
1. Diseñar e implementar una base de datos relacional Oracle con integridad y trazabilidad por usuario.
2. Construir interfaces WPF accesibles para registrar y consultar información financiera.
3. Implementar operaciones de ingresos, gastos, categorías, presupuestos y metas.
4. Diseñar un motor matemático de simulación por horizontes de 1, 3, 5 y 10 años.
5. Proporcionar comparaciones de escenarios y reportes interpretables.
6. Verificar integridad de datos, seguridad básica y exactitud de cálculos con pruebas documentadas.

## 3. Alcance y exclusiones
**Público objetivo:** estudiantes universitarios, con atención a economía personal y gastos cotidianos.  
**Moneda base:** bolivianos (BOB/Bs) en primera versión.  
**V1:** usuarios, ingresos, gastos, categorías, presupuestos, metas, registro de aportes, resumen mensual y simulador determinista.  
**V2:** análisis del historial, escenarios probabilísticos y alertas avanzadas, después de disponer de datos suficientes.  
**Fuera de alcance inicial:** conexión bancaria, pagos reales, transferencias, inversiones ejecutadas, asesoramiento financiero automático personalizado, tasas de mercado en tiempo real y aplicación móvil.

## 4. Requisitos funcionales
| ID | Requisito | Prioridad | Etapa |
|---|---|---|---|
| RF-01 | Registrar una cuenta con nombre, correo y contraseña | Alta | 1 |
| RF-02 | Iniciar sesión y cerrar sesión | Alta | 1 |
| RF-03 | Restringir temporalmente el acceso ante intentos fallidos repetidos | Alta | 1 |
| RF-04 | Registrar, editar, consultar y anular ingresos | Alta | 2 |
| RF-05 | Registrar y clasificar gastos | Alta | 2 |
| RF-06 | Definir presupuesto mensual y límites por categoría | Alta | 3 |
| RF-07 | Crear y seguir metas de ahorro | Alta | 3 |
| RF-08 | Proyectar acumulación nominal de ahorro | Alta | 4 |
| RF-09 | Comparar escenarios de ingresos, gastos, inflación y rendimiento | Media | 4 |
| RF-10 | Consultar indicadores, gráficos e informes exportables | Media | 5 |

## 5. Requisitos no funcionales
| ID | Condición verificable |
|---|---|
| RNF-01 | Windows x64 y .NET 10 instalado para desarrollo/ejecución dependiente del framework. |
| RNF-02 | Persistencia en Oracle con claves, restricciones e integridad referencial. |
| RNF-03 | Contraseñas con función de derivación lenta, sal aleatoria y comparación de tiempo constante. |
| RNF-04 | Todas las consultas con entradas del usuario usan parámetros SQL. |
| RNF-05 | Cálculos monetarios basados en decimal/NUMBER, no en float para importes. |
| RNF-06 | La UI no presenta proyecciones inciertas como resultados garantizados. |
| RNF-07 | Separación entre interfaz, aplicación, dominio e infraestructura. |
| RNF-08 | La aplicación muestra errores generales de conexión, sin exponer cadenas o secretos. |

## 6. Arquitectura y dependencias
```text
Finpulse.Desktop (WPF) ---> Finpulse.Application <--- Finpulse.Infrastructure (ODP.NET)
          |                         |                           |
          +--------------------> Finpulse.Domain <--------------+
                                                            |
                                                       Oracle FREEPDB1
```
**Presentación:** controles y navegación WPF. Los eventos de vistas únicamente coordinan casos de uso; no contienen SQL.  
**Aplicación:** registro, acceso y validación de datos; contratos de persistencia.  
**Dominio:** identidad mínima del usuario, en siguientes etapas entidades y reglas financieras.  
**Infraestructura:** acceso a Oracle, transacciones y derivación criptográfica de contraseñas.

**Decisión técnica:** utilizar `Oracle.ManagedDataAccess.Core` con acceso parametrizado para el módulo 1, evitando infraestructura adicional en el primer incremento. En iteraciones posteriores se evaluará introducir MVVM completo, inyección de dependencias y pruebas automatizadas adicionales.

## 7. Modelo conceptual del sistema completo
Entidades planificadas: USUARIOS (1:N) INGRESOS, GASTOS, PRESUPUESTOS, METAS_AHORRO, ESCENARIOS; CATEGORIAS (1:N) INGRESOS/GASTOS/PRESUPUESTOS; METAS_AHORRO (1:N) APORTES_AHORRO; ESCENARIOS (1:N) RESULTADOS_PROYECCION.  
**Importante:** estas relaciones son diseño conceptual; **solo FP_USUARIOS existe en los scripts de esta entrega**. Los esquemas posteriores se diseñarán antes de implementarse.

## 8. Diccionario de datos: FP_USUARIOS (implementado en script)
| Campo | Tipo Oracle | Propósito |
|---|---|---|
| USUARIO_ID | NUMBER(19) IDENTITY PK | Identificador único. |
| NOMBRE_COMPLETO | VARCHAR2(120 CHAR) | Nombre visible del estudiante. |
| CORREO | VARCHAR2(254 CHAR), UNIQUE | Correo normalizado en minúsculas. |
| CLAVE_HASH | VARCHAR2(300 CHAR) | Derivación PBKDF2-SHA256 con sal; nunca texto plano. |
| ACTIVO | NUMBER(1) | 1 habilitado, 0 deshabilitado. |
| INTENTOS_FALLIDOS | NUMBER(3) | Conteo de accesos fallidos. |
| BLOQUEADO_HASTA_UTC | TIMESTAMP(6) NULL | Fin del bloqueo temporal, expresado en UTC. |
| FECHA_REGISTRO_UTC | TIMESTAMP(6) | Creación de cuenta. |
| ULTIMO_ACCESO_UTC | TIMESTAMP(6) NULL | Último acceso válido. |

## 9. Casos de uso del módulo 1
**CU-01 Registrar estudiante.** Actor: estudiante. Precondición: Oracle disponible. Flujo: acceder a registro → introducir nombre, correo, contraseña y confirmación → validar reglas → crear hash → intentar insertar registro → informar resultado. Alternativas: campos inválidos, correo repetido, conexión no disponible. Postcondición positiva: nueva cuenta en FP_USUARIOS.

**CU-02 Iniciar sesión.** Actor: estudiante registrado. Flujo: introducir correo y contraseña → leer registro con bloqueo de fila transaccional → comprobar estado y bloqueo → verificar hash → actualizar fecha de acceso e intentos → mostrar bienvenida. Alternativas: datos incorrectos o restricción temporal; tras cinco fallos consecutivos se bloquea 15 minutos.

**CU-03 Cerrar sesión.** Actor: usuario autenticado. Flujo: solicitar cierre → limpiar controles de contraseña → regresar a inicio. En V1 la sesión solo vive en memoria; no hay token persistente.

## 10. Motor de proyección (diseño, no implementado)
Modelo mínimo sin rendimiento: `saldo_n = saldo_0 + aporte_mensual * n`.  
Modelo con tasa mensual constante `r`: `saldo_n = saldo_0 * (1+r)^n + aporte_mensual * (((1+r)^n - 1)/r)` si `r != 0`. Si `r = 0`, usar la fórmula lineal.  
Ajuste estimado por inflación mensual `i`: `valor_real_n = saldo_nominal_n / (1+i)^n` (referido a poder adquisitivo del período base).  
Se permitirá fijar condiciones de escenario y comparar resultados sin usar la etiqueta de 'predicción estadística' hasta entrenar/validar modelos reales. Las tasas serán supuestos explícitos y editables.

## 11. Pruebas de aceptación previstas
| ID | Prueba | Resultado esperado |
|---|---|---|
| T-01 | Correo nuevo, nombre válido y clave de al menos 12 caracteres | Se crea 1 fila y se confirma registro. |
| T-02 | Correo repetido con diferencias de mayúsculas | Se rechaza sin crear una segunda cuenta. |
| T-03 | Contraseña que no coincide con confirmación | No se inserta cuenta. |
| T-04 | Acceso correcto | Se muestra usuario y actualiza último acceso. |
| T-05 | Acceso con contraseña incorrecta 5 veces | Bloqueo temporal aproximado de 15 min. |
| T-06 | Intento correcto antes de expirar bloqueo | Continúa restringido. |
| T-07 | Expirado el bloqueo, acceso válido | Se permite; se reinician intentos. |
| T-08 | Consulta de FP_USUARIOS | CLAVE_HASH no coincide con contraseña clara. |
| T-09 | Oracle detenido | Error amigable, sin secretos. |
| T-10 | Cierre de sesión | Regresa al formulario de ingreso. |

**Estado de verificación (17/11/2026):** compilación Windows correcta, 26 comprobaciones Smoke y 29 de integración con Oracle real superadas. Los casos de persistencia y autenticación tienen evidencia automatizada; la interfaz, el cierre de sesión visual y el mensaje ante caída de Oracle siguen pendientes. Detalles y límites en [validación actual](03_Validacion_Actual.md).

## 12. Cronograma incremental sugerido
1. Etapa 1 (actual): arquitectura, documentación y usuarios.
2. Etapa 2: movimientos, categorías, balances y consultas.
3. Etapa 3: presupuestos y metas de ahorro.
4. Etapa 4: fórmulas de proyección, escenarios y visualizaciones.
5. Etapa 5: reportes, auditoría, pruebas integradas y manuales finales.

## 13. Riesgos y medidas
**Seguridad:** la conexión directa con usuario compartido Oracle es aceptable para laboratorio local; **no** para una distribución multiusuario. Al distribuir se debe añadir capa intermedia de servicios, identidad individual, control de acceso, secretos protegidos y copias de seguridad.  
**Predicción:** sin suficientes datos personales no corresponde prometer pronósticos aprendidos. Se priorizan simulaciones transparentes.  
**Inflación/tasas:** se registrará siempre la fuente o el supuesto elegido por el usuario.  
**Fiabilidad:** las validaciones y pruebas con Oracle real son obligatorias antes de declarar que el módulo funciona.

## 14. Referencias técnicas
- Microsoft Learn, tutorial oficial WPF: https://learn.microsoft.com/en-us/visualstudio/get-started/csharp/tutorial-wpf?view=vs-2022
- Oracle, Oracle AI Database Free: https://www.oracle.com/database/free/get-started/
- Oracle, ODP.NET: https://www.nuget.org/packages/Oracle.ManagedDataAccess.Core
- Oracle, conexión de Oracle Free en Windows: https://docs.oracle.com/en/database/oracle/oracle-database/26/xeinw/connecting-oracle-database-xe.html
- OWASP, Password Storage Cheat Sheet: https://cheatsheetseries.owasp.org/cheatsheets/Password_Storage_Cheat_Sheet.html
