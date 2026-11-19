using FerroSuite.Infraestructura.Configuracion;
using Microsoft.Data.SqlClient;

namespace FerroSuite.Infraestructura.Persistencia;

/// <summary>
/// Conexión con SQL Server (por defecto LocalDB) y preparación de la base:
/// la crea si no existe, aplica las migraciones pendientes y, solo en modo demostración,
/// carga los datos sintéticos de demostración. Hay dos bases separadas: la real y la de demostración.
/// </summary>
public sealed class BaseDatosSql
{
    public const string PrefijoBasesPrueba = "FerroSuite_Pruebas_";

    private readonly OpcionesBaseDatos opciones;

    /// <param name="carpetaScripts">Carpeta con "migraciones" y "demo". Por defecto, BaseDatos junto al ejecutable.</param>
    public BaseDatosSql(OpcionesBaseDatos opciones, string? carpetaScripts = null)
    {
        this.opciones = opciones;
        CarpetaScripts = carpetaScripts ?? Path.Combine(AppContext.BaseDirectory, "BaseDatos");
        if (!OpcionesBaseDatos.EsNombreValido(NombreBase))
            throw new ArgumentException($"Nombre de base de datos no válido: {NombreBase}");
    }

    public string CarpetaScripts { get; }
    public bool EsDemo => opciones.EsDemo;
    public string NombreBase => opciones.EsDemo ? opciones.NombreBaseDemo : opciones.NombreBaseReal;
    public string Servidor => opciones.Servidor;

    /// <summary>Para la prueba de integración 4: si está activo, se intenta conectar a un servidor que no existe.</summary>
    public bool SimularCaida { get; set; }

    /// <summary>Migraciones aplicadas en la última preparación (para informar al usuario).</summary>
    public IReadOnlyList<string> UltimasMigraciones { get; private set; } = [];

    public string CadenaConexion => SimularCaida
        ? Cadena(@"(localdb)\ServidorQueNoExiste", NombreBase, 3)
        : Cadena(opciones.Servidor, NombreBase, opciones.TiempoConexionSegundos);

    private string CadenaMaster => Cadena(opciones.Servidor, "master", opciones.TiempoConexionSegundos);

    private static string Cadena(string servidor, string baseDatos, int tiempo) => new SqlConnectionStringBuilder
    {
        DataSource = servidor,
        InitialCatalog = baseDatos,
        IntegratedSecurity = true,          // sin usuario ni contraseña: la cuenta de Windows
        TrustServerCertificate = true,
        ConnectTimeout = tiempo,
        ApplicationName = "FerroSuite",
    }.ConnectionString;

    public SqlConnection Conectar()
    {
        var conexion = new SqlConnection(CadenaConexion);
        conexion.Open();
        return conexion;
    }

    /// <summary>Crea la base si hace falta, aplica las migraciones pendientes y, en modo demo, carga la demostración si está vacía.</summary>
    public void Preparar()
    {
        CrearBaseSiNoExiste();
        UltimasMigraciones = new Migrador(this).Aplicar();
        if (EsDemo && EstaVacia()) CargarDemostracion();
    }

    /// <summary>Vuelve a crear la base de DEMOSTRACIÓN desde cero. Nunca toca la base real.</summary>
    public void RestablecerDemo()
    {
        if (!EsDemo) throw new InvalidOperationException("Solo se puede restablecer la base de demostración (Modo \"Demo\" en appsettings.json).");
        EliminarBase();
        Preparar();
    }

    public void CrearBaseSiNoExiste()
    {
        using var c = new SqlConnection(CadenaMaster);
        c.Open();
        // El nombre va como parámetro y QUOTENAME lo convierte en identificador seguro
        // (EXEC() no admite llamadas a funciones, por eso se arma antes en una variable).
        Sql.Ejecutar(c, """
            IF DB_ID(@nombre) IS NULL
            BEGIN
                DECLARE @sql NVARCHAR(300) = N'CREATE DATABASE ' + QUOTENAME(@nombre);
                EXEC sys.sp_executesql @sql;
            END
            """, null, Sql.NVarChar("@nombre", NombreBase, 128));
    }

    /// <summary>
    /// Borra la base actual. Solo se permite para la base de demostración o para bases temporales de pruebas
    /// (nombre que empieza por "FerroSuite_Pruebas_"); la base real nunca se borra.
    /// </summary>
    public void EliminarBase()
    {
        var esDemo = EsDemo && NombreBase == opciones.NombreBaseDemo;
        var esPrueba = NombreBase.StartsWith(PrefijoBasesPrueba, StringComparison.Ordinal);
        if (!esDemo && !esPrueba)
            throw new InvalidOperationException($"No se permite borrar la base {NombreBase}.");

        SqlConnection.ClearAllPools();
        using var c = new SqlConnection(CadenaMaster);
        c.Open();
        Sql.Ejecutar(c, """
            IF DB_ID(@nombre) IS NOT NULL
            BEGIN
                DECLARE @sql NVARCHAR(400) = N'ALTER DATABASE ' + QUOTENAME(@nombre) + N' SET SINGLE_USER WITH ROLLBACK IMMEDIATE; '
                                           + N'DROP DATABASE ' + QUOTENAME(@nombre) + N';';
                EXEC sys.sp_executesql @sql;
            END
            """, null, Sql.NVarChar("@nombre", NombreBase, 128));
    }

    public bool EstaVacia()
    {
        using var c = Conectar();
        return (int)Sql.Escalar(c, """
            SELECT CASE WHEN EXISTS (SELECT 1 FROM dbo.Producto) OR EXISTS (SELECT 1 FROM dbo.Proveedor)
                          OR EXISTS (SELECT 1 FROM dbo.OrdenCompra) THEN 0 ELSE 1 END
            """)! == 1;
    }

    private void CargarDemostracion()
    {
        var ruta = Path.Combine(CarpetaScripts, "demo", "datos_demo.sql");
        if (!File.Exists(ruta)) throw new FileNotFoundException("No se encontró el script de demostración.", ruta);
        using var c = Conectar();
        using var t = c.BeginTransaction();
        try
        {
            Sql.EjecutarScript(c, t, File.ReadAllText(ruta));
            t.Commit();
        }
        catch
        {
            t.Rollback();
            throw;
        }
    }
}
