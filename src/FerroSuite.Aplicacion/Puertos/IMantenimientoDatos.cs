namespace FerroSuite.Aplicacion.Puertos;

/// <summary>Mantenimiento de la base de datos (la implementación está en Infraestructura).</summary>
public interface IMantenimientoDatos
{
    /// <summary>¿El programa está usando la base de demostración?</summary>
    bool EsDemo { get; }

    /// <summary>Vuelve a crear la base de demostración. Nunca toca la base real.</summary>
    void RestablecerDemo();
}
