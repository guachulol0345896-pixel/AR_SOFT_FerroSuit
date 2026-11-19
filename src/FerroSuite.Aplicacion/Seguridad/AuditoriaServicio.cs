using System.Diagnostics;
using System.Text;

namespace FerroSuite.Aplicacion.Seguridad;

/// <summary>
/// Registra las operaciones sensibles. Reglas: nunca se le pasan claves, hash ni claves de API; todo texto se limpia
/// (sin caracteres de control) y se recorta. Si el registro falla, la operación no se interrumpe, pero queda el aviso
/// en la salida de depuración (sin datos del evento).
/// </summary>
public sealed class AuditoriaServicio(IRepositorioAuditoria repositorio, TimeProvider reloj, string? equipo = null)
{
    private readonly string equipo = Limpiar(equipo ?? Environment.MachineName, 64) ?? "";

    public void Registrar(SesionUsuario? sesion, string accion, string resultado, string? objeto = null, string? detalle = null,
        string? nombreUsuarioIntentado = null)
    {
        var evento = new EventoAuditoria
        {
            Fecha = reloj.GetLocalNow().DateTime,
            UsuarioId = sesion?.UsuarioId,
            NombreUsuario = Limpiar(sesion?.NombreUsuario ?? nombreUsuarioIntentado ?? "(sin sesión)", 60) ?? "(sin sesión)",
            RolCodigo = sesion?.RolCodigo,
            SesionId = string.IsNullOrEmpty(sesion?.SesionId) ? null : sesion.SesionId,
            Accion = Limpiar(accion, 60) ?? "desconocida",
            Resultado = resultado,
            Objeto = Limpiar(objeto, 120),
            Detalle = Limpiar(detalle, 400),
            Equipo = equipo,
        };
        try { repositorio.Registrar(evento); }
        catch (Exception ex)
        {
            // La operación no se bloquea, pero el evento tampoco se pierde: queda en el registro técnico (sin claves:
            // los textos ya pasaron por Limpiar y nunca incluyen contraseñas).
            Trace.WriteLine($"AUDITORÍA NO GUARDADA ({ex.GetType().Name}): {evento.Fecha:yyyy-MM-dd HH:mm:ss} · {evento.NombreUsuario} · " +
                            $"{evento.Accion} · {evento.Resultado} · {evento.Objeto} · {evento.Detalle}");
        }
    }

    public void Exito(SesionUsuario? s, string accion, string? objeto = null, string? detalle = null) =>
        Registrar(s, accion, ResultadosAuditoria.Exitoso, objeto, detalle);

    public List<EventoAuditoria> Listar(SesionUsuario? sesion, Autorizador autorizador, int limite, string? accion = null, int? usuarioId = null)
    {
        autorizador.Exigir(sesion, Permisos.VerAuditoria, "auditoria.listar");
        return repositorio.Listar(Math.Clamp(limite, 1, 1000), string.IsNullOrWhiteSpace(accion) ? null : Limpiar(accion, 60), usuarioId);
    }

    /// <summary>Quita caracteres de control (saltos de línea incluidos, para que nadie falsifique líneas) y recorta.</summary>
    public static string? Limpiar(string? texto, int largo)
    {
        if (string.IsNullOrWhiteSpace(texto)) return null;
        var sb = new StringBuilder(Math.Min(texto.Length, largo));
        foreach (var ch in texto.Trim())
        {
            if (sb.Length >= largo) break;
            sb.Append(char.IsControl(ch) ? ' ' : ch);
        }
        return sb.ToString();
    }
}
