"""Equivalencia PyTorch ↔ ONNX ↔ casos que usa C#, SIN reentrenar ni volver a exportar (solo lee).

Comprueba con el modelo entregado (modelos/demanda_mlp.onnx):
  1. La huella SHA-256 del .onnx coincide con la registrada en demanda_mlp_normalizacion.json.
  2. El orden de las 48 entradas del .json es el de los casos de referencia que usan las pruebas de C#.
  3. PyTorch (pesos guardados en ml/resultados/modelo_pytorch.pt) y ONNX Runtime dan lo mismo en todas las muestras
     de prueba (requiere ml/datos/muestras.npz; si falta, se regenera con `python ml/preparar_datos.py`).
  4. ONNX Runtime da hoy la misma salida que quedó registrada en los casos de referencia (los que comparan las pruebas
     de C#: PF/PI) y en el recorrido SINT-001.
Uso: python tests/revision/equivalencia_modelo.py  (desde la raíz del repositorio)
"""
import hashlib, json, sys
from pathlib import Path

import numpy as np
import onnxruntime as ort

RAIZ = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(RAIZ / "ml"))
sys.dont_write_bytecode = True
res = []
ok = lambda i, n, c, d="": res.append(f"{'PASA ' if c else 'FALLA'} | {i} | {n}{' — ' + d if d else ''}")

onnx_path = RAIZ / "modelos/demanda_mlp.onnx"
norm = json.loads((RAIZ / "modelos/demanda_mlp_normalizacion.json").read_text(encoding="utf-8"))
casos = json.loads((RAIZ / "modelos/demanda_mlp_casos_prueba.json").read_text(encoding="utf-8"))
h = hashlib.sha256(onnx_path.read_bytes()).hexdigest()
ok("EQ1", "Huella SHA-256 del modelo entregado = la registrada en sus parámetros", h == norm["verificacion_onnx"]["sha256_onnx"], h)
orden = norm.get("nombres_caracteristicas") or norm.get("caracteristicas")
ok("EQ2", "Orden de las 48 entradas: parámetros de normalización = casos de referencia de C#",
   orden == casos["nombres_caracteristicas"] and len(orden) == 48, f"{len(orden)} entradas")

sesion = ort.InferenceSession(str(onnx_path), providers=["CPUExecutionProvider"])
validos = [c for c in casos["caracteristicas"] if c["valido"]]
X = np.array([c["caracteristicas"] for c in validos], dtype=np.float32)
E = np.array([[c["escala"]] for c in validos], dtype=np.float32)
salida = sesion.run(["consumo_diario"], {"caracteristicas": X, "escala": E})[0].squeeze(1)
esperado = np.array([c["salida_onnx"] for c in validos], dtype=np.float32)
ok("EQ3", "ONNX Runtime reproduce las salidas registradas en los casos que comparan las pruebas de C#",
   np.allclose(salida, esperado, atol=1e-6), f"{len(validos)} casos, diferencia máx. {float(np.abs(salida - esperado).max()):.2e}")
r = casos["recorrido"]
s1 = sesion.run(["consumo_diario"], {"caracteristicas": np.array([r["caracteristicas"]], dtype=np.float32),
                                     "escala": np.array([[r["escala"]]], dtype=np.float32)})[0][0, 0]
ok("EQ4", "Recorrido SINT-001: ONNX Runtime da la salida registrada (la que C# convierte en 3,61/día)",
   abs(float(s1) - r["salida_onnx"]) < 1e-6, f"{float(s1):.4f} (registrado {r['salida_onnx']:.4f})")

try:
    import torch
    from evaluar import cargar_modelo, predecir
    from ferrosuite_demanda import config
    if not config.ARCHIVO_MUESTRAS.exists():
        ok("EQ5", "PyTorch = ONNX en las muestras de prueba", False, "falta ml/datos/muestras.npz: ejecuta python ml/preparar_datos.py")
    else:
        modelo, _ = cargar_modelo()
        m = np.load(config.ARCHIVO_MUESTRAS, allow_pickle=False)
        Xp, Ep = m["X"][m["pru"]], m["escala"][m["pru"]]
        o = sesion.run(["consumo_diario"], {"caracteristicas": Xp, "escala": Ep.reshape(-1, 1)})[0].squeeze(1)
        t = predecir(modelo, Xp, Ep)
        ok("EQ5", "PyTorch (pesos guardados) = ONNX Runtime en todas las muestras de prueba",
           bool(np.allclose(o, t, rtol=1e-4, atol=1e-4)), f"{len(Xp)} muestras, diferencia máx. {float(np.abs(o - t).max()):.2e}")
        ok("EQ6", "Salidas no negativas (el modelo incluye max(0, ·))", bool((o >= 0).all()))
except ImportError as e:
    ok("EQ5", "PyTorch = ONNX en las muestras de prueba", False, f"PyTorch no instalado ({e}); instala ml/requirements.txt")

print("\n".join(res))
print(f"\n{sum(x.startswith('PASA') for x in res)}/{len(res)} comprobaciones correctas")
