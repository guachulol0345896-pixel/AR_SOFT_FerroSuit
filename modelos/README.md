# Modelos neuronales exportados

El proyecto de escritorio copia automáticamente `modelos/*.onnx` a `<salida>\modelos\`. Todo modelo va con su ficha.

| Archivo | Contenido |
|---|---|
| `demanda_mlp.onnx` | Red MLP de pronóstico de demanda (con la normalización incluida). SHA-256 en `demanda_mlp_normalizacion.json` |
| `demanda_mlp_casos_prueba.json` | Casos de referencia (Python) para las pruebas de equivalencia de C#; no se copia al ejecutable |
| `demanda_mlp_normalizacion.json` | Orden y nombre de las 48 entradas, medias y desviaciones de normalización, parámetros de ventana, métricas de prueba y verificación ONNX |

# Ficha del modelo `demanda_mlp` v1.0.0

> **Entrenado con datos SINTÉTICOS** (`ml/datos/FICHA_DATOS_SINTETICOS.md`). Sirve para validar la integración académica
> (Python → ONNX → C#). **No** debe usarse para decidir compras reales hasta reentrenarlo y evaluarlo con datos reales.

## Qué hace

Estima el **consumo diario promedio de los próximos 7 días** de un producto (en sus propias unidades: unidades, metros,
kilos, galones…) a partir de sus ventas de los últimos 28 días y del calendario. Es el dato que `CalculadoraReposicion`
usa como "consumo diario" para calcular cuántos días alcanza el stock.

## Entradas y salida (ONNX, opset 18)

| Nombre | Tipo y forma | Contenido |
|---|---|---|
| `caracteristicas` | float32 `[N, 48]` | Las 48 entradas **sin normalizar**, en el orden de `caracteristicas` del JSON (el modelo normaliza por dentro) |
| `escala` | float32 `[N, 1]` | Promedio de la ventana de 28 días (con faltantes imputados) + 0,05 |
| `consumo_diario` (salida) | float32 `[N, 1]` | Consumo diario promedio esperado de los días t+1 … t+7. Nunca negativo |

`N` es libre (de 1 en adelante).

## Cómo se calculan las 48 entradas (lo que debe replicar C#)

Para un producto y un día de cierre `t`, con `v` = ventas diarias de los días `t-27 … t` (ventas por "Venta" menos
"Devolución"; un día sin ventas es 0; un día agotado o sin registro es faltante):

1. Si hay menos de 14 días con dato en `v` → **no se pronostica** (usar la regla actual).
2. Faltantes de `v` ← mediana de los días con dato de esa misma ventana.
3. `escala = promedio(v) + 0,05`; `rel = v / escala`.
4. Entradas, en este orden:
   - `lag_27_rel` … `lag_00_rel` (28): `rel`, del día más antiguo al día `t`;
   - `media7_rel`, `media14_rel`: promedio de los últimos 7 y 14 valores de `rel`;
   - `desv28_rel`: desviación estándar poblacional de `rel`; `max28_rel`: máximo de `rel`;
   - `prop_ceros`: proporción de días con venta 0 (después de imputar); `prop_faltantes`: proporción de faltantes originales;
   - `pendiente_rel`: pendiente de la recta de mínimos cuadrados de `rel` contra 0…27, multiplicada por 28;
   - `log_escala`: ln(1 + escala);
   - `dow_sen`, `dow_cos`: sen y cos de 2π·d/7, con d = día de la semana de `t` (lunes = 0);
   - `mes_sen`, `mes_cos`: sen y cos de 2π·(m−1)/12, con m = mes del día `t+4`;
   - `festivos_horizonte`: festivos entre `t+1` y `t+7`, dividido por 7;
   - `cat_Construcción`, `cat_Plomería`, `cat_Eléctricos`, `cat_Tornillería`, `cat_Pinturas`, `cat_Herramientas`: 1 o 0;
   - `unidad_continua`: 1 si la unidad es Metro o Kilogramo.

La implementación de referencia es `ml/ferrosuite_demanda/caracteristicas.py` (`caracteristicas_de_ventana`).

## Entrenamiento y resultados (valores obtenidos al ejecutar `python ml/ejecutar_todo.py`)

- Red: 48 → 64 → 32 → 1, ReLU, dropout 0,1; 5 249 parámetros. AdamW (tasa 0,001, decaimiento 0,0001), pérdida Huber,
  lotes de 256, parada temprana con paciencia 25: mejor época 15 de 40.
- Datos: 26 172 muestras; entrenamiento 18 856 (orígenes hasta 2024-06-15), validación 3 428 (2024-06-22 a 2024-09-19),
  prueba 3 418 (2024-09-26 a 2024-12-24), con 7 días de margen entre conjuntos.

Conjunto de **prueba** (no usado para entrenar ni para elegir la época), en unidades de cada producto:

| Método | MAE | RMSE | WAPE |
|---|---:|---:|---:|
| **Red MLP** | **2,427** | **6,300** | **18,4 %** |
| Promedio móvil 28 días (faltantes imputados) | 2,498 | 6,383 | 19,0 % |
| Regla actual de FerroSuite (vendido en 30 días / 30) | 2,674 | 6,797 | 20,3 % |
| Promedio móvil 7 días | 3,229 | 8,536 | 24,5 % |
| Ingenuo (venta del último día) | 6,792 | 19,543 | 51,6 % |
| Referencia: media verdadera del generador (ruido irreducible) | 2,210 | 5,595 | 16,8 % |

Frente a la regla actual: −9,2 % de MAE y −7,3 % de RMSE. Frente al mejor método base (promedio de 28 días): −2,9 % de
MAE y −1,3 % de RMSE: la mejora es **pequeña**. La referencia de la media verdadera muestra que el margen máximo posible
con estos datos es también limitado (el ruido diario es alto). El MAE y el RMSE globales mezclan unidades; por eso también
se informa el WAPE y, en `ml/resultados/`, el error por categoría y el MAE escalado por producto.

Verificación ONNX: en las 3 418 muestras de prueba, diferencia máxima con PyTorch de 7,6·10⁻⁶ unidades/día
(relativa 2,0·10⁻⁷); tolerancia 10⁻⁴.

## Límites y condiciones de uso

- Necesita al menos 30 días de historia del producto y 14 días con dato en la ventana; si no, usar la regla actual.
- Pronostica el promedio de 7 días, no el día a día ni picos puntuales (obras grandes, promociones).
- No conoce precios, promociones, inflación ni inventario disponible; los agotados reducen la información disponible.
- Productos de categorías distintas a las 6 de FerroSuite reciben ceros en las columnas de categoría (resultado no evaluado).
- Si cambian las categorías, la ventana o el horizonte, hay que reentrenar y exportar de nuevo.
- La lista de festivos usada en los datos sintéticos es aproximada; con datos reales debe usarse el calendario oficial.
