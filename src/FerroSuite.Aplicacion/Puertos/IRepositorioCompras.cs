namespace FerroSuite.Aplicacion.Puertos;

/// <summary>Persistencia de las órdenes de compra y su detalle.</summary>
public interface IRepositorioCompras
{
    List<OrdenCompra> ListarOrdenes();
    OrdenCompra? BuscarOrden(string codigo);
    bool HayOrdenesConProducto(string sku);

    /// <summary>Guarda el encabezado y todas las líneas en una transacción. Devuelve el código asignado.</summary>
    string CrearOrden(OrdenCompra orden);

    // Los cambios de estado solo se aplican si la orden sigue en el estado esperado (devuelven false si no).
    bool Aprobar(string codigo, DateTime fechaEntregaEsperada);
    bool Rechazar(string codigo);
    bool MarcarPagada(string codigo);

    /// <summary>
    /// Recibe una orden aprobada en una transacción: registra una entrada por cada línea en la bodega indicada,
    /// actualiza existencias y stock, y marca la orden como Recibida. Devuelve false si ya no estaba aprobada.
    /// </summary>
    bool Recibir(string codigo, int bodegaId, int usuarioId, string nombreUsuario);
}
