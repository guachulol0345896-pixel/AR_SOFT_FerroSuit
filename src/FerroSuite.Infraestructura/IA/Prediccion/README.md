# Infraestructura · IA · Predicción

`PronosticadorOnnx` implementa el puerto `IPronosticadorDemanda` con **ONNX Runtime** (`Microsoft.ML.OnnxRuntime`):
ejecuta `modelos/demanda_mlp.onnx` en el propio equipo, sin Gemini ni servicios externos.

- Comprueba la huella SHA-256 del modelo, que sus parámetros coincidan con `Aplicacion/IA/Pronostico/EspecificacionModelo`
  y que el grafo tenga las entradas `caracteristicas [N,48]`, `escala [N,1]` y la salida `consumo_diario [N,1]`.
- Si algo falla, `Estado.Disponible` es `false` con el motivo y la aplicación usa la regla de stock mínimo.
- Las entradas las prepara la capa de Aplicación (`SerieDemanda` y `PreparadorCaracteristicas`), igual que en el
  entrenamiento; este componente solo ejecuta el modelo.
