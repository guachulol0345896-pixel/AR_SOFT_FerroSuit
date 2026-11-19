using FerroSuite.Aplicacion.IA.Pronostico;

namespace FerroSuite.Aplicacion.Puertos;

/// <summary>Estado del modelo de pronóstico (para mostrarlo en pantalla y decidir si usar el respaldo).</summary>
public sealed record EstadoModelo(bool Disponible, string Descripcion, string? Motivo, bool DatosSinteticos);

/// <summary>
/// Ejecuta el modelo neuronal de demanda (la implementación usa ONNX Runtime en Infraestructura, localmente, sin
/// servicios externos). Recibe entradas ya preparadas y devuelve el consumo diario estimado de cada una.
/// </summary>
public interface IPronosticadorDemanda
{
    EstadoModelo Estado { get; }

    /// <summary>Lanza una excepción si el modelo falla; quien llama debe usar el respaldo.</summary>
    float[] Predecir(IReadOnlyList<EntradaModelo> entradas);
}
