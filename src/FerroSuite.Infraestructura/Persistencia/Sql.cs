using System.Data;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace FerroSuite.Infraestructura.Persistencia;

/// <summary>
/// Ayudas para consultas parametrizadas con tipos explícitos (VarChar, NVarChar, Decimal(12,2)...).
/// Indicar el tipo evita conversiones implícitas (por ejemplo, nvarchar contra columnas varchar) que
/// impiden usar los índices, y nunca se concatena texto del usuario en el SQL.
/// </summary>
internal static partial class Sql
{
    public static SqlParameter VarChar(string nombre, string? valor, int largo) =>
        new(nombre, SqlDbType.VarChar, largo) { Value = (object?)valor ?? DBNull.Value };

    public static SqlParameter NVarChar(string nombre, string? valor, int largo) =>
        new(nombre, SqlDbType.NVarChar, largo) { Value = (object?)valor ?? DBNull.Value };

    public static SqlParameter Int(string nombre, int? valor) =>
        new(nombre, SqlDbType.Int) { Value = (object?)valor ?? DBNull.Value };

    public static SqlParameter Decimal(string nombre, decimal? valor) =>
        new(nombre, SqlDbType.Decimal) { Precision = 12, Scale = 2, Value = (object?)valor ?? DBNull.Value };

    public static SqlParameter Bit(string nombre, bool valor) =>
        new(nombre, SqlDbType.Bit) { Value = valor };

    public static SqlParameter Fecha(string nombre, DateTime? valor) =>
        new(nombre, SqlDbType.DateTime2) { Scale = 0, Value = (object?)valor ?? DBNull.Value };

    public static SqlParameter Binario(string nombre, byte[]? valor, int largo) =>
        new(nombre, SqlDbType.VarBinary, largo) { Value = (object?)valor ?? DBNull.Value };

    public static SqlCommand Comando(SqlConnection conexion, string sql, SqlTransaction? transaccion = null, params SqlParameter[] parametros)
    {
        var comando = new SqlCommand(sql, conexion, transaccion);
        comando.Parameters.AddRange(parametros);
        return comando;
    }

    public static int Ejecutar(SqlConnection c, string sql, SqlTransaction? t = null, params SqlParameter[] p)
    {
        using var comando = Comando(c, sql, t, p);
        return comando.ExecuteNonQuery();
    }

    public static object? Escalar(SqlConnection c, string sql, SqlTransaction? t = null, params SqlParameter[] p)
    {
        using var comando = Comando(c, sql, t, p);
        var r = comando.ExecuteScalar();
        return r is DBNull ? null : r;
    }

    public static List<T> Leer<T>(SqlConnection c, string sql, Func<SqlDataReader, T> mapear, SqlTransaction? t = null, params SqlParameter[] p)
    {
        using var comando = Comando(c, sql, t, p);
        using var lector = comando.ExecuteReader();
        var lista = new List<T>();
        while (lector.Read()) lista.Add(mapear(lector));
        return lista;
    }

    // Lectura de columnas que admiten NULL.
    public static string? TextoONulo(this SqlDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);
    public static decimal? DecimalONulo(this SqlDataReader r, int i) => r.IsDBNull(i) ? null : r.GetDecimal(i);
    public static int? EnteroONulo(this SqlDataReader r, int i) => r.IsDBNull(i) ? null : r.GetInt32(i);
    public static DateTime? FechaONula(this SqlDataReader r, int i) => r.IsDBNull(i) ? null : r.GetDateTime(i);
    public static byte[]? BinarioONulo(this SqlDataReader r, int i) => r.IsDBNull(i) ? null : (byte[])r.GetValue(i);

    [GeneratedRegex(@"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex SeparadorLotes();

    /// <summary>Divide un script de SQL Server en lotes (separados por líneas que solo dicen GO).</summary>
    public static List<string> DividirLotes(string script) =>
        SeparadorLotes().Split(script).Where(l => !string.IsNullOrWhiteSpace(l)).ToList();

    /// <summary>Ejecuta todos los lotes de un script, en orden, dentro de la transacción indicada.</summary>
    public static void EjecutarScript(SqlConnection c, SqlTransaction? t, string script, int tiempoSegundos = 120)
    {
        foreach (var lote in DividirLotes(script))
        {
            using var comando = Comando(c, lote, t);
            comando.CommandTimeout = tiempoSegundos;
            comando.ExecuteNonQuery();
        }
    }

    /// <summary>El texto histórico del responsable en MovimientoInventario es NVARCHAR(80).</summary>
    public static string Recortar(string texto, int largo) => texto.Length <= largo ? texto : texto[..largo];
}
