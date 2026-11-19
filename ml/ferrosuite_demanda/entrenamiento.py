"""Bucle de entrenamiento reutilizable (lo usan entrenar.py y buscar_hiperparametros.py). Nunca toca el conjunto de prueba."""
from __future__ import annotations

import copy

import numpy as np
import torch
from torch import nn

from . import caracteristicas, config
from .modelo import MlpDemanda, fijar_semillas


def entrenar(m, ocultas=config.CAPAS_OCULTAS, dropout=config.DROPOUT, tasa=config.TASA_APRENDIZAJE,
             decaimiento=config.DECAIMIENTO_PESOS, lote=config.TAMANO_LOTE, epocas=config.EPOCAS_MAXIMAS,
             paciencia=config.PACIENCIA, informar=None):
    """Entrena con m['ent'] y usa m['val'] para la parada temprana. Devuelve (modelo, normalización, historial, mejor_época)."""
    fijar_semillas()
    X, y, ent, val = m["X"], m["y_rel"], m["ent"], m["val"]
    norm = caracteristicas.ajustar_normalizacion(X[ent], y[ent])         # solo entrenamiento

    def tensores(mascara):
        xs = (X[mascara] - norm["media_x"]) / norm["desv_x"]
        ys = (y[mascara] - norm["media_y"]) / norm["desv_y"]
        return torch.tensor(xs, dtype=torch.float32), torch.tensor(ys, dtype=torch.float32).unsqueeze(1)

    X_ent, y_ent = tensores(ent)
    X_val, y_val = tensores(val)
    modelo = MlpDemanda(X.shape[1], ocultas, dropout)
    optimizador = torch.optim.AdamW(modelo.parameters(), lr=tasa, weight_decay=decaimiento)
    perdida_fn = nn.SmoothL1Loss()   # Huber: menos sensible a días atípicos que el error cuadrático
    generador = torch.Generator().manual_seed(config.SEMILLA_ENTRENAMIENTO)

    historial, mejor, mejor_estado, mejor_epoca, sin_mejora = [], float("inf"), None, 0, 0
    for epoca in range(1, epocas + 1):
        modelo.train()
        orden = torch.randperm(len(X_ent), generator=generador)
        suma = 0.0
        for i in range(0, len(orden), lote):
            idx = orden[i: i + lote]
            optimizador.zero_grad()
            perdida = perdida_fn(modelo(X_ent[idx]), y_ent[idx])
            perdida.backward()
            optimizador.step()
            suma += perdida.item() * len(idx)
        modelo.eval()
        with torch.no_grad():
            p_val = perdida_fn(modelo(X_val), y_val).item()
            p_ent_eval = perdida_fn(modelo(X_ent), y_ent).item()
        historial.append({"epoca": epoca, "perdida_entrenamiento": suma / len(X_ent), "perdida_entrenamiento_eval": p_ent_eval,
                          "perdida_validacion": p_val})
        if p_val < mejor - 1e-6:
            mejor, mejor_estado, mejor_epoca, sin_mejora = p_val, copy.deepcopy(modelo.state_dict()), epoca, 0
        else:
            sin_mejora += 1
        if informar and (epoca == 1 or epoca % 10 == 0):
            informar(f"época {epoca:3d}  pérdida entrenamiento {suma / len(X_ent):.5f}  validación {p_val:.5f}")
        if sin_mejora >= paciencia:
            if informar:
                informar(f"Parada temprana en la época {epoca}: {paciencia} épocas sin mejorar la validación.")
            break
    modelo.load_state_dict(mejor_estado)
    return modelo.eval(), norm, historial, mejor_epoca
