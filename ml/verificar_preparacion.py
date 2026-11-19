"""Comprobaciones automáticas de la preparación (se ejecutan en ejecutar_todo.py):

  1. Sin fugas del futuro: si se cambian TODAS las ventas posteriores a una fecha, las entradas de los orígenes
     anteriores no cambian (solo pueden cambiar los objetivos cuyos 7 días pasan esa fecha).
  2. La normalización del modelo se calculó solo con el conjunto de entrenamiento.
  3. Separación cronológica: entrenamiento < validación < prueba, con margen de 7 días.
  4. Faltantes: ninguna entrada ni objetivo queda vacío (NaN) o infinito.
  5. Reproducibilidad del dataset sintético: generarlo otra vez da exactamente el mismo archivo.

    python ml/verificar_preparacion.py
"""
import io
import json

import numpy as np
import pandas as pd
import torch

from ferrosuite_demanda import caracteristicas, config, sintetico


def main() -> None:
    resultados = {}
    datos = pd.read_csv(config.ARCHIVO_DATOS_SINTETICOS, parse_dates=["fecha"])
    datos["motivo_faltante"] = datos["motivo_faltante"].fillna("")
    muestra = datos[datos["sku"].isin(sorted(datos["sku"].unique())[:6])].copy()
    corte = pd.Timestamp("2024-03-01")
    original = caracteristicas.construir_muestras(muestra)
    alterado = muestra.copy()
    futuro = (alterado["fecha"] > corte) & alterado["demanda"].notna()
    alterado.loc[futuro, "demanda"] = alterado.loc[futuro, "demanda"] * 50 + 1000   # el futuro cambia por completo
    cambiado = caracteristicas.construir_muestras(alterado)

    def por_clave(mm):
        return {(s, f): i for i, (s, f) in enumerate(zip(mm["sku"], mm["fecha_origen"]))}
    o_idx, c_idx = por_clave(original), por_clave(cambiado)
    corte64 = np.datetime64(corte.date())
    lejos64 = np.datetime64((corte - pd.Timedelta(days=config.HORIZONTE)).date())
    antes = [k for k in o_idx if k[1] <= corte64]
    iguales_x = all(k in c_idx and np.array_equal(original["X"][o_idx[k]], cambiado["X"][c_idx[k]]) for k in antes)
    iguales_y = all(original["y_rel"][o_idx[k]] == cambiado["y_rel"][c_idx[k]] for k in antes if k[1] <= lejos64)
    despues_cambian = any(not np.array_equal(original["X"][o_idx[k]], cambiado["X"][c_idx[k]]) for k in o_idx if k[1] > corte64 and k in c_idx)
    resultados["1_sin_fuga_entradas_no_cambian_al_alterar_el_futuro"] = bool(iguales_x)
    resultados["1b_objetivos_lejanos_al_corte_no_cambian"] = bool(iguales_y)
    resultados["1c_control_entradas_posteriores_si_cambian"] = bool(despues_cambian)
    resultados["1_origenes_comparados"] = len(antes)

    m = np.load(config.ARCHIVO_MUESTRAS, allow_pickle=False)
    guardado = torch.load(config.ARCHIVO_PESOS, weights_only=False)
    norm = caracteristicas.ajustar_normalizacion(m["X"][m["ent"]], m["y_rel"][m["ent"]])
    resultados["2_normalizacion_solo_entrenamiento"] = bool(
        np.allclose(norm["media_x"], guardado["normalizacion"]["media_x"]) and np.allclose(norm["desv_x"], guardado["normalizacion"]["desv_x"])
        and not np.allclose(caracteristicas.ajustar_normalizacion(m["X"], m["y_rel"])["media_x"], guardado["normalizacion"]["media_x"]))

    h = np.timedelta64(config.HORIZONTE, "D")
    f = m["fecha_origen"]
    resultados["3_separacion_cronologica_con_margen"] = bool(
        f[m["ent"]].max() + h < f[m["val"]].min() + np.timedelta64(1, "D") and f[m["val"]].max() + h < f[m["pru"]].min() + np.timedelta64(1, "D"))

    resultados["4_sin_vacios_ni_infinitos"] = bool(np.isfinite(m["X"]).all() and np.isfinite(m["y_rel"]).all() and np.isfinite(m["escala"]).all())
    resultados["4b_muestras_con_faltantes_en_la_ventana"] = int((m["X"][:, config.VENTANA + 5] > 0).sum())   # prop_faltantes > 0

    buffer = io.StringIO()
    sintetico.generar().to_csv(buffer, index=False, float_format="%.4f", date_format="%Y-%m-%d")
    import hashlib
    resultados["5_dataset_sintetico_reproducible"] = hashlib.sha256(buffer.getvalue().encode()).hexdigest() == sintetico.huella(config.ARCHIVO_DATOS_SINTETICOS)

    # 6. Lector de movimientos de FerroSuite: ventas - devoluciones; ajustes y compras no son demanda; agotado = faltante.
    from ferrosuite_demanda import movimientos
    mov = pd.DataFrame([
        ["2024-01-01", "X-1", "Plomería", "Unidad", "Entrada", 5, "Inventario inicial"],
        ["2024-01-01", "X-1", "Plomería", "Unidad", "Salida", 2, "Venta"],
        ["2024-01-02", "X-1", "Plomería", "Unidad", "Salida", 1, "Daño o pérdida"],
        ["2024-01-03", "X-1", "Plomería", "Unidad", "Salida", 3, "Venta"],
        ["2024-01-03", "X-1", "Plomería", "Unidad", "Entrada", 1, "Devolución"],
        ["2024-01-05", "X-1", "Plomería", "Unidad", "Entrada", 10, "Compra a proveedor"],
        ["2024-01-05", "X-1", "Plomería", "Unidad", "Salida", 4, "Venta"],
    ], columns=movimientos.COLUMNAS)
    d = movimientos.demanda_diaria(mov)["demanda"].tolist()
    # 01: vendió 2 (stock 3) · 02: sin venta (stock 2) · 03: 3-1 = 2 (stock 0) · 04: stock 0 sin venta → agotado · 05: 4
    resultados["6_lector_movimientos_ferrosuite"] = d[:3] == [2.0, 0.0, 2.0] and np.isnan(d[3]) and d[4] == 4.0
    # 6b. Con StockActual (producto migrado: tenía 3 unidades antes de su primer movimiento) la reconstrucción va hacia atrás.
    mov2 = pd.DataFrame([
        ["2024-01-01", "Y-1", "Pinturas", "Galón", "Salida", 1, "Venta", 0],
        ["2024-01-03", "Y-1", "Pinturas", "Galón", "Salida", 2, "Venta", 0],
    ], columns=movimientos.COLUMNAS + ["StockActual"])
    d2 = movimientos.demanda_diaria(mov2)["demanda"].tolist()
    # stock al cierre: 01 → 2 · 02 → 2 (sin venta, NO es agotado) · 03 → 0 con venta (observado)
    resultados["6b_lector_reconstruye_stock_hacia_atras"] = d2 == [1.0, 0.0, 2.0]

    resultados["todas_correctas"] = all(v for k, v in resultados.items() if isinstance(v, bool))
    (config.DIR_RESULTADOS / "verificacion_preparacion.json").write_text(json.dumps(resultados, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps(resultados, ensure_ascii=False, indent=2))
    if not resultados["todas_correctas"]:
        raise SystemExit("Alguna comprobación de la preparación falló.")


if __name__ == "__main__":
    main()
