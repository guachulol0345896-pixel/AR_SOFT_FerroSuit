// HERRAMIENTA DE PRUEBA (no es parte del programa): sirve la carpeta frontend por HTTP local y pasa
// cada mensaje al Puente real. Sustituye a WebView2, que solo existe en Windows, para probar las
// pantallas en un navegador. Datos en memoria, SINTÉTICOS. Uso: dotnet run -- servidor <carpeta frontend>
using System.Net;
using System.Text;
using FerroSuite.Presentacion.Comunicacion;
namespace FerroSuite.Pruebas.Herramientas;
public static class ServidorPruebaFrontend
{
    // control (opcional, solo pruebas): POST /__control?accion=... para simular fallas del backend desde el navegador.
    public static async Task Correr(Puente puente, string carpeta, string prefijo, Func<string, bool>? control = null)
    {
        var l = new HttpListener(); l.Prefixes.Add(prefijo); l.Start();
        Console.WriteLine("escuchando " + prefijo);
        while (true)
        {
            var ctx = await l.GetContextAsync();
            try
            {
                if (ctx.Request.HttpMethod == "POST" && ctx.Request.Url!.AbsolutePath == "/__msg")
                {
                    var cuerpo = await new StreamReader(ctx.Request.InputStream).ReadToEndAsync();
                    // Esta herramienta reemplaza a WebView2 y a su dirección interna: los mensajes de sus propias páginas
                    // se entregan con el origen de las pantallas del programa.
                    var r = await puente.AtenderAsync(cuerpo, Puente.OrigenPermitido);
                    if (r is null) { ctx.Response.StatusCode = 204; ctx.Response.Close(); continue; }
                    var b = Encoding.UTF8.GetBytes(r); ctx.Response.ContentType = "application/json"; await ctx.Response.OutputStream.WriteAsync(b); ctx.Response.Close(); continue;
                }
                if (ctx.Request.HttpMethod == "POST" && ctx.Request.Url!.AbsolutePath == "/__control" && control is not null)
                {
                    ctx.Response.StatusCode = control(ctx.Request.QueryString["accion"] ?? "") ? 204 : 400; ctx.Response.Close(); continue;
                }
                var ruta = Path.Combine(carpeta, ctx.Request.Url!.AbsolutePath.TrimStart('/'));
                if (!File.Exists(ruta)) { ctx.Response.StatusCode = 404; ctx.Response.Close(); continue; }
                ctx.Response.ContentType = Path.GetExtension(ruta) switch { ".html" => "text/html; charset=utf-8", ".css" => "text/css", ".js" => "text/javascript", _ => "application/octet-stream" };
                var bytes = await File.ReadAllBytesAsync(ruta); await ctx.Response.OutputStream.WriteAsync(bytes); ctx.Response.Close();
            }
            catch (Exception e) { Console.WriteLine(e.Message); try { ctx.Response.Abort(); } catch { } }
        }
    }
}
