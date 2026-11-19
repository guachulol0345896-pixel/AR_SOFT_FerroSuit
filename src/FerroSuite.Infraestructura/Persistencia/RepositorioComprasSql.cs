using Microsoft.Data.SqlClient;

namespace FerroSuite.Infraestructura.Persistencia;

/// <summary>Órdenes de compra y su detalle en SQL Server.</summary>
public sealed class RepositorioComprasSql(BaseDatosSql baseDatos) : IRepositorioCompras
{
    private const string ConsultaOrdenes = """
        SELECT o.OrdenId, o.Codigo, o.NitProveedor, pr.Nombre, o.Estado, o.FechaCreacion, o.FechaAprobacion,
               o.FechaEntregaEsperada, o.FechaRecepcion, o.FechaPago, o.Pagada, o.BodegaRecepcionId
        FROM dbo.OrdenCompra o
        JOIN dbo.Proveedor pr ON pr.Nit = o.NitProveedor
        """;

    private const string ConsultaDetalles = """
        SELECT d.DetalleId, d.OrdenId, d.Sku, p.Nombre, p.Unidad, d.Cantidad, d.PrecioUnitario
        FROM dbo.DetalleOrdenCompra d
        JOIN dbo.Producto p ON p.Sku = d.Sku
        """;

    public List<OrdenCompra> ListarOrdenes()
    {
        using var c = baseDatos.Conectar();
        var ordenes = Sql.Leer(c, ConsultaOrdenes + " ORDER BY o.OrdenId", LeerOrden);
        var detalles = Sql.Leer(c, ConsultaDetalles + " ORDER BY d.OrdenId, d.DetalleId", r => (OrdenId: r.GetInt32(1), Detalle: LeerDetalle(r)))
            .GroupBy(x => x.OrdenId).ToDictionary(g => g.Key, g => g.Select(x => x.Detalle).ToList());
        foreach (var o in ordenes) o.Detalles = detalles.GetValueOrDefault(o.OrdenId) ?? [];
        return ordenes;
    }

    public OrdenCompra? BuscarOrden(string codigo)
    {
        using var c = baseDatos.Conectar();
        var orden = Sql.Leer(c, ConsultaOrdenes + " WHERE o.Codigo = @codigo", LeerOrden, null, Sql.VarChar("@codigo", codigo, 20)).FirstOrDefault();
        if (orden is not null)
            orden.Detalles = Sql.Leer(c, ConsultaDetalles + " WHERE d.OrdenId = @id ORDER BY d.DetalleId", LeerDetalle, null, Sql.Int("@id", orden.OrdenId));
        return orden;
    }

    public bool HayOrdenesConProducto(string sku)
    {
        using var c = baseDatos.Conectar();
        return Sql.Escalar(c, "SELECT TOP (1) 1 FROM dbo.DetalleOrdenCompra WHERE Sku = @sku", null, Sql.VarChar("@sku", sku, 20)) is not null;
    }

    public string CrearOrden(OrdenCompra orden)
    {
        using var c = baseDatos.Conectar();
        using var t = c.BeginTransaction();
        try
        {
            int ordenId; string codigo;
            using (var cmd = Sql.Comando(c, """
                INSERT INTO dbo.OrdenCompra (NitProveedor, Estado)
                OUTPUT INSERTED.OrdenId, INSERTED.Codigo
                VALUES (@nit, @estado)
                """, t, Sql.VarChar("@nit", orden.NitProveedor, 20), Sql.NVarChar("@estado", EstadosOrden.Pendiente, 30)))
            using (var r = cmd.ExecuteReader())
            {
                r.Read();
                ordenId = r.GetInt32(0);
                codigo = r.GetString(1);
            }
            foreach (var d in orden.Detalles)
                Sql.Ejecutar(c, "INSERT INTO dbo.DetalleOrdenCompra (OrdenId, Sku, Cantidad, PrecioUnitario) VALUES (@orden, @sku, @cantidad, @precio)", t,
                    Sql.Int("@orden", ordenId), Sql.VarChar("@sku", d.Sku, 20), Sql.Decimal("@cantidad", d.Cantidad), Sql.Decimal("@precio", d.PrecioUnitario));
            t.Commit();
            return codigo;
        }
        catch
        {
            t.Rollback();
            throw;
        }
    }

    public bool Aprobar(string codigo, DateTime fechaEntregaEsperada)
    {
        using var c = baseDatos.Conectar();
        return Sql.Ejecutar(c, """
            UPDATE dbo.OrdenCompra
            SET Estado = @aprobada, FechaAprobacion = SYSDATETIME(), FechaEntregaEsperada = @entrega
            WHERE Codigo = @codigo AND Estado = @pendiente
            """, null, Sql.NVarChar("@aprobada", EstadosOrden.Aprobada, 30), Sql.NVarChar("@pendiente", EstadosOrden.Pendiente, 30),
            Sql.Fecha("@entrega", fechaEntregaEsperada), Sql.VarChar("@codigo", codigo, 20)) == 1;
    }

    public bool Rechazar(string codigo)
    {
        using var c = baseDatos.Conectar();
        return Sql.Ejecutar(c, "UPDATE dbo.OrdenCompra SET Estado = @rechazada WHERE Codigo = @codigo AND Estado = @pendiente", null,
            Sql.NVarChar("@rechazada", EstadosOrden.Rechazada, 30), Sql.NVarChar("@pendiente", EstadosOrden.Pendiente, 30),
            Sql.VarChar("@codigo", codigo, 20)) == 1;
    }

    public bool MarcarPagada(string codigo)
    {
        using var c = baseDatos.Conectar();
        return Sql.Ejecutar(c, """
            UPDATE dbo.OrdenCompra SET Pagada = 1, FechaPago = SYSDATETIME()
            WHERE Codigo = @codigo AND Pagada = 0 AND Estado IN (@aprobada, @recibida)
            """, null, Sql.VarChar("@codigo", codigo, 20), Sql.NVarChar("@aprobada", EstadosOrden.Aprobada, 30),
            Sql.NVarChar("@recibida", EstadosOrden.Recibida, 30)) == 1;
    }

    public bool Recibir(string codigo, int bodegaId, int usuarioId, string nombreUsuario)
    {
        using var c = baseDatos.Conectar();
        using var t = c.BeginTransaction();
        try
        {
            // Primero se cambia el estado con condición: si dos personas reciben la misma orden, solo una lo logra.
            var ordenId = (int?)Sql.Escalar(c, """
                UPDATE dbo.OrdenCompra
                SET Estado = @recibida, FechaRecepcion = SYSDATETIME(), BodegaRecepcionId = @bodega
                OUTPUT INSERTED.OrdenId
                WHERE Codigo = @codigo AND Estado = @aprobada
                """, t, Sql.NVarChar("@recibida", EstadosOrden.Recibida, 30), Sql.NVarChar("@aprobada", EstadosOrden.Aprobada, 30),
                Sql.Int("@bodega", bodegaId), Sql.VarChar("@codigo", codigo, 20));
            if (ordenId is null)
            {
                t.Rollback();
                return false;
            }
            var lineas = Sql.Leer(c, "SELECT Sku, Cantidad FROM dbo.DetalleOrdenCompra WHERE OrdenId = @id ORDER BY DetalleId",
                r => (Sku: r.GetString(0), Cantidad: r.GetDecimal(1)), t, Sql.Int("@id", ordenId.Value));
            foreach (var (sku, cantidad) in lineas)
                OperacionesStockSql.Aplicar(c, t, new Movimiento
                {
                    Sku = sku, Tipo = ReglasInventario.Entrada, Cantidad = cantidad, Motivo = ReglasInventario.MotivoCompra,
                    BodegaId = bodegaId, UsuarioId = usuarioId, Usuario = nombreUsuario, Referencia = codigo,
                });
            t.Commit();
            return true;
        }
        catch
        {
            t.Rollback();
            throw;
        }
    }

    private static OrdenCompra LeerOrden(SqlDataReader r) => new()
    {
        OrdenId = r.GetInt32(0),
        Codigo = r.GetString(1),
        NitProveedor = r.GetString(2),
        NombreProveedor = r.GetString(3),
        Estado = r.GetString(4),
        FechaCreacion = r.GetDateTime(5),
        FechaAprobacion = r.FechaONula(6),
        FechaEntregaEsperada = r.FechaONula(7),
        FechaRecepcion = r.FechaONula(8),
        FechaPago = r.FechaONula(9),
        Pagada = r.GetBoolean(10),
        BodegaRecepcionId = r.EnteroONulo(11),
    };

    private static DetalleOrdenCompra LeerDetalle(SqlDataReader r) => new()
    {
        DetalleId = r.GetInt32(0), Sku = r.GetString(2), NombreProducto = r.GetString(3), Unidad = r.GetString(4),
        Cantidad = r.GetDecimal(5), PrecioUnitario = r.DecimalONulo(6),
    };
}
