"""Opcional (paso 3a). Búsqueda pequeña de hiperparámetros usando SOLO el conjunto de validación (MAE en unidades).
El conjunto de prueba no se mira. El resultado se copia a mano a ferrosuite_demanda/config.py.

    python ml/buscar_hiperparametros.py
"""
import itertools
import json

import numpy as np
import torch

from ferrosuite_demanda import config, metricas
from ferrosuite_demanda.entrenamiento import entrenar
from ferrosuite_demanda.modelo import ModeloExportable

REJILLA = {
    "ocultas": [(32, 16), (64, 32), (128, 64)],
    "dropout": [0.1, 0.3],
    "decaimiento": [1e-4, 1e-2],
    "tasa": [1e-3, 3e-4],
}


def main() -> None:
    m = np.load(config.ARCHIVO_MUESTRAS, allow_pickle=False)
    val = m["val"]
    real = (m["y_rel"] * m["escala"])[val]
    filas = []
    for valores in itertools.product(*REJILLA.values()):
        params = dict(zip(REJILLA, valores))
        modelo, norm, hist, mejor = entrenar(m, **params)
        exp = ModeloExportable(modelo, norm).eval()
        with torch.no_grad():
            pred = exp(torch.tensor(m["X"][val]), torch.tensor(m["escala"][val]).unsqueeze(1)).squeeze(1).numpy()
        r = metricas.calcular(real, pred)
        fila = {**{k: list(v) if isinstance(v, tuple) else v for k, v in params.items()}, "mejor_epoca": mejor,
                "MAE_validacion": r["MAE"], "RMSE_validacion": r["RMSE"]}
        filas.append(fila)
        print(json.dumps(fila, ensure_ascii=False))
    filas.sort(key=lambda f: f["MAE_validacion"])
    (config.DIR_RESULTADOS / "busqueda_hiperparametros.json").write_text(
        json.dumps({"criterio": "MAE en validación (prueba no usada)", "resultados": filas}, ensure_ascii=False, indent=2), encoding="utf-8")
    print("Mejor:", json.dumps(filas[0], ensure_ascii=False))


if __name__ == "__main__":
    main()
