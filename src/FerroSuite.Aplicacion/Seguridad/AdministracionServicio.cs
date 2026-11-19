namespace FerroSuite.Aplicacion.Seguridad;

/// <summary>
/// Operaciones de administración del programa que no pasan por las pantallas (menú de la ventana):
/// cambios de configuración y reconstrucción de la base de demostración. Exigen permiso y quedan en la auditoría.
/// </summary>
public sealed class AdministracionServicio(Autorizador autorizador, IMantenimientoDatos mantenimiento)
{
    public bool EsDemo => mantenimiento.EsDemo;

    /// <summary>
    /// Ejecuta un cambio de configuración (por ejemplo, la clave de Gemini) si el usuario tiene permiso.
    /// <paramref name="descripcion"/> describe el cambio sin incluir valores secretos.
    /// </summary>
    public void CambiarConfiguracion(SesionUsuario? sesion, string accion, string descripcion, Action cambio)
    {
        autorizador.Exigir(sesion, Permisos.ModificarConfiguracion, accion);
        try
        {
            cambio();
        }
        catch (Exception ex)
        {
            autorizador.Auditoria.Registrar(sesion, accion, ResultadosAuditoria.Fallido, detalle: descripcion + " — " + ex.GetType().Name);
            throw;
        }
        autorizador.Auditoria.Exito(sesion, accion, detalle: descripcion);
    }

    public void RestablecerDemo(SesionUsuario? sesion)
    {
        autorizador.Exigir(sesion, Permisos.RestablecerDemo, "datos.restablecer");
        if (!mantenimiento.EsDemo)
        {
            autorizador.Auditoria.Registrar(sesion, "datos.restablecer", ResultadosAuditoria.Denegado, detalle: "No es la base de demostración");
            throw new ErrorValidacion(["Esta acción solo existe en modo demostración. La base real nunca se borra."]);
        }
        // La auditoría de la base de demostración se borra con ella; el evento se registra antes.
        autorizador.Auditoria.Exito(sesion, "datos.restablecer", detalle: "Base de demostración reconstruida");
        mantenimiento.RestablecerDemo();
    }
}
