namespace FerroSuite.Dominio.Inventario;

/// <summary>Entrada o salida de mercancía (tabla MovimientoInventario). No se modifica ni se borra.</summary>
public class Movimiento
{
    public int MovimientoId { get; set; }
    public string Sku { get; set; } = "";
    public string? NombreProducto { get; set; }
    public string? Unidad { get; set; }
    public string Tipo { get; set; } = "";
    public decimal Cantidad { get; set; }
    public string Motivo { get; set; } = "";
    public int BodegaId { get; set; }
    public string? NombreBodega { get; set; }
    /// <summary>Usuario responsable (lo pone el backend a partir de la sesión, nunca la pantalla).</summary>
    public int UsuarioId { get; set; }
    /// <summary>Nombre del responsable tal como se mostraba al registrar (texto histórico).</summary>
    public string Usuario { get; set; } = "";
    /// <summary>Referencia opcional, por ejemplo el código de la orden de compra recibida.</summary>
    public string? Referencia { get; set; }
    public DateTime Fecha { get; set; }
}
