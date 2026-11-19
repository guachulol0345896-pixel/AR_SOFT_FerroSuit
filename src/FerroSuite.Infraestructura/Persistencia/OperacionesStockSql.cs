using Microsoft.Data.SqlClient;

namespace FerroSuite.Infraestructura.Persistencia;

/// <summary>
/// Pasos de una entrada o salida de inventario. Se ejecutan SIEMPRE dentro de una transacción abierta por quien
/// llama, y siempre en el mismo orden (existencia → producto → movimiento) para evitar bloqueos cruzados.
/// </summary>
internal static class OperacionesStockSql
{
    /// <summary>
    /// Descuenta de la existencia solo si hay cantidad suficiente EN ESE INSTANTE (una sola sentencia UPDATE
    /// con condición: dos salidas simultáneas no pueden dejar la existencia en negativo).
    /// </summary>
    public static void Descontar(SqlConnection c, SqlTransaction t, string sku, int bodegaId, decimal cantidad)
    {
        var filas = Sql.Ejecutar(c, """
            UPDATE dbo.ExistenciaBodega
            SET Cantidad = Cantidad - @cantidad
            WHERE Sku = @sku AND BodegaId = @bodega AND Cantidad >= @cantidad
            """, t, Sql.VarChar("@sku", sku, 20), Sql.Int("@bodega", bodegaId), Sql.Decimal("@cantidad", cantidad));
        if (filas == 1) return;

        var disponible = (decimal?)Sql.Escalar(c, "SELECT Cantidad FROM dbo.ExistenciaBodega WHERE Sku = @sku AND BodegaId = @bodega", t,
            Sql.VarChar("@sku", sku, 20), Sql.Int("@bodega", bodegaId)) ?? 0;
        throw new ErrorValidacion([$"No hay stock suficiente en esa bodega (disponible: {disponible:0.##})."]);
    }

    /// <summary>Suma a la existencia; si el producto no tenía fila en esa bodega, la crea (con bloqueo para evitar duplicados).</summary>
    public static void Sumar(SqlConnection c, SqlTransaction t, string sku, int bodegaId, decimal cantidad)
    {
        Sql.Ejecutar(c, """
            UPDATE dbo.ExistenciaBodega WITH (UPDLOCK, HOLDLOCK)
            SET Cantidad = Cantidad + @cantidad
            WHERE Sku = @sku AND BodegaId = @bodega;
            IF @@ROWCOUNT = 0
                INSERT INTO dbo.ExistenciaBodega (Sku, BodegaId, Cantidad) VALUES (@sku, @bodega, @cantidad);
            """, t, Sql.VarChar("@sku", sku, 20), Sql.Int("@bodega", bodegaId), Sql.Decimal("@cantidad", cantidad));
    }

    /// <summary>Actualiza el total del producto con la diferencia y devuelve el nuevo total.</summary>
    public static decimal AjustarTotal(SqlConnection c, SqlTransaction t, string sku, decimal diferencia) =>
        (decimal)(Sql.Escalar(c, """
            UPDATE dbo.Producto
            SET StockActual = StockActual + @diferencia
            OUTPUT INSERTED.StockActual
            WHERE Sku = @sku
            """, t, Sql.VarChar("@sku", sku, 20), Sql.Decimal("@diferencia", diferencia))
            ?? throw new ErrorValidacion(["El producto ya no existe."]));

    public static void InsertarMovimiento(SqlConnection c, SqlTransaction t, Movimiento m) =>
        Sql.Ejecutar(c, """
            INSERT INTO dbo.MovimientoInventario (Sku, Tipo, Cantidad, Motivo, Usuario, BodegaId, UsuarioId, Referencia)
            VALUES (@sku, @tipo, @cantidad, @motivo, @usuario, @bodega, @usuarioId, @referencia)
            """, t,
            Sql.VarChar("@sku", m.Sku, 20), Sql.VarChar("@tipo", m.Tipo, 10), Sql.Decimal("@cantidad", m.Cantidad),
            Sql.NVarChar("@motivo", m.Motivo, 40), Sql.NVarChar("@usuario", Sql.Recortar(m.Usuario, 80), 80),
            Sql.Int("@bodega", m.BodegaId), Sql.Int("@usuarioId", m.UsuarioId), Sql.NVarChar("@referencia", m.Referencia, 30));

    /// <summary>Movimiento completo: existencia, total y registro. Devuelve el nuevo total del producto.</summary>
    public static decimal Aplicar(SqlConnection c, SqlTransaction t, Movimiento m)
    {
        if (m.Tipo == ReglasInventario.Salida) Descontar(c, t, m.Sku, m.BodegaId, m.Cantidad);
        else Sumar(c, t, m.Sku, m.BodegaId, m.Cantidad);
        var total = AjustarTotal(c, t, m.Sku, m.Tipo == ReglasInventario.Salida ? -m.Cantidad : m.Cantidad);
        InsertarMovimiento(c, t, m);
        return total;
    }
}
