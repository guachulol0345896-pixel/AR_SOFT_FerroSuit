namespace FerroSuite.Aplicacion.Seguridad;

/// <summary>
/// Comprueba permisos antes de cada operación (en el backend). Si no hay sesión o el rol no tiene el permiso,
/// registra el intento en la auditoría y lanza <see cref="ErrorAutorizacion"/> con un mensaje seguro.
/// </summary>
public sealed class Autorizador(AuditoriaServicio auditoria)
{
    public AuditoriaServicio Auditoria { get; } = auditoria;

    public void Exigir(SesionUsuario? sesion, string permiso, string accion) => ExigirAlguno(sesion, accion, permiso);

    public void ExigirAlguno(SesionUsuario? sesion, string accion, params string[] permisos)
    {
        if (sesion is null)
            throw new ErrorAutorizacion("Tu sesión no está iniciada o venció. Ingresa con tu usuario y clave.", sinSesion: true);
        if (sesion.DebeCambiarClave)
            throw new ErrorAutorizacion("Debes cambiar tu clave antes de continuar.");
        if (permisos.Any(p => Permisos.Tiene(sesion, p))) return;
        Auditoria.Registrar(sesion, accion, ResultadosAuditoria.Denegado, detalle: "Permiso requerido: " + string.Join(" o ", permisos));
        throw new ErrorAutorizacion($"Tu rol ({sesion.RolNombre}) no permite esta operación.");
    }
}
