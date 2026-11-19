"""Generador del dataset SINTÉTICO de demanda diaria (reproducible con semilla fija).

ESTOS DATOS SON INVENTADOS. No provienen de una ferretería ni de ventas reales. Solo sirven para validar la
integración académica (preparación → entrenamiento → evaluación → ONNX → C#). Ninguna métrica obtenida con ellos
demuestra que el modelo funcione con datos reales.

Patrones que se incluyen a propósito (documentados en ml/datos/FICHA_DATOS_SINTETICOS.md):
  1. Nivel base por producto según categoría y unidad (log-normal alrededor de una mediana por categoría).
  2. Semana: más ventas el viernes y el sábado; el domingo, medio día de atención.
  3. Año: dos temporadas de lluvia (abril-mayo y octubre-noviembre) suben plomería y bajan construcción;
     pinturas sube en noviembre-diciembre; construcción cae entre el 20 de diciembre y el 10 de enero.
  4. Festivos (lista aproximada de Colombia 2023-2024): la demanda baja al 45 %.
  5. Tendencia lineal por producto entre -15 % y +30 % anual.
  6. Cambios de nivel: 3 productos suben 50 % desde una fecha de 2024 (p. ej., un cliente constructor nuevo).
  7. Productos nuevos: 2 productos empiezan a venderse a mitad de 2023 (antes no hay historia).
  8. Ruido: binomial negativa para unidades discretas; gamma con un decimal para metros y kilos.
  9. Faltantes: días sin registro (huecos de 1 a 4 días) y agotados (3 a 10 días) en los que la demanda real no se
     observa; en ambos casos la demanda queda vacía (NaN) y se indica el motivo.
"""
from __future__ import annotations

import hashlib
from dataclasses import dataclass

import numpy as np
import pandas as pd

from . import config

FECHA_INICIO = "2023-01-01"
FECHA_FIN = "2024-12-31"

# Lista APROXIMADA de festivos de Colombia 2023-2024 (solo para el patrón sintético).
FESTIVOS = pd.to_datetime([
    "2023-01-01", "2023-01-09", "2023-03-20", "2023-04-06", "2023-04-07", "2023-05-01", "2023-05-22", "2023-06-12",
    "2023-06-19", "2023-07-03", "2023-07-20", "2023-08-07", "2023-08-21", "2023-10-16", "2023-11-06", "2023-11-13",
    "2023-12-08", "2023-12-25",
    "2024-01-01", "2024-01-08", "2024-03-25", "2024-03-28", "2024-03-29", "2024-05-01", "2024-05-13", "2024-06-03",
    "2024-06-10", "2024-07-01", "2024-07-20", "2024-08-07", "2024-08-19", "2024-10-14", "2024-11-04", "2024-11-11",
    "2024-12-08", "2024-12-25",
])


@dataclass(frozen=True)
class TipoProducto:
    categoria: str
    unidad: str
    mediana: float        # demanda diaria típica
    dispersion: float     # k de la binomial negativa (más bajo = más irregular); para continuas, coeficiente de variación


TIPOS = [
    TipoProducto("Construcción", "Bulto", 5.0, 3.0),
    TipoProducto("Construcción", "Varilla", 3.5, 2.5),
    TipoProducto("Construcción", "Kilogramo", 2.5, 0.45),
    TipoProducto("Plomería", "Unidad", 6.0, 3.0),
    TipoProducto("Eléctricos", "Metro", 25.0, 0.40),
    TipoProducto("Eléctricos", "Unidad", 4.0, 2.5),
    TipoProducto("Tornillería", "Unidad", 60.0, 4.0),
    TipoProducto("Tornillería", "Kilogramo", 1.5, 0.45),
    TipoProducto("Pinturas", "Galón", 1.2, 2.0),
    TipoProducto("Herramientas", "Unidad", 0.8, 1.5),
]

# Ventas relativas de lunes a domingo.
SEMANA = {
    "Construcción": [1.00, 0.95, 0.95, 1.00, 1.15, 1.40, 0.30],
    "default": [1.00, 0.95, 0.95, 1.00, 1.10, 1.30, 0.40],
}


def _bulto(dia_anio: np.ndarray, centro: float, ancho: float) -> np.ndarray:
    """Curva suave (gaussiana circular sobre el año) centrada en el día 'centro'."""
    d = np.minimum(np.abs(dia_anio - centro), 365 - np.abs(dia_anio - centro))
    return np.exp(-0.5 * (d / ancho) ** 2)


def factor_anual(categoria: str, fechas: pd.DatetimeIndex) -> np.ndarray:
    doy = fechas.dayofyear.to_numpy().astype(float)
    lluvias = _bulto(doy, 125, 22) + _bulto(doy, 300, 22)        # abril-mayo y octubre-noviembre
    f = np.ones(len(fechas))
    if categoria == "Plomería":
        f += 0.25 * lluvias
    elif categoria == "Construcción":
        f += -0.10 * lluvias + 0.15 * (_bulto(doy, 30, 25) + _bulto(doy, 210, 25))  # temporadas secas
        navidad = ((fechas.month == 12) & (fechas.day >= 20)) | ((fechas.month == 1) & (fechas.day <= 10))
        f[navidad] *= 0.60
    elif categoria == "Pinturas":
        f += 0.60 * _bulto(doy, 335, 20)                          # pintar la casa para diciembre
    elif categoria == "Eléctricos":
        f += 0.10 * _bulto(doy, 340, 15)                          # instalaciones de fin de año
    return f


def generar(semilla: int = config.SEMILLA_DATOS) -> pd.DataFrame:
    rng = np.random.default_rng(semilla)
    fechas = pd.date_range(FECHA_INICIO, FECHA_FIN, freq="D")
    n = len(fechas)
    t_anios = np.arange(n) / 365.0
    festivo = fechas.isin(FESTIVOS)

    # 40 productos: 4 de cada tipo.
    productos = []
    for i, tipo in enumerate(TIPOS):
        for j in range(4):
            productos.append((f"SINT-{i:02d}{j}", tipo))
    con_salto = set(rng.choice(len(productos), size=3, replace=False))
    restantes = [k for k in range(len(productos)) if k not in con_salto]
    nuevos = set(rng.choice(restantes, size=2, replace=False))

    filas = []
    for k, (sku, tipo) in enumerate(productos):
        nivel = tipo.mediana * float(np.exp(rng.normal(0, 0.35)))
        tendencia = float(rng.uniform(-0.15, 0.30))
        semana = np.array(SEMANA.get(tipo.categoria, SEMANA["default"]))[fechas.dayofweek.to_numpy()]
        media = nivel * semana * factor_anual(tipo.categoria, fechas) * (1 + tendencia * t_anios)
        media = np.where(festivo, media * 0.45, media)
        if k in con_salto:
            inicio_salto = int(rng.integers(n - 300, n - 120))
            media[inicio_salto:] *= 1.5
        media = np.maximum(media, 0.0)

        if tipo.unidad in config.UNIDADES_CONTINUAS:
            cv = tipo.dispersion
            forma = 1.0 / cv**2
            valores = rng.gamma(forma, media / forma)
            # Días sin venta, más probables cuando la demanda esperada es baja.
            valores *= rng.random(n) > np.exp(-3 * media)
            demanda = np.round(valores, 1)
        else:
            kdisp = tipo.dispersion
            p = kdisp / (kdisp + np.maximum(media, 1e-9))
            demanda = rng.negative_binomial(kdisp, p).astype(float)

        motivo = np.array([""] * n, dtype=object)
        if k in nuevos:
            lanzamiento = int(rng.integers(150, 210))
            demanda[:lanzamiento] = np.nan
            motivo[:lanzamiento] = "sin_historia"
        # Días sin registro: huecos cortos al azar.
        dia = 0
        while dia < n:
            if rng.random() < 0.006:
                largo = int(rng.integers(1, 5))
                sel = slice(dia, min(n, dia + largo))
                libres = motivo[sel] == ""
                demanda[sel] = np.where(libres, np.nan, demanda[sel])
                motivo[sel] = np.where(libres, "sin_registro", motivo[sel])
                dia += largo
            dia += 1
        # Agotados: la venta real no se observa (en FerroSuite se detectan cuando la existencia queda en cero).
        for _ in range(int(rng.poisson(2.0 * n / 365))):
            inicio = int(rng.integers(0, n - 10))
            largo = int(rng.integers(3, 11))
            sel = slice(inicio, inicio + largo)
            libres = motivo[sel] == ""
            demanda[sel] = np.where(libres, np.nan, demanda[sel])
            motivo[sel] = np.where(libres, "agotado", motivo[sel])

        filas.append(pd.DataFrame({
            "fecha": fechas, "sku": sku, "categoria": tipo.categoria, "unidad": tipo.unidad,
            "demanda": demanda, "motivo_faltante": motivo, "es_festivo": festivo.astype(int),
            "demanda_esperada_generador": np.round(media, 4),
        }))
    datos = pd.concat(filas, ignore_index=True)
    datos.insert(0, "dato_sintetico", "SI")
    return datos


def huella(ruta) -> str:
    return hashlib.sha256(open(ruta, "rb").read()).hexdigest()
