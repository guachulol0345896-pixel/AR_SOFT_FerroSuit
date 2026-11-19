namespace FerroSuite.Dominio.Inventario;

/// <summary>Producto del catálogo, con unidad de medida propia (tabla Producto).</summary>
public class Producto
{
    public string Sku { get; set; } = "";
    public string Nombre { get; set; } = "";
    public int CategoriaId { get; set; }
    public string Unidad { get; set; } = "";
    /// <summary>Total en todas las bodegas. Solo cambia con movimientos.</summary>
    public decimal StockActual { get; set; }
    public decimal StockMinimo { get; set; }
    public decimal? PrecioCompra { get; set; }
    public string? NitProveedor { get; set; }
    public bool Activo { get; set; } = true;

    // Datos que se leen de otras tablas o que calcula el backend (no se guardan en Producto).
    public string Categoria { get; set; } = "";
    public string? NombreProveedor { get; set; }
    public int? DiasEntregaProveedor { get; set; }
    public List<ExistenciaBodega> Existencias { get; set; } = [];
    public decimal ConsumoDiario { get; set; }
    public int? DiasRestantes { get; set; }
    public string Estado { get; set; } = "";

    /// <summary>Solo al crear: bodega que recibe el stock inicial (si es mayor que 0).</summary>
    public int? BodegaInicialId { get; set; }
}
