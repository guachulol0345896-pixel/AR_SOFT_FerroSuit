"""Paso 4. Evalúa la red en el conjunto de PRUEBA (no usado para entrenar ni para elegir la época) y la compara
con métodos base: pronóstico ingenuo, promedio móvil de 7 días y la regla actual de FerroSuite (vendido en 30 días / 30).

    python ml/evaluar.py

Guarda métricas (JSON y CSV), predicciones de prueba y gráficas en ml/resultados/.
"""
import json

import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
import numpy as np
import pandas as pd
import torch

from ferrosuite_demanda import config, metricas
from ferrosuite_demanda.modelo import MlpDemanda, ModeloExportable

METODOS = {
    "red_mlp": "Red neuronal MLP",
    "base_ingenuo": "Ingenuo (venta del último día)",
    "base_ma7": "Promedio móvil 7 días",
    "base_ma28": "Promedio móvil 28 días (faltantes imputados)",
    "base_regla30": "Regla actual FerroSuite (30 días / 30)",
}


def cargar_modelo():
    guardado = torch.load(config.ARCHIVO_PESOS, weights_only=False)
    mlp = MlpDemanda(guardado["n_entradas"])
    mlp.load_state_dict(guardado["estado"])
    modelo = ModeloExportable(mlp, guardado["normalizacion"]).eval()
    return modelo, guardado


def predecir(modelo, X, escala) -> np.ndarray:
    with torch.no_grad():
        return modelo(torch.tensor(X), torch.tensor(escala).unsqueeze(1)).squeeze(1).numpy()


def main() -> None:
    m = np.load(config.ARCHIVO_MUESTRAS, allow_pickle=False)
    modelo, _ = cargar_modelo()
    real = m["y_rel"] * m["escala"]                               # consumo diario promedio real (unidades/día)
    pred = {"red_mlp": predecir(modelo, m["X"], m["escala"]),
            "base_ingenuo": m["base_ingenuo"], "base_ma7": m["base_ma7"], "base_ma28": m["base_ma28"],
            "base_regla30": m["base_regla30"]}

    resultado = {"dato_sintetico": True, "aviso": "Métricas sobre datos SINTÉTICOS: validan la integración, no el desempeño con datos reales.",
                 "conjuntos": {}}
    for nombre, mascara in (("entrenamiento", m["ent"]), ("validacion", m["val"]), ("prueba", m["pru"])):
        bloque = {}
        for k in METODOS:
            r = metricas.calcular(real[mascara], pred[k][mascara])
            r["MAE_escalado_por_producto"] = metricas.mae_escalado_por_producto(real[mascara], pred[k][mascara], m["sku"][mascara])
            bloque[k] = r
        if not np.isnan(m["oraculo"]).all():
            # Referencia teórica: la media verdadera del generador (imposible de conocer con datos reales). Indica el ruido irreducible.
            bloque["referencia_media_verdadera"] = metricas.calcular(real[mascara], m["oraculo"][mascara])
        resultado["conjuntos"][nombre] = bloque

    prueba = resultado["conjuntos"]["prueba"]
    mejor_base = min(("base_ingenuo", "base_ma7", "base_ma28", "base_regla30"), key=lambda k: prueba[k]["MAE"])
    resultado["comparacion_prueba"] = {
        "mejor_metodo_base_por_MAE": mejor_base,
        "mejora_MAE_vs_mejor_base_pct": round(100 * (1 - prueba["red_mlp"]["MAE"] / prueba[mejor_base]["MAE"]), 2),
        "mejora_RMSE_vs_mejor_base_pct": round(100 * (1 - prueba["red_mlp"]["RMSE"] / prueba[mejor_base]["RMSE"]), 2),
        "mejora_MAE_vs_regla_actual_pct": round(100 * (1 - prueba["red_mlp"]["MAE"] / prueba["base_regla30"]["MAE"]), 2),
        "mejora_RMSE_vs_regla_actual_pct": round(100 * (1 - prueba["red_mlp"]["RMSE"] / prueba["base_regla30"]["RMSE"]), 2),
    }
    (config.DIR_RESULTADOS / "metricas.json").write_text(json.dumps(resultado, ensure_ascii=False, indent=2), encoding="utf-8")

    pru = m["pru"]
    tabla = pd.DataFrame({"fecha_origen": m["fecha_origen"][pru], "sku": m["sku"][pru], "categoria": m["categoria"][pru],
                          "real": real[pru], **{k: pred[k][pru] for k in METODOS}})
    tabla.to_csv(config.DIR_RESULTADOS / "predicciones_prueba.csv", index=False, float_format="%.4f")
    filas = []
    for (cat), g in tabla.groupby("categoria"):
        for k in METODOS:
            r = metricas.calcular(g["real"], g[k])
            filas.append({"categoria": cat, "metodo": k, "MAE": r["MAE"], "RMSE": r["RMSE"], "WAPE": r["WAPE"], "n": r["n"]})
    pd.DataFrame(filas).to_csv(config.DIR_RESULTADOS / "metricas_por_categoria_prueba.csv", index=False, float_format="%.4f")

    graficas(tabla, prueba)
    print(json.dumps({"prueba": prueba, "comparacion": resultado["comparacion_prueba"]}, ensure_ascii=False, indent=2))


def graficas(tabla: pd.DataFrame, prueba: dict) -> None:
    # 1. Real vs predicho en el tiempo, para un producto de cada categoría.
    elegidos = tabla.groupby("categoria")["sku"].first().tolist()
    fig, ejes = plt.subplots(len(elegidos), 1, figsize=(10, 2.3 * len(elegidos)), sharex=True)
    for eje, sku in zip(ejes, elegidos):
        g = tabla[tabla["sku"] == sku].sort_values("fecha_origen")
        eje.plot(g["fecha_origen"], g["real"], color="black", linewidth=1.6, label="Real (promedio 7 días siguientes)")
        eje.plot(g["fecha_origen"], g["red_mlp"], color="tab:orange", label="Red MLP")
        eje.plot(g["fecha_origen"], g["base_regla30"], color="tab:blue", linestyle="--", label="Regla actual (30 días)")
        eje.set_title(f"{sku} · {g['categoria'].iloc[0]}", fontsize=9, loc="left"); eje.grid(alpha=0.3)
        eje.set_ylabel("unid./día", fontsize=8)
    ejes[0].legend(fontsize=8, loc="upper left", ncol=3)
    fig.suptitle("Periodo de PRUEBA: consumo diario real vs pronosticado (datos SINTÉTICOS)")
    fig.tight_layout(); fig.savefig(config.DIR_RESULTADOS / "real_vs_predicho_series.png", dpi=120); plt.close(fig)

    # 2. Dispersión real vs predicho (escala logarítmica: los productos venden desde 0,5 hasta 90 unidades por día).
    fig, ejes = plt.subplots(1, 2, figsize=(10, 4.6), sharex=True, sharey=True)
    for eje, k in zip(ejes, ("red_mlp", "base_regla30")):
        eje.scatter(tabla["real"] + 0.05, tabla[k] + 0.05, s=4, alpha=0.35)
        lim = [0.04, max(tabla["real"].max(), tabla[k].max()) * 1.3]
        eje.plot(lim, lim, color="black", linewidth=1)
        eje.set_xscale("log"); eje.set_yscale("log"); eje.set_xlim(lim); eje.set_ylim(lim)
        eje.set_title(f"{METODOS[k]}\nMAE {prueba[k]['MAE']:.3f} · RMSE {prueba[k]['RMSE']:.3f}", fontsize=9)
        eje.set_xlabel("Real (unid./día)"); eje.grid(alpha=0.3, which="both")
    ejes[0].set_ylabel("Pronosticado (unid./día)")
    fig.suptitle("Prueba: real vs pronosticado (datos SINTÉTICOS)")
    fig.tight_layout(); fig.savefig(config.DIR_RESULTADOS / "dispersion_real_vs_predicho.png", dpi=120); plt.close(fig)

    # 3. Comparación de métodos.
    claves = list(METODOS)
    fig, ejes = plt.subplots(1, 3, figsize=(12, 4))
    for eje, met in zip(ejes, ("MAE", "RMSE", "WAPE")):
        valores = [prueba[k][met] for k in claves]
        barras = eje.bar(range(len(claves)), valores, color=["tab:orange", "tab:gray", "tab:gray", "tab:gray", "tab:blue"])
        eje.set_xticks(range(len(claves))); eje.set_xticklabels(["Red MLP", "Ingenuo", "Prom. 7 d", "Prom. 28 d", "Regla 30 d"], fontsize=8)
        eje.set_title(met); eje.grid(axis="y", alpha=0.3)
        for b, v in zip(barras, valores):
            eje.text(b.get_x() + b.get_width() / 2, v, f"{v:.3f}", ha="center", va="bottom", fontsize=8)
    fig.suptitle("Error en el conjunto de PRUEBA (menor es mejor) — datos SINTÉTICOS")
    fig.tight_layout(); fig.savefig(config.DIR_RESULTADOS / "comparacion_metodos.png", dpi=120); plt.close(fig)


if __name__ == "__main__":
    main()
