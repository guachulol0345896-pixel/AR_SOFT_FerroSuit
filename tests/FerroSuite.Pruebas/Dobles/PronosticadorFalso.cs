using FerroSuite.Aplicacion.IA.Pronostico;

namespace FerroSuite.Pruebas.Dobles;

/// <summary>Sustituto del modelo para pruebas unitarias: devuelve un valor fijo, falla o no está disponible.</summary>
public sealed class PronosticadorFalso(float valor = 2f, bool disponible = true, bool falla = false) : IPronosticadorDemanda
{
    public int Llamadas { get; private set; }
    public List<EntradaModelo> UltimasEntradas { get; } = [];
    public EstadoModelo Estado { get; } = new(disponible, "modelo falso de pruebas", disponible ? null : "archivo no encontrado (simulado)", true);

    public float[] Predecir(IReadOnlyList<EntradaModelo> entradas)
    {
        Llamadas++;
        UltimasEntradas.Clear();
        UltimasEntradas.AddRange(entradas);
        if (falla) throw new InvalidOperationException("Falla simulada del modelo");
        return entradas.Select(_ => valor).ToArray();
    }
}
