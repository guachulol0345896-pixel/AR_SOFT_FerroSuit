namespace FerroSuite.Aplicacion.Puertos;

/// <summary>Persistencia de usuarios y roles.</summary>
public interface IRepositorioUsuarios
{
    List<Rol> ListarRoles();
    List<Usuario> ListarUsuarios();
    Usuario? BuscarPorNombreUsuario(string nombreUsuario);
    Usuario? BuscarPorId(int usuarioId);
    int Crear(Usuario usuario);
    void CambiarEstado(int usuarioId, bool activo);
    /// <summary>Cambia el nombre completo y el rol (el nombre de usuario y la clave no cambian aquí).</summary>
    void ActualizarDatos(int usuarioId, string nombreCompleto, int rolId);
    /// <summary>Guarda el nuevo hash (borra el de formato anterior, si había) y si debe cambiarla al ingresar.</summary>
    void CambiarClave(int usuarioId, string hash, bool debeCambiar);
    void RegistrarIngresoFallido(int usuarioId, int intentos, DateTime? bloqueadoHasta);
    void RegistrarIngresoExitoso(int usuarioId, DateTime fecha);
    void Desbloquear(int usuarioId);
    /// <summary>¿Hay al menos un administrador activo que pueda iniciar sesión?</summary>
    bool ExisteAdministradorActivo();
}
