namespace FerroSuite.Dominio.Inventario;

/// <summary>Lugar donde se guarda la mercancía (tabla Bodega).</summary>
public class Bodega
{
    public int BodegaId { get; set; }
    public string Nombre { get; set; } = "";
    public string? Direccion { get; set; }
    public bool Activa { get; set; } = true;
}
