namespace FerroSuite.Aplicacion.Compras;

/// <summary>Lógica de negocio de las órdenes de compra (RF08 y RF09): creación, aprobación, recepción y pago.</summary>
public class OrdenServicio(IRepositorioCompras compras, IRepositorioInventario inventario, IRepositorioProveedores proveedores,
    TimeProvider reloj, Autorizador autorizador)
{
    private AuditoriaServicio Auditoria => autorizador.Auditoria;

    public List<OrdenCompra> Listar()
    {
        var ordenes = compras.ListarOrdenes();
        foreach (var o in ordenes)
        {
            if (o.Estado is EstadosOrden.Aprobada or EstadosOrden.Recibida && !o.Pagada && o.FechaAprobacion is DateTime aprobada)
                o.VencePago = aprobada.AddDays(GeneradorRecordatorios.PlazoPagoDias);
        }
        return ordenes;
    }

    /// <summary>Crea la orden en estado pendiente. El precio de cada línea se toma del producto en este momento y queda guardado.</summary>
    public string Crear(OrdenCompra d, SesionUsuario? sesion)
    {
        autorizador.Exigir(sesion, Permisos.CrearOrdenes, "ordenes.crear");
        if (d.Detalles.Count > 50) throw new ErrorValidacion(["Una orden no puede tener más de 50 líneas."]);
        var errores = new List<string>();
        d.NitProveedor = Identificadores.NormalizarNit(d.NitProveedor);
        if (d.NitProveedor.Length == 0 || proveedores.Buscar(d.NitProveedor) is not { Activo: true }) errores.Add("Elige un proveedor activo.");
        if (d.Detalles.Count == 0) errores.Add("La orden debe tener al menos un producto.");
        foreach (var linea in d.Detalles)
        {
            linea.Sku = Identificadores.NormalizarSku(linea.Sku);
            var producto = inventario.BuscarProducto(linea.Sku);
            if (producto is null || !producto.Activo) { errores.Add($"El producto {linea.Sku} no existe o está inactivo."); continue; }
            if (!ReglasInventario.EsCantidadValida(linea.Cantidad, producto.Unidad))
                errores.Add(linea.Cantidad <= 0 ? $"La cantidad de {producto.Nombre} debe ser mayor que 0."
                                                : $"La cantidad de {producto.Nombre} debe ser un número entero ({producto.Unidad.ToLower()}).");
            linea.PrecioUnitario = producto.PrecioCompra;
        }
        if (d.Detalles.GroupBy(l => l.Sku).Any(g => g.Count() > 1)) errores.Add("Un producto no puede aparecer dos veces en la misma orden.");
        if (errores.Count > 0) throw new ErrorValidacion(errores);

        d.Estado = EstadosOrden.Pendiente;
        var codigo = compras.CrearOrden(d);
        Auditoria.Exito(sesion, "ordenes.crear", $"Orden {codigo}", $"{d.Detalles.Count} línea(s), proveedor {d.NitProveedor}");
        return codigo;
    }

    public void Aprobar(string codigo, SesionUsuario? sesion)
    {
        autorizador.Exigir(sesion, Permisos.AprobarOrdenes, "ordenes.aprobar");
        var orden = Buscar(codigo);
        if (orden.Estado != EstadosOrden.Pendiente) throw new ErrorValidacion([$"La orden {orden.Codigo} ya no está pendiente ({orden.Estado.ToLower()})."]);
        var dias = proveedores.Buscar(orden.NitProveedor)?.DiasEntrega ?? 0;
        if (!compras.Aprobar(orden.Codigo, reloj.GetLocalNow().DateTime.AddDays(dias)))
            throw new ErrorValidacion([$"La orden {orden.Codigo} cambió mientras tanto. Vuelve a cargar la lista."]);
        Auditoria.Exito(sesion, "ordenes.aprobar", $"Orden {orden.Codigo}", orden.Valor is decimal v ? $"Valor {v:0}" : null);
    }

    public void Rechazar(string codigo, SesionUsuario? sesion)
    {
        autorizador.Exigir(sesion, Permisos.AprobarOrdenes, "ordenes.rechazar");
        var orden = Buscar(codigo);
        if (orden.Estado != EstadosOrden.Pendiente) throw new ErrorValidacion([$"Solo se pueden rechazar órdenes pendientes (la {orden.Codigo} está {orden.Estado.ToLower()})."]);
        if (!compras.Rechazar(orden.Codigo)) throw new ErrorValidacion([$"La orden {orden.Codigo} cambió mientras tanto. Vuelve a cargar la lista."]);
        Auditoria.Exito(sesion, "ordenes.rechazar", $"Orden {orden.Codigo}");
    }

    /// <summary>Recibe la mercancía: entradas en la bodega indicada a nombre del usuario de la sesión.</summary>
    public void Recibir(string codigo, int? bodegaId, SesionUsuario? sesion)
    {
        autorizador.Exigir(sesion, Permisos.RecibirOrdenes, "ordenes.recibir");
        var orden = Buscar(codigo);
        if (orden.Estado != EstadosOrden.Aprobada) throw new ErrorValidacion([$"Solo se reciben órdenes aprobadas (la {orden.Codigo} está {orden.Estado.ToLower()})."]);
        var bodega = bodegaId is int id ? inventario.BuscarBodega(id) : inventario.ListarBodegas().FirstOrDefault(b => b.Activa);
        if (bodega is not { Activa: true }) throw new ErrorValidacion(["Elige una bodega activa para recibir la mercancía."]);
        if (!compras.Recibir(orden.Codigo, bodega.BodegaId, sesion!.UsuarioId, sesion.NombreCompleto))
            throw new ErrorValidacion([$"La orden {orden.Codigo} cambió mientras tanto. Vuelve a cargar la lista."]);
        Auditoria.Exito(sesion, "ordenes.recibir", $"Orden {orden.Codigo}", $"Bodega {bodega.Nombre}");
    }

    public void MarcarPagada(string codigo, SesionUsuario? sesion)
    {
        autorizador.Exigir(sesion, Permisos.PagarOrdenes, "ordenes.pagar");
        var orden = Buscar(codigo);
        if (orden.Estado is not (EstadosOrden.Aprobada or EstadosOrden.Recibida)) throw new ErrorValidacion(["Primero hay que aprobar la orden."]);
        if (orden.Pagada) throw new ErrorValidacion([$"La orden {orden.Codigo} ya estaba pagada."]);
        if (!compras.MarcarPagada(orden.Codigo)) throw new ErrorValidacion([$"La orden {orden.Codigo} cambió mientras tanto. Vuelve a cargar la lista."]);
        Auditoria.Exito(sesion, "ordenes.pagar", $"Orden {orden.Codigo}");
    }

    private OrdenCompra Buscar(string codigo)
    {
        codigo = (codigo ?? "").Trim().ToUpperInvariant();
        if (codigo.Length is 0 or > 20) throw new ErrorValidacion(["El código de la orden no es válido."]);
        return compras.BuscarOrden(codigo) ?? throw new ErrorValidacion([$"La orden {codigo} no existe."]);
    }
}
