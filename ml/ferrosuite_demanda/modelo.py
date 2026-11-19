"""Red neuronal MLP pequeña para datos tabulares y el envoltorio que se exporta a ONNX."""
from __future__ import annotations

import numpy as np
import torch
from torch import nn

from . import config


class MlpDemanda(nn.Module):
    """Perceptrón multicapa: entradas normalizadas → 64 → 32 → 1 (consumo relativo normalizado). ReLU y dropout."""

    def __init__(self, n_entradas: int, ocultas=config.CAPAS_OCULTAS, dropout: float = config.DROPOUT):
        super().__init__()
        capas, anterior = [], n_entradas
        for n in ocultas:
            capas += [nn.Linear(anterior, n), nn.ReLU(), nn.Dropout(dropout)]
            anterior = n
        capas.append(nn.Linear(anterior, 1))
        self.red = nn.Sequential(*capas)

    def forward(self, x: torch.Tensor) -> torch.Tensor:
        return self.red(x)


class ModeloExportable(nn.Module):
    """Lo que se exporta a ONNX: incluye la normalización, para que C# solo calcule las entradas y la escala.

    Entradas:  caracteristicas [N, 48] (sin normalizar, en el orden de nombres_caracteristicas())
               escala          [N, 1]  (promedio de la ventana + 0,05)
    Salida:    consumo_diario  [N, 1]  (unidades del producto por día, promedio de los próximos 7 días; nunca negativo)
    """

    def __init__(self, mlp: MlpDemanda, norm: dict):
        super().__init__()
        self.mlp = mlp
        self.register_buffer("media_x", torch.tensor(np.asarray(norm["media_x"]), dtype=torch.float32))
        self.register_buffer("desv_x", torch.tensor(np.asarray(norm["desv_x"]), dtype=torch.float32))
        self.register_buffer("media_y", torch.tensor(float(norm["media_y"]), dtype=torch.float32))
        self.register_buffer("desv_y", torch.tensor(float(norm["desv_y"]), dtype=torch.float32))

    def forward(self, caracteristicas: torch.Tensor, escala: torch.Tensor) -> torch.Tensor:
        z = (caracteristicas - self.media_x) / self.desv_x
        relativo = self.mlp(z) * self.desv_y + self.media_y
        return torch.relu(relativo) * escala


def fijar_semillas(semilla: int = config.SEMILLA_ENTRENAMIENTO) -> None:
    import random
    random.seed(semilla)
    np.random.seed(semilla)
    torch.manual_seed(semilla)
    torch.use_deterministic_algorithms(True)
    torch.set_num_threads(1)   # resultados idénticos entre ejecuciones en CPU
