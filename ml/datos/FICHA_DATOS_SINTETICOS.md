# Ficha del dataset — DATOS SINTÉTICOS

> **Estos datos son inventados.** No provienen de una ferretería ni de ventas reales. Solo sirven para validar la
> integración académica del modelo (preparación → entrenamiento → evaluación → ONNX). Ninguna métrica calculada con
> ellos demuestra que el modelo funcione con datos reales. Cada fila lleva `dato_sintetico = SI`.

| Dato | Valor |
|---|---|
| Archivo | `demanda_sintetica.csv` (lo genera `python ml/generar_datos_sinteticos.py`) |
| Semilla | `20261117` (fija: el archivo es idéntico en cada ejecución) |
| SHA-256 | `bc95752bd7fe0e0e295fcf330df2980951f5de7b944472cb5b89913061e7e1d5` |
| Periodo | 2023-01-01 a 2024-12-31 (731 días) |
| Productos | 40 (`SINT-000` … `SINT-093`): 4 por cada uno de 10 tipos categoría/unidad |
| Filas | 29 240 (producto × día) |
| Días observados / faltantes | 27 325 observados; 1 072 agotado; 444 sin registro; 399 sin historia |

## Columnas

| Columna | Descripción |
|---|---|
| `dato_sintetico` | Siempre `SI` |
| `fecha`, `sku`, `categoria`, `unidad` | Día, producto, categoría (las 6 de FerroSuite) y unidad de medida |
| `demanda` | Unidades vendidas ese día (un decimal para metros y kilos). Vacía si el día es faltante |
| `motivo_faltante` | `agotado`, `sin_registro`, `sin_historia` o vacío |
| `es_festivo` | 1 si el día está en la lista aproximada de festivos usada por el generador |
| `demanda_esperada_generador` | Media verdadera con la que se sorteó la demanda. **No es una entrada del modelo**: solo se usa para informar el ruido irreducible ("referencia_media_verdadera" en las métricas) |

## Patrones incluidos (a propósito, para que haya algo que aprender)

1. **Nivel base** por tipo de producto (p. ej., tornillería ~60 unidades/día, cemento ~5 bultos/día, pintura ~1,2 galones/día,
   herramientas ~0,8/día), con variación log-normal entre productos.
2. **Semana**: viernes y sábado más altos; domingo con medio día de atención (30-40 % de un día normal).
3. **Año**: dos temporadas de lluvia (abril-mayo y octubre-noviembre) suben plomería (+25 %) y bajan construcción (−10 %);
   temporadas secas suben construcción (+15 %); pinturas sube en noviembre-diciembre (+60 %); eléctricos sube en diciembre;
   construcción cae 40 % entre el 20 de diciembre y el 10 de enero.
4. **Festivos** (lista aproximada de Colombia 2023-2024): demanda al 45 %.
5. **Tendencia** lineal por producto entre −15 % y +30 % por año.
6. **Cambios de nivel**: 3 productos suben 50 % desde una fecha de 2024.
7. **Productos nuevos**: 2 productos sin historia durante los primeros 150-210 días.
8. **Ruido**: binomial negativa para unidades discretas (sobredispersión según el tipo); gamma con coeficiente de variación
   0,40-0,45 y redondeo a un decimal para metros y kilos, con más días en cero cuando la demanda esperada es baja.
9. **Faltantes**: huecos de 1-4 días sin registro (0,6 % de probabilidad diaria de que empiece uno) y ~2 agotados por año y
   producto de 3-10 días. En los dos casos la venta real no se observa y la demanda queda vacía.

## Limitaciones

Los patrones son supuestos razonables, no mediciones. Una ferretería real puede tener efectos que aquí no existen
(promociones, obras grandes puntuales, cambios de precio, cierres, inflación) y no tener otros que aquí sí. Por eso el
modelo debe **reentrenarse y evaluarse con datos reales** antes de usar sus pronósticos para decidir compras.
