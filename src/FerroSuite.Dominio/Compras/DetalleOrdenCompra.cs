namespace FerroSuite.Dominio.Compras;

/// <summary>Línea de una orden de compra (tabla DetalleOrdenCompra). El precio se guarda al crear la orden.</summary>
public class DetalleOrdenCompra
{
    public int DetalleId { get; set; }
    public string Sku { get; set; } = "";
    public string? NombreProducto { get; set; }
    public string? Unidad { get; set; }
    public decimal Cantidad { get; set; }
    public decimal? PrecioUnitario { get; set; }
}
