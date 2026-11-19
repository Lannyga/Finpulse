# FINPULSE · Bosque

Portal financiero estudiantil ASP.NET Core MVC / Razor, Bootstrap 5 y Chart.js.

La búsqueda de ui-ux-pro-max confirmó Bento Box Grid (styles.csv), con cuadrícula modular, tarjetas de distintos tamaños y adaptación a una columna. El resultado automático financiero proponía una landing comercial y azul/dorado; se descartó esa parte por no corresponder al portal ni a la paleta solicitada.

## Identidad

- Fondo pino `#0b1713`, superficie `#12221c`, superficie elevada `#192c23`.
- Verde bosque `#214b36`, acento menta `#bde89b`, texto `#f0f4ee`, secundario salvia `#a6b8ad`.
- Borde `#30463a`; controles con borde más claro. Errores salmón `#ffb5a8` y mensajes escritos.
- Tipografía local Segoe UI / Arial: evita una descarga y mantiene métricas estables. Números tabulares.
- Marca vectorial propia «Crecimiento con raíces»: cuatro columnas ascendentes que evocan estudiantes, en verdes bosque, musgo y menta; la última germina en una hoja. Una flecha trazada como pulso conecta los avances. Parte del concepto de barras y flecha aportado como referencia, con proporciones, trazado y paleta propios. Marca sin fondo para el portal oscuro; favicon simplificado sobre pino para conservar legibilidad a 16–32 px. Sin dependencias gráficas ni imágenes remotas.

## Composición

Sidebar de 248 px en escritorio; navegación horizontal en pantallas pequeñas. Dashboard de cuatro columnas: saldo de dos columnas, dos indicadores, gráfico de tres columnas y tarjeta de ahorro; historial y recomendaciones debajo.

Tarjetas con radio 22 px, separación 20 px, relleno 28 px. En móvil: dos indicadores compactos; el resto a ancho completo. Formularios y listados con una sola columna por debajo de 900 px.

## Interacción y accesibilidad

Un botón principal por formulario, etiquetas visibles, validación de servidor, resumen con foco, confirmación antes de eliminar, estado guardando. Objetivos de interacción de al menos 44 px; foco de 3 px. Contraste del texto verificado matemáticamente. Gráficos con leyenda y patrones de línea; tablas y CSV dan acceso a los valores. Respeta reduced-motion; no cambia el orden visual respecto del DOM.

No se empleó una guía de stack ajeno: Razor/Bootstrap no figura entre los stacks del buscador. Se aplicaron sus reglas web independientes del framework.
