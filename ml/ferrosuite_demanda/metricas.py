"""Métricas de error en las unidades originales de cada producto."""
from __future__ import annotations

import numpy as np


def calcular(real: np.ndarray, pred: np.ndarray) -> dict:
    """MAE, RMSE, WAPE (error absoluto total / demanda total) y sesgo (promedio de predicho - real)."""
    real = np.asarray(real, dtype=float)
    pred = np.asarray(pred, dtype=float)
    e = pred - real
    return {
        "MAE": float(np.mean(np.abs(e))),
        "RMSE": float(np.sqrt(np.mean(e ** 2))),
        "WAPE": float(np.sum(np.abs(e)) / max(np.sum(np.abs(real)), 1e-12)),
        "sesgo": float(np.mean(e)),
        "n": int(len(real)),
    }


def mae_escalado_por_producto(real, pred, sku) -> float:
    """Promedio, entre productos, del MAE dividido por la demanda media del producto (comparable entre unidades)."""
    real, pred, sku = map(np.asarray, (real, pred, sku))
    valores = []
    for s in np.unique(sku):
        m = sku == s
        valores.append(np.mean(np.abs(pred[m] - real[m])) / max(np.mean(real[m]), 1e-6))
    return float(np.mean(valores))
