"""Lectura de movimientos REALES exportados de FerroSuite (para cuando existan datos de una ferretería).

Formato esperado (CSV, una fila por movimiento; ver ml/README.md para la consulta SQL que lo exporta):
    Fecha, Sku, Categoria, Unidad, Tipo, Cantidad, Motivo [, StockActual]
Demanda del día = ventas (Salida, Motivo "Venta") menos devoluciones de clientes (Entrada, Motivo "Devolución"), nunca
negativa. Los ajustes, daños, compras e inventario inicial NO son demanda. Un día sin ventas es 0, salvo:
  * antes del primer movimiento del producto → sin historia (faltante);
  * días en los que el stock al cierre queda en 0 o menos y no hubo ventas → agotado (la demanda real no se observa).

Stock al cierre de cada día: si el archivo trae StockActual (stock del producto al exportar), se reconstruye HACIA ATRÁS:
stock(d) = StockActual − suma de los movimientos posteriores a d. Es lo que hace FerroSuite en C#
(Aplicacion/IA/Pronostico/SerieDemanda.cs) y funciona aunque el producto tuviera stock antes de su primer movimiento
(bases migradas). Sin esa columna, se acumula desde el primer movimiento partiendo de 0.
"""
from __future__ import annotations

import numpy as np
import pandas as pd

COLUMNAS = ["Fecha", "Sku", "Categoria", "Unidad", "Tipo", "Cantidad", "Motivo"]


def demanda_diaria(movimientos: pd.DataFrame) -> pd.DataFrame:
    faltan = [c for c in COLUMNAS if c not in movimientos.columns]
    if faltan:
        raise ValueError(f"Faltan columnas en el archivo de movimientos: {faltan}")
    m = movimientos.copy()
    m["dia"] = pd.to_datetime(m["Fecha"]).dt.normalize()
    m["Cantidad"] = pd.to_numeric(m["Cantidad"], errors="coerce")
    m = m.dropna(subset=["Cantidad", "Sku"])
    signo_stock = np.where(m["Tipo"].eq("Entrada"), 1.0, -1.0)
    m["delta_stock"] = signo_stock * m["Cantidad"]
    m["venta_neta"] = np.where(m["Tipo"].eq("Salida") & m["Motivo"].eq("Venta"), m["Cantidad"], 0.0) \
        - np.where(m["Tipo"].eq("Entrada") & m["Motivo"].eq("Devolución"), m["Cantidad"], 0.0)
    filas = []
    fin = m["dia"].max()
    for sku, g in m.groupby("Sku"):
        dias = pd.date_range(g["dia"].min(), fin, freq="D")
        por_dia = g.groupby("dia").agg(venta=("venta_neta", "sum"), delta=("delta_stock", "sum")).reindex(dias, fill_value=0.0)
        if "StockActual" in g.columns and g["StockActual"].notna().any():
            posteriores = por_dia["delta"][::-1].cumsum()[::-1].shift(-1, fill_value=0.0)   # movimientos de días posteriores a d
            stock = float(g["StockActual"].iloc[0]) - posteriores
        else:
            stock = por_dia["delta"].cumsum()
        demanda = por_dia["venta"].clip(lower=0).astype(float)
        agotado = (stock <= 0) & (demanda == 0)
        filas.append(pd.DataFrame({
            "fecha": dias, "sku": sku, "categoria": g["Categoria"].iloc[0], "unidad": g["Unidad"].iloc[0],
            "demanda": demanda.where(~agotado, np.nan).to_numpy(),
            "motivo_faltante": np.where(agotado, "agotado", ""), "es_festivo": 0,
        }))
    datos = pd.concat(filas, ignore_index=True)
    datos.insert(0, "dato_sintetico", "NO")
    return datos
