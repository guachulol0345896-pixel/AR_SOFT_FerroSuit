namespace FerroSuite.Dominio.Inventario;

/// <summary>Cantidad de un producto en una bodega (tabla ExistenciaBodega).</summary>
public class ExistenciaBodega
{
    public string Sku { get; set; } = "";
    public int BodegaId { get; set; }
    public string NombreBodega { get; set; } = "";
    public decimal Cantidad { get; set; }
}
