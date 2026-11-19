"""Paso 3. Entrena la red MLP con las muestras de entrenamiento; usa validación para la parada temprana.

    python ml/entrenar.py

Guarda: ml/resultados/modelo_pytorch.pt (pesos + normalización), historial_entrenamiento.csv y perdida_entrenamiento.png.
El conjunto de prueba NO se usa aquí.
"""
import json
import time

import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
import numpy as np
import pandas as pd
import torch

from ferrosuite_demanda import config
from ferrosuite_demanda.entrenamiento import entrenar


def main() -> None:
    m = np.load(config.ARCHIVO_MUESTRAS, allow_pickle=False)
    inicio = time.time()
    modelo, norm, historial, mejor_epoca = entrenar(m, informar=print)
    duracion = time.time() - inicio
    mejor_estado = modelo.state_dict()
    X = m["X"]

    config.DIR_RESULTADOS.mkdir(parents=True, exist_ok=True)
    torch.save({"estado": mejor_estado, "n_entradas": int(X.shape[1]),
                "normalizacion": {k: np.asarray(v).tolist() for k, v in norm.items()},
                "nombres": [str(n) for n in m["nombres"]]}, config.ARCHIVO_PESOS)
    hist = pd.DataFrame(historial)
    hist.to_csv(config.DIR_RESULTADOS / "historial_entrenamiento.csv", index=False, float_format="%.6f")

    plt.figure(figsize=(8, 4.5))
    plt.plot(hist["epoca"], hist["perdida_entrenamiento"], label="Entrenamiento (promedio de la época)")
    plt.plot(hist["epoca"], hist["perdida_validacion"], label="Validación")
    plt.axvline(mejor_epoca, color="gray", linestyle="--", linewidth=1, label=f"Mejor época ({mejor_epoca})")
    plt.xlabel("Época"); plt.ylabel("Pérdida Huber (objetivo normalizado)")
    plt.title("Curva de aprendizaje — MLP de demanda (datos SINTÉTICOS)")
    plt.legend(); plt.grid(alpha=0.3); plt.tight_layout()
    plt.savefig(config.DIR_RESULTADOS / "perdida_entrenamiento.png", dpi=130); plt.close()

    resumen = {"epocas_ejecutadas": int(len(hist)), "mejor_epoca": int(mejor_epoca),
               "mejor_perdida_validacion": float(hist.loc[mejor_epoca - 1, "perdida_validacion"]),
               "perdida_entrenamiento_en_mejor_epoca": float(hist.loc[mejor_epoca - 1, "perdida_entrenamiento_eval"]),
               "duracion_segundos": round(duracion, 1), "parametros_entrenables": int(sum(p.numel() for p in modelo.parameters())),
               "torch": torch.__version__, "semilla": config.SEMILLA_ENTRENAMIENTO,
               "hiperparametros": {"capas_ocultas": list(config.CAPAS_OCULTAS), "dropout": config.DROPOUT, "tasa_aprendizaje": config.TASA_APRENDIZAJE,
                                   "decaimiento_pesos": config.DECAIMIENTO_PESOS, "tamano_lote": config.TAMANO_LOTE,
                                   "epocas_maximas": config.EPOCAS_MAXIMAS, "paciencia": config.PACIENCIA, "perdida": "SmoothL1 (Huber)",
                                   "optimizador": "AdamW"}}
    (config.DIR_RESULTADOS / "entrenamiento.json").write_text(json.dumps(resumen, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps(resumen, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
