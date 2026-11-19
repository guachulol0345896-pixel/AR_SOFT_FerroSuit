namespace FerroSuite.Aplicacion.IA;

// Datos que usa el asistente. Son una copia de lo que hay en Inventario en ese
// momento: el asistente no guarda nada, solo analiza y responde.

public sealed record ProductoDto(
    string Sku,
    string Nombre,
    string? Categoria,
    string Unidad,
    decimal StockActual,
    decimal StockMinimo,
    decimal? PrecioCompra,
    string? NitProveedor,
    string? NombreProveedor,
    int? DiasEntregaProveedor);

public sealed record MovimientoDto(string Sku, string Tipo, decimal Cantidad, DateTimeOffset Fecha);

// Orden de compra (RF08 y RF09). El pago vence unos días después de aprobada.
public sealed record OrdenDto(
    string Codigo,
    string Sku,
    string? NombreProducto,
    string? Proveedor,
    decimal Cantidad,
    string Estado,
    decimal? Valor,
    DateTimeOffset? FechaCreacion,
    DateTimeOffset? FechaAprobacion,
    bool Pagada,
    IReadOnlyList<string>? Skus = null);   // todos los productos de la orden (Sku es el primero)

public sealed record SolicitudInventario(
    List<ProductoDto>? Productos,
    List<MovimientoDto>? Movimientos,
    List<OrdenDto>? Ordenes = null);

public sealed record SolicitudPregunta(
    string? Pregunta,
    List<ProductoDto>? Productos,
    List<MovimientoDto>? Movimientos,
    List<OrdenDto>? Ordenes = null);

// Lo que el asistente calcula de cada producto (con reglas, sin IA).
public sealed record AnalisisProducto(
    string Sku,
    string Nombre,
    string? Categoria,
    string Unidad,
    decimal StockActual,
    decimal StockMinimo,
    decimal ConsumoDiario,
    decimal VendidoUltimos30Dias,
    int? DiasRestantes,
    string Proveedor,
    int DiasEntrega,
    decimal? PrecioCompra,
    string Estado);

public sealed record SugerenciaReposicion(
    string Sku,
    string Nombre,
    string Unidad,
    decimal StockActual,
    int? DiasRestantes,
    decimal CantidadSugerida,
    string Proveedor,
    string? NitProveedor,
    int DiasEntrega,
    decimal? CostoEstimado,
    string Prioridad,
    string Motivo);

public sealed record CostoPorProveedor(string Proveedor, int Productos, decimal Total);

public sealed record ResultadoReposicion(
    List<SugerenciaReposicion> Sugerencias,
    List<CostoPorProveedor> CostosPorProveedor,
    decimal CostoTotal);

// Respuestas del asistente. "Fuente" dice si el texto lo escribió Gemini
// o si salió de las reglas de respaldo (cuando no hay clave o la IA falla).
public sealed record RespuestaReposicion(
    string Fuente,
    string Resumen,
    List<SugerenciaReposicion> Sugerencias,
    List<CostoPorProveedor> CostosPorProveedor,
    decimal CostoTotal);

public sealed record RespuestaPregunta(string Fuente, string Respuesta);

// Tipo: "Pago", "Aprobación" o "Pedido". Prioridad: "Alta" o "Media".
public sealed record Recordatorio(string Tipo, string Prioridad, string Titulo, string Detalle);

public sealed record RespuestaRecordatorios(string Fuente, string Resumen, List<Recordatorio> Recordatorios);

public sealed record EstadoServicio(string Servicio, bool IaConfigurada, string Modelo);
