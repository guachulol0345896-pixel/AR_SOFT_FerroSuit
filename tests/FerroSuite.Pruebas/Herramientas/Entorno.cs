// Arma el backend real (servicios, autorización, auditoría, Puente) sobre el almacenamiento en memoria, con datos SINTÉTICOS.
// Las claves de los usuarios de prueba se generan al azar en cada ejecución: no hay contraseñas escritas en el código.
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using FerroSuite.Infraestructura.IA.Gemini;
using FerroSuite.Infraestructura.Seguridad;
using FerroSuite.Presentacion.Comunicacion;
using FerroSuite.Pruebas.Dobles;

namespace FerroSuite.Pruebas.Herramientas;

/// <summary>Reloj que se puede adelantar a mano (para probar vencimiento de sesión y bloqueo).</summary>
public sealed class RelojFalso : TimeProvider
{
    public DateTimeOffset Ahora { get; set; } = DateTimeOffset.Now;
    public override DateTimeOffset GetUtcNow() => Ahora.ToUniversalTime();
    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Local;
    public void Avanzar(TimeSpan t) => Ahora += t;
}

public sealed class Entorno
{
    public required Puente P { get; init; }
    public required AlmacenMemoria A { get; init; }
    public required ClienteGemini Gem { get; init; }
    public required MantenimientoFalso Mant { get; init; }
    public required RelojFalso Reloj { get; init; }
    public required GestorSesion Sesiones { get; init; }
    public required Autorizador Aut { get; init; }
    public required UsuarioServicio Usuarios { get; init; }
    public required InventarioServicio Inv { get; init; }
    public required OrdenServicio Ord { get; init; }
    public required ProveedorServicio Prov { get; init; }
    public required AdministracionServicio Adm { get; init; }
    public required PronosticoServicio Pronosticos { get; init; }

    /// <summary>Claves aleatorias de los usuarios de prueba (iguales en todos los entornos de una misma ejecución).</summary>
    public static readonly Dictionary<string, string> Claves = new();
    /// <summary>Toda clave usada en las pruebas, para comprobar que ninguna aparece en la auditoría.</summary>
    public static readonly HashSet<string> ClavesUsadas = [];

    public static readonly string[] UsuariosPrueba = ["admin.prueba", "bodega.prueba", "vendedor.prueba", "contador.prueba"];

    public static string ClaveAleatoria()
    {
        var c = "Pr" + RandomNumberGenerator.GetHexString(12) + "7";
        lock (ClavesUsadas) ClavesUsadas.Add(c);
        return c;
    }

    /// <summary>Hash rápido para pruebas (10.000 iteraciones). El valor de producción (210.000) se prueba aparte.</summary>
    public static readonly HasherIdentity HasherRapido = new(10_000);

    /// <summary>Carpeta "modelos" del repositorio (modelo ONNX real).</summary>
    public static string CarpetaModelos => Path.Combine(BuscarRaiz() ?? ".", "modelos");

    private static readonly Lazy<FerroSuite.Infraestructura.IA.Prediccion.PronosticadorOnnx> OnnxReal =
        new(() => new FerroSuite.Infraestructura.IA.Prediccion.PronosticadorOnnx(CarpetaModelos));

    /// <summary>El modelo ONNX real del repositorio (se carga una vez por ejecución).</summary>
    public static IPronosticadorDemanda ModeloReal => OnnxReal.Value;

    public static Entorno Nuevo(string clave = "", string url = "https://invalido.local", int timeout = 2, bool demo = false,
        IPronosticadorDemanda? pronosticador = null, DateTimeOffset? ahora = null)
    {
        var a = new AlmacenMemoria();
        // SINTÉTICO: NIT (con dígito de verificación correcto) y nombres inventados.
        a.Pr.Add(new Proveedor { Nit = "000000001-8", Nombre = "Proveedor Sintético A", DiasEntrega = 3, Activo = true });
        a.Pr.Add(new Proveedor { Nit = "000000002-5", Nombre = "Proveedor Sintético B", DiasEntrega = 10, Activo = true });
        a.Sembrar(new Producto { Sku = "SIN-001", Nombre = "Cable sintético", CategoriaId = 3, Unidad = "Metro", StockActual = 100, StockMinimo = 20, PrecioCompra = 1000, NitProveedor = "000000001-8" });
        a.Sembrar(new Producto { Sku = "SIN-002", Nombre = "Varilla sintética", CategoriaId = 1, Unidad = "Varilla", StockActual = 10, StockMinimo = 8, PrecioCompra = 20000, NitProveedor = "000000002-5" });

        var reloj = new RelojFalso();
        if (ahora is DateTimeOffset a0) reloj.Ahora = a0;
        var aut = new Autorizador(new AuditoriaServicio(a, reloj, "EQUIPO-PRUEBA"));
        var usuarios = new UsuarioServicio(a, HasherRapido, aut, reloj);
        lock (Claves)
            foreach (var u in UsuariosPrueba)
                if (!Claves.ContainsKey(u)) Claves[u] = ClaveAleatoria();
        usuarios.CrearAdministradorInicial("admin.prueba", "Usuario sintético admin", Claves["admin.prueba"]);
        var admin = usuarios.IniciarSesion("admin.prueba", Claves["admin.prueba"]);
        foreach (var (u, rol) in new[] { ("bodega.prueba", Rol.Bodega), ("vendedor.prueba", Rol.Vendedor), ("contador.prueba", Rol.Contador) })
        {
            usuarios.Crear(admin, u, "Usuario sintético " + u.Split('.')[0], rol, Claves[u]);
            a.U.First(x => x.NombreUsuario == u).DebeCambiarClave = false; // comodidad de las pruebas; el cambio obligatorio se prueba aparte
        }
        a.A.Clear(); // la auditoría de la preparación no interesa a las pruebas

        var calc = new CalculadoraReposicion();
        var gem = new ClienteGemini(new HttpClient(), new OpcionesGemini { ApiKey = clave, UrlBase = url, TimeoutSegundos = timeout });
        var mant = new MantenimientoFalso(demo);
        var sesiones = new GestorSesion(reloj);
        var inv = new InventarioServicio(a, a, a, calc, aut);
        var ord = new OrdenServicio(a, a, a, TimeProvider.System, aut);
        var prov = new ProveedorServicio(a, aut);
        var adm = new AdministracionServicio(aut, mant);
        var pron = new PronosticoServicio(a, a, pronosticador ?? ModeloReal, aut, reloj);
        var puente = new Puente(new ServiciosFerroSuite(inv, prov, ord, usuarios, adm,
            new AsistenteInventario(calc, gem, TimeProvider.System), pron, gem, aut, sesiones));
        return new Entorno
        {
            P = puente, A = a, Gem = gem, Mant = mant, Reloj = reloj, Sesiones = sesiones, Aut = aut, Usuarios = usuarios,
            Inv = inv, Ord = ord, Prov = prov, Adm = adm, Pronosticos = pron,
        };
    }

    public static async Task<JsonNode> PedirA(Puente p, string accion, object? datos = null, string? origen = Puente.OrigenPermitido)
    {
        var r = await p.AtenderAsync(JsonSerializer.Serialize(new { id = 1, accion, datos }), origen);
        return JsonNode.Parse(r!)!;
    }

    public static Task<JsonNode> IngresarA(Puente p, string usuario) => PedirA(p, "sesion.iniciar", new { usuario, clave = Claves[usuario] });

    public Task<JsonNode> Pedir(string accion, object? datos = null, string? origen = Puente.OrigenPermitido) => PedirA(P, accion, datos, origen);
    public Task<JsonNode> Ingresar(string usuario) => IngresarA(P, usuario);
    public SesionUsuario SesionDe(string usuario) => Usuarios.IniciarSesion(usuario, Claves[usuario]);

    public static string? BuscarRaiz()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "FerroSuite.slnx"))) return d.FullName;
        return null;
    }

    public static bool Ok(JsonNode r) => r["ok"]!.GetValue<bool>();
    public static bool Denegado(JsonNode r) => !Ok(r) && (r["mensaje"]?.ToString() ?? "").Contains("no permite");
    public static string Errores(JsonNode r) => string.Join(" | ", r["errores"]?.AsArray().Select(e => e!.ToString()) ?? []) + " " + r["mensaje"];
}
