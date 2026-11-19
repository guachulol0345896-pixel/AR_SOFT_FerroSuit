using FerroSuite.Infraestructura.Seguridad;
using Microsoft.Data.SqlClient;

namespace FerroSuite.Infraestructura.Persistencia;

/// <summary>Usuarios y roles en SQL Server. La clave solo existe como hash.</summary>
public sealed class RepositorioUsuariosSql(BaseDatosSql baseDatos) : IRepositorioUsuarios
{
    private const string Consulta = """
        SELECT u.UsuarioId, u.NombreUsuario, u.NombreCompleto, u.RolId, r.Codigo, r.Nombre, u.Activo, u.FechaCreacion,
               u.ClaveIdentity, u.ClaveHash, u.ClaveSal, u.ClaveIteraciones,
               u.DebeCambiarClave, u.IntentosFallidos, u.BloqueadoHasta, u.UltimoIngreso
        FROM dbo.Usuario u
        JOIN dbo.Rol r ON r.RolId = u.RolId
        """;

    public List<Rol> ListarRoles()
    {
        using var c = baseDatos.Conectar();
        return Sql.Leer(c, "SELECT RolId, Codigo, Nombre FROM dbo.Rol ORDER BY RolId",
            r => new Rol { RolId = r.GetInt32(0), Codigo = r.GetString(1), Nombre = r.GetString(2) });
    }

    public List<Usuario> ListarUsuarios()
    {
        using var c = baseDatos.Conectar();
        return Sql.Leer(c, Consulta + " ORDER BY u.NombreUsuario", Leer);
    }

    public Usuario? BuscarPorNombreUsuario(string nombreUsuario)
    {
        using var c = baseDatos.Conectar();
        return Sql.Leer(c, Consulta + " WHERE u.NombreUsuario = @nombre", Leer, null, Sql.VarChar("@nombre", nombreUsuario, 40)).FirstOrDefault();
    }

    public Usuario? BuscarPorId(int usuarioId)
    {
        using var c = baseDatos.Conectar();
        return Sql.Leer(c, Consulta + " WHERE u.UsuarioId = @id", Leer, null, Sql.Int("@id", usuarioId)).FirstOrDefault();
    }

    public int Crear(Usuario u)
    {
        using var c = baseDatos.Conectar();
        return (int)Sql.Escalar(c, """
            INSERT INTO dbo.Usuario (NombreUsuario, NombreCompleto, RolId, ClaveIdentity, DebeCambiarClave, Activo)
            OUTPUT INSERTED.UsuarioId
            VALUES (@nombre, @completo, @rol, @hash, @debe, @activo)
            """, null, Sql.VarChar("@nombre", u.NombreUsuario, 40), Sql.NVarChar("@completo", u.NombreCompleto, 100), Sql.Int("@rol", u.RolId),
            Sql.NVarChar("@hash", u.ClaveHash, 256), Sql.Bit("@debe", u.DebeCambiarClave), Sql.Bit("@activo", u.Activo))!;
    }

    public void CambiarEstado(int usuarioId, bool activo)
    {
        using var c = baseDatos.Conectar();
        Sql.Ejecutar(c, "UPDATE dbo.Usuario SET Activo = @activo WHERE UsuarioId = @id", null, Sql.Bit("@activo", activo), Sql.Int("@id", usuarioId));
    }

    public void ActualizarDatos(int usuarioId, string nombreCompleto, int rolId)
    {
        using var c = baseDatos.Conectar();
        Sql.Ejecutar(c, "UPDATE dbo.Usuario SET NombreCompleto = @nombre, RolId = @rol WHERE UsuarioId = @id", null,
            Sql.NVarChar("@nombre", nombreCompleto, 100), Sql.Int("@rol", rolId), Sql.Int("@id", usuarioId));
    }

    public void CambiarClave(int usuarioId, string hash, bool debeCambiar)
    {
        using var c = baseDatos.Conectar();
        // El hash anterior (formato de la versión 2) se borra en la misma sentencia: nunca quedan dos formatos.
        Sql.Ejecutar(c, """
            UPDATE dbo.Usuario
            SET ClaveIdentity = @hash, ClaveHash = NULL, ClaveSal = NULL, ClaveIteraciones = NULL, DebeCambiarClave = @debe
            WHERE UsuarioId = @id
            """, null, Sql.NVarChar("@hash", hash, 256), Sql.Bit("@debe", debeCambiar), Sql.Int("@id", usuarioId));
    }

    public void RegistrarIngresoFallido(int usuarioId, int intentos, DateTime? bloqueadoHasta)
    {
        using var c = baseDatos.Conectar();
        Sql.Ejecutar(c, "UPDATE dbo.Usuario SET IntentosFallidos = @i, BloqueadoHasta = @b WHERE UsuarioId = @id", null,
            Sql.Int("@i", intentos), Sql.Fecha("@b", bloqueadoHasta), Sql.Int("@id", usuarioId));
    }

    public void RegistrarIngresoExitoso(int usuarioId, DateTime fecha)
    {
        using var c = baseDatos.Conectar();
        Sql.Ejecutar(c, "UPDATE dbo.Usuario SET IntentosFallidos = 0, BloqueadoHasta = NULL, UltimoIngreso = @f WHERE UsuarioId = @id", null,
            Sql.Fecha("@f", fecha), Sql.Int("@id", usuarioId));
    }

    public void Desbloquear(int usuarioId)
    {
        using var c = baseDatos.Conectar();
        Sql.Ejecutar(c, "UPDATE dbo.Usuario SET IntentosFallidos = 0, BloqueadoHasta = NULL WHERE UsuarioId = @id", null, Sql.Int("@id", usuarioId));
    }

    public bool ExisteAdministradorActivo()
    {
        using var c = baseDatos.Conectar();
        return Sql.Escalar(c, """
            SELECT TOP (1) 1 FROM dbo.Usuario u JOIN dbo.Rol r ON r.RolId = u.RolId
            WHERE r.Codigo = @admin AND u.Activo = 1 AND (u.ClaveIdentity IS NOT NULL OR u.ClaveHash IS NOT NULL)
            """, null, Sql.VarChar("@admin", Rol.Administrador, 20)) is not null;
    }

    private static Usuario Leer(SqlDataReader r)
    {
        var anterior = r.BinarioONulo(9);
        return new Usuario
        {
            UsuarioId = r.GetInt32(0), NombreUsuario = r.GetString(1), NombreCompleto = r.GetString(2), RolId = r.GetInt32(3),
            RolCodigo = r.GetString(4), RolNombre = r.GetString(5), Activo = r.GetBoolean(6), FechaCreacion = r.GetDateTime(7),
            // Hash vigente (Identity) o, si el usuario aún tiene el de la versión 2, ese en forma de texto para verificarlo una vez.
            ClaveHash = r.TextoONulo(8) ?? (anterior is null ? null : HasherIdentity.FormatoAnterior(anterior, r.BinarioONulo(10)!, r.GetInt32(11))),
            DebeCambiarClave = r.GetBoolean(12), IntentosFallidos = r.GetInt32(13), BloqueadoHasta = r.FechaONula(14), UltimoIngreso = r.FechaONula(15),
        };
    }
}
