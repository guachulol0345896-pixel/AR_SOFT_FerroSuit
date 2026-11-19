namespace FerroSuite.Dominio.Proveedores;

/// <summary>Empresa a la que la ferretería le compra (tabla Proveedor).</summary>
public class Proveedor
{
    public string Nit { get; set; } = "";
    public string Nombre { get; set; } = "";
    public string? Telefono { get; set; }
    public int DiasEntrega { get; set; }
    /// <summary>Valor heredado de la versión anterior (se escribía a mano); todavía no se calcula.</summary>
    public decimal? Cumplimiento { get; set; }
    public bool Activo { get; set; } = true;
}
