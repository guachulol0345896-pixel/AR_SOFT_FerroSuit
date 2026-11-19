"""Paso 5. Exporta la red a ONNX (con la normalización incluida), guarda los parámetros de normalización en JSON y
comprueba que ONNX Runtime da los mismos resultados que PyTorch en todas las muestras de prueba.

    python ml/exportar_onnx.py

Salidas: modelos/demanda_mlp.onnx, modelos/demanda_mlp_normalizacion.json, ml/resultados/verificacion_onnx.json.
"""
import json
from datetime import datetime, timezone

import numpy as np
import onnx
import onnxruntime as ort
import torch

from ferrosuite_demanda import config, sintetico
from evaluar import cargar_modelo, predecir

NOMBRE_ENTRADA, NOMBRE_ESCALA, NOMBRE_SALIDA = "caracteristicas", "escala", "consumo_diario"


def main() -> None:
    modelo, guardado = cargar_modelo()
    m = np.load(config.ARCHIVO_MUESTRAS, allow_pickle=False)
    n = guardado["n_entradas"]
    config.DIR_MODELOS.mkdir(parents=True, exist_ok=True)

    ejemplo = (torch.zeros(2, n), torch.ones(2, 1))
    lote = torch.export.Dim("lote", min=1, max=100_000)
    torch.onnx.export(
        modelo, ejemplo, str(config.ARCHIVO_ONNX), dynamo=True, opset_version=18,
        input_names=[NOMBRE_ENTRADA, NOMBRE_ESCALA], output_names=[NOMBRE_SALIDA],
        dynamic_shapes={"caracteristicas": {0: lote}, "escala": {0: lote}}, external_data=False,
    )
    onnx_modelo = onnx.load(str(config.ARCHIVO_ONNX))
    onnx.checker.check_model(onnx_modelo)
    preparacion = json.loads((config.DIR_RESULTADOS / "preparacion.json").read_text(encoding="utf-8"))
    metricas = json.loads((config.DIR_RESULTADOS / "metricas.json").read_text(encoding="utf-8"))
    for k, v in {"modelo": "FerroSuite · demanda MLP", "datos": "SINTETICOS (solo integración académica)",
                 "sha256_datos": preparacion["sha256_datos"]}.items():
        onnx_modelo.metadata_props.append(onnx.StringStringEntryProto(key=k, value=v))
    onnx.save(onnx_modelo, str(config.ARCHIVO_ONNX))

    # Coherencia ONNX vs PyTorch en TODAS las muestras de prueba.
    pru = m["pru"]
    X, escala = m["X"][pru], m["escala"][pru]
    sesion = ort.InferenceSession(str(config.ARCHIVO_ONNX), providers=["CPUExecutionProvider"])
    salida_onnx = sesion.run([NOMBRE_SALIDA], {NOMBRE_ENTRADA: X, NOMBRE_ESCALA: escala.reshape(-1, 1)})[0].squeeze(1)
    salida_torch = predecir(modelo, X, escala)
    dif = np.abs(salida_onnx - salida_torch)
    rel = dif / np.maximum(np.abs(salida_torch), 1e-3)
    una = sesion.run([NOMBRE_SALIDA], {NOMBRE_ENTRADA: X[:1], NOMBRE_ESCALA: escala[:1].reshape(1, 1)})[0]
    verificacion = {
        "muestras_comparadas": int(len(X)),
        "max_diferencia_absoluta": float(dif.max()), "max_diferencia_relativa": float(rel.max()),
        "media_diferencia_absoluta": float(dif.mean()),
        "tolerancia_absoluta": 1e-4, "tolerancia_relativa": 1e-4,
        "coherente": bool(np.allclose(salida_onnx, salida_torch, rtol=1e-4, atol=1e-4)),
        "lote_de_1_igual": bool(np.allclose(una[0, 0], salida_torch[0], rtol=1e-4, atol=1e-4)),
        "salidas_no_negativas": bool((salida_onnx >= 0).all()),
        "onnxruntime": ort.__version__, "onnx": onnx.__version__, "torch": torch.__version__,
        "opset": int(onnx_modelo.opset_import[0].version),
        "entradas": [{"nombre": i.name, "forma": [d.dim_param or d.dim_value for d in i.type.tensor_type.shape.dim]} for i in onnx_modelo.graph.input],
        "salidas": [{"nombre": o.name, "forma": [d.dim_param or d.dim_value for d in o.type.tensor_type.shape.dim]} for o in onnx_modelo.graph.output],
        "sha256_onnx": sintetico.huella(config.ARCHIVO_ONNX),
    }
    (config.DIR_RESULTADOS / "verificacion_onnx.json").write_text(json.dumps(verificacion, ensure_ascii=False, indent=2), encoding="utf-8")

    norm = guardado["normalizacion"]
    parametros = {
        "modelo": config.ARCHIVO_ONNX.name, "version": "1.0.0",
        "fecha_entrenamiento_utc": datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
        "datos": {"sinteticos": preparacion["dato_sintetico"], "origen": preparacion["origen_datos"], "sha256": preparacion["sha256_datos"],
                  "aviso": "Entrenado con datos SINTÉTICOS: solo valida la integración académica; no usar para decisiones reales."},
        "entradas_onnx": {NOMBRE_ENTRADA: f"float32 [N, {n}] sin normalizar (el modelo normaliza)", NOMBRE_ESCALA: "float32 [N, 1]"},
        "salida_onnx": {NOMBRE_SALIDA: "float32 [N, 1]: consumo diario promedio esperado de los próximos 7 días, en unidades del producto; >= 0"},
        "ventana_dias": config.VENTANA, "horizonte_dias": config.HORIZONTE, "historia_minima_dias": config.HISTORIA_MINIMA,
        "min_dias_observados_en_ventana": config.MIN_DIAS_VENTANA, "epsilon_escala": config.EPSILON_ESCALA,
        "categorias": config.CATEGORIAS, "unidades_continuas": config.UNIDADES_CONTINUAS,
        "caracteristicas": guardado["nombres"],
        "normalizacion": {"media_x": norm["media_x"], "desv_x": norm["desv_x"], "media_y": norm["media_y"], "desv_y": norm["desv_y"],
                          "nota": "Ya incluida en el ONNX. Se guarda para documentar y verificar: z = (x - media_x) / desv_x; consumo = max(0, red(z)*desv_y + media_y) * escala."},
        "metricas_prueba": {k: {m_: metricas["conjuntos"]["prueba"][k][m_] for m_ in ("MAE", "RMSE", "WAPE")} for k in metricas["conjuntos"]["prueba"]},
        "verificacion_onnx": {k: verificacion[k] for k in ("coherente", "max_diferencia_absoluta", "sha256_onnx")},
    }
    config.ARCHIVO_NORMALIZACION.write_text(json.dumps(parametros, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps(verificacion, ensure_ascii=False, indent=2))
    if not verificacion["coherente"]:
        raise SystemExit("ONNX y PyTorch NO coinciden dentro de la tolerancia.")


if __name__ == "__main__":
    main()
