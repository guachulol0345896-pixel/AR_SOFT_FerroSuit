namespace FerroSuite.Aplicacion.IA.Pronostico;

/// <summary>Entradas listas para el modelo: 48 valores sin normalizar (el modelo normaliza) y la escala.</summary>
public sealed record EntradaModelo(float[] Caracteristicas, float Escala);

/// <summary>
/// Prepara las 48 entradas del modelo EXACTAMENTE como en el entrenamiento
/// (ml/ferrosuite_demanda/caracteristicas.py, función caracteristicas_de_ventana). Los cálculos se hacen en double y se
/// convierten a float al final, igual que en Python. Las pruebas comparan contra casos exportados desde Python.
/// </summary>
public static class PreparadorCaracteristicas
{
    /// <param name="ventana">Demanda de los 28 días que terminan en <paramref name="fechaOrigen"/> (NaN = faltante).</param>
    /// <returns>null si hay menos de 14 días con dato (no se debe pronosticar).</returns>
    public static EntradaModelo? Preparar(ReadOnlySpan<double> ventana, DateOnly fechaOrigen, string categoria, string unidad, Func<DateOnly, bool> esFestivo)
    {
        if (ventana.Length != EspecificacionModelo.Ventana) throw new ArgumentException($"La ventana debe tener {EspecificacionModelo.Ventana} días.");
        var observados = new List<double>();
        foreach (var x in ventana) if (!double.IsNaN(x)) observados.Add(x);
        if (observados.Count < EspecificacionModelo.MinDiasVentana) return null;

        observados.Sort();
        var mitad = observados.Count / 2;
        var mediana = observados.Count % 2 == 1 ? observados[mitad] : (observados[mitad - 1] + observados[mitad]) / 2.0;
        var n = ventana.Length;
        var v = new double[n];
        for (var i = 0; i < n; i++) v[i] = double.IsNaN(ventana[i]) ? mediana : ventana[i];
        var escala = Promedio(v, 0, n) + EspecificacionModelo.EpsilonEscala;
        var rel = v.Select(x => x / escala).ToArray();

        var media = Promedio(rel, 0, n);
        var varianza = rel.Sum(x => (x - media) * (x - media)) / n;          // desviación poblacional, como numpy
        var xm = (n - 1) / 2.0;
        double num = 0, den = 0;
        for (var i = 0; i < n; i++) { num += (i - xm) * (rel[i] - media); den += (i - xm) * (i - xm); }

        var dow = ((int)fechaOrigen.DayOfWeek + 6) % 7;                      // lunes = 0
        var mesMedio = fechaOrigen.AddDays(1 + EspecificacionModelo.Horizonte / 2).Month;
        var festivos = 0;
        for (var d = 1; d <= EspecificacionModelo.Horizonte; d++) if (esFestivo(fechaOrigen.AddDays(d))) festivos++;

        var resultado = new List<double>(EspecificacionModelo.NumeroEntradas);
        resultado.AddRange(rel);
        resultado.Add(Promedio(rel, n - 7, 7));
        resultado.Add(Promedio(rel, n - 14, 14));
        resultado.Add(Math.Sqrt(varianza));
        resultado.Add(rel.Max());
        resultado.Add(v.Count(x => x == 0) / (double)n);
        resultado.Add(1.0 - observados.Count / (double)n);
        resultado.Add(num / den * EspecificacionModelo.Ventana);
        resultado.Add(Math.Log(1.0 + escala));
        resultado.Add(Math.Sin(2 * Math.PI * dow / 7));
        resultado.Add(Math.Cos(2 * Math.PI * dow / 7));
        resultado.Add(Math.Sin(2 * Math.PI * (mesMedio - 1) / 12));
        resultado.Add(Math.Cos(2 * Math.PI * (mesMedio - 1) / 12));
        resultado.Add(festivos / (double)EspecificacionModelo.Horizonte);
        foreach (var c in EspecificacionModelo.Categorias) resultado.Add(categoria == c ? 1.0 : 0.0);
        resultado.Add(EspecificacionModelo.UnidadesContinuas.Contains(unidad) ? 1.0 : 0.0);

        return new EntradaModelo(resultado.Select(x => (float)x).ToArray(), (float)escala);
    }

    private static double Promedio(double[] a, int desde, int cuantos)
    {
        double s = 0;
        for (var i = desde; i < desde + cuantos; i++) s += a[i];
        return s / cuantos;
    }
}
