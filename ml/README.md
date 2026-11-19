# Red neuronal de pronóstico de demanda (PyTorch → ONNX)

Red MLP pequeña que estima el **consumo diario promedio de los próximos 7 días** de un producto a partir de sus salidas
de inventario por venta de los últimos 28 días y del calendario. Se entrena con PyTorch y se exporta a ONNX para
ejecutarla desde C#, sin Gemini ni servicios externos.

> **Los datos de entrenamiento actuales son SINTÉTICOS** (no hay historial real suficiente). Son reproducibles, tienen
> semilla fija y sus patrones están documentados en [datos/FICHA_DATOS_SINTETICOS.md](datos/FICHA_DATOS_SINTETICOS.md).
> Solo permiten validar la integración académica; las métricas no demuestran desempeño con datos reales.

## Ejecución

Requisitos: Python 3.12 o 3.13 (probado con 3.13; `numpy` 2.5.3 no existe para 3.11).

```bash
python -m venv .venv
.venv\Scripts\activate            # Windows   (Linux/macOS: source .venv/bin/activate)
pip install -r ml/requirements.txt
python ml/ejecutar_todo.py        # todo el flujo (menos de un minuto en CPU)
```

O paso a paso:

| Paso | Script | Qué hace | Salidas |
|---|---|---|---|
| 1 | `generar_datos_sinteticos.py` | Dataset sintético (semilla 20261117) | `datos/demanda_sintetica.csv`, `datos/resumen_datos_sinteticos.json` |
| 2 | `preparar_datos.py` | Ventanas, faltantes, separación cronológica | `datos/muestras.npz`, `resultados/preparacion.json` |
| 3 | `entrenar.py` | Entrena con parada temprana sobre validación | `resultados/modelo_pytorch.pt`, `historial_entrenamiento.csv`, `perdida_entrenamiento.png`, `entrenamiento.json` |
| 3a (opcional) | `buscar_hiperparametros.py` | 24 combinaciones, elegidas **solo** con validación | `resultados/busqueda_hiperparametros.json` |
| 4 | `evaluar.py` | MAE, RMSE y WAPE en prueba frente a métodos base; gráficas | `resultados/metricas.json`, `metricas_por_categoria_prueba.csv`, `predicciones_prueba.csv`, 3 gráficas |
| 5 | `exportar_onnx.py` | Exporta a ONNX y compara ONNX Runtime con PyTorch | `../modelos/demanda_mlp.onnx`, `../modelos/demanda_mlp_normalizacion.json`, `resultados/verificacion_onnx.json` |
| 7 | `exportar_casos_prueba.py` | Casos de referencia para comprobar que C# prepara las entradas y ejecuta el modelo igual | `../modelos/demanda_mlp_casos_prueba.json` |
| 6 | `verificar_preparacion.py` | Comprueba ausencia de fugas, normalización, faltantes, reproducibilidad y el lector de movimientos | `resultados/verificacion_preparacion.json` |

`ejecutar_todo.py` además guarda `resultados/reproducibilidad.json` con las huellas SHA-256 del dataset, los pesos, las
predicciones y las métricas: dos ejecuciones en el mismo entorno dieron exactamente las mismas huellas.

### Con datos reales de FerroSuite

1. Exportar los movimientos (SQL Server Management Studio o `sqlcmd`) a CSV:
   ```sql
   SELECT m.Fecha, m.Sku, c.Nombre AS Categoria, p.Unidad, m.Tipo, m.Cantidad, m.Motivo, p.StockActual
   FROM dbo.MovimientoInventario m
   JOIN dbo.Producto p ON p.Sku = m.Sku
   JOIN dbo.CategoriaProducto c ON c.CategoriaId = p.CategoriaId
   ORDER BY m.Fecha;
   ```
2. `python ml/preparar_datos.py --movimientos movimientos.csv` y luego los pasos 3 a 5.
3. Demanda = ventas menos devoluciones de clientes; ajustes, daños, compras e inventario inicial no cuentan. Los días con
   stock al cierre en cero y sin ventas se tratan como **agotados** (faltantes); el stock se reconstruye hacia atrás desde
   `StockActual`, igual que en el programa. Ese archivo puede contener información
   del negocio: **no lo subas al repositorio**.

## Diseño

**Muestras.** Cada muestra es un producto en un día de cierre `t`. Entradas: las ventas de `t-27 … t` divididas por la
escala del producto (promedio de la ventana + 0,05), resúmenes de la ventana (promedios de 7 y 14 días, desviación,
máximo, proporción de ceros y de faltantes, pendiente), el logaritmo de la escala, el calendario (día de la semana, mes y
festivos de los días a pronosticar, que se conocen de antemano), la categoría y si la unidad admite decimales: 48
entradas en total. Objetivo: el promedio de ventas de `t+1 … t+7` dividido por la misma escala; la salida se multiplica
de nuevo por la escala. Así un mismo modelo sirve para productos que venden 60 tornillos y 0,5 galones por día.

**Faltantes.** En la ventana se imputan con la mediana de los días con dato de esa misma ventana y la proporción de
faltantes es una entrada más; con menos de 14 días con dato la muestra se descarta. En el objetivo se promedian los días
observados; con menos de 4 de 7 se descarta.

**Sin fugas del futuro.**
- Las entradas solo usan días hasta `t`. La prueba automática cambia todas las ventas posteriores a una fecha y comprueba
  que las entradas anteriores no cambian.
- La normalización usa solo entrenamiento.
- La separación es cronológica: prueba son los últimos 90 orígenes y validación los 90 anteriores. Hay 7 días de margen
  entre conjuntos para que ningún objetivo cruce al siguiente.
- La prueba no se usa para entrenar, elegir la época ni elegir hiperparámetros.

**Red.** MLP 48 → 64 → 32 → 1 con ReLU y dropout 0,1 (5 249 parámetros); AdamW, pérdida Huber, lotes de 256, parada
temprana (paciencia 25). La búsqueda de hiperparámetros sobre validación dio diferencias de MAE menores al 2 % entre las
24 combinaciones. La mejor (128-64, decaimiento 0,01) solo mejora 0,2 % a la elegida, así que se conserva la red pequeña.
El modelo exportado incluye la normalización y un `max(0, ·)` para que nunca pronostique consumo negativo.

**Métodos base.**
- ingenuo: la venta del último día;
- promedio móvil de 7 días;
- promedio móvil de 28 días con faltantes imputados;
- la **regla actual de FerroSuite** (`CalculadoraReposicion`): vendido en 30 días / 30, que cuenta los faltantes como cero.

## Resultados obtenidos (datos SINTÉTICOS)

Prueba: 3 418 muestras, orígenes del 2024-09-26 al 2024-12-24.

| Método | MAE | RMSE | WAPE |
|---|---:|---:|---:|
| **Red MLP** | **2,427** | **6,300** | **18,4 %** |
| Promedio móvil 28 días | 2,498 | 6,383 | 19,0 % |
| Regla actual FerroSuite (30 días) | 2,674 | 6,797 | 20,3 % |
| Promedio móvil 7 días | 3,229 | 8,536 | 24,5 % |
| Ingenuo | 6,792 | 19,543 | 51,6 % |
| Media verdadera del generador (límite por ruido) | 2,210 | 5,595 | 16,8 % |

- La red mejora la regla actual en 9,2 % de MAE y 7,3 % de RMSE, y al mejor método base (promedio de 28 días) en 2,9 % y
  1,3 %. La mejora es pequeña: con estos datos el ruido diario domina y ni la media verdadera baja de 2,21 de MAE.
- Por categoría (MAE de prueba), la red es la mejor o empata en construcción, eléctricos, plomería y tornillería. En
  herramientas y pinturas, que venden menos de una unidad por día, el promedio de 28 días o la regla actual son iguales o
  ligeramente mejores (`resultados/metricas_por_categoria_prueba.csv`).
- Curva de aprendizaje: la validación mejora hasta la época 15 y luego la red empieza a sobreajustar. La parada temprana
  conserva la época 15 (`resultados/perdida_entrenamiento.png`).
- ONNX frente a PyTorch: diferencia máxima de 7,6·10⁻⁶ en 3 418 muestras.

Gráficas en `resultados/`:
- `perdida_entrenamiento.png`
- `real_vs_predicho_series.png` (un producto por categoría en el periodo de prueba)
- `dispersion_real_vs_predicho.png`
- `comparacion_metodos.png`

## Entradas, salidas, límites y condiciones de uso

Ver la ficha del modelo en [../modelos/README.md](../modelos/README.md): especificación exacta de las 48 entradas (lo
que debe replicar C#), forma de la salida, condiciones mínimas de historia y límites.

## Estructura

```text
ml/
├── requirements.txt
├── ejecutar_todo.py, generar_datos_sinteticos.py, preparar_datos.py, entrenar.py,
│   buscar_hiperparametros.py, evaluar.py, exportar_onnx.py, verificar_preparacion.py
├── ferrosuite_demanda/        config, sintetico, movimientos (lector de FerroSuite), caracteristicas,
│                              modelo, entrenamiento, metricas
├── datos/                     dataset sintético + ficha (muestras.npz se regenera)
└── resultados/                métricas, historial, gráficas, verificaciones y pesos de PyTorch
```
