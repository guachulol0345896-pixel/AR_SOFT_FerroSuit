"""Preparación de datos: de una tabla de demanda diaria (fecha, sku, categoría, unidad, demanda) a muestras para la red.

Cada muestra es un "origen de pronóstico" t (un día y un producto):
  * ENTRADAS: solo información disponible al cierre del día t:
      - las ventas de los días t-27 … t (ventana de 28 días), divididas por la escala del producto;
      - resúmenes de esa ventana (promedios de 7 y 14 días, desviación, máximo, proporción de días sin venta,
        proporción de días sin registro, pendiente);
      - el logaritmo de la escala (nivel de ventas del producto);
      - el calendario: día de la semana de t, mes de los días pronosticados y cuántos festivos caen en ellos
        (el calendario se conoce de antemano: no es información del futuro);
      - la categoría (una columna por categoría) y si la unidad admite decimales.
  * OBJETIVO: consumo diario promedio de los días t+1 … t+7, dividido por la misma escala.
  * ESCALA: promedio de la ventana (días con registro) + 0,05. Hace comparables productos que venden 60 tornillos y
    0,5 galones por día, y solo usa el pasado.

Valores faltantes (días sin registro o agotados):
  * en la ventana se reemplazan por la mediana de los días con registro de esa misma ventana, y se informa la
    proporción de faltantes como una entrada más; si hay menos de 14 días con registro, el origen se descarta;
  * en el objetivo se promedian solo los días observados; si hay menos de 4 de 7, el origen se descarta.

Sin fugas del futuro: las entradas nunca usan días posteriores a t; la normalización (media y desviación de cada
entrada) se calcula solo con las muestras de entrenamiento; y la separación es cronológica con un margen de 7 días
para que ningún objetivo de entrenamiento se superponga con el periodo de validación (ni el de validación con prueba).
"""
from __future__ import annotations

import numpy as np
import pandas as pd

from . import config

NOMBRES_RESUMEN = ["media7_rel", "media14_rel", "desv28_rel", "max28_rel", "prop_ceros", "prop_faltantes", "pendiente_rel"]
NOMBRES_CALENDARIO = ["dow_sen", "dow_cos", "mes_sen", "mes_cos", "festivos_horizonte"]


def nombres_caracteristicas() -> list[str]:
    return ([f"lag_{d:02d}_rel" for d in range(config.VENTANA - 1, -1, -1)]  # lag_27 (más antiguo) … lag_00 (día t)
            + NOMBRES_RESUMEN + ["log_escala"] + NOMBRES_CALENDARIO
            + [f"cat_{c}" for c in config.CATEGORIAS] + ["unidad_continua"])


def _pendiente(v: np.ndarray) -> float:
    x = np.arange(len(v), dtype=float)
    x -= x.mean()
    return float((x * (v - v.mean())).sum() / (x * x).sum())


def caracteristicas_de_ventana(ventana: np.ndarray, fecha_origen: pd.Timestamp, categoria: str, unidad: str,
                               festivos: set) -> tuple[np.ndarray, float] | None:
    """Entradas y escala para un origen. 'ventana' son las ventas de los 28 días que terminan en t (NaN = faltante).
    Devuelve None si no hay suficientes días con registro. Esta función es la especificación que debe replicar C#."""
    observados = ~np.isnan(ventana)
    if observados.sum() < config.MIN_DIAS_VENTANA:
        return None
    mediana = float(np.median(ventana[observados]))
    v = np.where(observados, ventana, mediana)
    escala = float(v.mean()) + config.EPSILON_ESCALA
    rel = v / escala
    dias = pd.date_range(fecha_origen + pd.Timedelta(days=1), periods=config.HORIZONTE, freq="D")
    mes_medio = dias[config.HORIZONTE // 2].month
    resumen = [
        rel[-7:].mean(), rel[-14:].mean(), rel.std(), rel.max(),
        float((v == 0).mean()), float(1 - observados.mean()), _pendiente(rel) * config.VENTANA,
    ]
    calendario = [
        np.sin(2 * np.pi * fecha_origen.dayofweek / 7), np.cos(2 * np.pi * fecha_origen.dayofweek / 7),
        np.sin(2 * np.pi * (mes_medio - 1) / 12), np.cos(2 * np.pi * (mes_medio - 1) / 12),
        sum(d in festivos for d in dias) / config.HORIZONTE,
    ]
    categoria_1h = [1.0 if categoria == c else 0.0 for c in config.CATEGORIAS]
    x = np.concatenate([rel, resumen, [np.log1p(escala)], calendario, categoria_1h,
                        [1.0 if unidad in config.UNIDADES_CONTINUAS else 0.0]]).astype(np.float32)
    return x, escala


def construir_muestras(datos: pd.DataFrame) -> dict[str, np.ndarray]:
    """Recorre cada producto y cada día con historia suficiente y arma entradas, objetivo y métodos base."""
    festivos = set(pd.to_datetime(datos.loc[datos["es_festivo"] == 1, "fecha"]).unique()) if "es_festivo" in datos else set()
    X, y, escalas, fechas, skus, cats = [], [], [], [], [], []
    base_ingenuo, base_ma7, base_ma28, base_regla30, oraculo = [], [], [], [], []
    for sku, g in datos.sort_values("fecha").groupby("sku", sort=True):
        g = g.set_index("fecha").asfreq("D")           # días ausentes en la tabla = faltantes
        dem = g["demanda"].to_numpy(dtype=float)
        esperada = g["demanda_esperada_generador"].to_numpy(dtype=float) if "demanda_esperada_generador" in g else None
        categoria, unidad = g["categoria"].dropna().iloc[0], g["unidad"].dropna().iloc[0]
        idx = g.index
        for t in range(config.HISTORIA_MINIMA - 1, len(dem) - config.HORIZONTE):
            futuro = dem[t + 1: t + 1 + config.HORIZONTE]
            if (~np.isnan(futuro)).sum() < config.MIN_DIAS_OBJETIVO:
                continue
            r = caracteristicas_de_ventana(dem[t - config.VENTANA + 1: t + 1], idx[t], categoria, unidad, festivos)
            if r is None:
                continue
            x, escala = r
            objetivo = float(np.nanmean(futuro))
            X.append(x); y.append(objetivo / escala); escalas.append(escala)
            fechas.append(idx[t].to_datetime64()); skus.append(sku); cats.append(categoria)
            # Métodos base (todos con información hasta t):
            v = x[: config.VENTANA] * escala                                   # ventana con faltantes imputados
            base_ingenuo.append(v[-1])                                         # ingenuo: la venta del último día
            base_ma7.append(v[-7:].mean())                                     # promedio móvil de 7 días
            base_ma28.append(v.mean())                                         # promedio móvil de 28 días (faltantes imputados)
            ult30 = dem[t - 29: t + 1]
            base_regla30.append(np.nansum(ult30) / 30.0)                       # regla actual de FerroSuite: vendido en 30 días / 30
            oraculo.append(np.mean(esperada[t + 1: t + 1 + config.HORIZONTE]) if esperada is not None else np.nan)
    return {
        "X": np.asarray(X, dtype=np.float32), "y_rel": np.asarray(y, dtype=np.float32),
        "escala": np.asarray(escalas, dtype=np.float32), "fecha_origen": np.asarray(fechas, dtype="datetime64[D]"),
        "sku": np.asarray(skus), "categoria": np.asarray(cats),
        "base_ingenuo": np.asarray(base_ingenuo, dtype=np.float32), "base_ma7": np.asarray(base_ma7, dtype=np.float32),
        "base_ma28": np.asarray(base_ma28, dtype=np.float32),
        "base_regla30": np.asarray(base_regla30, dtype=np.float32), "oraculo": np.asarray(oraculo, dtype=np.float32),
        "nombres": np.asarray(nombres_caracteristicas()),
    }


def separar_cronologicamente(fecha_origen: np.ndarray) -> tuple[np.ndarray, np.ndarray, np.ndarray, dict]:
    """Prueba = últimos 90 orígenes; validación = los 90 anteriores; entrenamiento = lo anterior.
    Se deja un margen de HORIZONTE días entre conjuntos para que los objetivos no se superpongan."""
    ultimo = fecha_origen.max()
    h = np.timedelta64(config.HORIZONTE, "D")
    inicio_prueba = ultimo - np.timedelta64(config.DIAS_PRUEBA - 1, "D")
    fin_val = inicio_prueba - h                       # el objetivo de validación termina antes del primer objetivo de prueba
    inicio_val = fin_val - np.timedelta64(config.DIAS_VALIDACION - 1, "D")
    fin_ent = inicio_val - h
    ent = fecha_origen <= fin_ent
    val = (fecha_origen >= inicio_val) & (fecha_origen <= fin_val)
    pru = fecha_origen >= inicio_prueba
    limites = {k: str(v) for k, v in {"entrenamiento_hasta": fin_ent, "validacion_desde": inicio_val, "validacion_hasta": fin_val,
                                     "prueba_desde": inicio_prueba, "prueba_hasta": ultimo}.items()}
    return ent, val, pru, limites


def ajustar_normalizacion(X_ent: np.ndarray, y_ent: np.ndarray) -> dict:
    """Media y desviación de cada entrada y del objetivo, SOLO con datos de entrenamiento."""
    media = X_ent.mean(axis=0)
    desv = X_ent.std(axis=0)
    desv = np.where(desv < 1e-6, 1.0, desv)          # columnas constantes: no se escalan
    return {"media_x": media.astype(np.float32), "desv_x": desv.astype(np.float32),
            "media_y": np.float32(y_ent.mean()), "desv_y": np.float32(y_ent.std())}
