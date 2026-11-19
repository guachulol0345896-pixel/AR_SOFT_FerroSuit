namespace FerroSuite.Infraestructura.IA.Gemini;

/// <summary>
/// Clave del asistente Gemini (opcional: el pronóstico neuronal no la necesita).
/// <para>
/// Se guarda <b>cifrada con DPAPI de Windows</b> (ámbito del usuario actual) en %LocalAppData%\FerroSuite\clave-gemini.dat,
/// fuera del repositorio. Solo la misma cuenta de Windows en el mismo equipo puede descifrarla. Nunca se guarda en texto
/// plano: si DPAPI no está disponible (otro sistema operativo), no se guarda y hay que usar la variable de entorno
/// GEMINI_API_KEY. Un archivo antiguo en texto plano (clave-gemini.txt, versiones anteriores) se cifra y se borra al leerlo.
/// </para>
/// </summary>
public static class ClaveGemini
{
    public static readonly string Carpeta =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FerroSuite");

    public const string ArchivoCifrado = "clave-gemini.dat";
    public const string ArchivoAntiguo = "clave-gemini.txt"; // versiones anteriores (texto plano): solo se lee para migrarlo
    private const string Prefijo = "DPAPI1:";

    /// <summary>Primero la variable de entorno GEMINI_API_KEY; si no, el archivo cifrado (migrando el antiguo si existe).</summary>
    public static string Leer(string? carpeta = null, IProtectorDatos? protector = null)
    {
        var deEntorno = Environment.GetEnvironmentVariable("GEMINI_API_KEY");
        if (!string.IsNullOrWhiteSpace(deEntorno)) return deEntorno.Trim();
        carpeta ??= Carpeta;
        protector ??= ProtectorDpapi.Instancia;
        try
        {
            var cifrado = Path.Combine(carpeta, ArchivoCifrado);
            if (File.Exists(cifrado))
            {
                var texto = File.ReadAllText(cifrado).Trim();
                if (!texto.StartsWith(Prefijo, StringComparison.Ordinal) || !protector.Disponible) return "";
                return System.Text.Encoding.UTF8.GetString(protector.Desproteger(Convert.FromBase64String(texto[Prefijo.Length..]))).Trim();
            }
            var antiguo = Path.Combine(carpeta, ArchivoAntiguo);
            if (File.Exists(antiguo))
            {
                var clave = File.ReadAllText(antiguo).Trim();
                // Migración: se cifra y se borra el texto plano. Si no se puede cifrar, el archivo antiguo se deja como estaba.
                if (protector.Disponible && Guardar(clave, carpeta, protector) is null) File.Delete(antiguo);
                return clave;
            }
            return "";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or System.Security.Cryptography.CryptographicException)
        {
            System.Diagnostics.Trace.WriteLine($"No se pudo leer la clave de Gemini ({ex.GetType().Name}); el asistente usará reglas.");
            return "";
        }
    }

    /// <summary>Guarda la clave cifrada (vacía = borrarla). Devuelve un mensaje de error o null si todo salió bien.</summary>
    public static string? Guardar(string clave, string? carpeta = null, IProtectorDatos? protector = null)
    {
        carpeta ??= Carpeta;
        protector ??= ProtectorDpapi.Instancia;
        var cifrado = Path.Combine(carpeta, ArchivoCifrado);
        try
        {
            clave = (clave ?? "").Trim();
            if (clave.Length == 0)
            {
                File.Delete(cifrado);
                File.Delete(Path.Combine(carpeta, ArchivoAntiguo));
                return null;
            }
            if (!protector.Disponible)
                return "En este sistema no se puede cifrar la clave con DPAPI de Windows, así que no se guardó. " +
                       "Usa la variable de entorno GEMINI_API_KEY.";
            Directory.CreateDirectory(carpeta);
            var datos = protector.Proteger(System.Text.Encoding.UTF8.GetBytes(clave));
            File.WriteAllText(cifrado, Prefijo + Convert.ToBase64String(datos));
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
        {
            System.Diagnostics.Trace.WriteLine($"No se pudo guardar la clave de Gemini: {ex.GetType().Name}");
            return $"No se pudo guardar la clave en {cifrado} ({ex.GetType().Name}).";
        }
    }
}
