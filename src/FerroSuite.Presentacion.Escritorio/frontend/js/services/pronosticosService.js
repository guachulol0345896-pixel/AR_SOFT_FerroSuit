// FerroSuite — Servicio de Pronósticos del frontend. El cálculo (red neuronal ONNX) ocurre en el backend.
window.FerroSuite = window.FerroSuite || {};

FerroSuite.pronosticosService = {
  reposicion() {
    return FerroSuite.api.pedir("pronosticos.reposicion", null, 30000);
  },

  // Detalle de un producto: historial, pronóstico de la red (o de la regla de respaldo), riesgo y recomendación.
  producto(sku) {
    return FerroSuite.api.pedir("pronosticos.producto", { sku, dias: 90 }, 30000);
  },

  // Borrador de orden: se crea en "Pendiente de aprobación" (no se aprueba ni se envía al proveedor).
  generarBorrador(sku, nitProveedor, cantidad) {
    return FerroSuite.ordenesService.crear({ sku, nitProveedor, cantidad });
  },
};
