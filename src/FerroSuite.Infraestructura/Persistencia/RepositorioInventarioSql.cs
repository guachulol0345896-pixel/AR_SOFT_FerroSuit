using Microsoft.Data.SqlClient;

namespace FerroSuite.Infraestructura.Persistencia;

/// <summary>Inventario en SQL Server: categorías, bodegas, productos, existencias y movimientos.</summary>
public sealed class RepositorioInventarioSql(BaseDatosSql baseDatos) : IRepositorioInventario
{
    // ---------------- Catálogos ----------------

    public List<Categoria> ListarCategorias()
    {
        using var c = baseDatos.Conectar();
        return Sql.Leer(c, "SELECT CategoriaId, Nombre, Activa FROM dbo.CategoriaProducto ORDER BY Nombre",
            r => new Categoria { CategoriaId = r.GetInt32(0), Nombre = r.GetString(1), Activa = r.GetBoolean(2) });
    }

    public List<Bodega> ListarBodegas()
    {
        using var c = baseDatos.Conectar();
        return Sql.Leer(c, "SELECT BodegaId, Nombre, Direccion, Activa FROM dbo.Bodega ORDER BY BodegaId", LeerBodega);
    }

    public Bodega? BuscarBodega(int bodegaId)
    {
        using var c = baseDatos.Conectar();
        return Sql.Leer(c, "SELECT BodegaId, Nombre, Direccion, Activa FROM dbo.Bodega WHERE BodegaId = @id", LeerBodega, null,
            Sql.Int("@id", bodegaId)).FirstOrDefault();
    }

    public bool ExisteBodegaConNombre(string nombre, int? excepto = null)
    {
        using var c = baseDatos.Conectar();
        return Sql.Escalar(c, "SELECT 1 FROM dbo.Bodega WHERE Nombre = @nombre AND (@excepto IS NULL OR BodegaId <> @excepto)", null,
            Sql.NVarChar("@nombre", nombre, 100), Sql.Int("@excepto", excepto)) is not null;
    }

    public void ActualizarBodega(Bodega b)
    {
        using var c = baseDatos.Conectar();
        Sql.Ejecutar(c, "UPDATE dbo.Bodega SET Nombre = @nombre, Direccion = @direccion, Activa = @activa WHERE BodegaId = @id", null,
            Sql.NVarChar("@nombre", b.Nombre, 100), Sql.NVarChar("@direccion", b.Direccion, 200), Sql.Bit("@activa", b.Activa), Sql.Int("@id", b.BodegaId));
    }

    public bool ExisteCategoriaConNombre(string nombre, int? excepto = null)
    {
        using var c = baseDatos.Conectar();
        return Sql.Escalar(c, "SELECT 1 FROM dbo.CategoriaProducto WHERE Nombre = @nombre AND (@excepto IS NULL OR CategoriaId <> @excepto)", null,
            Sql.NVarChar("@nombre", nombre, 50), Sql.Int("@excepto", excepto)) is not null;
    }

    public int CrearCategoria(Categoria cat)
    {
        using var c = baseDatos.Conectar();
        return (int)Sql.Escalar(c, "INSERT INTO dbo.CategoriaProducto (Nombre, Activa) OUTPUT INSERTED.CategoriaId VALUES (@nombre, 1)", null,
            Sql.NVarChar("@nombre", cat.Nombre, 50))!;
    }

    public void ActualizarCategoria(Categoria cat)
    {
        using var c = baseDatos.Conectar();
        Sql.Ejecutar(c, "UPDATE dbo.CategoriaProducto SET Nombre = @nombre, Activa = @activa WHERE CategoriaId = @id", null,
            Sql.NVarChar("@nombre", cat.Nombre, 50), Sql.Bit("@activa", cat.Activa), Sql.Int("@id", cat.CategoriaId));
    }

    public int CrearBodega(Bodega b)
    {
        using var c = baseDatos.Conectar();
        return (int)Sql.Escalar(c, "INSERT INTO dbo.Bodega (Nombre, Direccion, Activa) OUTPUT INSERTED.BodegaId VALUES (@nombre, @direccion, 1)", null,
            Sql.NVarChar("@nombre", b.Nombre, 100), Sql.NVarChar("@direccion", b.Direccion, 200))!;
    }

    // ---------------- Productos ----------------

    private const string ConsultaProductos = """
        SELECT p.Sku, p.Nombre, p.CategoriaId, c.Nombre, p.Unidad, p.StockActual, p.StockMinimo,
               p.PrecioCompra, p.NitProveedor, pr.Nombre, pr.DiasEntrega, p.Activo
        FROM dbo.Producto p
        JOIN dbo.CategoriaProducto c ON c.CategoriaId = p.CategoriaId
        LEFT JOIN dbo.Proveedor pr ON pr.Nit = p.NitProveedor
        """;

    private const string ConsultaExistencias = """
        SELECT e.Sku, e.BodegaId, b.Nombre, e.Cantidad
        FROM dbo.ExistenciaBodega e
        JOIN dbo.Bodega b ON b.BodegaId = e.BodegaId
        """;

    public List<Producto> ListarProductos(bool incluirInactivos = false)
    {
        using var c = baseDatos.Conectar();
        var productos = Sql.Leer(c, ConsultaProductos + " WHERE (@todos = 1 OR p.Activo = 1) ORDER BY p.Nombre", LeerProducto, null,
            Sql.Bit("@todos", incluirInactivos));
        var existencias = Sql.Leer(c, ConsultaExistencias + " ORDER BY e.Sku, e.BodegaId", LeerExistencia)
            .GroupBy(e => e.Sku).ToDictionary(g => g.Key, g => g.ToList());
        foreach (var p in productos) p.Existencias = existencias.GetValueOrDefault(p.Sku) ?? [];
        return productos;
    }

    public Producto? BuscarProducto(string sku)
    {
        using var c = baseDatos.Conectar();
        var p = Sql.Leer(c, ConsultaProductos + " WHERE p.Sku = @sku", LeerProducto, null, Sql.VarChar("@sku", sku, 20)).FirstOrDefault();
        if (p is not null)
            p.Existencias = Sql.Leer(c, ConsultaExistencias + " WHERE e.Sku = @sku ORDER BY e.BodegaId", LeerExistencia, null, Sql.VarChar("@sku", sku, 20));
        return p;
    }

    public void CrearProducto(Producto p, int bodegaId, int usuarioId, string nombreUsuario)
    {
        using var c = baseDatos.Conectar();
        using var t = c.BeginTransaction();
        try
        {
            // El producto nace con stock 0; el stock inicial entra como movimiento para que quede registrado quién lo puso.
            Sql.Ejecutar(c, """
                INSERT INTO dbo.Producto (Sku, Nombre, CategoriaId, Unidad, StockActual, StockMinimo, PrecioCompra, NitProveedor, Activo)
                VALUES (@sku, @nombre, @categoria, @unidad, 0, @minimo, @precio, @nit, 1)
                """, t,
                Sql.VarChar("@sku", p.Sku, 20), Sql.NVarChar("@nombre", p.Nombre, 120), Sql.Int("@categoria", p.CategoriaId),
                Sql.NVarChar("@unidad", p.Unidad, 20), Sql.Decimal("@minimo", p.StockMinimo), Sql.Decimal("@precio", p.PrecioCompra),
                Sql.VarChar("@nit", p.NitProveedor, 20));
            if (p.StockActual > 0)
                OperacionesStockSql.Aplicar(c, t, new Movimiento
                {
                    Sku = p.Sku, Tipo = ReglasInventario.Entrada, Cantidad = p.StockActual, Motivo = ReglasInventario.MotivoInventarioInicial,
                    BodegaId = bodegaId, UsuarioId = usuarioId, Usuario = nombreUsuario,
                });
            t.Commit();
        }
        catch
        {
            t.Rollback();
            throw;
        }
    }

    public void ActualizarProducto(Producto p)
    {
        using var c = baseDatos.Conectar();
        Sql.Ejecutar(c, """
            UPDATE dbo.Producto
            SET Nombre = @nombre, CategoriaId = @categoria, Unidad = @unidad, StockMinimo = @minimo,
                PrecioCompra = @precio, NitProveedor = @nit
            WHERE Sku = @sku
            """, null,
            Sql.VarChar("@sku", p.Sku, 20), Sql.NVarChar("@nombre", p.Nombre, 120), Sql.Int("@categoria", p.CategoriaId),
            Sql.NVarChar("@unidad", p.Unidad, 20), Sql.Decimal("@minimo", p.StockMinimo), Sql.Decimal("@precio", p.PrecioCompra),
            Sql.VarChar("@nit", p.NitProveedor, 20));
    }

    public bool TieneMovimientos(string sku)
    {
        using var c = baseDatos.Conectar();
        return Sql.Escalar(c, "SELECT TOP (1) 1 FROM dbo.MovimientoInventario WHERE Sku = @sku", null, Sql.VarChar("@sku", sku, 20)) is not null;
    }

    public void EliminarProducto(string sku)
    {
        using var c = baseDatos.Conectar();
        using var t = c.BeginTransaction();
        try
        {
            // Solo se llega aquí sin historial ni stock: las filas de existencia (en cero) se quitan junto con el producto.
            Sql.Ejecutar(c, "DELETE FROM dbo.ExistenciaBodega WHERE Sku = @sku AND Cantidad = 0", t, Sql.VarChar("@sku", sku, 20));
            Sql.Ejecutar(c, "DELETE FROM dbo.Producto WHERE Sku = @sku", t, Sql.VarChar("@sku", sku, 20));
            t.Commit();
        }
        catch
        {
            t.Rollback();
            throw;
        }
    }

    public void CambiarEstadoProducto(string sku, bool activo)
    {
        using var c = baseDatos.Conectar();
        Sql.Ejecutar(c, "UPDATE dbo.Producto SET Activo = @activo WHERE Sku = @sku", null, Sql.Bit("@activo", activo), Sql.VarChar("@sku", sku, 20));
    }

    // ---------------- Movimientos ----------------

    private const string ConsultaMovimientos = """
        SELECT m.MovimientoId, m.Sku, p.Nombre, p.Unidad, m.Tipo, m.Cantidad, m.Motivo,
               m.BodegaId, b.Nombre, m.UsuarioId, m.Usuario, m.Referencia, m.Fecha
        FROM dbo.MovimientoInventario m
        JOIN dbo.Producto p ON p.Sku = m.Sku
        JOIN dbo.Bodega b ON b.BodegaId = m.BodegaId
        """;

    public List<Movimiento> ListarMovimientos(int limite)
    {
        using var c = baseDatos.Conectar();
        return Sql.Leer(c, ConsultaMovimientos.Replace("SELECT m.", "SELECT TOP (@limite) m.") + " ORDER BY m.Fecha DESC, m.MovimientoId DESC",
            LeerMovimiento, null, Sql.Int("@limite", limite));
    }

    public List<Movimiento> ListarMovimientosDesde(DateTime desde)
    {
        using var c = baseDatos.Conectar();
        return Sql.Leer(c, ConsultaMovimientos + " WHERE m.Fecha >= @desde ORDER BY m.Fecha DESC, m.MovimientoId DESC",
            LeerMovimiento, null, Sql.Fecha("@desde", desde));
    }

    public HistorialVentas ObtenerHistorialVentas(DateOnly desde)
    {
        using var c = baseDatos.Conectar();
        // Mismo criterio del entrenamiento: ventas por "Venta", devoluciones de clientes y cambio de stock de todos los motivos.
        var filas = Sql.Leer(c, """
            SELECT Sku, CAST(Fecha AS date) AS Dia,
                   SUM(CASE WHEN Tipo = 'Salida' AND Motivo = N'Venta' THEN Cantidad ELSE 0 END) AS Ventas,
                   SUM(CASE WHEN Tipo = 'Entrada' AND Motivo = N'Devolución' THEN Cantidad ELSE 0 END) AS Devoluciones,
                   SUM(CASE WHEN Tipo = 'Entrada' THEN Cantidad ELSE -Cantidad END) AS DeltaStock
            FROM dbo.MovimientoInventario
            WHERE Fecha >= @desde
            GROUP BY Sku, CAST(Fecha AS date)
            """, r => new VentaDiaria(r.GetString(0), DateOnly.FromDateTime(r.GetDateTime(1)), r.GetDecimal(2), r.GetDecimal(3), r.GetDecimal(4)),
            null, Sql.Fecha("@desde", desde.ToDateTime(TimeOnly.MinValue)));
        var primeros = Sql.Leer(c, "SELECT Sku, CAST(MIN(Fecha) AS date) FROM dbo.MovimientoInventario GROUP BY Sku",
            r => (Sku: r.GetString(0), Dia: DateOnly.FromDateTime(r.GetDateTime(1))));
        return new HistorialVentas(filas, primeros.ToDictionary(x => x.Sku, x => x.Dia));
    }

    public decimal RegistrarMovimiento(Movimiento m)
    {
        using var c = baseDatos.Conectar();
        using var t = c.BeginTransaction();
        try
        {
            var total = OperacionesStockSql.Aplicar(c, t, m);
            t.Commit();
            return total;
        }
        catch
        {
            t.Rollback();
            throw;
        }
    }

    // ---------------- Lectura de filas ----------------

    private static Bodega LeerBodega(SqlDataReader r) => new()
    {
        BodegaId = r.GetInt32(0), Nombre = r.GetString(1), Direccion = r.TextoONulo(2), Activa = r.GetBoolean(3),
    };

    private static ExistenciaBodega LeerExistencia(SqlDataReader r) => new()
    {
        Sku = r.GetString(0), BodegaId = r.GetInt32(1), NombreBodega = r.GetString(2), Cantidad = r.GetDecimal(3),
    };

    private static Producto LeerProducto(SqlDataReader r) => new()
    {
        Sku = r.GetString(0),
        Nombre = r.GetString(1),
        CategoriaId = r.GetInt32(2),
        Categoria = r.GetString(3),
        Unidad = r.GetString(4),
        StockActual = r.GetDecimal(5),
        StockMinimo = r.GetDecimal(6),
        PrecioCompra = r.DecimalONulo(7),
        NitProveedor = r.TextoONulo(8),
        NombreProveedor = r.TextoONulo(9),
        DiasEntregaProveedor = r.EnteroONulo(10),
        Activo = r.GetBoolean(11),
    };

    private static Movimiento LeerMovimiento(SqlDataReader r) => new()
    {
        MovimientoId = r.GetInt32(0),
        Sku = r.GetString(1),
        NombreProducto = r.GetString(2),
        Unidad = r.GetString(3),
        Tipo = r.GetString(4),
        Cantidad = r.GetDecimal(5),
        Motivo = r.GetString(6),
        BodegaId = r.GetInt32(7),
        NombreBodega = r.GetString(8),
        UsuarioId = r.GetInt32(9),
        Usuario = r.GetString(10),
        Referencia = r.TextoONulo(11),
        Fecha = r.GetDateTime(12),
    };
}
