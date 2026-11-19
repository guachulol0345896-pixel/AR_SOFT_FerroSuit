namespace FerroSuite.Aplicacion.Inventario;

/// <summary>
/// Lógica de negocio del módulo de Inventario: valida lo que llega del frontend,
/// aplica las reglas y usa los repositorios para consultar o guardar.
/// </summary>
public class InventarioServicio(IRepositorioInventario inventario, IRepositorioProveedores proveedores,
    IRepositorioCompras compras, CalculadoraReposicion calculadora, Autorizador autorizador)
{
    private AuditoriaServicio Auditoria => autorizador.Auditoria;

    // ---------------- Catálogos ----------------

    public List<Categoria> ListarCategorias() => inventario.ListarCategorias().Where(c => c.Activa).ToList();

    public List<Bodega> ListarBodegas() => inventario.ListarBodegas().Where(b => b.Activa).ToList();

    public int CrearBodega(Bodega b, SesionUsuario? sesion)
    {
        autorizador.Exigir(sesion, Permisos.GestionarBodegas, "bodegas.crear");
        b.Nombre = (b.Nombre ?? "").Trim();
        b.Direccion = string.IsNullOrWhiteSpace(b.Direccion) ? null : b.Direccion.Trim();
        var errores = new List<string>();
        if (b.Nombre.Length == 0) errores.Add("El nombre de la bodega es obligatorio.");
        else if (b.Nombre.Length > 100) errores.Add("El nombre de la bodega no puede tener más de 100 caracteres.");
        else if (inventario.ExisteBodegaConNombre(b.Nombre)) errores.Add("Ya existe una bodega con ese nombre.");
        if (b.Direccion?.Length > 200) errores.Add("La dirección no puede tener más de 200 caracteres.");
        if (errores.Count > 0) throw new ErrorValidacion(errores);
        b.Activa = true;
        var id = inventario.CrearBodega(b);
        Auditoria.Exito(sesion, "bodegas.crear", $"Bodega {b.Nombre}");
        return id;
    }

    /// <summary>Todas las bodegas, también las inactivas (para administrarlas).</summary>
    public List<Bodega> ListarBodegasTodas(SesionUsuario? sesion)
    {
        autorizador.Exigir(sesion, Permisos.GestionarBodegas, "bodegas.listarTodas");
        return inventario.ListarBodegas();
    }

    /// <summary>Cambia nombre, dirección o estado de una bodega. No se desactiva si tiene existencias o si es la última activa.</summary>
    public void ActualizarBodega(Bodega d, SesionUsuario? sesion)
    {
        autorizador.Exigir(sesion, Permisos.GestionarBodegas, "bodegas.actualizar");
        var actual = d.BodegaId == 0 ? null : inventario.BuscarBodega(d.BodegaId);
        if (actual is null) throw new ErrorValidacion(["La bodega no existe."]);
        d.Nombre = (d.Nombre ?? "").Trim();
        d.Direccion = string.IsNullOrWhiteSpace(d.Direccion) ? null : d.Direccion.Trim();
        var errores = new List<string>();
        if (d.Nombre.Length == 0) errores.Add("El nombre de la bodega es obligatorio.");
        else if (d.Nombre.Length > 100) errores.Add("El nombre de la bodega no puede tener más de 100 caracteres.");
        else if (inventario.ExisteBodegaConNombre(d.Nombre, d.BodegaId)) errores.Add("Ya existe otra bodega con ese nombre.");
        if (d.Direccion?.Length > 200) errores.Add("La dirección no puede tener más de 200 caracteres.");
        if (actual.Activa && !d.Activa)
        {
            var existencias = inventario.ListarProductos().SelectMany(p => p.Existencias).Where(e => e.BodegaId == d.BodegaId).Sum(e => e.Cantidad);
            if (existencias > 0) errores.Add($"La bodega tiene mercancía ({existencias:0.##}); trasládala o ajústala antes de desactivarla.");
            if (!inventario.ListarBodegas().Any(b => b.Activa && b.BodegaId != d.BodegaId)) errores.Add("Debe quedar al menos una bodega activa.");
        }
        if (errores.Count > 0) throw new ErrorValidacion(errores);
        inventario.ActualizarBodega(d);
        Auditoria.Exito(sesion, "bodegas.actualizar", $"Bodega {d.BodegaId}",
            $"{actual.Nombre} → {d.Nombre}{(actual.Activa != d.Activa ? (d.Activa ? " · activada" : " · desactivada") : "")}");
    }

    /// <summary>Todas las categorías, también las inactivas (para administrarlas).</summary>
    public List<Categoria> ListarCategoriasTodas(SesionUsuario? sesion)
    {
        autorizador.Exigir(sesion, Permisos.GestionarProductos, "categorias.listarTodas");
        return inventario.ListarCategorias();
    }

    public int CrearCategoria(Categoria d, SesionUsuario? sesion)
    {
        autorizador.Exigir(sesion, Permisos.GestionarProductos, "categorias.crear");
        d.Nombre = (d.Nombre ?? "").Trim();
        ValidarCategoria(d, excepto: null);
        var id = inventario.CrearCategoria(d);
        Auditoria.Exito(sesion, "categorias.crear", $"Categoría {d.Nombre}");
        return id;
    }

    /// <summary>Cambia nombre o estado de una categoría. No se desactiva si tiene productos activos.</summary>
    public void ActualizarCategoria(Categoria d, SesionUsuario? sesion)
    {
        autorizador.Exigir(sesion, Permisos.GestionarProductos, "categorias.actualizar");
        var actual = inventario.ListarCategorias().FirstOrDefault(c => c.CategoriaId == d.CategoriaId);
        if (actual is null) throw new ErrorValidacion(["La categoría no existe."]);
        d.Nombre = (d.Nombre ?? "").Trim();
        ValidarCategoria(d, excepto: d.CategoriaId);
        if (actual.Activa && !d.Activa)
        {
            var enUso = inventario.ListarProductos().Count(p => p.Activo && p.CategoriaId == d.CategoriaId);
            if (enUso > 0) throw new ErrorValidacion([$"La categoría tiene {enUso} producto(s) activo(s); cámbialos de categoría antes de desactivarla."]);
        }
        inventario.ActualizarCategoria(d);
        Auditoria.Exito(sesion, "categorias.actualizar", $"Categoría {d.CategoriaId}",
            $"{actual.Nombre} → {d.Nombre}{(actual.Activa != d.Activa ? (d.Activa ? " · activada" : " · desactivada") : "")}");
    }

    private void ValidarCategoria(Categoria d, int? excepto)
    {
        var errores = new List<string>();
        if (d.Nombre.Length == 0) errores.Add("El nombre de la categoría es obligatorio.");
        else if (d.Nombre.Length > 50) errores.Add("El nombre de la categoría no puede tener más de 50 caracteres.");
        else if (d.Nombre.Any(char.IsControl)) errores.Add("El nombre de la categoría tiene caracteres no permitidos.");
        else if (inventario.ExisteCategoriaConNombre(d.Nombre, excepto)) errores.Add("Ya existe una categoría con ese nombre.");
        if (errores.Count > 0) throw new ErrorValidacion(errores);
    }

    // ---------------- Consultas ----------------

    /// <summary>Productos activos con consumo diario, días que alcanza el stock y estado.</summary>
    public List<Producto> ListarProductos()
    {
        var productos = inventario.ListarProductos();
        var analisis = calculadora.Analizar(DatosParaAsistente(productos), DateTimeOffset.Now).ToDictionary(a => a.Sku);
        foreach (var p in productos)
        {
            var a = analisis[p.Sku];
            p.ConsumoDiario = a.ConsumoDiario;
            p.DiasRestantes = a.DiasRestantes;
            p.Estado = a.Estado;
        }
        return productos;
    }

    /// <summary>
    /// Productos para mostrar al usuario: sin el precio de compra si su rol no lo necesita (mínimo privilegio:
    /// el vendedor de mostrador no ve costos).
    /// </summary>
    public List<Producto> ListarProductos(SesionUsuario? sesion)
    {
        var productos = ListarProductos();
        if (!Permisos.Tiene(sesion, Permisos.VerFinanzas) && !Permisos.Tiene(sesion, Permisos.GestionarProductos) && !Permisos.Tiene(sesion, Permisos.CrearOrdenes))
            foreach (var p in productos) p.PrecioCompra = null;
        return productos;
    }

    public List<Movimiento> ListarMovimientos(int limite) => inventario.ListarMovimientos(Math.Clamp(limite, 1, 500));

    /// <summary>Resumen del inicio. Los valores en pesos solo se incluyen si el rol puede ver finanzas.</summary>
    public object Resumen(SesionUsuario? sesion)
    {
        var finanzas = Permisos.Tiene(sesion, Permisos.VerFinanzas);
        var productos = ListarProductos();
        var ordenes = compras.ListarOrdenes();
        return new
        {
            Productos = productos.Count,
            EnRiesgo = productos.Count(p => p.Estado != "Normal"),
            Agotados = productos.Count(p => p.StockActual <= 0),
            ValorBodega = finanzas ? productos.Sum(p => (p.PrecioCompra ?? 0) * p.StockActual) : (decimal?)null,
            Proveedores = proveedores.Listar(incluirInactivos: false).Count,
            OrdenesPendientes = ordenes.Count(o => o.Estado == EstadosOrden.Pendiente),
            PorPagar = finanzas ? ordenes.Where(o => o.Estado is EstadosOrden.Aprobada or EstadosOrden.Recibida && !o.Pagada).Sum(o => o.Valor ?? 0) : (decimal?)null,
        };
    }

    // ---------------- Registro, actualización y eliminación ----------------

    public void CrearProducto(Producto d, SesionUsuario? sesion)
    {
        autorizador.Exigir(sesion, Permisos.GestionarProductos, "productos.crear");
        Limpiar(d);
        var errores = Validar(d);
        if (d.StockActual < 0) errores.Add("El stock inicial no puede ser negativo.");
        if (inventario.BuscarProducto(d.Sku) is not null) errores.Add("Ya existe un producto con ese SKU.");

        var bodegaId = d.BodegaInicialId ?? ListarBodegas().Select(b => b.BodegaId).FirstOrDefault();
        if (d.StockActual > 0 && (bodegaId == 0 || inventario.BuscarBodega(bodegaId) is not { Activa: true }))
            errores.Add("Elige una bodega activa para el stock inicial.");
        if (errores.Count > 0) throw new ErrorValidacion(errores);

        d.Activo = true;
        inventario.CrearProducto(d, bodegaId, sesion!.UsuarioId, sesion.NombreCompleto);
        Auditoria.Exito(sesion, "productos.crear", $"Producto {d.Sku}", d.StockActual > 0 ? $"Stock inicial {d.StockActual:0.##}" : null);
    }

    // RF04: el stock no se edita aquí; solo cambia con movimientos, para que quede registro.
    public void ActualizarProducto(Producto d, SesionUsuario? sesion)
    {
        autorizador.Exigir(sesion, Permisos.GestionarProductos, "productos.actualizar");
        Limpiar(d);
        var actual = inventario.BuscarProducto(d.Sku) ?? throw new ErrorValidacion(["El producto ya no existe."]);
        d.StockActual = actual.StockActual;
        var errores = Validar(d);
        if (actual.Unidad != d.Unidad && !ReglasInventario.AdmiteDecimales(d.Unidad) && actual.StockActual != Math.Floor(actual.StockActual))
            errores.Add($"No se puede cambiar a {d.Unidad.ToLower()}: el stock actual tiene decimales.");
        if (errores.Count > 0) throw new ErrorValidacion(errores);
        inventario.ActualizarProducto(d);
        Auditoria.Exito(sesion, "productos.actualizar", $"Producto {d.Sku}");
    }

    /// <summary>
    /// Si el producto tiene historial (movimientos u órdenes), se desactiva en lugar de borrarse:
    /// el historial no se pierde. Devuelve el mensaje para el usuario.
    /// </summary>
    public string EliminarProducto(string sku, SesionUsuario? sesion)
    {
        autorizador.Exigir(sesion, Permisos.EliminarProductos, "productos.eliminar");
        sku = Identificadores.NormalizarSku(sku);
        var producto = inventario.BuscarProducto(sku) ?? throw new ErrorValidacion(["El producto ya no existe."]);
        if (producto.StockActual != 0 || inventario.TieneMovimientos(sku) || compras.HayOrdenesConProducto(sku))
        {
            inventario.CambiarEstadoProducto(sku, false);
            Auditoria.Exito(sesion, "productos.eliminar", $"Producto {sku}", "Desactivado (tiene historial o existencias)");
            return "El producto tiene historial o existencias, así que se desactivó (no se borró) y ya no aparece en el catálogo.";
        }
        inventario.EliminarProducto(sku);
        Auditoria.Exito(sesion, "productos.eliminar", $"Producto {sku}", "Eliminado (sin historial)");
        return "Producto eliminado.";
    }

    // RF05: registra una entrada o salida en una bodega, a nombre del usuario de la sesión.
    public decimal RegistrarMovimiento(Movimiento m, SesionUsuario? sesion)
    {
        // Cada motivo exige su permiso: el vendedor solo registra ventas y devoluciones; los ajustes son de bodega.
        autorizador.ExigirAlguno(sesion, "movimientos.registrar", Permisos.RegistrarVenta, Permisos.AjustarInventario);
        if (ReglasInventario.Motivos.ContainsKey(m.Motivo ?? ""))
            autorizador.Exigir(sesion, Permisos.ParaMotivo(m.Motivo!), "movimientos.registrar");
        if (m.Motivo == ReglasInventario.MotivoInventarioInicial)
            throw new ErrorValidacion(["El inventario inicial se registra al crear el producto."]);
        m.Sku = Identificadores.NormalizarSku(m.Sku);
        var errores = new List<string>();
        var producto = inventario.BuscarProducto(m.Sku);
        if (producto is null || !producto.Activo) errores.Add("Elige un producto activo.");
        if (m.Tipo != ReglasInventario.Entrada && m.Tipo != ReglasInventario.Salida) errores.Add("El tipo debe ser Entrada o Salida.");
        if (m.BodegaId == 0) m.BodegaId = ListarBodegas().Select(b => b.BodegaId).FirstOrDefault();
        var bodega = m.BodegaId == 0 ? null : inventario.BuscarBodega(m.BodegaId);
        if (bodega is not { Activa: true }) errores.Add("Elige una bodega activa.");
        if (!ReglasInventario.Motivos.TryGetValue(m.Motivo ?? "", out var tipoMotivo)) errores.Add("Elige un motivo válido.");
        else if (tipoMotivo != "Ambos" && tipoMotivo != m.Tipo)
            errores.Add($"El motivo \"{m.Motivo}\" corresponde a una {tipoMotivo.ToLower()}, no a una {m.Tipo?.ToLower()}.");
        if (m.Cantidad <= 0) errores.Add("La cantidad debe ser mayor que 0.");
        else if (producto is not null && !ReglasInventario.EsCantidadValida(m.Cantidad, producto.Unidad))
            errores.Add($"La cantidad debe ser un número entero para {producto.Unidad.ToLower()}.");
        if (producto is not null && bodega is not null && m.Tipo == ReglasInventario.Salida)
        {
            var disponible = producto.Existencias.FirstOrDefault(e => e.BodegaId == bodega.BodegaId)?.Cantidad ?? 0;
            if (m.Cantidad > disponible)
                errores.Add($"No hay stock suficiente en {bodega.Nombre} (disponible: {disponible:0.##}).");
        }
        if (errores.Count > 0) throw new ErrorValidacion(errores);

        // El responsable sale de la sesión del backend, nunca de lo que envíe la pantalla.
        m.UsuarioId = sesion!.UsuarioId;
        m.Usuario = sesion.NombreCompleto;
        m.Referencia = null;
        return inventario.RegistrarMovimiento(m);
    }

    // ---------------- Validaciones (RF12) ----------------

    private void Limpiar(Producto d)
    {
        d.Sku = Identificadores.NormalizarSku(d.Sku);
        d.Nombre = (d.Nombre ?? "").Trim();
        d.NitProveedor = string.IsNullOrWhiteSpace(d.NitProveedor) ? null : Identificadores.NormalizarNit(d.NitProveedor);
        // Compatibilidad: si llega el nombre de la categoría en lugar del identificador.
        if (d.CategoriaId == 0 && !string.IsNullOrWhiteSpace(d.Categoria))
            d.CategoriaId = inventario.ListarCategorias().FirstOrDefault(c => c.Nombre == d.Categoria.Trim())?.CategoriaId ?? 0;
    }

    private List<string> Validar(Producto d)
    {
        var errores = new List<string>();
        if (Identificadores.ValidarSku(d.Sku) is string errorSku) errores.Add(errorSku);
        if (d.Nombre.Length == 0) errores.Add("El nombre del producto es obligatorio.");
        else if (d.Nombre.Length > 120) errores.Add("El nombre no puede tener más de 120 caracteres.");
        if (!inventario.ListarCategorias().Any(c => c.CategoriaId == d.CategoriaId && c.Activa)) errores.Add("Elige una categoría válida.");
        if (!ReglasInventario.Unidades.Contains(d.Unidad)) errores.Add("Elige una unidad de medida válida.");
        if (d.StockMinimo < 0) errores.Add("El stock mínimo no puede ser negativo.");
        if (ReglasInventario.Unidades.Contains(d.Unidad) && !ReglasInventario.AdmiteDecimales(d.Unidad) &&
            (d.StockActual != Math.Floor(d.StockActual) || d.StockMinimo != Math.Floor(d.StockMinimo)))
            errores.Add($"Las cantidades deben ser números enteros para {d.Unidad.ToLower()}.");
        if (d.PrecioCompra < 0) errores.Add("El precio de compra no puede ser negativo.");
        if (d.NitProveedor is not null && proveedores.Buscar(d.NitProveedor) is not { Activo: true })
            errores.Add("El proveedor elegido no existe o está inactivo.");
        return errores;
    }

    // ---------------- Datos para el asistente de IA ----------------

    /// <summary>Arma lo que necesita el asistente: productos, ventas de 60 días y órdenes.</summary>
    public SolicitudInventario DatosParaAsistente(List<Producto>? productos = null)
    {
        productos ??= inventario.ListarProductos();

        var productosDto = productos.Select(p => new ProductoDto(
            p.Sku, p.Nombre, p.Categoria, p.Unidad, p.StockActual, p.StockMinimo, p.PrecioCompra,
            p.NitProveedor, p.NombreProveedor, p.DiasEntregaProveedor)).ToList();

        var movimientosDto = inventario.ListarMovimientosDesde(DateTime.Now.AddDays(-60))
            .Select(m => new MovimientoDto(m.Sku, m.Tipo, m.Cantidad, new DateTimeOffset(m.Fecha))).ToList();

        var ordenesDto = compras.ListarOrdenes()
            .Where(o => o.Estado != EstadosOrden.Rechazada)
            .Select(o => new OrdenDto(
                o.Codigo, o.Detalles.FirstOrDefault()?.Sku ?? "", o.NombreProducto, o.NombreProveedor, o.Cantidad,
                o.Estado == EstadosOrden.Recibida ? EstadosOrden.Aprobada : o.Estado, o.Valor,
                new DateTimeOffset(o.FechaCreacion),
                o.FechaAprobacion is DateTime f ? new DateTimeOffset(f) : null,
                o.Pagada, o.Detalles.Select(d => d.Sku).ToList())).ToList();

        return new SolicitudInventario(productosDto, movimientosDto, ordenesDto);
    }
}
