namespace FerroSuite.Dominio.Usuarios;

/// <summary>Rol de un usuario (tabla Rol). Los códigos son fijos.</summary>
public class Rol
{
    public const string Administrador = "ADMIN";
    public const string Bodega = "BODEGA";
    public const string Vendedor = "VENDEDOR";
    public const string Contador = "CONTADOR";

    public int RolId { get; set; }
    public string Codigo { get; set; } = "";
    public string Nombre { get; set; } = "";
}
