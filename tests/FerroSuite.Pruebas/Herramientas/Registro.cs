namespace FerroSuite.Pruebas.Herramientas;

/// <summary>Anota el resultado de cada prueba y, si se pide, lo guarda en un archivo como evidencia.</summary>
public sealed class Registro
{
    private readonly Lock candado = new();
    public int Pasan { get; private set; }
    public int Fallan { get; private set; }
    public List<string> Lineas { get; } = [];

    public void Check(string id, string nombre, bool cond, string detalle = "")
    {
        lock (candado)
        {
            if (cond) Pasan++; else Fallan++;
            Lineas.Add($"{(cond ? "PASA " : "FALLA")} | {id} | {nombre}{(detalle.Length > 0 ? " — " + detalle : "")}");
        }
    }

    /// <summary>Ejecuta una prueba que puede lanzar excepción: si lanza, cuenta como FALLA con el motivo.</summary>
    public async Task Intentar(string id, string nombre, Func<Task<(bool Ok, string Detalle)>> prueba)
    {
        try { var (ok, detalle) = await prueba(); Check(id, nombre, ok, detalle); }
        catch (Exception ex) { Check(id, nombre, false, "excepción: " + ex.GetType().Name + ": " + ex.Message); }
    }

    public void Imprimir(string titulo, string? archivo = null)
    {
        var texto = new List<string> { titulo, $"Fecha: {DateTime.Now:yyyy-MM-dd HH:mm:ss}  Equipo: {Environment.MachineName}  .NET {Environment.Version}  SO: {System.Runtime.InteropServices.RuntimeInformation.OSDescription}", "" };
        texto.AddRange(Lineas);
        texto.Add("");
        texto.Add($"Total: {Pasan} pasan, {Fallan} fallan");
        foreach (var l in texto) Console.WriteLine(l);
        if (archivo is not null)
        {
            File.WriteAllLines(archivo, texto);
            Console.WriteLine($"\nResultados guardados en: {Path.GetFullPath(archivo)}");
        }
    }
}
