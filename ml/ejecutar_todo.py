"""Ejecuta el flujo completo en orden y deja un registro de reproducibilidad:

    python ml/ejecutar_todo.py

  1. generar_datos_sinteticos.py   2. preparar_datos.py   3. entrenar.py   4. evaluar.py
  5. exportar_onnx.py              6. verificar_preparacion.py   7. exportar_casos_prueba.py (casos para las pruebas de C#)
"""
import hashlib
import json
import platform
import subprocess
import sys
from pathlib import Path

import numpy as np
import onnxruntime
import pandas
import torch

from ferrosuite_demanda import config

PASOS = ["generar_datos_sinteticos.py", "preparar_datos.py", "entrenar.py", "evaluar.py", "exportar_onnx.py", "verificar_preparacion.py",
         "exportar_casos_prueba.py"]


def sha(ruta: Path) -> str:
    return hashlib.sha256(ruta.read_bytes()).hexdigest()


def main() -> None:
    for paso in PASOS:
        print(f"\n===== {paso} =====", flush=True)
        subprocess.run([sys.executable, str(config.RAIZ_ML / paso)], check=True, cwd=config.RAIZ_ML)
    pesos = torch.load(config.ARCHIVO_PESOS, weights_only=False)["estado"]
    huella_pesos = hashlib.sha256(b"".join(v.numpy().tobytes() for v in pesos.values())).hexdigest()
    registro = {
        "python": platform.python_version(), "torch": torch.__version__, "numpy": np.__version__, "pandas": pandas.__version__,
        "onnxruntime": onnxruntime.__version__, "sistema": platform.platform(),
        "sha256_datos_sinteticos": sha(config.ARCHIVO_DATOS_SINTETICOS),
        "sha256_pesos_entrenados": huella_pesos,
        "sha256_predicciones_prueba": sha(config.DIR_RESULTADOS / "predicciones_prueba.csv"),
        "sha256_metricas": sha(config.DIR_RESULTADOS / "metricas.json"),
    }
    (config.DIR_RESULTADOS / "reproducibilidad.json").write_text(json.dumps(registro, ensure_ascii=False, indent=2), encoding="utf-8")
    print("\n" + json.dumps(registro, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
