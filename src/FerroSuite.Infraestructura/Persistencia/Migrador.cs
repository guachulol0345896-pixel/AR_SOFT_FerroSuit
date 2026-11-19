using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace FerroSuite.Infraestructura.Persistencia;

/// <summary>
/// Aplica las migraciones versionadas (BaseDatos/migraciones/V###__nombre.sql) que aún no estén en la tabla
/// dbo.VersionEsquema. Cada migración se ejecuta completa dentro de UNA transacción: si falla, se deshace y la
/// base queda como estaba. Así una base creada por una versión anterior se actualiza sin perder datos.
/// </summary>
public sealed partial class Migrador(BaseDatosSql baseDatos)
{
    [GeneratedRegex(@"^V(?<version>\d{3,})__(?<nombre>[A-Za-z0-9_]+)\.sql$")]
    private static partial Regex PatronArchivo();

    public sealed record Migracion(int Version, string Nombre, string Ruta);

    /// <summary>Migraciones disponibles en la carpeta, en orden de versión.</summary>
    public IReadOnlyList<Migracion> Disponibles()
    {
        var carpeta = Path.Combine(baseDatos.CarpetaScripts, "migraciones");
        if (!Directory.Exists(carpeta)) throw new DirectoryNotFoundException($"No se encontró la carpeta de migraciones: {carpeta}");
        var lista = Directory.GetFiles(carpeta, "V*.sql")
            .Select(r => (Ruta: r, M: PatronArchivo().Match(Path.GetFileName(r))))
            .Where(x => x.M.Success)
            .Select(x => new Migracion(int.Parse(x.M.Groups["version"].Value), x.M.Groups["nombre"].Value, x.Ruta))
            .OrderBy(m => m.Version)
            .ToList();
        if (lista.GroupBy(m => m.Version).Any(g => g.Count() > 1)) throw new InvalidOperationException("Hay dos migraciones con el mismo número de versión.");
        return lista;
    }

    /// <summary>Aplica las pendientes y devuelve sus nombres.</summary>
    public IReadOnlyList<string> Aplicar()
    {
        var disponibles = Disponibles();
        using var c = baseDatos.Conectar();
        // Cualquier error deshace la transacción completa de la migración (no solo la sentencia que falló).
        Sql.Ejecutar(c, "SET XACT_ABORT ON;");
        Sql.Ejecutar(c, """
            IF OBJECT_ID(N'dbo.VersionEsquema', N'U') IS NULL
            CREATE TABLE dbo.VersionEsquema (
                Version     INT            NOT NULL CONSTRAINT PK_VersionEsquema PRIMARY KEY,
                Nombre      NVARCHAR(200)  NOT NULL,
                Huella      CHAR(64)       NOT NULL,
                AplicadaEn  DATETIME2(0)   NOT NULL CONSTRAINT DF_VersionEsquema_Fecha DEFAULT (SYSDATETIME())
            );
            """);

        var aplicadas = Sql.Leer(c, "SELECT Version FROM dbo.VersionEsquema", r => r.GetInt32(0)).ToHashSet();
        var maxima = disponibles.Count == 0 ? 0 : disponibles.Max(m => m.Version);
        if (aplicadas.Count > 0 && aplicadas.Max() > maxima)
            throw new InvalidOperationException($"La base {baseDatos.NombreBase} fue actualizada por una versión más nueva del programa (esquema {aplicadas.Max()}). Actualiza FerroSuite.");

        var hechas = new List<string>();
        foreach (var m in disponibles.Where(m => !aplicadas.Contains(m.Version)))
        {
            var texto = File.ReadAllText(m.Ruta);
            using var t = c.BeginTransaction();
            try
            {
                Sql.EjecutarScript(c, t, texto);
                Sql.Ejecutar(c, "INSERT INTO dbo.VersionEsquema (Version, Nombre, Huella) VALUES (@v, @n, @h)", t,
                    Sql.Int("@v", m.Version), Sql.NVarChar("@n", m.Nombre, 200), Sql.VarChar("@h", Huella(texto), 64));
                t.Commit();
                hechas.Add($"V{m.Version:000}__{m.Nombre}");
            }
            catch (Exception ex)
            {
                try { t.Rollback(); } catch (InvalidOperationException) { /* la transacción ya no existía */ }
                throw new InvalidOperationException($"La migración V{m.Version:000}__{m.Nombre} falló y se deshizo; la base quedó como estaba. Detalle: {ex.Message}", ex);
            }
        }
        return hechas;
    }

    /// <summary>Versión actual del esquema (0 si nunca se migró).</summary>
    public int VersionActual()
    {
        using var c = baseDatos.Conectar();
        if (Sql.Escalar(c, "SELECT OBJECT_ID(N'dbo.VersionEsquema', N'U')") is null) return 0;
        return (int)(Sql.Escalar(c, "SELECT ISNULL(MAX(Version), 0) FROM dbo.VersionEsquema") ?? 0);
    }

    private static string Huella(string texto) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(texto.Replace("\r\n", "\n")))).ToLowerInvariant();
}
