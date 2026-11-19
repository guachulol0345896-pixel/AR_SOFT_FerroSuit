// Almacenamiento EN MEMORIA solo para pruebas. Implementa los cuatro repositorios con las mismas reglas que
// los repositorios SQL (salida condicional, cambios de estado condicionales, código de orden 'OC-' + 3 dígitos
// hasta 999 y número completo desde 1000, movimientos que no se borran). Es seguro entre hilos (un candado).
// No es SQL Server: lo que depende de SQL Server se prueba aparte con "--bd".
using FerroSuite.Dominio.Usuarios;
using FerroSuite.Dominio.Seguridad;
namespace FerroSuite.Pruebas.Dobles;

public class AlmacenMemoria : IRepositorioInventario, IRepositorioProveedores, IRepositorioCompras, IRepositorioUsuarios, IRepositorioAuditoria
{
    private readonly Lock c = new();
    public List<Producto> P = []; public List<Proveedor> Pr = []; public List<Movimiento> M = []; public List<OrdenCompra> O = [];
    public List<Categoria> Cat = []; public List<Bodega> B = []; public List<ExistenciaBodega> E = []; public List<Rol> R = []; public List<Usuario> U = [];
    int idMov, idOrden, idBodega, idUsuario, idDetalle;

    public AlmacenMemoria()
    {
        var i = 1;
        foreach (var n in new[] { "Construcción", "Plomería", "Eléctricos", "Tornillería", "Pinturas", "Herramientas" }) Cat.Add(new Categoria { CategoriaId = i++, Nombre = n, Activa = true });
        B.Add(new Bodega { BodegaId = ++idBodega, Nombre = "Bodega principal", Activa = true });
        R.AddRange([new Rol { RolId = 1, Codigo = Rol.Administrador, Nombre = "Administrador" }, new Rol { RolId = 2, Codigo = Rol.Bodega, Nombre = "Encargado de bodega" },
                    new Rol { RolId = 3, Codigo = Rol.Vendedor, Nombre = "Vendedor de mostrador" }, new Rol { RolId = 4, Codigo = Rol.Contador, Nombre = "Contador externo" }]);
    }

    public static string Codigo(int id) => "OC-" + (id < 1000 ? id.ToString("000") : id.ToString());

    /// <summary>Para pruebas: carga un producto con existencia en la bodega principal (sin movimiento).</summary>
    public void Sembrar(Producto p) { lock (c) { P.Add(p); if (p.StockActual > 0) E.Add(new ExistenciaBodega { Sku = p.Sku, BodegaId = 1, Cantidad = p.StockActual }); } }

    // ---------- Inventario ----------
    /// <summary>Si es true, listar categorías lanza una excepción inesperada con texto "interno" (no debe llegar a la pantalla).</summary>
    public bool FallaInesperada;
    public List<Categoria> ListarCategorias() { if (FallaInesperada) throw new InvalidOperationException("SECRETO-INTERNO Server=equipo;ruta=C:\\datos"); lock (c) return Cat.ToList(); }
    public List<Bodega> ListarBodegas() { lock (c) return B.ToList(); }
    public Bodega? BuscarBodega(int id) { lock (c) return B.FirstOrDefault(b => b.BodegaId == id); }
    public bool ExisteBodegaConNombre(string n, int? excepto = null) { lock (c) return B.Any(b => b.Nombre == n && b.BodegaId != excepto); }
    public void ActualizarBodega(Bodega b) { lock (c) { var x = B.First(y => y.BodegaId == b.BodegaId); x.Nombre = b.Nombre; x.Direccion = b.Direccion; x.Activa = b.Activa; } }
    public bool ExisteCategoriaConNombre(string n, int? excepto = null) { lock (c) return Cat.Any(x => x.Nombre == n && x.CategoriaId != excepto); }
    public int CrearCategoria(Categoria cat) { lock (c) { cat.CategoriaId = Cat.Max(x => x.CategoriaId) + 1; cat.Activa = true; Cat.Add(cat); return cat.CategoriaId; } }
    public void ActualizarCategoria(Categoria cat) { lock (c) { var x = Cat.First(y => y.CategoriaId == cat.CategoriaId); x.Nombre = cat.Nombre; x.Activa = cat.Activa; } }
    public int CrearBodega(Bodega b) { lock (c) { b.BodegaId = ++idBodega; b.Activa = true; B.Add(b); return b.BodegaId; } }

    Producto Copia(Producto p)
    {
        var pr = Pr.FirstOrDefault(x => x.Nit == p.NitProveedor);
        return new Producto
        {
            Sku = p.Sku, Nombre = p.Nombre, CategoriaId = p.CategoriaId, Categoria = Cat.First(x => x.CategoriaId == p.CategoriaId).Nombre, Unidad = p.Unidad,
            StockActual = p.StockActual, StockMinimo = p.StockMinimo, PrecioCompra = p.PrecioCompra, NitProveedor = p.NitProveedor, Activo = p.Activo,
            NombreProveedor = pr?.Nombre, DiasEntregaProveedor = pr?.DiasEntrega,
            Existencias = E.Where(e => e.Sku == p.Sku).Select(e => new ExistenciaBodega { Sku = e.Sku, BodegaId = e.BodegaId, Cantidad = e.Cantidad, NombreBodega = B.First(b => b.BodegaId == e.BodegaId).Nombre }).ToList(),
        };
    }
    /// <summary>Si es true, listar productos falla como si se cayera la base de datos.</summary>
    public bool Caido;
    public List<Producto> ListarProductos(bool todos = false) { if (Caido) throw new ErrorBdSimulado("conexión"); lock (c) return P.Where(p => todos || p.Activo).OrderBy(p => p.Nombre).Select(Copia).ToList(); }
    public Producto? BuscarProducto(string sku) { lock (c) return P.FirstOrDefault(p => p.Sku == sku) is { } p ? Copia(p) : null; }
    public void CrearProducto(Producto p, int bodegaId, int usuarioId, string nombre)
    {
        lock (c)
        {
            if (P.Any(x => x.Sku == p.Sku)) throw new ErrorBdSimulado("PK_Producto");
            var stock = p.StockActual;
            P.Add(new Producto { Sku = p.Sku, Nombre = p.Nombre, CategoriaId = p.CategoriaId, Unidad = p.Unidad, StockActual = 0, StockMinimo = p.StockMinimo, PrecioCompra = p.PrecioCompra, NitProveedor = p.NitProveedor, Activo = true });
            if (stock > 0) Aplicar(new Movimiento { Sku = p.Sku, Tipo = ReglasInventario.Entrada, Cantidad = stock, Motivo = ReglasInventario.MotivoInventarioInicial, BodegaId = bodegaId, UsuarioId = usuarioId, Usuario = nombre });
        }
    }
    public void ActualizarProducto(Producto p) { lock (c) { var a = P.First(x => x.Sku == p.Sku); a.Nombre = p.Nombre; a.CategoriaId = p.CategoriaId; a.Unidad = p.Unidad; a.StockMinimo = p.StockMinimo; a.PrecioCompra = p.PrecioCompra; a.NitProveedor = p.NitProveedor; } }
    public bool TieneMovimientos(string sku) { lock (c) return M.Any(m => m.Sku == sku); }
    public void EliminarProducto(string sku)
    {
        lock (c)
        {
            // Igual que la llave foránea sin cascada: con historial no se puede borrar.
            if (M.Any(m => m.Sku == sku) || O.Any(o => o.Detalles.Any(d => d.Sku == sku))) throw new ErrorBdSimulado("FK_Movimiento_Producto");
            E.RemoveAll(e => e.Sku == sku && e.Cantidad == 0); P.RemoveAll(p => p.Sku == sku);
        }
    }
    public void CambiarEstadoProducto(string sku, bool activo) { lock (c) P.First(p => p.Sku == sku).Activo = activo; }

    Movimiento Ver(Movimiento m) { var p = P.First(x => x.Sku == m.Sku); return new Movimiento { MovimientoId = m.MovimientoId, Sku = m.Sku, NombreProducto = p.Nombre, Unidad = p.Unidad, Tipo = m.Tipo, Cantidad = m.Cantidad, Motivo = m.Motivo, BodegaId = m.BodegaId, NombreBodega = B.First(b => b.BodegaId == m.BodegaId).Nombre, UsuarioId = m.UsuarioId, Usuario = m.Usuario, Referencia = m.Referencia, Fecha = m.Fecha }; }
    public List<Movimiento> ListarMovimientos(int limite) { lock (c) return M.OrderByDescending(m => m.Fecha).ThenByDescending(m => m.MovimientoId).Take(limite).Select(Ver).ToList(); }
    public List<Movimiento> ListarMovimientosDesde(DateTime d) { lock (c) return M.Where(m => m.Fecha >= d).OrderByDescending(m => m.Fecha).Select(Ver).ToList(); }
    public decimal RegistrarMovimiento(Movimiento m) { lock (c) return Aplicar(m); }

    // Igual que la consulta SQL: totales diarios desde 'desde' y primer movimiento de cada producto.
    public FerroSuite.Aplicacion.IA.Pronostico.HistorialVentas ObtenerHistorialVentas(DateOnly desde)
    {
        lock (c)
        {
            var filas = M.Where(m => DateOnly.FromDateTime(m.Fecha) >= desde)
                .GroupBy(m => (m.Sku, Dia: DateOnly.FromDateTime(m.Fecha)))
                .Select(g => new FerroSuite.Aplicacion.IA.Pronostico.VentaDiaria(g.Key.Sku, g.Key.Dia,
                    g.Where(m => m.Tipo == "Salida" && m.Motivo == "Venta").Sum(m => m.Cantidad),
                    g.Where(m => m.Tipo == "Entrada" && m.Motivo == "Devolución").Sum(m => m.Cantidad),
                    g.Sum(m => m.Tipo == "Entrada" ? m.Cantidad : -m.Cantidad)))
                .ToList();
            var primeros = M.GroupBy(m => m.Sku).ToDictionary(g => g.Key, g => DateOnly.FromDateTime(g.Min(m => m.Fecha)));
            return new FerroSuite.Aplicacion.IA.Pronostico.HistorialVentas(filas, primeros);
        }
    }

    // Igual que OperacionesStockSql.Aplicar: existencia (salida solo si alcanza) → total → movimiento.
    decimal Aplicar(Movimiento m)
    {
        var e = E.FirstOrDefault(x => x.Sku == m.Sku && x.BodegaId == m.BodegaId);
        if (m.Tipo == ReglasInventario.Salida)
        {
            if (e is null || e.Cantidad < m.Cantidad) throw new ErrorValidacion([$"No hay stock suficiente en esa bodega (disponible: {e?.Cantidad ?? 0:0.##})."]);
            e.Cantidad -= m.Cantidad;
        }
        else if (e is null) E.Add(new ExistenciaBodega { Sku = m.Sku, BodegaId = m.BodegaId, Cantidad = m.Cantidad });
        else e.Cantidad += m.Cantidad;
        var p = P.First(x => x.Sku == m.Sku);
        p.StockActual += m.Tipo == ReglasInventario.Salida ? -m.Cantidad : m.Cantidad;
        M.Add(new Movimiento { MovimientoId = ++idMov, Sku = m.Sku, Tipo = m.Tipo, Cantidad = m.Cantidad, Motivo = m.Motivo, BodegaId = m.BodegaId, UsuarioId = m.UsuarioId, Usuario = m.Usuario, Referencia = m.Referencia, Fecha = m.Fecha == default ? DateTime.Now : m.Fecha });
        return p.StockActual;
    }

    // ---------- Proveedores ----------
    public List<Proveedor> Listar(bool todos = true) { lock (c) return Pr.Where(p => todos || p.Activo).OrderBy(p => p.Nombre).ToList(); }
    public Proveedor? Buscar(string nit) { lock (c) return Pr.FirstOrDefault(p => p.Nit == nit); }
    public void Crear(Proveedor p) { lock (c) { p.Activo = true; Pr.Add(p); } }
    public void Actualizar(Proveedor p) { lock (c) { var a = Pr.First(x => x.Nit == p.Nit); a.Nombre = p.Nombre; a.Telefono = p.Telefono; a.DiasEntrega = p.DiasEntrega; } }
    public void CambiarEstado(string nit, bool activo) { lock (c) Pr.First(p => p.Nit == nit).Activo = activo; }

    // ---------- Compras ----------
    OrdenCompra Ver(OrdenCompra o) => new()
    {
        OrdenId = o.OrdenId, Codigo = o.Codigo, NitProveedor = o.NitProveedor, NombreProveedor = Pr.First(x => x.Nit == o.NitProveedor).Nombre, Estado = o.Estado,
        FechaCreacion = o.FechaCreacion, FechaAprobacion = o.FechaAprobacion, FechaEntregaEsperada = o.FechaEntregaEsperada, FechaRecepcion = o.FechaRecepcion,
        FechaPago = o.FechaPago, Pagada = o.Pagada, BodegaRecepcionId = o.BodegaRecepcionId,
        Detalles = o.Detalles.Select(d => { var p = P.First(x => x.Sku == d.Sku); return new DetalleOrdenCompra { DetalleId = d.DetalleId, Sku = d.Sku, NombreProducto = p.Nombre, Unidad = p.Unidad, Cantidad = d.Cantidad, PrecioUnitario = d.PrecioUnitario }; }).ToList(),
    };
    public List<OrdenCompra> ListarOrdenes() { lock (c) return O.OrderBy(o => o.OrdenId).Select(Ver).ToList(); }
    public OrdenCompra? BuscarOrden(string codigo) { lock (c) return O.FirstOrDefault(o => o.Codigo == codigo) is { } o ? Ver(o) : null; }
    public bool HayOrdenesConProducto(string sku) { lock (c) return O.Any(o => o.Detalles.Any(d => d.Sku == sku)); }
    public string CrearOrden(OrdenCompra o)
    {
        lock (c)
        {
            var n = new OrdenCompra { OrdenId = ++idOrden, NitProveedor = o.NitProveedor, Estado = EstadosOrden.Pendiente, FechaCreacion = DateTime.Now,
                Detalles = o.Detalles.Select(d => new DetalleOrdenCompra { DetalleId = ++idDetalle, Sku = d.Sku, Cantidad = d.Cantidad, PrecioUnitario = d.PrecioUnitario }).ToList() };
            n.Codigo = Codigo(n.OrdenId);
            if (O.Any(x => x.Codigo == n.Codigo)) throw new ErrorBdSimulado("UX_OrdenCompra_Codigo");
            O.Add(n);
            return n.Codigo;
        }
    }
    bool Cambiar(string codigo, string desde, Action<OrdenCompra> cambio) { lock (c) { var o = O.FirstOrDefault(x => x.Codigo == codigo && x.Estado == desde); if (o is null) return false; cambio(o); return true; } }
    public bool Aprobar(string codigo, DateTime entrega) => Cambiar(codigo, EstadosOrden.Pendiente, o => { o.Estado = EstadosOrden.Aprobada; o.FechaAprobacion = DateTime.Now; o.FechaEntregaEsperada = entrega; });
    public bool Rechazar(string codigo) => Cambiar(codigo, EstadosOrden.Pendiente, o => o.Estado = EstadosOrden.Rechazada);
    public bool MarcarPagada(string codigo)
    {
        lock (c)
        {
            var o = O.FirstOrDefault(x => x.Codigo == codigo && !x.Pagada && (x.Estado == EstadosOrden.Aprobada || x.Estado == EstadosOrden.Recibida));
            if (o is null) return false;
            o.Pagada = true; o.FechaPago = DateTime.Now; return true;
        }
    }
    public bool Recibir(string codigo, int bodegaId, int usuarioId, string nombre)
    {
        lock (c)
        {
            var o = O.FirstOrDefault(x => x.Codigo == codigo && x.Estado == EstadosOrden.Aprobada);
            if (o is null) return false;
            o.Estado = EstadosOrden.Recibida; o.FechaRecepcion = DateTime.Now; o.BodegaRecepcionId = bodegaId;
            foreach (var d in o.Detalles)
                Aplicar(new Movimiento { Sku = d.Sku, Tipo = ReglasInventario.Entrada, Cantidad = d.Cantidad, Motivo = ReglasInventario.MotivoCompra, BodegaId = bodegaId, UsuarioId = usuarioId, Usuario = nombre, Referencia = codigo });
            return true;
        }
    }

    // ---------- Usuarios ----------
    Usuario Ver(Usuario u)
    {
        var r = R.First(x => x.RolId == u.RolId);
        return new Usuario
        {
            UsuarioId = u.UsuarioId, NombreUsuario = u.NombreUsuario, NombreCompleto = u.NombreCompleto, RolId = u.RolId, RolCodigo = r.Codigo, RolNombre = r.Nombre,
            Activo = u.Activo, FechaCreacion = u.FechaCreacion, ClaveHash = u.ClaveHash, DebeCambiarClave = u.DebeCambiarClave,
            IntentosFallidos = u.IntentosFallidos, BloqueadoHasta = u.BloqueadoHasta, UltimoIngreso = u.UltimoIngreso,
        };
    }
    public List<Rol> ListarRoles() { lock (c) return R.ToList(); }
    public List<Usuario> ListarUsuarios() { lock (c) return U.Select(Ver).ToList(); }
    public Usuario? BuscarPorNombreUsuario(string n) { lock (c) return U.FirstOrDefault(u => u.NombreUsuario == n) is { } u ? Ver(u) : null; }
    public Usuario? BuscarPorId(int id) { lock (c) return U.FirstOrDefault(u => u.UsuarioId == id) is { } u ? Ver(u) : null; }
    public int Crear(Usuario u)
    {
        lock (c)
        {
            if (U.Any(x => x.NombreUsuario == u.NombreUsuario)) throw new ErrorBdSimulado("UQ_Usuario_Nombre");
            u.UsuarioId = ++idUsuario; u.FechaCreacion = DateTime.Now; U.Add(u); return u.UsuarioId;
        }
    }
    public void CambiarEstado(int id, bool activo) { lock (c) U.First(u => u.UsuarioId == id).Activo = activo; }
    public void ActualizarDatos(int id, string nombre, int rolId) { lock (c) { var u = U.First(x => x.UsuarioId == id); u.NombreCompleto = nombre; u.RolId = rolId; } }
    public void CambiarClave(int id, string hash, bool debe) { lock (c) { var u = U.First(x => x.UsuarioId == id); u.ClaveHash = hash; u.DebeCambiarClave = debe; } }
    public void RegistrarIngresoFallido(int id, int intentos, DateTime? hasta) { lock (c) { var u = U.First(x => x.UsuarioId == id); u.IntentosFallidos = intentos; u.BloqueadoHasta = hasta; } }
    public void RegistrarIngresoExitoso(int id, DateTime f) { lock (c) { var u = U.First(x => x.UsuarioId == id); u.IntentosFallidos = 0; u.BloqueadoHasta = null; u.UltimoIngreso = f; } }
    public void Desbloquear(int id) { lock (c) { var u = U.First(x => x.UsuarioId == id); u.IntentosFallidos = 0; u.BloqueadoHasta = null; } }
    public bool ExisteAdministradorActivo() { lock (c) return U.Any(u => u.RolId == 1 && u.Activo && u.ClaveHash is not null); }

    /// <summary>Para pruebas: agrega un usuario sin clave (como las cuentas de demostración antes de generar sus claves).</summary>
    public int AgregarSinClave(string nombre, int rolId) { lock (c) { U.Add(new Usuario { UsuarioId = ++idUsuario, NombreUsuario = nombre, NombreCompleto = nombre, RolId = rolId, Activo = true }); return idUsuario; } }

    // ---------- Auditoría (solo agregar) ----------
    public List<EventoAuditoria> A = [];
    long idEvento;
    public void Registrar(EventoAuditoria e) { lock (c) { e.EventoId = ++idEvento; A.Add(e); } }
    public List<EventoAuditoria> Listar(int limite, string? accion = null, int? usuarioId = null)
    {
        lock (c) return A.Where(e => (accion is null || e.Accion == accion) && (usuarioId is null || e.UsuarioId == usuarioId))
            .OrderByDescending(e => e.EventoId).Take(limite).ToList();
    }

    public void SaltarIdOrden(int a) { lock (c) idOrden = a; }
}

/// <summary>Imita una violación de restricción de la base (SqlException hereda de DbException).</summary>
public sealed class ErrorBdSimulado(string restriccion) : System.Data.Common.DbException("Violación simulada de " + restriccion);
