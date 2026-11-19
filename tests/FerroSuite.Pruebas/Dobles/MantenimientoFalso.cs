namespace FerroSuite.Pruebas.Dobles;

/// <summary>Sustituto del mantenimiento de la base: cuenta cuántas veces se pidió reconstruir la demostración.</summary>
public sealed class MantenimientoFalso(bool esDemo = false) : IMantenimientoDatos
{
    public bool EsDemo { get; } = esDemo;
    public int VecesRestablecido { get; private set; }
    public void RestablecerDemo()
    {
        if (!EsDemo) throw new ErrorValidacion(["Esta acción solo existe en modo demostración. La base real nunca se borra."]);
        VecesRestablecido++;
    }
}
