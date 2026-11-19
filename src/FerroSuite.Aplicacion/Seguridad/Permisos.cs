namespace FerroSuite.Aplicacion.Seguridad;

/// <summary>
/// Permisos del programa y qué rol tiene cada uno (mínimo privilegio: cada rol solo lo que necesita su trabajo,
/// según los roles del Documento Maestro). La matriz vive solo aquí; el Puente y los servicios la consultan.
/// </summary>
public static class Permisos
{
    // Inventario
    public const string VerInventario = "inventario.ver";
    public const string VerMovimientos = "movimientos.ver";
    /// <summary>Registrar ventas (salida) y devoluciones de clientes (entrada).</summary>
    public const string RegistrarVenta = "movimientos.venta";
    /// <summary>Ajustes por conteo, daños o pérdidas y entradas manuales de compra.</summary>
    public const string AjustarInventario = "movimientos.ajuste";
    public const string GestionarProductos = "productos.gestionar";
    public const string EliminarProductos = "productos.eliminar";
    public const string GestionarBodegas = "bodegas.gestionar";
    // Proveedores y compras
    public const string VerProveedores = "proveedores.ver";
    public const string GestionarProveedores = "proveedores.gestionar";
    public const string VerOrdenes = "ordenes.ver";
    public const string CrearOrdenes = "ordenes.crear";
    public const string AprobarOrdenes = "ordenes.aprobar";
    public const string RecibirOrdenes = "ordenes.recibir";
    public const string PagarOrdenes = "ordenes.pagar";
    /// <summary>Valores en pesos de inventario y cuentas por pagar.</summary>
    public const string VerFinanzas = "finanzas.ver";
    // Asistente
    public const string UsarAsistente = "asistente.usar";
    public const string VerRecordatorios = "asistente.recordatorios";
    /// <summary>Pronóstico de demanda con la red neuronal y recomendaciones de reposición.</summary>
    public const string VerPronosticos = "pronosticos.ver";
    // Administración
    public const string GestionarUsuarios = "usuarios.gestionar";
    public const string VerAuditoria = "auditoria.ver";
    public const string ModificarConfiguracion = "configuracion.modificar";
    public const string RestablecerDemo = "demo.restablecer";

    public static readonly IReadOnlyList<string> Todos =
    [
        VerInventario, VerMovimientos, RegistrarVenta, AjustarInventario, GestionarProductos, EliminarProductos, GestionarBodegas,
        VerProveedores, GestionarProveedores, VerOrdenes, CrearOrdenes, AprobarOrdenes, RecibirOrdenes, PagarOrdenes, VerFinanzas,
        UsarAsistente, VerRecordatorios, VerPronosticos, GestionarUsuarios, VerAuditoria, ModificarConfiguracion, RestablecerDemo,
    ];

    private static readonly Dictionary<string, HashSet<string>> PorRol = new()
    {
        // Dueño o administrador: todo, incluido el CRUD de catálogos, usuarios, aprobación de compras y configuración.
        [Rol.Administrador] = [.. Todos],
        // Encargado de bodega: existencias, ajustes, recibir mercancía y proponer órdenes. No vende, no aprueba, no paga.
        [Rol.Bodega] = [VerInventario, VerMovimientos, AjustarInventario, VerProveedores, VerOrdenes, CrearOrdenes, RecibirOrdenes,
                        UsarAsistente, VerRecordatorios, VerPronosticos],
        // Vendedor de mostrador: consulta el stock y registra ventas y devoluciones. Nada más.
        [Rol.Vendedor] = [VerInventario, RegistrarVenta],
        // Contador externo: consulta valores, movimientos, proveedores y órdenes, y registra pagos. No cambia inventario.
        [Rol.Contador] = [VerInventario, VerMovimientos, VerProveedores, VerOrdenes, PagarOrdenes, VerFinanzas, VerRecordatorios],
    };

    public static IReadOnlySet<string> DelRol(string? rolCodigo) =>
        rolCodigo is not null && PorRol.TryGetValue(rolCodigo, out var p) ? p : new HashSet<string>();

    public static bool Tiene(SesionUsuario? sesion, string permiso) => sesion is not null && DelRol(sesion.RolCodigo).Contains(permiso);

    /// <summary>Motivos de movimiento y el permiso que exige cada uno.</summary>
    public static string ParaMotivo(string motivo) => motivo is "Venta" or "Devolución" ? RegistrarVenta : AjustarInventario;
}
