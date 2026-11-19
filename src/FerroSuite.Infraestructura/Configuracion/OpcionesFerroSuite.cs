using System.Text.Json;

namespace FerroSuite.Infraestructura.Configuracion;

/// <summary>
/// Opciones del programa leídas de appsettings.json (junto al ejecutable). El archivo NO debe
/// contener credenciales: la clave de Gemini se guarda aparte, en la carpeta del usuario (ClaveGemini).
/// Si el archivo no existe o está mal escrito, se usan los valores por defecto y se informa el motivo.
/// </summary>
public sealed class OpcionesFerroSuite
{
    public const string NombreArchivo = "appsettings.json";

    public OpcionesBaseDatos BaseDatos { get; set; } = new();
    public OpcionesAsistente Gemini { get; set; } = new();

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>Lee el archivo. "aviso" recibe un mensaje si hubo que usar valores por defecto.</summary>
    public static OpcionesFerroSuite Cargar(string? ruta = null, Action<string>? aviso = null)
    {
        ruta ??= Path.Combine(AppContext.BaseDirectory, NombreArchivo);
        OpcionesFerroSuite opciones;
        try
        {
            opciones = File.Exists(ruta)
                ? JsonSerializer.Deserialize<OpcionesFerroSuite>(File.ReadAllText(ruta), Json) ?? new()
                : new();
            if (!File.Exists(ruta)) aviso?.Invoke($"No se encontró {NombreArchivo}; se usan los valores por defecto.");
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            aviso?.Invoke($"No se pudo leer {NombreArchivo} ({ex.Message}); se usan los valores por defecto.");
            opciones = new();
        }
        opciones.Normalizar();
        return opciones;
    }

    // Corrige valores vacíos o fuera de rango para que el programa siempre arranque.
    private void Normalizar()
    {
        BaseDatos ??= new();
        Gemini ??= new();
        if (string.IsNullOrWhiteSpace(BaseDatos.Servidor)) BaseDatos.Servidor = OpcionesBaseDatos.ServidorPorDefecto;
        BaseDatos.TiempoConexionSegundos = Math.Clamp(BaseDatos.TiempoConexionSegundos, 1, 120);
        BaseDatos.Modo = BaseDatos.EsDemo ? OpcionesBaseDatos.ModoDemo : OpcionesBaseDatos.ModoReal;
        if (!OpcionesBaseDatos.EsNombreValido(BaseDatos.NombreBaseReal)) BaseDatos.NombreBaseReal = "FerroSuite";
        if (!OpcionesBaseDatos.EsNombreValido(BaseDatos.NombreBaseDemo)) BaseDatos.NombreBaseDemo = "FerroSuiteDemo";
        // Las dos bases nunca pueden ser la misma: así la demostración jamás toca los datos reales.
        if (string.Equals(BaseDatos.NombreBaseReal, BaseDatos.NombreBaseDemo, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("En appsettings.json, NombreBaseReal y NombreBaseDemo deben ser distintos.");
        if (string.IsNullOrWhiteSpace(Gemini.Modelo)) Gemini.Modelo = OpcionesAsistente.ModeloPorDefecto;
        Gemini.TiempoEsperaSegundos = Math.Clamp(Gemini.TiempoEsperaSegundos, 5, 300);
    }
}

/// <summary>Sección "BaseDatos" de appsettings.json.</summary>
public sealed class OpcionesBaseDatos
{
    public const string ServidorPorDefecto = @"(localdb)\MSSQLLocalDB";

    /// <summary>Servidor de SQL Server. Por defecto, la instancia LocalDB que instala Visual Studio.</summary>
    public string Servidor { get; set; } = ServidorPorDefecto;

    /// <summary>Segundos de espera al conectar (1 a 120).</summary>
    public int TiempoConexionSegundos { get; set; } = 30;

    /// <summary>
    /// "Real" (por defecto): base con los datos de la ferretería; nunca se borra ni se le cargan datos ficticios.
    /// "Demo": base separada con datos SINTÉTICOS de demostración, que se puede reiniciar desde el menú.
    /// </summary>
    public string Modo { get; set; } = ModoReal;

    public string NombreBaseReal { get; set; } = "FerroSuite";
    public string NombreBaseDemo { get; set; } = "FerroSuiteDemo";

    public const string ModoReal = "Real";
    public const string ModoDemo = "Demo";

    public bool EsDemo => string.Equals(Modo, ModoDemo, StringComparison.OrdinalIgnoreCase);

    /// <summary>Nombres de base aceptados: letras, números y guion bajo, empezando por letra (máx. 100).</summary>
    public static bool EsNombreValido(string? nombre) =>
        !string.IsNullOrEmpty(nombre) && nombre.Length <= 100 && char.IsAsciiLetter(nombre[0])
        && nombre.All(ch => char.IsAsciiLetterOrDigit(ch) || ch == '_');
}

/// <summary>Sección "Gemini" de appsettings.json. No lleva la clave.</summary>
public sealed class OpcionesAsistente
{
    public const string ModeloPorDefecto = "gemini-flash-latest";

    /// <summary>Si es false, el asistente nunca llama a Gemini y responde solo con reglas.</summary>
    public bool Habilitado { get; set; } = true;

    public string Modelo { get; set; } = ModeloPorDefecto;

    /// <summary>Segundos de espera por respuesta (5 a 300).</summary>
    public int TiempoEsperaSegundos { get; set; } = 60;
}
