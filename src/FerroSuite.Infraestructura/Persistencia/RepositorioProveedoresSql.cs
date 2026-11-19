using Microsoft.Data.SqlClient;

namespace FerroSuite.Infraestructura.Persistencia;

/// <summary>Proveedores en SQL Server.</summary>
public sealed class RepositorioProveedoresSql(BaseDatosSql baseDatos) : IRepositorioProveedores
{
    private const string Consulta = "SELECT Nit, Nombre, Telefono, DiasEntrega, Cumplimiento, Activo FROM dbo.Proveedor";

    public List<Proveedor> Listar(bool incluirInactivos = true)
    {
        using var c = baseDatos.Conectar();
        return Sql.Leer(c, Consulta + " WHERE (@todos = 1 OR Activo = 1) ORDER BY Nombre", Leer, null, Sql.Bit("@todos", incluirInactivos));
    }

    public Proveedor? Buscar(string nit)
    {
        using var c = baseDatos.Conectar();
        return Sql.Leer(c, Consulta + " WHERE Nit = @nit", Leer, null, Sql.VarChar("@nit", nit, 20)).FirstOrDefault();
    }

    public void Crear(Proveedor p)
    {
        using var c = baseDatos.Conectar();
        Sql.Ejecutar(c, """
            INSERT INTO dbo.Proveedor (Nit, Nombre, Telefono, DiasEntrega, Cumplimiento, Activo)
            VALUES (@nit, @nombre, @telefono, @dias, NULL, 1)
            """, null, Sql.VarChar("@nit", p.Nit, 20), Sql.NVarChar("@nombre", p.Nombre, 120),
            Sql.VarChar("@telefono", p.Telefono, 30), Sql.Int("@dias", p.DiasEntrega));
    }

    public void Actualizar(Proveedor p)
    {
        using var c = baseDatos.Conectar();
        Sql.Ejecutar(c, "UPDATE dbo.Proveedor SET Nombre = @nombre, Telefono = @telefono, DiasEntrega = @dias WHERE Nit = @nit", null,
            Sql.VarChar("@nit", p.Nit, 20), Sql.NVarChar("@nombre", p.Nombre, 120), Sql.VarChar("@telefono", p.Telefono, 30), Sql.Int("@dias", p.DiasEntrega));
    }

    public void CambiarEstado(string nit, bool activo)
    {
        using var c = baseDatos.Conectar();
        Sql.Ejecutar(c, "UPDATE dbo.Proveedor SET Activo = @activo WHERE Nit = @nit", null, Sql.Bit("@activo", activo), Sql.VarChar("@nit", nit, 20));
    }

    private static Proveedor Leer(SqlDataReader r) => new()
    {
        Nit = r.GetString(0), Nombre = r.GetString(1), Telefono = r.TextoONulo(2), DiasEntrega = r.GetInt32(3),
        Cumplimiento = r.DecimalONulo(4), Activo = r.GetBoolean(5),
    };
}
