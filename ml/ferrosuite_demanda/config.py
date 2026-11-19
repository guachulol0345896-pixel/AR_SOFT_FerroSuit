"""Parámetros fijos del experimento. Cambiarlos cambia el modelo: quedan registrados en las métricas y en el JSON del modelo."""
from pathlib import Path

RAIZ_ML = Path(__file__).resolve().parent.parent
DIR_DATOS = RAIZ_ML / "datos"
DIR_RESULTADOS = RAIZ_ML / "resultados"
DIR_MODELOS = RAIZ_ML.parent / "modelos"

ARCHIVO_DATOS_SINTETICOS = DIR_DATOS / "demanda_sintetica.csv"
ARCHIVO_MUESTRAS = DIR_DATOS / "muestras.npz"
ARCHIVO_PESOS = DIR_RESULTADOS / "modelo_pytorch.pt"
ARCHIVO_ONNX = DIR_MODELOS / "demanda_mlp.onnx"
ARCHIVO_NORMALIZACION = DIR_MODELOS / "demanda_mlp_normalizacion.json"

SEMILLA_DATOS = 20261117        # semilla del dataset sintético
SEMILLA_ENTRENAMIENTO = 1234    # semilla de PyTorch y NumPy al entrenar

VENTANA = 28                    # días de historial que ve la red (lags)
HISTORIA_MINIMA = 30            # días de historial necesarios (la regla actual de FerroSuite usa 30)
HORIZONTE = 7                   # se pronostica el consumo diario promedio de los próximos 7 días
MIN_DIAS_OBJETIVO = 4           # el objetivo exige al menos 4 de los 7 días observados
MIN_DIAS_VENTANA = 14           # y la ventana, al menos 14 de los 28 días con registro
EPSILON_ESCALA = 0.05           # se suma al promedio de la ventana para normalizar productos de muy baja rotación

DIAS_VALIDACION = 90            # orígenes de pronóstico reservados para validación
DIAS_PRUEBA = 90                # y para prueba (los últimos del periodo)

CATEGORIAS = ["Construcción", "Plomería", "Eléctricos", "Tornillería", "Pinturas", "Herramientas"]
UNIDADES_CONTINUAS = ["Metro", "Kilogramo"]   # igual que ReglasInventario en C#

# Red y entrenamiento
CAPAS_OCULTAS = (64, 32)
DROPOUT = 0.10
TASA_APRENDIZAJE = 1e-3
DECAIMIENTO_PESOS = 1e-4
TAMANO_LOTE = 256
EPOCAS_MAXIMAS = 300
PACIENCIA = 25                  # parada temprana: épocas sin mejorar la pérdida de validación
