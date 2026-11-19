namespace FerroSuite.Aplicacion.Puertos;

/// <summary>
/// Servicio externo que redacta textos a partir de cifras ya calculadas (hoy: Gemini, opcional).
/// La capa de Aplicación solo conoce esta interfaz; la implementación está en Infraestructura.
/// Si devuelve null, el asistente responde con sus reglas de respaldo.
/// </summary>
public interface IGeneradorTexto
{
    bool Configurado { get; }
    string Modelo { get; }
    Task<string?> GenerarAsync(string instrucciones, string mensaje, CancellationToken ct);
}
