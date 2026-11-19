using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace FerroSuite.Infraestructura.IA.Prediccion;

/// <summary>
/// Ejecuta el modelo neuronal de demanda (modelos/demanda_mlp.onnx) con ONNX Runtime, en el propio equipo y sin
/// servicios externos.
///
/// Al iniciar comprueba que:
///  * el archivo del modelo es el mismo con el que se verificó (huella SHA-256 del JSON);
///  * el modelo corresponde a esta versión del programa (mismas 48 entradas, ventana, horizonte y reglas que
///    EspecificacionModelo);
///  * las entradas y la salida del grafo tienen los nombres y formas esperados.
/// Si algo falla, el modelo queda "no disponible" con el motivo, y la aplicación usa la regla de respaldo.
/// El programa nunca se detiene por el modelo.
/// </summary>
public sealed class PronosticadorOnnx : IPronosticadorDemanda, IDisposable
{
    public const string ArchivoModelo = "demanda_mlp.onnx";
    public const string ArchivoParametros = "demanda_mlp_normalizacion.json";
    private const string Entrada = "caracteristicas", Escala = "escala", Salida = "consumo_diario";

    private readonly InferenceSession? sesion;

    public EstadoModelo Estado { get; }

    /// <param name="carpeta">Carpeta con el .onnx y su JSON (por defecto, "modelos" junto al ejecutable).</param>
    public PronosticadorOnnx(string? carpeta = null)
    {
        carpeta ??= Path.Combine(AppContext.BaseDirectory, "modelos");
        var rutaModelo = Path.Combine(carpeta, ArchivoModelo);
        var rutaParametros = Path.Combine(carpeta, ArchivoParametros);
        try
        {
            if (!File.Exists(rutaModelo)) throw new FileNotFoundException($"No se encontró {ArchivoModelo} en la carpeta de modelos.");
            if (!File.Exists(rutaParametros)) throw new FileNotFoundException($"No se encontró {ArchivoParametros} en la carpeta de modelos.");
            using var json = JsonDocument.Parse(File.ReadAllText(rutaParametros));
            var raiz = json.RootElement;
            ValidarParametros(raiz);
            var huella = raiz.GetProperty("verificacion_onnx").GetProperty("sha256_onnx").GetString();
            var real = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(rutaModelo))).ToLowerInvariant();
            if (!string.Equals(huella, real, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("El archivo del modelo no coincide con su huella SHA-256 (está dañado o fue reemplazado).");

            var opciones = new SessionOptions { IntraOpNumThreads = 1, GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL };
            sesion = new InferenceSession(rutaModelo, opciones);
            ValidarGrafo(sesion);

            var sinteticos = raiz.GetProperty("datos").GetProperty("sinteticos").GetBoolean();
            Estado = new EstadoModelo(true,
                $"{ArchivoModelo} v{raiz.GetProperty("version").GetString()} · ONNX Runtime local · 48 entradas" +
                (sinteticos ? " · entrenado con datos SINTÉTICOS" : ""), null, sinteticos);
        }
        catch (Exception ex)
        {
            sesion?.Dispose();
            sesion = null;
            Trace.WriteLine($"Modelo de demanda no disponible: {ex.GetType().Name}: {ex.Message}");
            Estado = new EstadoModelo(false, ArchivoModelo, Motivo(ex), false);
        }
    }

    public float[] Predecir(IReadOnlyList<EntradaModelo> entradas)
    {
        if (sesion is null) throw new InvalidOperationException("El modelo no está disponible.");
        var n = entradas.Count;
        if (n == 0) return [];
        var columnas = EspecificacionModelo.NumeroEntradas;
        var x = new float[n * columnas];
        var e = new float[n];
        for (var i = 0; i < n; i++)
        {
            if (entradas[i].Caracteristicas.Length != columnas) throw new ArgumentException($"Cada entrada debe tener {columnas} valores.");
            Array.Copy(entradas[i].Caracteristicas, 0, x, i * columnas, columnas);
            e[i] = entradas[i].Escala;
        }
        var valores = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor(Entrada, new DenseTensor<float>(x, [n, columnas])),
            NamedOnnxValue.CreateFromTensor(Escala, new DenseTensor<float>(e, [n, 1])),
        };
        using var resultado = sesion.Run(valores);
        var tensor = resultado.First(r => r.Name == Salida).AsTensor<float>();
        var salida = new float[n];
        for (var i = 0; i < n; i++)
        {
            salida[i] = tensor[i, 0];
            if (!float.IsFinite(salida[i]) || salida[i] < 0) throw new InvalidDataException("El modelo devolvió un valor no válido.");
        }
        return salida;
    }

    public void Dispose() => sesion?.Dispose();

    private static void ValidarParametros(JsonElement j)
    {
        void Igual<T>(string campo, T esperado, T real)
        {
            if (!EqualityComparer<T>.Default.Equals(esperado, real))
                throw new InvalidDataException($"El modelo no corresponde a esta versión del programa ({campo}: {real}, se esperaba {esperado}).");
        }
        Igual("ventana_dias", EspecificacionModelo.Ventana, j.GetProperty("ventana_dias").GetInt32());
        Igual("horizonte_dias", EspecificacionModelo.Horizonte, j.GetProperty("horizonte_dias").GetInt32());
        Igual("historia_minima_dias", EspecificacionModelo.HistoriaMinima, j.GetProperty("historia_minima_dias").GetInt32());
        Igual("min_dias_observados_en_ventana", EspecificacionModelo.MinDiasVentana, j.GetProperty("min_dias_observados_en_ventana").GetInt32());
        Igual("epsilon_escala", EspecificacionModelo.EpsilonEscala, j.GetProperty("epsilon_escala").GetDouble());
        Igual("categorias", string.Join("|", EspecificacionModelo.Categorias), string.Join("|", j.GetProperty("categorias").EnumerateArray().Select(c => c.GetString())));
        Igual("unidades_continuas", string.Join("|", EspecificacionModelo.UnidadesContinuas), string.Join("|", j.GetProperty("unidades_continuas").EnumerateArray().Select(c => c.GetString())));
        Igual("caracteristicas", string.Join("|", EspecificacionModelo.NombresCaracteristicas), string.Join("|", j.GetProperty("caracteristicas").EnumerateArray().Select(c => c.GetString())));
    }

    private static void ValidarGrafo(InferenceSession s)
    {
        bool Forma(NodeMetadata m, int columnas) => m.ElementType == typeof(float) && m.Dimensions.Length == 2 && m.Dimensions[1] == columnas;
        if (!s.InputMetadata.TryGetValue(Entrada, out var mx) || !Forma(mx, EspecificacionModelo.NumeroEntradas)
            || !s.InputMetadata.TryGetValue(Escala, out var me) || !Forma(me, 1)
            || !s.OutputMetadata.TryGetValue(Salida, out var ms) || !Forma(ms, 1))
            throw new InvalidDataException("Las entradas o la salida del modelo no tienen los nombres y formas esperados.");
    }

    // Mensaje claro para la pantalla, sin rutas del equipo.
    private static string Motivo(Exception ex) => ex switch
    {
        FileNotFoundException f => f.Message,
        InvalidDataException d => d.Message,
        DllNotFoundException or TypeInitializationException or BadImageFormatException => "No se pudo cargar ONNX Runtime en este equipo.",
        JsonException or KeyNotFoundException => "El archivo de parámetros del modelo está dañado.",
        _ => "No se pudo cargar el modelo (" + ex.GetType().Name + ").",
    };
}
