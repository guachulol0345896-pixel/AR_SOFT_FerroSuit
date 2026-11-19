namespace FerroSuite.Aplicacion.Puertos;

/// <summary>Persistencia del módulo de Inventario: categorías, bodegas, productos, existencias y movimientos.</summary>
public interface IRepositorioInventario
{
    List<Categoria> ListarCategorias();
    List<Bodega> ListarBodegas();
    Bodega? BuscarBodega(int bodegaId);
    bool ExisteBodegaConNombre(string nombre, int? excepto = null);
    int CrearBodega(Bodega bodega);
    void ActualizarBodega(Bodega bodega);
    bool ExisteCategoriaConNombre(string nombre, int? excepto = null);
    int CrearCategoria(Categoria categoria);
    void ActualizarCategoria(Categoria categoria);

    /// <summary>Productos con su categoría, proveedor y existencias por bodega.</summary>
    List<Producto> ListarProductos(bool incluirInactivos = false);
    Producto? BuscarProducto(string sku);

    /// <summary>
    /// Crea el producto en una transacción. Si StockActual &gt; 0, registra su existencia en la bodega indicada
    /// y un movimiento de "Inventario inicial" a nombre del usuario responsable.
    /// </summary>
    void CrearProducto(Producto producto, int bodegaId, int usuarioId, string nombreUsuario);
    /// <summary>Actualiza los datos del producto, pero no el stock (solo cambia con movimientos).</summary>
    void ActualizarProducto(Producto producto);
    bool TieneMovimientos(string sku);
    void EliminarProducto(string sku);
    void CambiarEstadoProducto(string sku, bool activo);

    List<Movimiento> ListarMovimientos(int limite);
    List<Movimiento> ListarMovimientosDesde(DateTime desde);

    /// <summary>
    /// Totales diarios por producto desde <paramref name="desde"/> hasta hoy (ventas, devoluciones y cambio de stock de todos
    /// los movimientos) y la fecha del primer movimiento de cada producto. Lo usa el pronóstico de demanda.
    /// </summary>
    IA.Pronostico.HistorialVentas ObtenerHistorialVentas(DateOnly desde);

    /// <summary>
    /// Registra el movimiento y actualiza la existencia de la bodega y el total del producto en UNA transacción.
    /// En una salida, el descuento solo se aplica si hay cantidad suficiente en ese instante (seguro ante
    /// movimientos simultáneos); si no la hay, lanza <see cref="ErrorValidacion"/> y no cambia nada.
    /// Devuelve el nuevo stock total del producto.
    /// </summary>
    decimal RegistrarMovimiento(Movimiento movimiento);
}
