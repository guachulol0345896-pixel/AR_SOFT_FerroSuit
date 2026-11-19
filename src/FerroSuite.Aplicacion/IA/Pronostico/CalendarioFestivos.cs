namespace FerroSuite.Aplicacion.IA.Pronostico;

/// <summary>
/// Festivos de Colombia calculados para cualquier año: fijos, trasladables al lunes siguiente (Ley 51 de 1983,
/// "Ley Emiliani") y los que dependen de la Pascua. Es una entrada del modelo de demanda (festivos en los 7 días
/// pronosticados). Las pruebas comprueban que coincide con los festivos usados al entrenar (2023-2024).
/// </summary>
public static class CalendarioFestivos
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, HashSet<DateOnly>> Cache = new();

    public static bool EsFestivo(DateOnly dia) => DelAnio(dia.Year).Contains(dia);

    public static IReadOnlySet<DateOnly> DelAnio(int anio) => Cache.GetOrAdd(anio, Calcular);

    private static HashSet<DateOnly> Calcular(int anio)
    {
        DateOnly D(int mes, int dia) => new(anio, mes, dia);
        static DateOnly LunesSiguiente(DateOnly d) => d.DayOfWeek == DayOfWeek.Monday ? d : d.AddDays(((int)DayOfWeek.Monday - (int)d.DayOfWeek + 7) % 7);
        var pascua = DomingoDePascua(anio);
        return
        [
            // Fijos
            D(1, 1), D(5, 1), D(7, 20), D(8, 7), D(12, 8), D(12, 25),
            // Se trasladan al lunes siguiente
            LunesSiguiente(D(1, 6)), LunesSiguiente(D(3, 19)), LunesSiguiente(D(6, 29)), LunesSiguiente(D(8, 15)),
            LunesSiguiente(D(10, 12)), LunesSiguiente(D(11, 1)), LunesSiguiente(D(11, 11)),
            // Dependen de la Pascua
            pascua.AddDays(-3), pascua.AddDays(-2),                 // Jueves y Viernes Santo
            LunesSiguiente(pascua.AddDays(39)),                     // Ascensión del Señor
            LunesSiguiente(pascua.AddDays(60)),                     // Corpus Christi
            LunesSiguiente(pascua.AddDays(68)),                     // Sagrado Corazón
        ];
    }

    /// <summary>Domingo de Pascua (algoritmo anónimo gregoriano, Meeus/Jones/Butcher).</summary>
    public static DateOnly DomingoDePascua(int anio)
    {
        int a = anio % 19, b = anio / 100, c = anio % 100, d = b / 4, e = b % 4, f = (b + 8) / 25, g = (b - f + 1) / 3;
        int h = (19 * a + b - d - g + 15) % 30, i = c / 4, k = c % 4, l = (32 + 2 * e + 2 * i - h - k) % 7, m = (a + 11 * h + 22 * l) / 451;
        int mes = (h + l - 7 * m + 114) / 31, dia = (h + l - 7 * m + 114) % 31 + 1;
        return new DateOnly(anio, mes, dia);
    }
}
