namespace FerroSuite.Dominio.Compras;

/// <summary>Pedido a un proveedor, con una o varias líneas de detalle (tablas OrdenCompra y DetalleOrdenCompra).</summary>
public class OrdenCompra
{
    public int OrdenId { get; set; }
    public string Codigo { get; set; } = "";
    public string NitProveedor { get; set; } = "";
    public string? NombreProveedor { get; set; }
    public string Estado { get; set; } = EstadosOrden.Pendiente;
    public DateTime FechaCreacion { get; set; }
    public DateTime? FechaAprobacion { get; set; }
    public DateTime? FechaEntregaEsperada { get; set; }
    public DateTime? FechaRecepcion { get; set; }
    public DateTime? FechaPago { get; set; }
    public bool Pagada { get; set; }
    public int? BodegaRecepcionId { get; set; }
    public List<DetalleOrdenCompra> Detalles { get; set; } = [];

    // Calculados (para mostrar).
    public decimal? Valor => Detalles.Count == 0 || Detalles.Any(d => d.PrecioUnitario is null)
        ? null : Detalles.Sum(d => d.Cantidad * d.PrecioUnitario!.Value);
    public string NombreProducto => Detalles.Count switch
    {
        0 => "",
        1 => Detalles[0].NombreProducto ?? Detalles[0].Sku,
        _ => $"{Detalles[0].NombreProducto ?? Detalles[0].Sku} y {Detalles.Count - 1} más",
    };
    public decimal Cantidad => Detalles.Sum(d => d.Cantidad);
    public DateTime? VencePago { get; set; }
}
