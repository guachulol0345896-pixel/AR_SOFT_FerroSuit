namespace FerroSuite.Infraestructura.Persistencia;

/// <summary>Implementa el puerto de mantenimiento: solo puede reconstruir la base de demostración.</summary>
public sealed class MantenimientoSqlServer(BaseDatosSql baseDatos) : IMantenimientoDatos
{
    public bool EsDemo => baseDatos.EsDemo;

    public void RestablecerDemo()
    {
        if (!baseDatos.EsDemo) throw new ErrorValidacion(["Esta acción solo existe en modo demostración. La base real nunca se borra."]);
        baseDatos.RestablecerDemo();
    }
}
