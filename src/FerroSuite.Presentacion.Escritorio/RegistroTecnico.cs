using System.Diagnostics;
using System.Security.Cryptography;

namespace FerroSuite.Presentacion.Escritorio;

/// <summary>
/// Registro técnico del programa: un archivo por día en %LocalAppData%\FerroSuite\registros (se conservan 14 días).
/// Recibe todo lo que el programa escribe con <see cref="Trace"/> (errores con su número de referencia, fallas del
/// modelo, de la base de datos o de WebView2). Al usuario solo se le muestra el número de referencia, nunca el detalle.
/// No se escriben claves: ninguna parte del programa envía claves a Trace.
/// </summary>
internal static class RegistroTecnico
{
    public static string Carpeta { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FerroSuite", "registros");

    public static void Iniciar()
    {
        try
        {
            Directory.CreateDirectory(Carpeta);
            foreach (var viejo in Directory.GetFiles(Carpeta, "ferrosuite-*.log").Where(f => File.GetLastWriteTime(f) < DateTime.Now.AddDays(-14)))
                File.Delete(viejo);
            var archivo = Path.Combine(Carpeta, $"ferrosuite-{DateTime.Now:yyyyMMdd}.log");
            Trace.Listeners.Add(new TextWriterTraceListener(new StreamWriter(archivo, append: true) { AutoFlush = true }));
            Trace.AutoFlush = true;
            Trace.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss} · FerroSuite {typeof(RegistroTecnico).Assembly.GetName().Version} iniciado");
        }
        catch (Exception ex)
        {
            Debug.WriteLine("No se pudo abrir el registro técnico: " + ex.Message); // el programa sigue sin registro en archivo
        }
    }

    /// <summary>Registra una excepción con un número de referencia corto y lo devuelve (para mostrarlo al usuario).</summary>
    public static string Error(string donde, Exception ex)
    {
        var referencia = Convert.ToHexString(RandomNumberGenerator.GetBytes(4));
        Trace.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{referencia}] {donde}: {ex}");
        return referencia;
    }
}
