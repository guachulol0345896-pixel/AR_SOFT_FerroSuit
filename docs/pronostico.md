# Pronóstico de demanda en el programa (etapa 5): red neuronal ONNX integrada en C#

## 1. Flujo

```text
Pantalla "Pronósticos" (o botón en Inventario)
  └─ Puente: acción "pronosticos.reposicion"  (permiso pronosticos.ver: administrador y encargado de bodega)
      └─ Aplicación · PronosticoServicio
          1. IRepositorioInventario.ObtenerHistorialVentas  → SQL Server: ventas, devoluciones y cambio de stock por día
          2. SerieDemanda          → demanda diaria (ventas − devoluciones; agotados y días sin historia = faltantes)
             PreparadorCaracteristicas → 48 entradas + escala (mismo procedimiento que el entrenamiento)
          3. IPronosticadorDemanda → Infraestructura · PronosticadorOnnx → ONNX Runtime (local) → consumo diario
          4. días hasta agotarse = stock actual / consumo; se compara con el tiempo de entrega del proveedor
          5. recomendación: urgencia y cantidad (descontando lo ya pedido)
  └─ "Preparar orden": lleva producto, proveedor y cantidad al formulario de Órdenes. La orden solo se crea si el
     usuario pulsa "Generar orden" y queda "Pendiente de aprobación". Nada se aprueba ni se envía automáticamente.
```

La red **no depende de Gemini ni de ningún servicio externo**: el modelo `modelos/demanda_mlp.onnx` se ejecuta en el
equipo con `Microsoft.ML.OnnxRuntime`.

## 2. Componentes por capa

| Capa | Componente | Responsabilidad |
|---|---|---|
| Aplicación | `IA/Pronostico/EspecificacionModelo` | Ventana 28, horizonte 7, historia mínima 30, mínimo 14 días con dato, ε 0,05, categorías y orden de las 48 entradas |
| | `IA/Pronostico/CalendarioFestivos` | Festivos oficiales de Colombia para cualquier año (Ley Emiliani y Pascua) |
| | `IA/Pronostico/SerieDemanda` | Demanda diaria con el mismo criterio del lector de entrenamiento (`ml/ferrosuite_demanda/movimientos.py`) |
| | `IA/Pronostico/PreparadorCaracteristicas` | Las 48 entradas, igual que `caracteristicas_de_ventana` en Python |
| | `IA/Pronostico/PronosticoServicio` | Orquesta el flujo, aplica el respaldo y arma las recomendaciones |
| | `Puertos/IPronosticadorDemanda` | Puerto del modelo (estado y predicción por lotes) |
| Infraestructura | `IA/Prediccion/PronosticadorOnnx` | Carga y ejecuta el ONNX; verifica huella SHA-256, parámetros y grafo |
| | `Persistencia/RepositorioInventarioSql.ObtenerHistorialVentas` | Totales diarios por producto y primer movimiento (consultas parametrizadas) |
| Presentación | `Puente` (`pronosticos.reposicion`), `pronosticos.html` | Acción protegida por permiso y pantalla de recomendaciones |

## 3. Reglas de la recomendación

- **Día de corte**: ayer (último día completo). Pronóstico: hoy a hoy + 6.
- **Urgente**: sin stock, o se agota antes de que llegue un pedido (días ≤ entrega del proveedor).
- **Pedir ahora**: en el stock mínimo o por debajo, o se agota antes de entrega + 3 días.
- **Programar pedido** (solo con la red): se agota antes de entrega + 7 días.
- **Cantidad**: la regla existente de `CalculadoraReposicion`:
  `máx(consumo × (entrega + 15) + mínimo − stock, 2 × mínimo − stock)`. Se le resta lo que está en órdenes pendientes o
  aprobadas sin recibir y se redondea hacia arriba (a 0,1 en metros y kilos).
- Sin proveedor asignado se usan 3 días de entrega (como la regla existente) y no se puede preparar la orden.

### Respaldo: regla de stock mínimo

Se usa, **indicando el motivo en cada producto**, cuando:
- el producto tiene menos de 30 días de historia;
- tiene menos de 14 de los últimos 28 días con dato (agotado o sin registro);
- el modelo no está disponible (archivo ausente, dañado, de otra versión o ONNX Runtime no carga);
- el modelo falla al calcular (se registra en la auditoría).

En esos casos el consumo es lo vendido en 30 días ÷ 30. Se recomienda pedir solo con la regla existente: stock en el
mínimo o por debajo, o agotamiento antes de entrega + 3 días. El pronóstico es una consulta aparte, así que **una falla del
modelo nunca impide registrar movimientos, productos u órdenes** (probado en PU8).

## 4. Equivalencia con el entrenamiento

`ml/exportar_casos_prueba.py` (paso 7 de `ml/ejecutar_todo.py`) exporta `modelos/demanda_mlp_casos_prueba.json`:
ventanas con sus entradas y salida ONNX calculadas en Python, series de movimientos con su demanda diaria, los festivos
usados al entrenar y un recorrido completo. Las pruebas de C# los comparan.

Ajuste hecho en esta etapa: el lector de Python ahora también reconstruye el stock **hacia atrás desde el stock actual**
cuando el archivo trae `StockActual`, igual que C#. Así un producto migrado sin movimiento de inventario inicial no
aparece como agotado. El dataset sintético, el modelo y sus métricas no cambiaron: dos ejecuciones completas dieron las
mismas huellas SHA-256.

## 5. Resultados obtenidos

Entorno: Linux, .NET 10.0.115; Python 3.13 con ONNX Runtime 1.29.0. C# ejecutó el motor nativo real de ONNX Runtime
1.29.0 (ver 6).

| Prueba | Resultado medido |
|---|---|
| Entradas de C# frente a Python (35 casos: faltantes, ceros, decimales, festivos, Semana Santa, categoría desconocida) | **Diferencia máxima 0** (idénticas en float32); el caso con 13 días con dato no se pronostica en ninguno de los dos |
| Demanda diaria desde movimientos (4 casos, con agotados y devoluciones) | Idéntica |
| Festivos calculados en C# frente a los del entrenamiento (2023-2024) | 36 de 36 iguales; 2026 verificado aparte |
| ONNX Runtime en C# frente a ONNX Runtime en Python (35 casos) | **Diferencia máxima 0** unidades/día |
| Recorrido completo `SINT-001`, corte 2024-12-24 (Puente → aplicación → historial → entradas → ONNX → recomendación) | Red neuronal **3,61** por día (Python: 3,6096); regla de 30 días 3,9 (Python: 3,9); stock 40 → 11,1 días; entrega 10 días → "Pedir ahora", pedir 61 |
| Inferencia de 1 000 productos en un lote | 2,1–2,4 ms |
| Modelo dañado / de otra versión / ausente | No disponible, con motivo: "no coincide con su huella SHA-256", "no corresponde a esta versión del programa (ventana_dias: 21…)", "No se encontró demanda_mlp.onnx…"; el pronóstico responde con el respaldo |

Al cerrar la etapa 5 — pruebas en memoria: **132 de 132** (19 de pronóstico: PF1-PF5, PU1-PU8, PI1-PI6). Recorrido de pantallas: **28 de 28**,
incluidos el pronóstico con "Modelo neuronal activo", "Preparar orden" (llena el formulario sin crear nada) y la orden
generada en estado "Pendiente de aprobación". Captura: `docs/img/pronosticos_pantalla.png`.

**Pendiente de ejecutar en Windows**: la suite contra SQL Server (`-- --bd`), que ahora incluye F1-F3. Comprueba el
historial leído de SQL Server, el recorrido con la base real y el respaldo con poca historia.

## 6. Verificación en este entorno

NuGet y GitHub no están disponibles aquí. El proyecto referencia el paquete oficial `Microsoft.ML.OnnxRuntime` 1.29.0,
que en Windows trae su motor nativo. Para compilar y ejecutar las pruebas aquí se usó, solo durante la verificación:
- el **motor nativo real** `libonnxruntime.so` 1.29.0 del paquete npm `onnxruntime-node` 1.29.0;
- el envoltorio .NET oficial `Microsoft.ML.OnnxRuntime.dll` de una versión anterior, incluido en el paquete PyPI `nimbusml` 1.8.0.

El código usa solo funciones que existen en ambas versiones: `InferenceSession`, `SessionOptions`, `NamedOnnxValue`,
`DenseTensor` y `InputMetadata`. Debe confirmarse en Visual Studio que el paquete 1.29.0 se restaura y compila sin
cambios.

## 7. Límites

- El modelo fue entrenado con datos **sintéticos**: la recomendación es una ayuda para validar la integración, no un
  sustituto del criterio de quien compra. Debe reentrenarse con el historial real (`ml/README.md`).
- Pronostica el promedio de 7 días; no conoce precios, promociones ni obras grandes puntuales.
- La demo de FerroSuite (`datos_demo.sql`) solo tiene 30 días de ventas: alcanza justo la historia mínima.
