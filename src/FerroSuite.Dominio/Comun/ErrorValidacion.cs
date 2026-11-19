namespace FerroSuite.Dominio.Comun;

/// <summary>Error de datos ingresados: lleva la lista de mensajes para mostrar al usuario.</summary>
public class ErrorValidacion(List<string> errores) : Exception(string.Join(" ", errores))
{
    public List<string> Errores { get; } = errores;
}
