using System.Text.Json.Serialization;

namespace FerroSuite.Dominio.Usuarios;

/// <summary>
/// Persona que usa el programa (tabla Usuario). La clave nunca se guarda: solo su hash
/// (formato de ASP.NET Core Identity). Ni el hash ni los datos de bloqueo se envían a la pantalla.
/// </summary>
public class Usuario
{
    public int UsuarioId { get; set; }
    public string NombreUsuario { get; set; } = "";
    public string NombreCompleto { get; set; } = "";
    public int RolId { get; set; }
    public string RolCodigo { get; set; } = "";
    public string RolNombre { get; set; } = "";
    public bool Activo { get; set; } = true;
    public DateTime FechaCreacion { get; set; }

    /// <summary>Hash de la clave (null = el usuario no puede iniciar sesión).</summary>
    [JsonIgnore] public string? ClaveHash { get; set; }

    /// <summary>Si es true, al ingresar debe cambiar la clave (por ejemplo, después de que el administrador la restableció).</summary>
    public bool DebeCambiarClave { get; set; }

    /// <summary>Intentos fallidos seguidos. Al llegar al límite, la cuenta se bloquea un tiempo.</summary>
    [JsonIgnore] public int IntentosFallidos { get; set; }
    public DateTime? BloqueadoHasta { get; set; }
    public DateTime? UltimoIngreso { get; set; }

    public bool TieneClave => ClaveHash is not null;
}
