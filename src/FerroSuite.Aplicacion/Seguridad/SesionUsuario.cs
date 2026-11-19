namespace FerroSuite.Aplicacion.Seguridad;

/// <summary>Usuario que inició sesión. La guarda el backend (GestorSesion); la pantalla no puede cambiarla.</summary>
public sealed record SesionUsuario(int UsuarioId, string NombreUsuario, string NombreCompleto, string RolCodigo, string RolNombre)
{
    /// <summary>Identificador aleatorio de esta sesión (para relacionar los eventos de auditoría).</summary>
    public string SesionId { get; init; } = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16));

    /// <summary>Si es true, solo puede cambiar su clave o cerrar sesión.</summary>
    public bool DebeCambiarClave { get; init; }

    public bool EsAdministrador => RolCodigo == Rol.Administrador;

    /// <summary>Permisos del rol (para que la pantalla muestre solo lo que corresponde; el backend vuelve a verificarlos).</summary>
    public IReadOnlyCollection<string> PermisosDelRol => Permisos.DelRol(RolCodigo);
}
