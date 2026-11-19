"""Paso 1. Genera el dataset SINTÉTICO reproducible (semilla fija) en ml/datos/demanda_sintetica.csv.

    python ml/generar_datos_sinteticos.py

Datos INVENTADOS: solo validan la integración académica. Ver ml/datos/FICHA_DATOS_SINTETICOS.md.
"""
import json

from ferrosuite_demanda import config, sintetico


def main() -> None:
    config.DIR_DATOS.mkdir(parents=True, exist_ok=True)
    datos = sintetico.generar()
    datos.to_csv(config.ARCHIVO_DATOS_SINTETICOS, index=False, float_format="%.4f", date_format="%Y-%m-%d")
    resumen = {
        "archivo": config.ARCHIVO_DATOS_SINTETICOS.name,
        "dato_sintetico": True,
        "semilla": config.SEMILLA_DATOS,
        "sha256": sintetico.huella(config.ARCHIVO_DATOS_SINTETICOS),
        "productos": int(datos["sku"].nunique()),
        "dias": int(datos["fecha"].nunique()),
        "desde": str(datos["fecha"].min().date()), "hasta": str(datos["fecha"].max().date()),
        "filas": int(len(datos)),
        "faltantes": {k: int(v) for k, v in datos["motivo_faltante"].replace("", "observado").value_counts().items()},
        "productos_por_categoria": {k: int(v) for k, v in datos.groupby("categoria")["sku"].nunique().items()},
    }
    (config.DIR_DATOS / "resumen_datos_sinteticos.json").write_text(json.dumps(resumen, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps(resumen, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
