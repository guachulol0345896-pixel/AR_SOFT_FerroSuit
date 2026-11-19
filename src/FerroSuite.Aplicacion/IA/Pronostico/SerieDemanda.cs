namespace FerroSuite.Aplicacion.IA.Pronostico;

/// <summary>Totales de un día de un producto, leídos de los movimientos de inventario.</summary>
/// <param name="Ventas">Salidas con motivo "Venta".</param>
/// <param name="Devoluciones">Entradas con motivo "Devolución" (de clientes).</param>
/// <param name="DeltaStock">Entradas menos salidas de TODOS los motivos (para reconstruir el stock).</param>
public sealed record VentaDiaria(string Sku, DateOnly Dia, decimal Ventas, decimal Devoluciones, decimal DeltaStock);

/// <summary>Historial que entrega el repositorio: totales diarios y fecha del primer movimiento de cada producto.</summary>
public sealed record HistorialVentas(IReadOnlyList<VentaDiaria> Filas, IReadOnlyDictionary<string, DateOnly> PrimerMovimiento);

/// <summary>
/// Demanda diaria de un producto con el MISMO criterio del entrenamiento (ml/ferrosuite_demanda/movimientos.py):
///  * demanda = ventas − devoluciones, nunca negativa; los ajustes, daños, compras e inventario inicial no son demanda;
///  * antes del primer movimiento del producto → faltante (sin historia);
///  * stock al cierre de cada día = stock actual − movimientos de los días posteriores; si queda en 0 o menos y ese día
///    no hubo ventas → faltante (agotado: la demanda real no se observa).
/// Los faltantes se representan con double.NaN.
/// </summary>
public static class SerieDemanda
{
    /// <param name="filas">Totales diarios del producto (pueden incluir días posteriores a <paramref name="hasta"/>, hasta hoy).</param>
    public static double[] Construir(IEnumerable<VentaDiaria> filas, DateOnly? primerMovimiento, decimal stockActual, DateOnly desde, DateOnly hasta)
    {
        var porDia = filas.GroupBy(f => f.Dia).ToDictionary(g => g.Key,
            g => (Venta: g.Sum(f => f.Ventas) - g.Sum(f => f.Devoluciones), Delta: g.Sum(f => f.DeltaStock)));
        var n = hasta.DayNumber - desde.DayNumber + 1;
        var serie = new double[n];
        // Movimientos posteriores a 'hasta' (hasta hoy): el stock al cierre de 'hasta' es el actual menos esos movimientos.
        var stock = stockActual - porDia.Where(kv => kv.Key > hasta).Sum(kv => kv.Value.Delta);
        for (var i = n - 1; i >= 0; i--)
        {
            var dia = desde.AddDays(i);
            var (venta, delta) = porDia.TryGetValue(dia, out var v) ? v : (0m, 0m);
            var demanda = Math.Max(0m, venta);
            if (primerMovimiento is not DateOnly primero || dia < primero) serie[i] = double.NaN;
            else if (stock <= 0 && demanda == 0) serie[i] = double.NaN;
            else serie[i] = (double)demanda;
            stock -= delta; // stock al cierre del día anterior
        }
        return serie;
    }
}
