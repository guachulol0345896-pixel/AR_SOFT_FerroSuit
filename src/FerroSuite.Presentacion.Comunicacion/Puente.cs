using System.Data.Common;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace FerroSuite.Presentacion.Comunicacion;

/// <summary>Servicios que atiende el Puente (los arma la ventana, que es la raíz de composición).</summary>
public sealed record ServiciosFerroSuite(
    InventarioServicio Inventario,
    ProveedorServicio Proveedores,
    OrdenServicio Ordenes,
    UsuarioServicio Usuarios,
    AdministracionServicio Administracion,
    AsistenteInventario Asistente,
    PronosticoServicio Pronosticos,
    IGeneradorTexto Generador,
    Autorizador Autorizador,
    GestorSesion Sesiones);

/// <summary>
/// Comunicación entre el frontend y el backend. El frontend envía { id, accion, datos } y el backend responde
/// { id, ok, mensaje, datos, errores }.
///
/// Seguridad (todo se decide aquí, en el backend; ocultar botones en la pantalla es solo comodidad):
/// - Solo se aceptan mensajes de las pantallas propias (origen https://ferrosuite.example/).
/// - Toda acción está en <see cref="PermisoPorAccion"/>; lo que no está, se rechaza (denegar por defecto).
/// - Salvo ingresar, cada acción exige sesión vigente y el permiso del rol; los servicios lo vuelven a comprobar.
/// - Los errores inesperados no muestran detalles internos: solo un número de referencia que queda en la auditoría.
/// </summary>
public partial class Puente(ServiciosFerroSuite s)
{
    public const string OrigenPermitido = "https://ferrosuite.example/";
    public const int TamanoMaximoMensaje = 64 * 1024;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        MaxDepth = 16,
    };

    /// <summary>Acciones que no necesitan sesión.</summary>
    public static readonly IReadOnlySet<string> AccionesPublicas = new HashSet<string> { "sesion.iniciar", "sesion.actual", "sesion.cerrar", "sistema.estado" };

    /// <summary>
    /// Permiso que exige cada acción (basta con tener uno de la lista). Lista vacía = solo exige sesión.
    /// Una acción que no aparezca aquí ni en <see cref="AccionesPublicas"/> se rechaza.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string[]> PermisoPorAccion = new Dictionary<string, string[]>
    {
        ["sesion.cambiarClave"] = [],
        ["ia.estado"] = [],
        ["categorias.listar"] = [Permisos.VerInventario],
        ["bodegas.listar"] = [Permisos.VerInventario],
        ["bodegas.crear"] = [Permisos.GestionarBodegas],
        ["bodegas.listarTodas"] = [Permisos.GestionarBodegas],
        ["bodegas.actualizar"] = [Permisos.GestionarBodegas],
        ["categorias.listarTodas"] = [Permisos.GestionarProductos],
        ["categorias.crear"] = [Permisos.GestionarProductos],
        ["categorias.actualizar"] = [Permisos.GestionarProductos],
        ["productos.listar"] = [Permisos.VerInventario],
        ["productos.crear"] = [Permisos.GestionarProductos],
        ["productos.actualizar"] = [Permisos.GestionarProductos],
        ["productos.eliminar"] = [Permisos.EliminarProductos],
        ["movimientos.listar"] = [Permisos.VerMovimientos],
        ["movimientos.registrar"] = [Permisos.RegistrarVenta, Permisos.AjustarInventario],
        ["resumen"] = [Permisos.VerInventario],
        ["proveedores.listar"] = [Permisos.VerProveedores],
        ["proveedores.crear"] = [Permisos.GestionarProveedores],
        ["proveedores.actualizar"] = [Permisos.GestionarProveedores],
        ["proveedores.activar"] = [Permisos.GestionarProveedores],
        ["proveedores.desactivar"] = [Permisos.GestionarProveedores],
        ["ordenes.listar"] = [Permisos.VerOrdenes],
        ["ordenes.crear"] = [Permisos.CrearOrdenes],
        ["ordenes.aprobar"] = [Permisos.AprobarOrdenes],
        ["ordenes.rechazar"] = [Permisos.AprobarOrdenes],
        ["ordenes.recibir"] = [Permisos.RecibirOrdenes],
        ["ordenes.pagar"] = [Permisos.PagarOrdenes],
        ["ia.reposicion"] = [Permisos.UsarAsistente],
        ["ia.preguntar"] = [Permisos.UsarAsistente],
        ["ia.recordatorios"] = [Permisos.VerRecordatorios],
        ["pronosticos.reposicion"] = [Permisos.VerPronosticos],
        ["pronosticos.producto"] = [Permisos.VerPronosticos],
        ["roles.listar"] = [Permisos.GestionarUsuarios],
        ["usuarios.listar"] = [Permisos.GestionarUsuarios],
        ["usuarios.crear"] = [Permisos.GestionarUsuarios],
        ["usuarios.activar"] = [Permisos.GestionarUsuarios],
        ["usuarios.desactivar"] = [Permisos.GestionarUsuarios],
        ["usuarios.restablecerClave"] = [Permisos.GestionarUsuarios],
        ["usuarios.desbloquear"] = [Permisos.GestionarUsuarios],
        ["usuarios.actualizar"] = [Permisos.GestionarUsuarios],
        ["auditoria.listar"] = [Permisos.VerAuditoria],
        ["datos.restablecer"] = [Permisos.RestablecerDemo],
    };

    [GeneratedRegex(@"^[a-z]{2,20}\.[a-zA-Z]{2,20}$|^resumen$")]
    private static partial Regex FormatoAccion();

    /// <summary>Usuario con sesión vigente (null si nadie ha ingresado o la sesión venció).</summary>
    public SesionUsuario? Sesion => s.Sesiones.Actual();

    public void CerrarSesion() => s.Sesiones.Cerrar();

    /// <summary>Para la prueba 4: si está activo, el backend no responde (como si se cayera la conexión).</summary>
    public bool SimularDesconexion { get; set; }

    /// <summary>¿El mensaje viene de las pantallas del programa? (WebView2 informa la dirección de la página que lo envió.)</summary>
    public static bool EsOrigenPermitido(string? origen) =>
        Uri.TryCreate(origen, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
        && uri.IsDefaultPort && string.Equals(uri.Host, new Uri(OrigenPermitido).Host, StringComparison.OrdinalIgnoreCase);

    /// <summary>Atiende un mensaje del frontend. <paramref name="origen"/> es la dirección de la página que lo envió.</summary>
    public async Task<string?> AtenderAsync(string mensajeJson, string? origen)
    {
        if (SimularDesconexion) return null;

        JsonNode? id = null;
        string accion = "";
        try
        {
            if (!EsOrigenPermitido(origen))
            {
                s.Autorizador.Auditoria.Registrar(s.Sesiones.Actual(), "puente.origen", ResultadosAuditoria.Denegado,
                    detalle: "Mensaje desde una página no permitida: " + AuditoriaServicio.Limpiar(origen, 100));
                return Responder(new { ok = false, mensaje = "Origen no permitido.", errores = Array.Empty<string>() });
            }
            if (mensajeJson.Length > TamanoMaximoMensaje)
                return Responder(new { ok = false, mensaje = "El mensaje es demasiado grande.", errores = Array.Empty<string>() });

            // Propiedades repetidas (p. ej. dos "accion") se rechazan como formato incorrecto, sin llegar a un error inesperado.
            var mensaje = JsonNode.Parse(mensajeJson, documentOptions: new JsonDocumentOptions { MaxDepth = 16, AllowDuplicateProperties = false })
                as JsonObject ?? throw new JsonException();
            id = mensaje["id"]?.DeepClone();
            accion = mensaje["accion"] is JsonValue v && v.TryGetValue<string>(out var a) ? a : "";
            var datos = mensaje["datos"];

            var (mensajeOk, resultado) = await EjecutarAsync(accion, datos);
            return Responder(new { id, ok = true, mensaje = mensajeOk, datos = resultado });
        }
        catch (ErrorAutorizacion ex)
        {
            return Responder(new { id, ok = false, mensaje = ex.Message, errores = Array.Empty<string>(), sinSesion = ex.SinSesion });
        }
        catch (ErrorValidacion ex)
        {
            return Responder(new { id, ok = false, mensaje = "Revisa los datos ingresados.", errores = ex.Errores });
        }
        catch (DbException ex) // SqlException hereda de DbException: así esta capa no depende de SQL Server
        {
            var referencia = Referencia(accion, ex);
            return Responder(new
            {
                id, ok = false,
                mensaje = "No se pudo completar la operación en la base de datos. No se guardó ningún cambio.",
                errores = new[] { $"Revisa que SQL Server LocalDB esté disponible y vuelve a intentar. Referencia: {referencia}" },
            });
        }
        catch (JsonException)
        {
            return Responder(new { id, ok = false, mensaje = "Los datos enviados tienen un formato incorrecto.", errores = Array.Empty<string>() });
        }
        catch (Exception ex)
        {
            var referencia = Referencia(accion, ex);
            return Responder(new { id, ok = false, mensaje = $"Ocurrió un error inesperado. Referencia: {referencia}", errores = Array.Empty<string>() });
        }
    }

    // Registra el error con un número corto para buscarlo, sin enviar detalles internos a la pantalla.
    private string Referencia(string accion, Exception ex)
    {
        var referencia = Convert.ToHexString(RandomNumberGenerator.GetBytes(4));
        Trace.WriteLine($"[{referencia}] {accion}: {ex.GetType().FullName}\n{ex.StackTrace}");
        s.Autorizador.Auditoria.Registrar(s.Sesiones.Actual(), "error.inesperado", ResultadosAuditoria.Fallido,
            detalle: $"Referencia {referencia}, acción {AuditoriaServicio.Limpiar(accion, 40)}, {ex.GetType().Name}");
        return referencia;
    }

    private async Task<(string? Mensaje, object? Datos)> EjecutarAsync(string accion, JsonNode? datos)
    {
        if (!FormatoAccion().IsMatch(accion) || (!AccionesPublicas.Contains(accion) && !PermisoPorAccion.ContainsKey(accion)))
            throw new ErrorValidacion(["Operación no reconocida."]);

        var sesion = s.Sesiones.Actual(out var vencida);
        if (vencida is not null) s.Autorizador.Auditoria.Exito(vencida, "sesion.vencida", detalle: "Cerrada por inactividad o duración máxima");

        if (PermisoPorAccion.TryGetValue(accion, out var permisos))
        {
            if (permisos.Length > 0) s.Autorizador.ExigirAlguno(sesion, accion, permisos);
            else if (sesion is null) throw new ErrorAutorizacion("Tu sesión no está iniciada o venció. Ingresa con tu usuario y clave.", sinSesion: true);
            else if (sesion.DebeCambiarClave && accion != "sesion.cambiarClave") throw new ErrorAutorizacion("Debes cambiar tu clave antes de continuar.");
        }

        switch (accion)
        {
            // ---------- Sesión ----------
            case "sesion.iniciar":
            {
                var anterior = s.Sesiones.Cerrar();
                if (anterior is not null) s.Autorizador.Auditoria.Exito(anterior, "sesion.cerrar", detalle: "Reemplazada por un nuevo ingreso");
                var nueva = s.Usuarios.IniciarSesion(Texto(datos, "usuario", 60), datos?["clave"]?.ToString());
                s.Sesiones.Iniciar(nueva);
                return (nueva.DebeCambiarClave ? "Debes cambiar tu clave antes de continuar." : $"Bienvenido, {nueva.NombreCompleto}.", Vista(nueva));
            }
            case "sesion.cerrar":
            {
                var anterior = s.Sesiones.Cerrar();
                if (anterior is not null) s.Autorizador.Auditoria.Exito(anterior, "sesion.cerrar");
                return ("Sesión cerrada.", null);
            }
            case "sesion.actual": return (null, sesion is null ? null : Vista(sesion));
            case "sesion.cambiarClave":
            {
                var cambiada = s.Usuarios.CambiarClavePropia(sesion, datos?["actual"]?.ToString(), datos?["nueva"]?.ToString());
                s.Sesiones.Actualizar(cambiada);
                return ("Clave cambiada.", Vista(cambiada));
            }
            case "sistema.estado": return (null, new { modoDemo = s.Administracion.EsDemo });

            // ---------- Catálogos ----------
            case "categorias.listar": return (null, s.Inventario.ListarCategorias());
            case "bodegas.listar": return (null, s.Inventario.ListarBodegas());
            case "bodegas.crear": s.Inventario.CrearBodega(Leer<Bodega>(datos), sesion); return ("Bodega creada.", null);
            case "bodegas.listarTodas": return (null, s.Inventario.ListarBodegasTodas(sesion));
            case "bodegas.actualizar": s.Inventario.ActualizarBodega(Leer<Bodega>(datos), sesion); return ("Bodega actualizada.", null);
            case "categorias.listarTodas": return (null, s.Inventario.ListarCategoriasTodas(sesion));
            case "categorias.crear": s.Inventario.CrearCategoria(Leer<Categoria>(datos), sesion); return ("Categoría creada.", null);
            case "categorias.actualizar": s.Inventario.ActualizarCategoria(Leer<Categoria>(datos), sesion); return ("Categoría actualizada.", null);

            // ---------- Inventario ----------
            case "productos.listar": return (null, s.Inventario.ListarProductos(sesion));
            case "productos.crear": s.Inventario.CrearProducto(Leer<Producto>(datos), sesion); return ("Producto guardado en la base de datos.", null);
            case "productos.actualizar": s.Inventario.ActualizarProducto(Leer<Producto>(datos), sesion); return ("Cambios guardados.", null);
            case "productos.eliminar": return (s.Inventario.EliminarProducto(Texto(datos, "sku", 20), sesion), null);
            case "movimientos.listar": return (null, s.Inventario.ListarMovimientos(Numero(datos, "limite") ?? 20));
            case "movimientos.registrar":
                var stock = s.Inventario.RegistrarMovimiento(Leer<Movimiento>(datos), sesion);
                return ($"Movimiento registrado. Nuevo stock: {stock:0.##}.", null);
            case "resumen": return (null, s.Inventario.Resumen(sesion));

            // ---------- Proveedores ----------
            case "proveedores.listar": return (null, s.Proveedores.Listar());
            case "proveedores.crear": s.Proveedores.Crear(Leer<Proveedor>(datos), sesion); return ("Proveedor guardado en la base de datos.", null);
            case "proveedores.actualizar": s.Proveedores.Actualizar(Leer<Proveedor>(datos), sesion); return ("Cambios guardados.", null);
            case "proveedores.desactivar": s.Proveedores.CambiarEstado(Texto(datos, "nit", 20), false, sesion); return ("Proveedor desactivado. Su historial se conserva.", null);
            case "proveedores.activar": s.Proveedores.CambiarEstado(Texto(datos, "nit", 20), true, sesion); return ("Proveedor activado.", null);

            // ---------- Órdenes de compra ----------
            case "ordenes.listar": return (null, s.Ordenes.Listar());
            case "ordenes.crear":
                var codigo = s.Ordenes.Crear(LeerOrden(datos), sesion);
                return ($"Orden {codigo} creada. Queda pendiente de aprobación.", null);
            case "ordenes.aprobar": s.Ordenes.Aprobar(Texto(datos, "codigo", 20), sesion); return ("Orden aprobada. El pago vence en 15 días.", null);
            case "ordenes.rechazar": s.Ordenes.Rechazar(Texto(datos, "codigo", 20), sesion); return ("Orden rechazada.", null);
            case "ordenes.recibir":
                s.Ordenes.Recibir(Texto(datos, "codigo", 20), Numero(datos, "bodegaId"), sesion);
                return ("Mercancía recibida: el stock quedó actualizado.", null);
            case "ordenes.pagar": s.Ordenes.MarcarPagada(Texto(datos, "codigo", 20), sesion); return ("Orden marcada como pagada.", null);

            // ---------- Usuarios y auditoría ----------
            case "roles.listar": return (null, s.Usuarios.ListarRoles(sesion));
            case "usuarios.listar": return (null, s.Usuarios.ListarUsuarios(sesion));
            case "usuarios.crear":
                s.Usuarios.Crear(sesion, Texto(datos, "nombreUsuario", 60), datos?["nombreCompleto"]?.ToString(),
                    datos?["rolCodigo"]?.ToString(), datos?["clave"]?.ToString());
                return ("Usuario creado. Deberá cambiar la clave la primera vez que ingrese.", null);
            case "usuarios.desactivar": s.Usuarios.CambiarEstado(sesion, Id(datos, "usuarioId"), false); return ("Usuario desactivado.", null);
            case "usuarios.activar": s.Usuarios.CambiarEstado(sesion, Id(datos, "usuarioId"), true); return ("Usuario activado.", null);
            case "usuarios.actualizar":
                s.Usuarios.Actualizar(sesion, Id(datos, "usuarioId"), Texto(datos, "nombreCompleto", 100), Texto(datos, "rolCodigo", 20));
                return ("Usuario actualizado. Si cambiaste el rol, rige desde su próximo ingreso.", null);
            case "usuarios.desbloquear": s.Usuarios.Desbloquear(sesion, Id(datos, "usuarioId")); return ("Usuario desbloqueado.", null);
            case "usuarios.restablecerClave":
                var temporal = s.Usuarios.RestablecerClave(sesion, Id(datos, "usuarioId"));
                return ("Clave temporal generada. Entrégala al usuario: no se volverá a mostrar y deberá cambiarla al ingresar.", new { claveTemporal = temporal });
            case "auditoria.listar":
                return (null, s.Autorizador.Auditoria.Listar(sesion, s.Autorizador, Numero(datos, "limite") ?? 200, datos?["accion"]?.ToString(), Numero(datos, "usuarioId")));

            // ---------- Solo en modo demostración ----------
            case "datos.restablecer":
                s.Administracion.RestablecerDemo(sesion);
                s.Sesiones.Cerrar(); // los usuarios de la base anterior ya no existen
                return ("Se reconstruyó la base de demostración. Vuelve a ingresar.", null);

            // ---------- Pronóstico de demanda (red neuronal ONNX local) ----------
            // Solo devuelve recomendaciones: no crea, aprueba ni envía órdenes.
            case "pronosticos.reposicion": return (null, s.Pronosticos.RecomendarReposicion(sesion));
            case "pronosticos.producto":
                return (null, s.Pronosticos.PronosticarProducto(sesion, Texto(datos, "sku", 20), Numero(datos, "dias") ?? 90));

            // ---------- Asistente ----------
            case "ia.estado": return (null, new EstadoServicio("FerroSuite", s.Generador.Configurado, s.Generador.Modelo));
            case "ia.reposicion": return (null, await s.Asistente.SugerirReposicionAsync(s.Inventario.DatosParaAsistente(), CancellationToken.None));
            case "ia.recordatorios": return (null, await s.Asistente.RecordatoriosAsync(s.Inventario.DatosParaAsistente(), CancellationToken.None));
            case "ia.preguntar":
                var pregunta = Texto(datos, "pregunta", 500);
                var d = s.Inventario.DatosParaAsistente();
                return (null, await s.Asistente.PreguntarAsync(new SolicitudPregunta(pregunta, d.Productos, d.Movimientos, d.Ordenes), CancellationToken.None));

            default: throw new ErrorValidacion(["Operación no reconocida."]);
        }
    }

    // Lo que la pantalla necesita saber de la sesión (sin identificador de sesión ni datos de la clave).
    private static object Vista(SesionUsuario s) => new
    {
        s.UsuarioId, s.NombreUsuario, s.NombreCompleto, s.RolCodigo, s.RolNombre, s.DebeCambiarClave,
        Permisos = s.PermisosDelRol.Order().ToArray(),
    };

    /// <summary>Acepta { nitProveedor, detalles: [{ sku, cantidad }] } o, como antes, una sola línea { nitProveedor, sku, cantidad }.</summary>
    private static OrdenCompra LeerOrden(JsonNode? datos)
    {
        var orden = Leer<OrdenCompra>(datos);
        if (orden.Detalles.Count == 0 && datos?["sku"] is not null)
            orden.Detalles = [new DetalleOrdenCompra { Sku = datos["sku"]!.ToString(), Cantidad = Leer<decimal>(datos["cantidad"]) }];
        return orden;
    }

    private static T Leer<T>(JsonNode? datos) =>
        datos.Deserialize<T>(Json) ?? throw new ErrorValidacion(["No llegaron datos."]);

    private static string Texto(JsonNode? datos, string campo, int largoMaximo)
    {
        var valor = datos?[campo]?.ToString().Trim() ?? "";
        if (valor.Length == 0) throw new ErrorValidacion([$"Falta el dato \"{campo}\"."]);
        if (valor.Length > largoMaximo) throw new ErrorValidacion([$"El dato \"{campo}\" no puede tener más de {largoMaximo} caracteres."]);
        return valor;
    }

    private static int? Numero(JsonNode? datos, string campo) =>
        int.TryParse(datos?[campo]?.ToString(), out var n) ? n : null;

    private static int Id(JsonNode? datos, string campo) =>
        Numero(datos, campo) is int n and > 0 ? n : throw new ErrorValidacion([$"Falta el dato \"{campo}\"."]);

    private static string Responder(object respuesta) => JsonSerializer.Serialize(respuesta, Json);
}
