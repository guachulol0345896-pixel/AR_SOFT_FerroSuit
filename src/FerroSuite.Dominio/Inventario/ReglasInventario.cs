namespace FerroSuite.Dominio.Inventario;

/// <summary>Listas y reglas fijas del inventario.</summary>
public static class ReglasInventario
{
    public const string Entrada = "Entrada";
    public const string Salida = "Salida";

    public static readonly string[] Unidades = ["Unidad", "Metro", "Kilogramo", "Varilla", "Bulto", "Galón"];
    public static readonly string[] UnidadesConDecimales = ["Metro", "Kilogramo"];

    public const string MotivoInventarioInicial = "Inventario inicial";
    public const string MotivoCompra = "Compra a proveedor";

    /// <summary>Motivo y el tipo con el que tiene sentido ("Ambos" = entrada o salida).</summary>
    public static readonly IReadOnlyDictionary<string, string> Motivos = new Dictionary<string, string>
    {
        [MotivoCompra] = Entrada,
        ["Venta"] = Salida,
        ["Devolución"] = Entrada,
        ["Ajuste por conteo"] = "Ambos",
        ["Daño o pérdida"] = Salida,
        [MotivoInventarioInicial] = Entrada,
    };

    public static bool AdmiteDecimales(string unidad) => UnidadesConDecimales.Contains(unidad);

    public static bool EsCantidadValida(decimal cantidad, string unidad) =>
        cantidad > 0 && (AdmiteDecimales(unidad) || cantidad == Math.Floor(cantidad));
}
