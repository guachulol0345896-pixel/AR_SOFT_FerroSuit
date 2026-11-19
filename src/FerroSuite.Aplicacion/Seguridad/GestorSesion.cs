namespace FerroSuite.Aplicacion.Seguridad;

/// <summary>
/// Sesión del programa (una por ventana). Vence por inactividad (30 minutos por defecto) y, en todo caso,
/// a las 12 horas de iniciada. Es seguro entre hilos.
/// </summary>
public sealed class GestorSesion(TimeProvider reloj, TimeSpan? inactividad = null, TimeSpan? duracionMaxima = null)
{
    public TimeSpan Inactividad { get; } = inactividad ?? TimeSpan.FromMinutes(30);
    public TimeSpan DuracionMaxima { get; } = duracionMaxima ?? TimeSpan.FromHours(12);

    private readonly Lock candado = new();
    private SesionUsuario? sesion;
    private DateTimeOffset inicio, ultimaActividad;

    /// <summary>Se dispara cuando la sesión empieza, cambia, termina o vence (para habilitar menús en la ventana).</summary>
    public event Action<SesionUsuario?>? Cambio;

    public void Iniciar(SesionUsuario nueva)
    {
        lock (candado) { sesion = nueva; inicio = ultimaActividad = reloj.GetUtcNow(); }
        Cambio?.Invoke(nueva);
    }

    public void Actualizar(SesionUsuario cambiada)
    {
        lock (candado) { if (sesion?.SesionId == cambiada.SesionId) sesion = cambiada; else return; }
        Cambio?.Invoke(cambiada);
    }

    /// <summary>Sesión vigente, o null. Si estaba vencida, la cierra y devuelve la que venció en <paramref name="vencida"/>.</summary>
    public SesionUsuario? Actual(out SesionUsuario? vencida, bool registrarActividad = true)
    {
        vencida = null;
        SesionUsuario? actual;
        lock (candado)
        {
            if (sesion is null) return null;
            var ahora = reloj.GetUtcNow();
            if (ahora - ultimaActividad > Inactividad || ahora - inicio > DuracionMaxima)
            {
                vencida = sesion;
                sesion = null;
            }
            else if (registrarActividad) ultimaActividad = ahora;
            actual = sesion;
        }
        if (vencida is not null) Cambio?.Invoke(null);
        return actual;
    }

    public SesionUsuario? Actual() => Actual(out _, registrarActividad: false);

    public SesionUsuario? Cerrar()
    {
        SesionUsuario? anterior;
        lock (candado) { anterior = sesion; sesion = null; }
        if (anterior is not null) Cambio?.Invoke(null);
        return anterior;
    }
}
