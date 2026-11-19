namespace FerroSuite.Dominio.Compras;

/// <summary>Estados de una orden de compra (los mismos que permite la restricción CK_Orden_Estado).</summary>
public static class EstadosOrden
{
    public const string Pendiente = "Pendiente de aprobación";
    public const string Aprobada = "Aprobada";
    public const string Rechazada = "Rechazada";
    public const string Recibida = "Recibida";
}
