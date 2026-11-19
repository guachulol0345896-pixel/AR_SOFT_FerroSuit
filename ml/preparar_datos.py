"""Paso 2. Construye las muestras (ventanas de 28 días → consumo promedio de los 7 días siguientes), separa
cronológicamente en entrenamiento/validación/prueba y guarda ml/datos/muestras.npz.

    python ml/preparar_datos.py                                  # usa el dataset sintético
    python ml/preparar_datos.py --movimientos exportado.csv      # usa movimientos reales exportados de FerroSuite
"""
import argparse
import json

import numpy as np
import pandas as pd

from ferrosuite_demanda import caracteristicas, config, movimientos, sintetico


def main() -> None:
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("--movimientos", help="CSV de movimientos reales exportado de FerroSuite (ver README)")
    args = p.parse_args()

    if args.movimientos:
        datos = movimientos.demanda_diaria(pd.read_csv(args.movimientos))
        origen, huella = args.movimientos, sintetico.huella(args.movimientos)
    else:
        datos = pd.read_csv(config.ARCHIVO_DATOS_SINTETICOS, parse_dates=["fecha"], keep_default_na=True)
        datos["motivo_faltante"] = datos["motivo_faltante"].fillna("")
        origen, huella = str(config.ARCHIVO_DATOS_SINTETICOS.relative_to(config.RAIZ_ML.parent)), sintetico.huella(config.ARCHIVO_DATOS_SINTETICOS)
    sintetico_si = bool((datos["dato_sintetico"] == "SI").all())

    m = caracteristicas.construir_muestras(datos)
    ent, val, pru, limites = caracteristicas.separar_cronologicamente(m["fecha_origen"])
    np.savez_compressed(config.ARCHIVO_MUESTRAS, **m, ent=ent, val=val, pru=pru)

    resumen = {
        "origen_datos": origen, "sha256_datos": huella, "dato_sintetico": sintetico_si,
        "ventana_dias": config.VENTANA, "horizonte_dias": config.HORIZONTE,
        "n_caracteristicas": int(m["X"].shape[1]),
        "muestras": {"total": int(len(m["y_rel"])), "entrenamiento": int(ent.sum()), "validacion": int(val.sum()), "prueba": int(pru.sum())},
        "limites": limites,
        "dias_faltantes_en_datos": {k: int(v) for k, v in datos["motivo_faltante"].replace("", "observado").value_counts().items()},
        "comprobaciones_sin_fuga": {
            "ultimo_objetivo_entrenamiento_antes_de_validacion":
                bool(m["fecha_origen"][ent].max() + np.timedelta64(config.HORIZONTE, "D") < m["fecha_origen"][val].min() + np.timedelta64(1, "D")),
            "ultimo_objetivo_validacion_antes_de_prueba":
                bool(m["fecha_origen"][val].max() + np.timedelta64(config.HORIZONTE, "D") < m["fecha_origen"][pru].min() + np.timedelta64(1, "D")),
            "conjuntos_disjuntos": bool(not (ent & val).any() and not (val & pru).any() and not (ent & pru).any()),
        },
    }
    config.DIR_RESULTADOS.mkdir(parents=True, exist_ok=True)
    (config.DIR_RESULTADOS / "preparacion.json").write_text(json.dumps(resumen, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps(resumen, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
