# Pantalla de Inteligencia Artificial (Etapa 6)

Pantalla `ia.html` («Inteligencia artificial» en el menú) para el **encargado de bodega** y el **administrador**.
Muestra, para un producto, su historial de demanda y lo que la red neuronal espera vender en los próximos 7 días, y
traduce eso en riesgo de desabastecimiento y en una recomendación de reposición. Usa el sistema de presentación
existente (Windows Forms + WebView2) y el servicio neuronal local de la Etapa 5 (`PronosticoServicio` + `PronosticadorOnnx`).

**Ningún valor se calcula en JavaScript.** El historial, el pronóstico, los días hasta el agotamiento, el riesgo, la
cantidad sugerida y la fuente (red neuronal o regla de respaldo) llegan del backend C#, que ejecuta el modelo ONNX.
La pantalla solo los dibuja y les da formato.

## 1. Qué muestra

| Elemento | De dónde sale |
|---|---|
| Búsqueda / selección de producto | `pronosticos.reposicion` (lista ordenada por riesgo); también se abre con `ia.html?sku=…` |
| Historial de demanda (90 días) | `SerieDemanda` sobre los movimientos: ventas − devoluciones por día; días agotados = demanda **no observada** (no cero); días antes del primer movimiento = sin historial |
| Gráfica histórica y pronosticada | Componente `js/components/graficaDemanda.js` (SVG, sin librerías). Barras = ventas diarias; bandas = agotado / sin historial; zona sombreada = ventana de 28 días que vio el modelo; línea discontinua = pronóstico; línea punteada = regla actual (vendido en 30 días ÷ 30) |
| Inventario actual y mínimo | Producto |
| Demanda futura estimada | Consumo diario de la red (promedio de los próximos 7 días) × 7 |
| Días hasta agotarse | stock ÷ consumo; **«No aplica»** cuando el consumo esperado es 0 (el cálculo no es válido) |
| Tiempo de entrega del proveedor | Proveedor del producto (3 días por defecto si no tiene, y se indica) |
| Riesgo de desabastecimiento | Urgente → **Alto**, Pedir ahora → **Medio**, Programar pedido → **Bajo**, Sin acción → **Muy bajo**, con una frase que explica por qué |
| Recomendación de reposición | La misma de la Etapa 5 (descuenta lo ya pedido) |
| Fuente del resultado | Etiqueta «Red neuronal» o «Regla de respaldo (stock mínimo)» y, si es respaldo, el motivo (historial insuficiente, modelo no disponible, modelo falló) |
| Borrador de orden de compra | Confirmación con cantidad editable → `ordenes.crear` → orden **Pendiente de aprobación** (no se aprueba ni se envía) |

Estados: vacío («elige un producto»), **cargando** («Consultando el modelo neuronal para SKU…»), error con «Reintentar»,
y aviso general cuando el modelo no está disponible (el inventario sigue funcionando).

## 2. Backend

- `PronosticoServicio.PronosticarProducto(sesion, sku, diasHistorial = 90)` → `DetallePronostico`
  (recomendación, historial diario, ventana del modelo, 7 días de pronóstico con stock proyectado, resumen del historial,
  estado del modelo). Exige `pronosticos.ver`; SKU vacío, inexistente o inactivo → `ErrorValidacion`.
- La lista y el detalle comparten el mismo cálculo (`Calcular`): el detalle de un producto da **exactamente** la misma
  recomendación que su fila en la tabla (prueba PD1). La serie de 90 días coincide con la de 30 en los días comunes.
- `RecomendacionReposicion` suma `RiesgoDesabastecimiento` y `RiesgoDescripcion`.
- Puente: acción `pronosticos.producto` con el permiso `pronosticos.ver` (ADMIN y BODEGA), denegada por defecto al resto.
- No hubo cambios de base de datos: el «borrador» es la orden en el estado existente «Pendiente de aprobación».

## 3. Frontend

| Archivo | Cambio |
|---|---|
| `ia.html` | Nueva página |
| `js/controllers/ia.js` | Controlador: lista y búsqueda, estados, detalle, borrador. Descarta respuestas de una selección anterior (contador de peticiones) y comprueba que la respuesta corresponde al SKU elegido |
| `js/components/graficaDemanda.js` | Gráfica SVG que solo dibuja datos del backend |
| `js/services/pronosticosService.js` | `producto(sku)` y `generarBorrador(...)` (reutiliza `ordenesService.crear`) |
| `js/components/encabezado.js` | Menú «Inteligencia artificial» (permiso `pronosticos.ver`) |
| `js/controllers/pronosticos.js`, `inventario.html` | Enlaces a la nueva pantalla |
| `css/components.css` | Estilos de la pantalla y de la gráfica |

## 4. Pruebas (resultados reales)

| Prueba | Resultado |
|---|---|
| En memoria (`dotnet run --project tests/FerroSuite.Pruebas`) | **142 de 142** al cerrar la etapa 6 (hoy 143: la revisión integral agregó G6); 10 nuevas (PD1-PD10) |
| PD1 | Producto con historial: red neuronal 3,61/día (Python 3,6096) e igual a la fila de la lista |
| PD2-PD3 | Historial = movimientos (90 días, ventana 28, días previos sin historial); 7 días de pronóstico coherentes |
| PD4 | Producto **sin historial**: respaldo con motivo «no tiene movimientos», días «no aplica», riesgo Alto |
| PD5-PD10 | SKU inválido, modelo que falla, modelo no disponible, días agotados como no observados, riesgo coherente, permisos |
| SQL Server (`-- --bd`) | F4 nueva (detalle = lista con SQL Server). **No ejecutada** aquí (sin SQL Server) |
| Navegador `tests/pantallas/pantalla_ia.js` | **17 de 17** con el modelo ONNX real; **3 de 3** con `--sin-modelo` |
| Recorrido general `recorrido.js` | **28 de 28** (sin regresiones) |

El recorrido en navegador compara lo que muestra la pantalla con lo que responde el backend para el mismo producto
(nombre, stock, consumo, días, entrega, riesgo, número de barras = días con dato), y prueba: producto con datos
(SIN-001), con días agotados (SIN-002), **sin historial** (SIN-007), con historial corto y sin proveedor (SIN-008),
búsqueda, estado de carga (respuesta retrasada 1,5 s), cambio rápido de producto (la respuesta tardía se descarta),
respuesta de error del backend y reintento, creación del borrador (orden pendiente con la cantidad confirmada y
recálculo de lo pedido), administrador con `?sku=`, vendedor sin acceso (menú, pantalla y mensaje directo al backend).

## 5. Límites

- Las capturas y el recorrido se hicieron en **Chromium** sobre el mismo frontend y el mismo backend C# (herramienta
  `servidor`, que reemplaza a WebView2 y guarda en memoria), no en la ventana de Windows. Falta mirarla en WebView2.
- Los datos son **sintéticos** y el modelo está entrenado con datos sintéticos: las cifras no son evidencia de demanda real.
- El pronóstico es un promedio diario para los 7 días (así se entrenó el modelo); por eso la línea del horizonte es plana.
