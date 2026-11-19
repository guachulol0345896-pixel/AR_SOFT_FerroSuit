namespace FerroSuite.Aplicacion.Seguridad;

/// <summary>El usuario no tiene sesión o su rol no permite la operación. El mensaje es seguro para mostrar.</summary>
public sealed class ErrorAutorizacion(string mensaje, bool sinSesion = false) : Exception(mensaje)
{
    public bool SinSesion { get; } = sinSesion;
}
