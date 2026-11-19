"""Paso 7. Exporta CASOS DE REFERENCIA para comprobar que C# prepara las entradas exactamente igual que Python y que
ONNX Runtime en C# da la misma salida. Los usan las pruebas de tests/FerroSuite.Pruebas.

    python ml/exportar_casos_prueba.py      → modelos/demanda_mlp_casos_prueba.json

Contenido:
  * festivos: los días festivos usados al entrenar (C# los recalcula con el calendario oficial y deben coincidir);
  * caracteristicas: ventanas de 28 días (con faltantes) + fecha + categoría + unidad → 48 entradas, escala y salida ONNX;
  * series: movimientos de FerroSuite + stock actual → demanda diaria con agotados (mismo lector que el entrenamiento);
  * recorrido: un producto del periodo de prueba con sus ventas de 30 días como movimientos, para la prueba completa
    aplicación → modelo (fecha de corte 2024-12-24).
Datos SINTÉTICOS.
"""
import json

import numpy as np
import onnxruntime as ort
import pandas as pd

from ferrosuite_demanda import caracteristicas, config, movimientos, sintetico


def nulos(v):
    return [None if (isinstance(x, float) and np.isnan(x)) else float(x) for x in v]


def main() -> None:
    datos = pd.read_csv(config.ARCHIVO_DATOS_SINTETICOS, parse_dates=["fecha"])
    festivos = set(sintetico.FESTIVOS)
    sesion = ort.InferenceSession(str(config.ARCHIVO_ONNX), providers=["CPUExecutionProvider"])

    def onnx(x, escala):
        return float(sesion.run(None, {"caracteristicas": x.reshape(1, -1), "escala": np.array([[escala]], np.float32)})[0][0, 0])

    casos = []
    def agregar(nombre, ventana, fecha, categoria, unidad):
        r = caracteristicas.caracteristicas_de_ventana(np.asarray(ventana, dtype=float), pd.Timestamp(fecha), categoria, unidad, festivos)
        caso = {"nombre": nombre, "ventana": nulos(ventana), "fecha_origen": str(pd.Timestamp(fecha).date()),
                "categoria": categoria, "unidad": unidad}
        if r is None:
            caso.update({"valido": False})
        else:
            x, escala = r
            caso.update({"valido": True, "caracteristicas": [float(v) for v in x], "escala": float(np.float32(escala)),
                         "salida_onnx": onnx(x, np.float32(escala))})
        casos.append(caso)

    # Casos reales del conjunto de prueba (uno cada pocos días para cubrir productos y fechas distintas).
    rng = np.random.default_rng(7)
    for sku in sorted(datos["sku"].unique())[::3]:
        g = datos[datos["sku"] == sku].set_index("fecha").asfreq("D")
        for t in rng.choice(np.arange(len(g) - 90, len(g) - config.HORIZONTE), size=2, replace=False):
            agregar(f"prueba {sku} {g.index[t].date()}", g["demanda"].to_numpy()[t - 27: t + 1], g.index[t],
                    g["categoria"].iloc[0], g["unidad"].iloc[0])
    # Casos borde.
    base = list(np.arange(28, dtype=float) % 5)
    agregar("todo ceros", [0.0] * 28, "2024-06-03", "Herramientas", "Unidad")
    con_nan = base.copy()
    for i in range(0, 28, 2):
        con_nan[i] = np.nan                                  # exactamente 14 días con dato
    agregar("14 dias con dato", con_nan, "2024-03-27", "Plomería", "Unidad")
    con_nan13 = con_nan.copy(); con_nan13[1] = np.nan
    agregar("13 dias con dato (no se pronostica)", con_nan13, "2024-03-27", "Plomería", "Unidad")
    agregar("unidad continua con decimales", [round(0.3 * i % 4.1, 1) for i in range(28)], "2024-10-30", "Eléctricos", "Metro")
    agregar("festivos en el horizonte", [3.0 + (i % 7) for i in range(28)], "2024-12-24", "Pinturas", "Galón")
    agregar("semana santa en el horizonte", [10.0 + i for i in range(28)], "2024-03-24", "Construcción", "Bulto")
    agregar("valores pequenos", [0.0, 0.1, 0.0, 0.0, 0.2] * 5 + [0.0, 0.1, 0.0], "2024-01-15", "Tornillería", "Kilogramo")
    agregar("categoria desconocida", base, "2024-05-05", "Jardinería", "Unidad")

    # Series a partir de movimientos (mismo lector del entrenamiento, con stock actual).
    series = []
    movs = [
        ["2024-11-01", "S-1", "Plomería", "Unidad", "Salida", 2, "Venta"],
        ["2024-11-02", "S-1", "Plomería", "Unidad", "Salida", 1, "Daño o pérdida"],
        ["2024-11-03", "S-1", "Plomería", "Unidad", "Salida", 3, "Venta"],
        ["2024-11-03", "S-1", "Plomería", "Unidad", "Entrada", 1, "Devolución"],
        ["2024-11-06", "S-1", "Plomería", "Unidad", "Entrada", 10, "Compra a proveedor"],
        ["2024-11-06", "S-1", "Plomería", "Unidad", "Salida", 4, "Venta"],
        ["2024-11-07", "S-1", "Plomería", "Unidad", "Salida", 1.5, "Ajuste por conteo"],
        ["2024-11-08", "S-1", "Plomería", "Unidad", "Salida", 6, "Venta"],
    ]
    # Con agotados: antes de la compra del 2024-11-04 el stock queda en 0 y no hay ventas.
    movs_agotado = [
        ["2024-11-01", "S-2", "Pinturas", "Galón", "Salida", 2, "Venta"],
        ["2024-11-04", "S-2", "Pinturas", "Galón", "Entrada", 10, "Compra a proveedor"],
        ["2024-11-05", "S-2", "Pinturas", "Galón", "Salida", 3, "Venta"],
        ["2024-11-07", "S-2", "Pinturas", "Galón", "Salida", 1, "Venta"],
        ["2024-11-07", "S-2", "Pinturas", "Galón", "Entrada", 1, "Devolución"],
    ]
    for movs_caso, stock in ((movs, 0.0), (movs, 5.0), (movs_agotado, 7.0), (movs_agotado, 9.0)):
        movs = movs_caso
        df = pd.DataFrame([m + [stock] for m in movs], columns=movimientos.COLUMNAS + ["StockActual"])
        d = movimientos.demanda_diaria(df)
        series.append({"stock_actual": stock, "movimientos": [dict(zip(movimientos.COLUMNAS, m)) for m in movs],
                       "fechas": [str(f.date()) for f in d["fecha"]], "demanda": nulos(d["demanda"].tolist())})

    # Recorrido completo: un producto con los 30 días previos al 2024-12-24 sin faltantes.
    corte = pd.Timestamp("2024-12-24")
    recorrido = None
    for sku in sorted(datos["sku"].unique()):
        g = datos[datos["sku"] == sku].set_index("fecha").asfreq("D")
        ventana30 = g.loc[corte - pd.Timedelta(days=29): corte, "demanda"]
        if ventana30.isna().any() or g["unidad"].iloc[0] in config.UNIDADES_CONTINUAS:
            continue
        x, escala = caracteristicas.caracteristicas_de_ventana(ventana30.to_numpy()[-28:], corte, g["categoria"].iloc[0], g["unidad"].iloc[0], festivos)
        recorrido = {"sku": sku, "categoria": g["categoria"].iloc[0], "unidad": g["unidad"].iloc[0], "fecha_corte": str(corte.date()),
                     "ventas_30_dias": [float(v) for v in ventana30], "fechas": [str(f.date()) for f in ventana30.index],
                     "caracteristicas": [float(v) for v in x], "escala": float(np.float32(escala)), "salida_onnx": onnx(x, np.float32(escala)),
                     "regla_30_dias": float(ventana30.sum() / 30.0)}
        break

    salida = {"aviso": "Casos de referencia generados con datos SINTÉTICOS para pruebas de equivalencia Python ↔ C#.",
              "nombres_caracteristicas": caracteristicas.nombres_caracteristicas(),
              "festivos": [str(f.date()) for f in sorted(festivos)], "caracteristicas": casos, "series": series, "recorrido": recorrido}
    destino = config.DIR_MODELOS / "demanda_mlp_casos_prueba.json"
    destino.write_text(json.dumps(salida, ensure_ascii=False, indent=1), encoding="utf-8")
    print(f"{len(casos)} casos de entradas, {len(series)} series y el recorrido de {recorrido['sku']} en {destino}")


if __name__ == "__main__":
    main()
