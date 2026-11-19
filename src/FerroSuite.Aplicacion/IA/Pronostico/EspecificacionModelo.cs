namespace FerroSuite.Aplicacion.IA.Pronostico;

/// <summary>
/// Parámetros del modelo de demanda que deben coincidir EXACTAMENTE con el entrenamiento
/// (ml/ferrosuite_demanda/config.py y caracteristicas.py). La infraestructura compara estos valores con el archivo
/// demanda_mlp_normalizacion.json al cargar el modelo y no lo usa si no coinciden.
/// </summary>
public static class EspecificacionModelo
{
    public const int Ventana = 28;
    public const int HistoriaMinima = 30;
    public const int Horizonte = 7;
    public const int MinDiasVentana = 14;
    public const double EpsilonEscala = 0.05;

    public static readonly IReadOnlyList<string> Categorias = ["Construcción", "Plomería", "Eléctricos", "Tornillería", "Pinturas", "Herramientas"];
    public static readonly IReadOnlyList<string> UnidadesContinuas = ["Metro", "Kilogramo"];

    /// <summary>Nombres de las 48 entradas, en el orden del modelo.</summary>
    public static readonly IReadOnlyList<string> NombresCaracteristicas = CrearNombres();

    public static int NumeroEntradas => NombresCaracteristicas.Count;

    private static List<string> CrearNombres()
    {
        var n = new List<string>();
        for (var d = Ventana - 1; d >= 0; d--) n.Add($"lag_{d:00}_rel");
        n.AddRange(["media7_rel", "media14_rel", "desv28_rel", "max28_rel", "prop_ceros", "prop_faltantes", "pendiente_rel", "log_escala",
                    "dow_sen", "dow_cos", "mes_sen", "mes_cos", "festivos_horizonte"]);
        n.AddRange(Categorias.Select(c => "cat_" + c));
        n.Add("unidad_continua");
        return n;
    }
}
