# Evidencias de FerroSuite 0.5.0

Todo lo que hay aquí salió de **ejecuciones reales** del 18 de noviembre de 2026, en un entorno Linux. Ver
«Cómo se obtuvieron» al final. Los datos son **SINTÉTICOS** y el modelo está entrenado con datos sintéticos.
Lo que no se pudo ejecutar figura como **NO PROBADA**; no hay capturas ni resultados fabricados.

| Evidencia pedida | Archivo | Estado |
|---|---|---|
| Compilación del proyecto | `revision/01_compilacion.txt` (5 proyectos + código de la ventana: 0 errores, 0 advertencias); `revision/01b_compilacion_solucion_completa.txt` (intento de compilar la solución completa en Linux) | Capas: VERIFICADO. Solución completa con paquetes reales: **NO PROBADA** (requiere Windows) |
| Inicio del programa | — | **NO PROBADA**: la ventana WinForms + WebView2 solo existe en Windows. No hay capturas de la ventana real |
| Funcionamiento de los módulos | `revision/02_pruebas_en_memoria.txt` (154/154), `revision/03_recorrido_general.txt` (28/28), `revision/06_revision_integral.txt` (35/35), `revision/07b_manipulacion_mensajes.txt` (16/16), `revision/04_pantalla_ia.txt` (17/17) | VERIFICADO (backend real, pantallas en Chromium, repositorio en memoria) |
| Persistencia de la base de datos | — | **NO PROBADA** en SQL Server: las 54 pruebas de `-- --bd` requieren Windows con LocalDB. Generan su propia evidencia en `tests/resultados/` |
| Entrenamiento de la red neuronal | `modelo/reentrenamiento_reproduccion.txt` (ejecución completa de `ml/ejecutar_todo.py`), `modelo/entrenamiento.json`, `modelo/perdida_entrenamiento.png`, `modelo/reproducibilidad.json` | VERIFICADO (con las mismas huellas de datos, pesos, predicciones y métricas que el entrenamiento original) |
| Evaluación del modelo | `modelo/metricas.json`, `modelo/comparacion_metodos.png`, `modelo/real_vs_predicho_series.png`, `modelo/verificacion_preparacion.json` | VERIFICADO (datos sintéticos) |
| Predicciones generadas | `../ml/resultados/predicciones_prueba.csv` (3 418 predicciones de prueba); mediciones al final de `revision/02_pruebas_en_memoria.txt` | VERIFICADO |
| Integración C# ↔ ONNX | `modelo/verificacion_onnx.json` (ONNX frente a PyTorch); `revision/02_pruebas_en_memoria.txt`: PI1 (carga verificada), PI2 (C# = Python en 35 casos, diferencia 0), PI3 (recorrido completo: 3,61 frente a 3,6096) | VERIFICADO |
| Resultados usados dentro del ERP | Capturas `ia_producto_con_historial.png` (red neuronal), `ia_producto_sin_historial.png` y `ia_modelo_no_disponible.png` (regla de respaldo), `ia_producto_con_agotados.png`, `ia_borrador_orden.png`, `ia_cargando.png`; `revision/05_pantalla_ia_sin_modelo.txt`, `revision/07_predictor_con_falla.txt` | VERIFICADO (pantallas reales en Chromium con el backend C# real; no es la ventana de Windows) |
| Funcionamiento sin internet | `revision/sin_red/` (154/154, 17/17, 35/35 en un espacio de red aislado) | VERIFICADO |
| Revisión estática (rutas, claves, dependencias) | `revision/11_verificaciones_estaticas.txt` (19/19); equivalencia PyTorch ↔ ONNX ↔ C# en `revision/12_equivalencia_modelo.txt` (6/6) | VERIFICADO |

| Correcciones finales | `correcciones/`: registro técnico en Release (C1), equivalencia del modelo (C6), capturas de la administración de bodegas y categorías y de la edición de usuarios | VERIFICADO (las capturas son de Chromium) |
| Estado antes de las correcciones | `auditoria_independiente/`: registros de la auditoría técnica independiente | Histórico |

## Cómo se obtuvieron

- **Registros `revision/`:** los genera `tests/revision/ejecutar_revision.sh`. Las pantallas se probaron con Playwright y Chromium sobre la herramienta `FerroSuite.Pruebas servidor`, que entrega cada mensaje al `Puente` real (en lugar de WebView2) y guarda en memoria.
- **Capturas:** las guarda `tests/pantallas/pantalla_ia.js`.
- **Carpeta `modelo/`:** son copias de `ml/resultados/` y el registro de reentrenar en una copia del repositorio.
- **Claves:** los registros del servidor de prueba (traen claves aleatorias de cada ejecución) se borran y no están aquí.
