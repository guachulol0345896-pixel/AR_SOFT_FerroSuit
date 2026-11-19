namespace FerroSuite.Dominio.Seguridad;

/// <summary>
/// Registro de una operación sensible (tabla AuditoriaEvento). Nunca contiene claves, hash ni otros secretos:
/// solo quién, cuándo, qué operación, sobre qué y con qué resultado.
/// </summary>
public class EventoAuditoria
{
    public long EventoId { get; set; }
    public DateTime Fecha { get; set; }
    public int? UsuarioId { get; set; }
    /// <summary>Nombre de usuario en ese momento (o el que se intentó usar al ingresar).</summary>
    public string NombreUsuario { get; set; } = "";
    public string? RolCodigo { get; set; }
    /// <summary>Identificador aleatorio de la sesión, para relacionar los eventos de una misma sesión.</summary>
    public string? SesionId { get; set; }
    public string Accion { get; set; } = "";
    public string Resultado { get; set; } = "";
    /// <summary>Sobre qué se hizo (por ejemplo "Producto FER-0001", "Orden OC-004").</summary>
    public string? Objeto { get; set; }
    public string? Detalle { get; set; }
    public string? Equipo { get; set; }
}

public static class ResultadosAuditoria
{
    public const string Exitoso = "Exitoso";
    public const string Fallido = "Fallido";
    public const string Denegado = "Denegado";
}
