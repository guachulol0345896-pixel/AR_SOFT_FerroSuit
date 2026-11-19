using Microsoft.Data.SqlClient;

namespace FerroSuite.Infraestructura.Persistencia;

/// <summary>Registro de auditoría en SQL Server (la tabla solo admite inserciones).</summary>
public sealed class RepositorioAuditoriaSql(BaseDatosSql baseDatos) : IRepositorioAuditoria
{
    public void Registrar(EventoAuditoria e)
    {
        using var c = baseDatos.Conectar();
        Sql.Ejecutar(c, """
            INSERT INTO dbo.AuditoriaEvento (Fecha, UsuarioId, NombreUsuario, RolCodigo, SesionId, Accion, Resultado, Objeto, Detalle, Equipo)
            VALUES (@fecha, @usuarioId, @nombre, @rol, @sesion, @accion, @resultado, @objeto, @detalle, @equipo)
            """, null,
            Sql.Fecha("@fecha", e.Fecha), Sql.Int("@usuarioId", e.UsuarioId), Sql.NVarChar("@nombre", e.NombreUsuario, 60),
            Sql.VarChar("@rol", e.RolCodigo, 20), Sql.VarChar("@sesion", e.SesionId, 32), Sql.VarChar("@accion", e.Accion, 60),
            Sql.VarChar("@resultado", e.Resultado, 10), Sql.NVarChar("@objeto", e.Objeto, 120), Sql.NVarChar("@detalle", e.Detalle, 400),
            Sql.NVarChar("@equipo", e.Equipo, 64));
    }

    public List<EventoAuditoria> Listar(int limite, string? accion = null, int? usuarioId = null)
    {
        using var c = baseDatos.Conectar();
        return Sql.Leer(c, """
            SELECT TOP (@limite) EventoId, Fecha, UsuarioId, NombreUsuario, RolCodigo, SesionId, Accion, Resultado, Objeto, Detalle, Equipo
            FROM dbo.AuditoriaEvento
            WHERE (@accion IS NULL OR Accion = @accion) AND (@usuarioId IS NULL OR UsuarioId = @usuarioId)
            ORDER BY EventoId DESC
            """, r => new EventoAuditoria
            {
                EventoId = r.GetInt64(0), Fecha = r.GetDateTime(1), UsuarioId = r.EnteroONulo(2), NombreUsuario = r.GetString(3),
                RolCodigo = r.TextoONulo(4), SesionId = r.TextoONulo(5), Accion = r.GetString(6), Resultado = r.GetString(7),
                Objeto = r.TextoONulo(8), Detalle = r.TextoONulo(9), Equipo = r.TextoONulo(10),
            }, null, Sql.Int("@limite", limite), Sql.VarChar("@accion", accion, 60), Sql.Int("@usuarioId", usuarioId));
    }
}
