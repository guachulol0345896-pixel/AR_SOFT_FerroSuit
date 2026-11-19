namespace FerroSuite.Dominio.Inventario;

/// <summary>Categoría de producto (tabla CategoriaProducto).</summary>
public class Categoria
{
    public int CategoriaId { get; set; }
    public string Nombre { get; set; } = "";
    public bool Activa { get; set; } = true;
}
