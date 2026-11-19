// Pruebas automáticas de FerroSuite.
//   dotnet run --project tests/FerroSuite.Pruebas                      -> pruebas unitarias y de integración del backend en memoria
//   dotnet run --project tests/FerroSuite.Pruebas -- --bd [servidor]    -> pruebas contra SQL Server LocalDB real (solo Windows):
//                                                                         integridad, transacciones fallidas, concurrencia,
//                                                                         persistencia al cerrar y abrir, y migración de una base antigua.
//                                                                         Usa una base TEMPORAL "FerroSuite_Pruebas_<id>" y la borra al final.
//   dotnet run --project tests/FerroSuite.Pruebas -- servidor <dir>     -> herramienta para probar las pantallas en un navegador
// Todos los datos de estas pruebas son SINTÉTICOS. Ninguna prueba usa la base real ni la de demostración.
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FerroSuite.Infraestructura.Configuracion;
using FerroSuite.Infraestructura.IA.Gemini;
using FerroSuite.Infraestructura.Persistencia;
using FerroSuite.Presentacion.Comunicacion;
using FerroSuite.Pruebas;
using FerroSuite.Pruebas.Dobles;
using FerroSuite.Pruebas.Herramientas;

if (args.Length > 0 && args[0] == "--bd")
    return await PruebasBaseDatos.Correr(args.Length > 1 ? args[1] : OpcionesBaseDatos.ServidorPorDefecto);
if (args.Length > 2 && args[0] == "--hijo-persistencia")
    return PruebasBaseDatos.ProcesoHijo(args[1], args[2]); // lo lanza la prueba de persistencia (otro proceso = programa reabierto)

// El entorno de prueba (backend real sobre memoria, claves aleatorias) está en Herramientas/Entorno.cs.
var claves = Entorno.Claves;
(Puente, AlmacenMemoria, ClienteGemini, MantenimientoFalso) Nuevo(string clave = "", string url = "https://invalido.local", int timeout = 2, bool demo = false)
{
    var e = Entorno.Nuevo(clave, url, timeout, demo);
    return (e.P, e.A, e.Gem, e.Mant);
}
Task<JsonNode> Pedir(Puente p, string accion, object? datos = null) => Entorno.PedirA(p, accion, datos);
Task<JsonNode> Ingresar(Puente p, string usuario) => Entorno.IngresarA(p, usuario);
bool Ok(JsonNode r) => Entorno.Ok(r);
string Errores(JsonNode r) => Entorno.Errores(r);

if (args.Length > 1 && args[0] == "servidor")
{
    // --sin-modelo: arranca con una carpeta de modelos vacía para ver la pantalla cuando el modelo no está disponible.
    var sinModelo = args.Contains("--sin-modelo");
    // --modelo-falla: el predictor lanza una excepción al calcular (doble de prueba; simula una falla de ONNX Runtime).
    var esrv = Entorno.Nuevo(demo: true, pronosticador: sinModelo
        ? new FerroSuite.Infraestructura.IA.Prediccion.PronosticadorOnnx(Path.Combine(Path.GetTempPath(), "ferrosuite-sin-modelo"))
        : args.Contains("--modelo-falla") ? new PronosticadorFalso(falla: true) : null);
    var (ps, asrv) = (esrv.P, esrv.A);
    // Historial SINTÉTICO (90 días) para probar las pantallas de pronóstico. Nada de esto es información real.
    var idMov = 500;
    void Mov(string sku, string tipo, decimal cantidad, string motivo, int haceDias) =>
        asrv.M.Add(new Movimiento { MovimientoId = ++idMov, Sku = sku, Tipo = tipo, Cantidad = cantidad, Motivo = motivo, BodegaId = 1, UsuarioId = 1, Usuario = "SINTÉTICO", Fecha = DateTime.Now.AddDays(-haceDias) });
    // SIN-001 (cable): ventas todos los días con patrón semanal (más los sábados) y ruido determinista.
    for (int d = 1; d <= 90; d++)
        Mov("SIN-001", "Salida", 4.5m + d % 3 + (DateTime.Now.AddDays(-d).DayOfWeek == DayOfWeek.Saturday ? 3 : 0), "Venta", d);
    // SIN-002 (varilla): ventas pequeñas; estuvo AGOTADA del día 42 al 47 (sin ventas, stock en cero) hasta una compra el día 41.
    decimal vendidoDesdeCompra = 0;
    for (int d = 1; d <= 40; d++) { var q = d % 7 == 0 ? 3 : 1; vendidoDesdeCompra += q; Mov("SIN-002", "Salida", q, "Venta", d); }
    Mov("SIN-002", "Entrada", 10 + vendidoDesdeCompra, "Compra", 41);
    for (int d = 48; d <= 90; d++) Mov("SIN-002", "Salida", d % 7 == 0 ? 3 : 2, "Venta", d);
    // SIN-007: producto nuevo SIN NINGÚN movimiento (sin historial) -> la pantalla debe usar la regla de respaldo.
    asrv.Sembrar(new Producto { Sku = "SIN-007", Nombre = "Tornillo nuevo sintético", CategoriaId = 2, Unidad = "Unidad", StockActual = 0, StockMinimo = 50, PrecioCompra = 150, NitProveedor = "000000001-8" });
    // SIN-008: solo 12 días de ventas y sin proveedor asignado -> respaldo por historial corto.
    asrv.Sembrar(new Producto { Sku = "SIN-008", Nombre = "Pintura sintética", CategoriaId = 5, Unidad = "Galón", StockActual = 6, StockMinimo = 4, PrecioCompra = 45000 });
    for (int d = 1; d <= 12; d++) Mov("SIN-008", "Salida", d % 4 == 0 ? 1 : 0.5m, "Venta", d);
    // Solo para esta herramienta local: imprime las claves aleatorias de los usuarios sintéticos de esta ejecución.
    foreach (var (u, c) in claves) Console.WriteLine($"usuario sintético: {u}  clave de esta ejecución: {c}");
    // Control de fallas para las pruebas en navegador: base de datos caída (DbException real del doble) y backend sin respuesta.
    bool Control(string accion)
    {
        switch (accion)
        {
            case "bd-caida": asrv.Caido = true; return true;
            case "bd-ok": asrv.Caido = false; return true;
            case "desconectar": ps.SimularDesconexion = true; return true;
            case "conectar": ps.SimularDesconexion = false; return true;
            default: return false;
        }
    }
    await ServidorPruebaFrontend.Correr(ps, args[1], "http://127.0.0.1:18766/", Control);
    return 0;
}

var reg = new Registro();
void Check(string id, string nombre, bool cond, string detalle = "") => reg.Check(id, nombre, cond, detalle);

// ---------- Identificadores: SKU, NIT con dígito de verificación y nombre de usuario ----------
{
    Check("I1", "SKU: se normaliza a mayúsculas y se valida el formato",
        Identificadores.NormalizarSku(" fer-0001 ") == "FER-0001" && Identificadores.ValidarSku("FER-0001") is null
        && Identificadores.ValidarSku("-AB") is not null && Identificadores.ValidarSku("AB-") is not null && Identificadores.ValidarSku("A") is not null
        && Identificadores.ValidarSku("AB C") is not null && Identificadores.ValidarSku(new string('A', 21)) is not null);
    // NIT públicos de entidades colombianas usados solo como casos conocidos del algoritmo de la DIAN.
    Check("I2", "NIT: dígito de verificación (algoritmo DIAN) correcto e incorrecto",
        Identificadores.ValidarNit("899999068-1") is null && Identificadores.ValidarNit("890903938-8") is null && Identificadores.ValidarNit("800197268-4") is null
        && Identificadores.ValidarNit("899999068-2") is not null && Identificadores.DigitoVerificacion("000000001") == 8 && Identificadores.DigitoVerificacion("000000002") == 5);
    Check("I3", "NIT: acepta puntos y espacios al escribirlo, y también sin dígito de verificación",
        Identificadores.NormalizarNit(" 899.999.068-1 ") == "899999068-1" && Identificadores.ValidarNit("899999068") is null && Identificadores.ValidarNit("12-3") is not null);
    Check("I4", "Nombre de usuario: minúsculas, números, punto, guion y guion bajo (3 a 40)",
        Identificadores.ValidarNombreUsuario("maria.gomez") is null && Identificadores.ValidarNombreUsuario("ab") is not null && Identificadores.ValidarNombreUsuario("María") is not null);
}

// ---------- Autenticación, autorización y auditoría (Herramientas/PruebasSeguridad.cs) ----------
await PruebasSeguridad.Correr(reg);

// ---------- Pronóstico de demanda con la red neuronal ONNX (Herramientas/PruebasPronostico.cs) ----------
await PruebasPronostico.Correr(reg);

// ---------- Inventario: productos ----------
{
    var (p, a, _, _) = Nuevo();
    await Ingresar(p, "admin.prueba");
    var r = await Pedir(p, "productos.crear", new { sku = "sin-003", nombre = "Tubo sintético", categoriaId = 2, unidad = "Unidad", stockActual = 5, stockMinimo = 2, precioCompra = 500 });
    var mov = a.M.LastOrDefault();
    Check("P1", "Crear producto válido (el SKU se pasa a mayúsculas)", Ok(r) && a.P.Any(x => x.Sku == "SIN-003"));
    Check("P2", "El stock inicial entra como movimiento 'Inventario inicial' a nombre del usuario, en la bodega principal",
        mov is { Sku: "SIN-003", Motivo: "Inventario inicial", Cantidad: 5, BodegaId: 1 } && mov.UsuarioId == a.U.First(u => u.NombreUsuario == "admin.prueba").UsuarioId
        && a.E.Single(e => e.Sku == "SIN-003").Cantidad == 5);
    r = await Pedir(p, "productos.crear", new { sku = "SIN-003", nombre = "Otro", categoriaId = 2, unidad = "Unidad", stockActual = 1, stockMinimo = 1 });
    Check("P3", "Rechaza SKU repetido", !Ok(r) && Errores(r).Contains("Ya existe"));
    r = await Pedir(p, "productos.crear", new { sku = "SIN-004", nombre = "X", categoriaId = 1, unidad = "Varilla", stockActual = -2, stockMinimo = 1.5 });
    Check("P4", "Rechaza stock negativo y decimales en varilla", !Ok(r) && Errores(r).Contains("negativo") && Errores(r).Contains("enteros"));
    r = await Pedir(p, "productos.crear", new { sku = "SIN 005!", nombre = "X", categoriaId = 99, unidad = "Litro", stockActual = 1, stockMinimo = 1 });
    Check("P5", "Rechaza SKU con caracteres no permitidos, categoría inexistente y unidad fuera de la lista",
        !Ok(r) && Errores(r).Contains("SKU") && Errores(r).Contains("categoría") && Errores(r).Contains("unidad"));
    r = await Pedir(p, "productos.crear", new { sku = "SIN-006", nombre = "Compatibilidad", categoria = "Pinturas", unidad = "Galón", stockActual = 0, stockMinimo = 1 });
    Check("P6", "Acepta el nombre de la categoría en lugar del identificador (compatibilidad con la pantalla anterior)", Ok(r) && a.P.First(x => x.Sku == "SIN-006").CategoriaId == 5);
    r = await Pedir(p, "productos.actualizar", new { sku = "SIN-001", nombre = "Cable editado", categoriaId = 3, unidad = "Metro", stockActual = 9999, stockMinimo = 20 });
    Check("P7", "Editar no cambia el stock (solo cambia con movimientos)", Ok(r) && a.P.First(x => x.Sku == "SIN-001").StockActual == 100);
    r = await Pedir(p, "productos.eliminar", new { sku = "SIN-006" });
    Check("P8", "Un producto sin historial ni stock se elimina", Ok(r) && a.P.All(x => x.Sku != "SIN-006"));
    r = await Pedir(p, "productos.eliminar", new { sku = "SIN-003" });
    var listado = (await Pedir(p, "productos.listar"))["datos"]!.AsArray();
    Check("P9", "Un producto con historial se desactiva en lugar de borrarse: el historial se conserva",
        Ok(r) && Errores(r).Contains("desactiv") && a.P.Any(x => x.Sku == "SIN-003" && !x.Activo) && a.M.Any(m => m.Sku == "SIN-003")
        && listado.All(x => x!["sku"]!.ToString() != "SIN-003"));
}

// ---------- Inventario: movimientos y bodegas ----------
{
    var (p, a, _, _) = Nuevo();
    await Ingresar(p, "vendedor.prueba");
    var r = await Pedir(p, "movimientos.registrar", new { sku = "SIN-001", tipo = "Salida", cantidad = 2.5, motivo = "Venta", bodegaId = 1 });
    Check("M1", "Salida válida con decimales en metros actualiza el stock y la existencia de la bodega",
        Ok(r) && a.P.First(x => x.Sku == "SIN-001").StockActual == 97.5m && a.E.First(e => e.Sku == "SIN-001").Cantidad == 97.5m);
    r = await Pedir(p, "movimientos.registrar", new { sku = "SIN-002", tipo = "Salida", cantidad = 11, motivo = "Venta", bodegaId = 1 });
    Check("M2", "Rechaza salida mayor al stock", !Ok(r) && Errores(r).Contains("stock suficiente"));
    await Ingresar(p, "admin.prueba");
    r = await Pedir(p, "movimientos.registrar", new { sku = "SIN-002", tipo = "Entrada", cantidad = 2.5, motivo = "Venta", bodegaId = 1 });
    Check("M3", "Rechaza motivo incoherente y decimales en varilla", !Ok(r) && Errores(r).Contains("salida") && Errores(r).Contains("entero"));
    r = await Pedir(p, "movimientos.registrar", new { sku = "SIN-002", tipo = "Entrada", cantidad = 1, motivo = "Devolución", bodegaId = 77 });
    Check("M4", "Rechaza una bodega inexistente", !Ok(r) && Errores(r).Contains("bodega"));

    await Pedir(p, "bodegas.crear", new { nombre = "Bodega sintética 2" });
    var b2 = a.B.First(b => b.Nombre == "Bodega sintética 2").BodegaId;
    await Pedir(p, "movimientos.registrar", new { sku = "SIN-002", tipo = "Entrada", cantidad = 5, motivo = "Devolución", bodegaId = b2 });
    r = await Pedir(p, "movimientos.registrar", new { sku = "SIN-002", tipo = "Salida", cantidad = 12, motivo = "Venta", bodegaId = 1 });
    Check("M5", "El stock se controla por bodega: hay 15 en total pero solo 10 en la principal, así que no deja sacar 12 de ella",
        !Ok(r) && a.P.First(x => x.Sku == "SIN-002").StockActual == 15 && a.E.First(e => e.Sku == "SIN-002" && e.BodegaId == 1).Cantidad == 10);
    var movs = (await Pedir(p, "movimientos.listar", new { limite = 5 }))["datos"]!.AsArray();
    Check("M6", "La lista de movimientos muestra bodega y responsable", movs.Count > 0 && movs[0]!["nombreBodega"] is not null && movs[0]!["usuario"]!.ToString().Length > 0);
    Check("M7", "Total del producto = suma de sus existencias por bodega",
        a.P.All(prod => prod.StockActual == a.E.Where(e => e.Sku == prod.Sku).Sum(e => e.Cantidad)));
}

// ---------- Proveedores ----------
{
    var (p, a, _, _) = Nuevo();
    await Ingresar(p, "admin.prueba");
    var r = await Pedir(p, "proveedores.crear", new { nombre = "Sintético C", nit = "abc", diasEntrega = 3 });
    Check("V1", "Rechaza NIT con formato inválido", !Ok(r) && Errores(r).Contains("NIT"));
    r = await Pedir(p, "proveedores.crear", new { nombre = "Sintético C", nit = "000000003-1", diasEntrega = 3 });
    Check("V2", "Rechaza NIT con dígito de verificación incorrecto", !Ok(r) && Errores(r).Contains("verificación"));
    r = await Pedir(p, "proveedores.crear", new { nombre = "Sintético C", nit = "000000001", diasEntrega = 91 });
    Check("V3", "Rechaza el mismo NIT escrito sin dígito de verificación y días de entrega fuera de rango", !Ok(r) && Errores(r).Contains("Ya existe") && Errores(r).Contains("90"));
    r = await Pedir(p, "proveedores.crear", new { nombre = "Sintético C", nit = "000.000.003", diasEntrega = 4 });
    Check("V4", "Crea un proveedor válido (NIT normalizado)", Ok(r) && a.Pr.Any(x => x.Nit == "000000003"));
    r = await Pedir(p, "proveedores.actualizar", new { nit = "000000001-8", nombre = "Proveedor Sintético A (editado)", telefono = "3000000000", diasEntrega = 4 });
    Check("V5", "Editar proveedor (antes E-06: no existía)", Ok(r) && a.Pr.First(x => x.Nit == "000000001-8").DiasEntrega == 4);
    r = await Pedir(p, "proveedores.desactivar", new { nit = "000000002-5" });
    var r2 = await Pedir(p, "productos.crear", new { sku = "SIN-010", nombre = "X", categoriaId = 1, unidad = "Unidad", stockActual = 0, stockMinimo = 0, nitProveedor = "000000002-5" });
    var r3 = await Pedir(p, "ordenes.crear", new { nitProveedor = "000000002-5", detalles = new[] { new { sku = "SIN-002", cantidad = 1 } } });
    Check("V6", "Un proveedor desactivado se conserva pero no se puede usar en productos ni órdenes nuevas",
        Ok(r) && a.Pr.Any(x => x.Nit == "000000002-5" && !x.Activo) && !Ok(r2) && !Ok(r3));
}

// ---------- Órdenes de compra ----------
{
    var (p, a, _, _) = Nuevo();
    await Ingresar(p, "admin.prueba");
    var r = await Pedir(p, "ordenes.crear", new { sku = "SIN-002", nitProveedor = "000000002-5", cantidad = 2.5 });
    Check("O1", "Rechaza 2,5 varillas en una orden (antes E-05: se aceptaba)", !Ok(r) && Errores(r).Contains("entero"));
    r = await Pedir(p, "ordenes.crear", new { sku = "SIN-002", nitProveedor = "000000002-5", cantidad = 3 });
    Check("O2", "Crea una orden de una línea con el formato anterior (sku y cantidad sueltos)", Ok(r) && a.O.Single().Detalles.Single().Cantidad == 3);
    r = await Pedir(p, "ordenes.pagar", new { codigo = "OC-001" });
    Check("O3", "No deja pagar una orden sin aprobar", !Ok(r));
    r = await Pedir(p, "ordenes.aprobar", new { codigo = "OC-001" });
    var r2 = await Pedir(p, "ordenes.aprobar", new { codigo = "OC-001" });
    Check("O4", "Aprueba una vez, rechaza la segunda aprobación y calcula la entrega esperada (10 días del proveedor)",
        Ok(r) && !Ok(r2) && a.O[0].FechaEntregaEsperada?.Date == DateTime.Today.AddDays(10));
    var antes = a.ListarOrdenes()[0].Valor;
    a.P.First(x => x.Sku == "SIN-002").PrecioCompra = 40000;
    var despues = a.ListarOrdenes()[0].Valor;
    Check("O5", "El valor de la orden se guarda con el precio del momento (antes E-04: cambiaba con el producto)", antes == 60000 && despues == 60000, $"{antes:0} → {despues:0}");
    r = await Pedir(p, "ordenes.crear", new { nitProveedor = "000000001-8", detalles = new[] { new { sku = "SIN-001", cantidad = 5.5m }, new { sku = "SIN-002", cantidad = 2m } } });
    Check("O6", "Orden con varias líneas", Ok(r) && a.O.Last().Detalles.Count == 2);
    r = await Pedir(p, "ordenes.crear", new { nitProveedor = "000000001-8", detalles = new[] { new { sku = "SIN-001", cantidad = 1m }, new { sku = "SIN-001", cantidad = 2m } } });
    Check("O7", "Rechaza el mismo producto repetido en dos líneas", !Ok(r));

    var stockAntes = a.P.First(x => x.Sku == "SIN-002").StockActual;
    await Ingresar(p, "bodega.prueba");
    r = await Pedir(p, "ordenes.recibir", new { codigo = "OC-001" });
    r2 = await Pedir(p, "ordenes.recibir", new { codigo = "OC-001" });
    var entrada = a.M.Last();
    Check("O8", "Recibir una orden aprobada suma al inventario, registra la entrada con el código de la orden y quién la recibió; no se recibe dos veces",
        Ok(r) && !Ok(r2) && a.P.First(x => x.Sku == "SIN-002").StockActual == stockAntes + 3
        && entrada is { Referencia: "OC-001", Motivo: "Compra a proveedor", Tipo: "Entrada" } && entrada.UsuarioId == a.U.First(u => u.NombreUsuario == "bodega.prueba").UsuarioId);
    await Ingresar(p, "contador.prueba");
    r = await Pedir(p, "ordenes.pagar", new { codigo = "OC-001" });
    Check("O9", "Una orden recibida se puede pagar (contador)", Ok(r) && a.O[0].Pagada && a.O[0].FechaPago is not null);
    await Ingresar(p, "admin.prueba");
    r = await Pedir(p, "ordenes.rechazar", new { codigo = "OC-002" });
    r2 = await Pedir(p, "ordenes.aprobar", new { codigo = "OC-002" });
    Check("O10", "Una orden rechazada ya no se puede aprobar", Ok(r) && !Ok(r2) && a.O[1].Estado == "Rechazada");
    r = await Pedir(p, "productos.eliminar", new { sku = "SIN-002" });
    Check("O11", "Un producto con órdenes no se borra: se desactiva", Ok(r) && a.P.Any(x => x.Sku == "SIN-002" && !x.Activo));

    var (p2, a2, _, _) = Nuevo();
    await Ingresar(p2, "admin.prueba");
    a2.SaltarIdOrden(998);
    var c999 = (await Pedir(p2, "ordenes.crear", new { sku = "SIN-001", nitProveedor = "000000001-8", cantidad = 1 }))["mensaje"]!.ToString();
    var c1000 = (await Pedir(p2, "ordenes.crear", new { sku = "SIN-001", nitProveedor = "000000001-8", cantidad = 1 }))["mensaje"]!.ToString();
    Check("O12", "Códigos únicos: OC-999 y luego OC-1000 (antes E-01: la orden 1001 repetía OC-001). Misma fórmula que la columna calculada",
        c999.Contains("OC-999") && c1000.Contains("OC-1000") && AlmacenMemoria.Codigo(1001) == "OC-1001" && AlmacenMemoria.Codigo(7) == "OC-007");
}

// ---------- Concurrencia básica (en memoria; la versión contra SQL Server está en --bd) ----------
{
    var (p, a, _, _) = Nuevo();
    await Ingresar(p, "vendedor.prueba");
    var tareas = Enumerable.Range(0, 20).Select(_ => Task.Run(() => Pedir(p, "movimientos.registrar", new { sku = "SIN-002", tipo = "Salida", cantidad = 1, motivo = "Venta", bodegaId = 1 }))).ToArray();
    var respuestas = await Task.WhenAll(tareas);
    var exitos = respuestas.Count(Ok);
    Check("X1", "20 salidas simultáneas de 1 unidad con stock 10: exactamente 10 se registran y el stock queda en 0, nunca negativo",
        exitos == 10 && a.P.First(x => x.Sku == "SIN-002").StockActual == 0 && a.E.First(e => e.Sku == "SIN-002").Cantidad == 0 && a.M.Count(m => m.Sku == "SIN-002") == 10, $"éxitos: {exitos}");

    var (p2, a2, _, _) = Nuevo();
    await Ingresar(p2, "admin.prueba");
    await Pedir(p2, "ordenes.crear", new { sku = "SIN-001", nitProveedor = "000000001-8", cantidad = 7 });
    await Pedir(p2, "ordenes.aprobar", new { codigo = "OC-001" });
    var rec = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => Task.Run(() => Pedir(p2, "ordenes.recibir", new { codigo = "OC-001" }))));
    Check("X2", "5 recepciones simultáneas de la misma orden: solo una suma al inventario",
        rec.Count(Ok) == 1 && a2.P.First(x => x.Sku == "SIN-001").StockActual == 107, $"éxitos: {rec.Count(Ok)}");
}

// ---------- Comunicación (Puente) ----------
{
    var (p, a, _, _) = Nuevo();
    await Ingresar(p, "admin.prueba");
    var r = await Pedir(p, "accion.inexistente");
    Check("CM1", "Acción desconocida responde ok=false", !Ok(r));
    var crudo = await p.AtenderAsync("{no es json", Puente.OrigenPermitido);
    Check("CM2", "JSON mal formado responde con mensaje de formato", crudo!.Contains("formato incorrecto"));
    p.SimularDesconexion = true;
    Check("CM3", "Simular desconexión: el Puente no responde", await p.AtenderAsync("{\"id\":1,\"accion\":\"resumen\"}", Puente.OrigenPermitido) is null);
    p.SimularDesconexion = false;
    var rr = await Pedir(p, "productos.crear", new { sku = "SIN-009" });
    Check("CM4", "Datos incompletos: devuelve lista de errores en español", !Ok(rr) && rr["errores"]!.AsArray().Count >= 2);
    a.Caido = true;
    var rbd = await Pedir(p, "productos.listar");
    a.Caido = false;
    Check("CM5", "Una falla de base de datos (DbException) se informa sin cerrar el programa", !Ok(rbd) && Errores(rbd).Contains("base de datos"));
    var est = await Pedir(p, "sistema.estado");
    Check("CM6", "El estado del sistema indica si es modo demostración (sin necesidad de sesión)", Ok(est) && est["datos"]!["modoDemo"]!.GetValue<bool>() == false);
    var duplicado = await p.AtenderAsync("{\"id\":7,\"accion\":\"productos.listar\",\"accion\":\"usuarios.crear\",\"datos\":{}}", Puente.OrigenPermitido);
    Check("CM7", "Un mensaje con propiedades repetidas (dos \"accion\") se rechaza como formato incorrecto, sin error inesperado ni ejecución",
        duplicado!.Contains("formato incorrecto") && !duplicado.Contains("inesperado") && !duplicado.Contains("\"ok\":true"), duplicado);
}

// ---------- Inteligencia artificial: reglas, recordatorios y asistente sin Gemini ----------
{
    var (p, a, _, _) = Nuevo();
    await Ingresar(p, "admin.prueba");
    for (int d = 1; d <= 30; d += 3)
        a.M.Add(new Movimiento { MovimientoId = 100 + d, Sku = "SIN-001", Tipo = "Salida", Cantidad = 6, Motivo = "Venta", BodegaId = 1, UsuarioId = 1, Usuario = "SINTÉTICO", Fecha = DateTime.Now.AddDays(-d) });
    var lista = (await Pedir(p, "productos.listar"))["datos"]!.AsArray();
    var cable = lista.First(x => x!["sku"]!.ToString() == "SIN-001")!;
    Check("AS1", "Consumo diario = vendido en 30 días / 30 (60/30 = 2; 100/2 = 50 días)", cable["consumoDiario"]!.GetValue<decimal>() == 2m && cable["diasRestantes"]!.GetValue<int>() == 50);
    var rep = (await Pedir(p, "ia.reposicion"))["datos"]!;
    Check("AS2", "Sin clave de Gemini, la sugerencia de reposición se responde con reglas", rep["fuente"]!.ToString() == "reglas");
    await Pedir(p, "ordenes.crear", new { sku = "SIN-001", nitProveedor = "000000001-8", cantidad = 5 });
    await Pedir(p, "ordenes.aprobar", new { codigo = "OC-001" });
    a.O[0].FechaAprobacion = DateTime.Now.AddDays(-12);
    var rec = (await Pedir(p, "ia.recordatorios"))["datos"]!;
    Check("AS3", "Recordatorio de pago cuando faltan 5 días o menos del plazo de 15", rec["recordatorios"]!.AsArray().Any(x => x!["tipo"]!.ToString() == "Pago"));
    var pr = (await Pedir(p, "ia.preguntar", new { pregunta = "¿Qué me falta de construcción?" }))["datos"]!;
    Check("AS4", "Pregunta sin Gemini: responde con reglas", pr["fuente"]!.ToString() == "reglas");
    var larga = await Pedir(p, "ia.preguntar", new { pregunta = new string('a', 501) });
    Check("AS5", "Rechaza preguntas de más de 500 caracteres", !Ok(larga));
}

// ---------- ClienteGemini contra un servidor FALSO local (no es Gemini real) ----------
{
    var oyente = new HttpListener(); oyente.Prefixes.Add("http://127.0.0.1:18765/"); oyente.Start();
    string modo = "ok"; string? claveRecibida = null; string? urlRecibida = null;
    _ = Task.Run(async () =>
    {
        while (oyente.IsListening)
        {
            HttpListenerContext ctx; try { ctx = await oyente.GetContextAsync(); } catch { break; }
            claveRecibida = ctx.Request.Headers["x-goog-api-key"]; urlRecibida = ctx.Request.Url!.PathAndQuery;
            if (modo == "lento") await Task.Delay(3000);
            string cuerpo; int code = 200;
            if (modo == "ok") cuerpo = """{"candidates":[{"content":{"parts":[{"text":"pensando","thought":true},{"text":"Hola dueño"}]}}]}""";
            else if (modo == "error") { code = 400; cuerpo = """{"error":{"message":"API key not valid"}}"""; }
            else cuerpo = """{"candidates":[{"content":{"parts":[{"text":"tarde"}]}}]}""";
            var b = Encoding.UTF8.GetBytes(cuerpo); ctx.Response.StatusCode = code; ctx.Response.ContentType = "application/json";
            try { await ctx.Response.OutputStream.WriteAsync(b); ctx.Response.Close(); } catch { }
        }
    });
    var (p, _, gem, _) = Nuevo("CLAVE-DE-PRUEBA", "http://127.0.0.1:18765/v1beta", 1);
    await Ingresar(p, "admin.prueba");
    var t = await gem.GenerarAsync("inst", "msg", CancellationToken.None);
    Check("G1", "Lee la respuesta e ignora las partes de 'pensamiento'", t == "Hola dueño");
    Check("G2", "La clave viaja en el encabezado x-goog-api-key y no en la URL", claveRecibida == "CLAVE-DE-PRUEBA" && !(urlRecibida ?? "").Contains("CLAVE"));
    modo = "error"; t = await gem.GenerarAsync("inst", "msg", CancellationToken.None);
    Check("G3", "Error 400: devuelve null y guarda el motivo", t is null && (gem.UltimoError ?? "").Contains("400"));
    modo = "lento"; t = await gem.GenerarAsync("inst", "msg", CancellationToken.None);
    Check("G4", "Tiempo agotado: devuelve null y se usan las reglas de respaldo", t is null && (gem.UltimoError ?? "").Contains("tardó"));
    modo = "ok"; await Task.Delay(3500); // deja terminar la petición lenta anterior
    var rep = (await Pedir(p, "ia.preguntar", new { pregunta = "hola" }))["datos"]!;
    Check("G5", "Con respuesta del servidor falso, la fuente queda como 'gemini'", rep["fuente"]!.ToString() == "gemini");
    oyente.Stop();
}

// ---------- Sin acceso a internet: Gemini configurado pero inalcanzable (el dominio .invalid nunca se resuelve) ----------
{
    var (pg, _, gemSinRed, _) = Nuevo("CLAVE-DE-PRUEBA", "https://ferrosuite-sin-red.invalid", 5);
    await Ingresar(pg, "admin.prueba");
    var reloj = System.Diagnostics.Stopwatch.StartNew();
    var preg = await Pedir(pg, "ia.preguntar", new { pregunta = "¿Qué debo pedir?" });
    var repo = await Pedir(pg, "ia.reposicion");
    reloj.Stop();
    Check("G6", "Sin internet (servidor de Gemini inalcanzable): el asistente responde con reglas, informa el motivo y no bloquea el programa",
        Ok(preg) && preg["datos"]!["fuente"]!.ToString() == "reglas" && Ok(repo) && repo["datos"]!["fuente"]!.ToString() == "reglas"
        && gemSinRed.UltimoError is not null && reloj.Elapsed < TimeSpan.FromSeconds(15),
        $"{reloj.Elapsed.TotalSeconds:0.0} s · motivo: {gemSinRed.UltimoError}");
}

// ---------- Administración: bodegas, categorías y edición de usuarios (D-04) ----------
{
    var ea = Entorno.Nuevo(demo: true);
    await ea.Ingresar("admin.prueba");
    var nueva = await ea.Pedir("bodegas.crear", new { nombre = "Bodega sintética norte", direccion = "Calle 1" });
    var todas = (await ea.Pedir("bodegas.listarTodas"))["datos"]!.AsArray();
    var norte = todas.First(b => b!["nombre"]!.ToString() == "Bodega sintética norte")!;
    var idNorte = norte["bodegaId"]!.GetValue<int>();
    var ren = await ea.Pedir("bodegas.actualizar", new { bodegaId = idNorte, nombre = "Bodega sintética sur", direccion = "Calle 2", activa = true });
    var dup = await ea.Pedir("bodegas.actualizar", new { bodegaId = idNorte, nombre = "Bodega principal", activa = true });
    var tras = ea.A.B.First(b => b.BodegaId == idNorte);
    Check("CA1", "Administrador: crea una bodega y la edita (nombre y dirección); un nombre repetido se rechaza",
        Entorno.Ok(nueva) && Entorno.Ok(ren) && tras.Nombre == "Bodega sintética sur" && tras.Direccion == "Calle 2" && !Entorno.Ok(dup) && Entorno.Errores(dup).Contains("Ya existe"),
        Entorno.Errores(dup));

    var desNorte = await ea.Pedir("bodegas.actualizar", new { bodegaId = idNorte, nombre = "Bodega sintética sur", activa = false });
    var principal = ea.A.B.First(b => b.Nombre == "Bodega principal");
    var desPrincipal = await ea.Pedir("bodegas.actualizar", new { bodegaId = principal.BodegaId, nombre = principal.Nombre, activa = false });
    var listaActivas = (await ea.Pedir("bodegas.listar"))["datos"]!.AsArray();
    Check("CA2", "Desactivar una bodega vacía funciona y deja de ofrecerse en los formularios; una bodega con mercancía no se puede desactivar",
        Entorno.Ok(desNorte) && !listaActivas.Any(b => b!["bodegaId"]!.GetValue<int>() == idNorte) && !Entorno.Ok(desPrincipal)
        && Entorno.Errores(desPrincipal).Contains("mercancía") && principal.Activa, Entorno.Errores(desPrincipal));

    var cat = await ea.Pedir("categorias.crear", new { nombre = "Jardinería sintética" });
    var catId = ea.A.Cat.First(c => c.Nombre == "Jardinería sintética").CategoriaId;
    var catRen = await ea.Pedir("categorias.actualizar", new { categoriaId = catId, nombre = "Jardín sintético", activa = true });
    var catDup = await ea.Pedir("categorias.crear", new { nombre = "Plomería" });
    var catUsada = ea.A.P.First(pp => pp.Activo).CategoriaId;
    var desUsada = await ea.Pedir("categorias.actualizar", new { categoriaId = catUsada, nombre = ea.A.Cat.First(c => c.CategoriaId == catUsada).Nombre, activa = false });
    var desLibre = await ea.Pedir("categorias.actualizar", new { categoriaId = catId, nombre = "Jardín sintético", activa = false });
    var activas = (await ea.Pedir("categorias.listar"))["datos"]!.AsArray();
    var todasCat = (await ea.Pedir("categorias.listarTodas"))["datos"]!.AsArray();
    var prodCatInactiva = await ea.Pedir("productos.crear", new { sku = "CAT-INA-1", nombre = "Producto sintético", categoriaId = catId, unidad = "Unidad", stockActual = 0, stockMinimo = 1 });
    Check("CA3", "Categorías: crear, renombrar, nombre repetido rechazado, no se desactiva con productos activos, la inactiva sale de los formularios y no se puede usar",
        Entorno.Ok(cat) && Entorno.Ok(catRen) && !Entorno.Ok(catDup) && !Entorno.Ok(desUsada) && Entorno.Errores(desUsada).Contains("producto") && Entorno.Ok(desLibre)
        && !activas.Any(c => c!["categoriaId"]!.GetValue<int>() == catId) && todasCat.Any(c => c!["categoriaId"]!.GetValue<int>() == catId)
        && !Entorno.Ok(prodCatInactiva), $"{Entorno.Errores(desUsada)} | {Entorno.Errores(prodCatInactiva)}");

    var usuariosLista = (await ea.Pedir("usuarios.listar"))["datos"]!.AsArray();
    int IdDe(string n) => usuariosLista.First(u => u!["nombreUsuario"]!.ToString() == n)!["usuarioId"]!.GetValue<int>();
    var edit = await ea.Pedir("usuarios.actualizar", new { usuarioId = IdDe("bodega.prueba"), nombreCompleto = "Bodeguero sintético editado", rolCodigo = Rol.Contador });
    var propio = await ea.Pedir("usuarios.actualizar", new { usuarioId = IdDe("admin.prueba"), nombreCompleto = "Admin", rolCodigo = Rol.Vendedor });
    var malo = await ea.Pedir("usuarios.actualizar", new { usuarioId = IdDe("vendedor.prueba"), nombreCompleto = "", rolCodigo = "NO-EXISTE" });
    var auditoria = (await ea.Pedir("auditoria.listar", new { limite = 100 }))["datos"]!.ToJsonString();
    await ea.Ingresar("bodega.prueba");
    var ses = await ea.Pedir("sesion.actual");
    Check("CA4", "Editar usuario: cambia nombre y rol (rige en su siguiente ingreso), queda auditado; no se cambia el propio rol; datos inválidos se rechazan",
        Entorno.Ok(edit) && ses["datos"]!.ToJsonString().Contains(Rol.Contador) && ses["datos"]!["nombreCompleto"]!.ToString() == "Bodeguero sintético editado"
        && !Entorno.Ok(propio) && Entorno.Errores(propio).Contains("propio rol") && !Entorno.Ok(malo) && auditoria.Contains("usuarios.actualizar")
        && auditoria.Contains("bodegas.actualizar") && auditoria.Contains("categorias.crear"),
        Entorno.Errores(propio));
    var bodegaDespues = await ea.Pedir("categorias.crear", new { nombre = "Intento" });
    Check("CA5", "Las nuevas acciones administrativas se rechazan a otros roles en el backend (también figuran en la matriz MZ)",
        Entorno.Denegado(bodegaDespues));
}

// ---------- Clave de Gemini: nunca en texto plano (DPAPI en Windows; aquí un protector de prueba reversible) ----------
{
    var carpeta = Path.Combine(Path.GetTempPath(), "ferrosuite-clave-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(carpeta);
    var prueba = new ProtectorPrueba();
    const string clave = "CLAVE-SINTETICA-DE-PRUEBA-123";
    var entornoAnterior = Environment.GetEnvironmentVariable("GEMINI_API_KEY");
    Environment.SetEnvironmentVariable("GEMINI_API_KEY", null);
    try
    {
        var error = FerroSuite.Infraestructura.IA.Gemini.ClaveGemini.Guardar(clave, carpeta, prueba);
        var archivo = Path.Combine(carpeta, FerroSuite.Infraestructura.IA.Gemini.ClaveGemini.ArchivoCifrado);
        var contenido = File.Exists(archivo) ? File.ReadAllText(archivo) : "";
        Check("GK1", "La clave de Gemini se guarda cifrada: el archivo no contiene la clave en texto plano y se recupera igual",
            error is null && contenido.StartsWith("DPAPI1:") && !contenido.Contains(clave)
            && FerroSuite.Infraestructura.IA.Gemini.ClaveGemini.Leer(carpeta, prueba) == clave, contenido.Length > 0 ? contenido[..Math.Min(20, contenido.Length)] + "…" : "sin archivo");

        File.Delete(archivo);
        var antiguo = Path.Combine(carpeta, FerroSuite.Infraestructura.IA.Gemini.ClaveGemini.ArchivoAntiguo);
        File.WriteAllText(antiguo, clave);
        var leida = FerroSuite.Infraestructura.IA.Gemini.ClaveGemini.Leer(carpeta, prueba);
        Check("GK2", "Un archivo antiguo en texto plano se migra: se cifra y se borra el texto plano",
            leida == clave && !File.Exists(antiguo) && File.Exists(archivo) && !File.ReadAllText(archivo).Contains(clave));

        var sinDpapi = new ProtectorPrueba { Disponible = false };
        var carpeta2 = Path.Combine(carpeta, "sin-dpapi");
        var error2 = FerroSuite.Infraestructura.IA.Gemini.ClaveGemini.Guardar(clave, carpeta2, sinDpapi);
        Check("GK3", "Si no hay DPAPI (fuera de Windows), la clave NO se guarda en texto plano: se avisa y no se escribe ningún archivo",
            error2 is not null && !Directory.Exists(carpeta2), error2 ?? "");

        var error3 = FerroSuite.Infraestructura.IA.Gemini.ClaveGemini.Guardar("", carpeta, prueba);
        Check("GK4", "Guardar una clave vacía la borra", error3 is null && !File.Exists(archivo)
            && FerroSuite.Infraestructura.IA.Gemini.ClaveGemini.Leer(carpeta, prueba) == "");

        Check("GK5", "El protector real usa DPAPI solo en Windows (en este sistema se declara no disponible y no intenta guardar)",
            FerroSuite.Infraestructura.IA.Gemini.ProtectorDpapi.Instancia.Disponible == OperatingSystem.IsWindows(),
            OperatingSystem.IsWindows() ? "Windows: DPAPI disponible" : "no es Windows: DPAPI no disponible");
    }
    finally
    {
        Environment.SetEnvironmentVariable("GEMINI_API_KEY", entornoAnterior);
        Directory.Delete(carpeta, recursive: true);
    }
}

// ---------- Configuración y preparación de la base (sin conectarse a SQL Server) ----------
{
    var dir = Directory.CreateTempSubdirectory("ferrosuite-pruebas-");
    string? aviso = null;
    var o = OpcionesFerroSuite.Cargar(Path.Combine(dir.FullName, "no-existe.json"), m => aviso = m);
    Check("K1", "Sin archivo: valores por defecto (LocalDB, 30 s, modo Real, bases FerroSuite y FerroSuiteDemo) y aviso",
        o.BaseDatos.Servidor == @"(localdb)\MSSQLLocalDB" && o.BaseDatos.TiempoConexionSegundos == 30 && !o.BaseDatos.EsDemo
        && o.BaseDatos.NombreBaseReal == "FerroSuite" && o.BaseDatos.NombreBaseDemo == "FerroSuiteDemo" && aviso is not null);
    var malo = Path.Combine(dir.FullName, "malo.json"); File.WriteAllText(malo, "{ esto no es json");
    aviso = null; o = OpcionesFerroSuite.Cargar(malo, m => aviso = m);
    Check("K2", "Archivo mal escrito: valores por defecto y aviso, sin cerrar el programa", o.Gemini.Modelo == "gemini-flash-latest" && aviso is not null);
    var bueno = Path.Combine(dir.FullName, "bueno.json");
    File.WriteAllText(bueno, """
        { // comentario permitido
          "BaseDatos": { "Servidor": "(localdb)\\Otra", "TiempoConexionSegundos": 999, "Modo": "demo", "NombreBaseDemo": "Mala;DROP" },
          "Gemini": { "Habilitado": false, "Modelo": "", "TiempoEsperaSegundos": 1, "ApiKey": "NO-DEBE-LEERSE" }
        }
        """);
    o = OpcionesFerroSuite.Cargar(bueno);
    Check("K3", "Lee los valores, corrige los fuera de rango y rechaza nombres de base no válidos",
        o.BaseDatos.Servidor == @"(localdb)\Otra" && o.BaseDatos.TiempoConexionSegundos == 120 && o.BaseDatos.EsDemo && o.BaseDatos.Modo == "Demo"
        && o.BaseDatos.NombreBaseDemo == "FerroSuiteDemo" && !o.Gemini.Habilitado && o.Gemini.Modelo == "gemini-flash-latest" && o.Gemini.TiempoEsperaSegundos == 5);
    Check("K4", "El archivo de configuración no tiene dónde guardar una clave (se ignora 'ApiKey')", typeof(OpcionesAsistente).GetProperty("ApiKey") is null);
    var iguales = Path.Combine(dir.FullName, "iguales.json");
    File.WriteAllText(iguales, """{ "BaseDatos": { "NombreBaseReal": "Misma", "NombreBaseDemo": "misma" } }""");
    var errorIguales = false;
    try { OpcionesFerroSuite.Cargar(iguales); } catch (InvalidOperationException) { errorIguales = true; }
    Check("K5", "La base real y la de demostración no pueden tener el mismo nombre", errorIguales);

    var bdDemo = new BaseDatosSql(o.BaseDatos);
    var bdReal = new BaseDatosSql(new OpcionesBaseDatos());
    Check("K6", "Cada modo usa su propia base y la cadena de conexión no lleva usuario ni contraseña",
        bdDemo.NombreBase == "FerroSuiteDemo" && bdReal.NombreBase == "FerroSuite" && bdDemo.CadenaConexion.Contains(@"(localdb)\Otra")
        && bdDemo.CadenaConexion.Contains("Integrated Security=True", StringComparison.OrdinalIgnoreCase) && !bdDemo.CadenaConexion.Contains("Password", StringComparison.OrdinalIgnoreCase),
        bdDemo.CadenaConexion);
    var protegida = false;
    try { bdReal.EliminarBase(); } catch (InvalidOperationException) { protegida = true; }
    var protegida2 = false;
    try { bdReal.RestablecerDemo(); } catch (InvalidOperationException) { protegida2 = true; }
    Check("K7", "La base real no se puede borrar ni restablecer desde el programa (se rechaza antes de conectarse)", protegida && protegida2);

    var raiz = BuscarRaiz();
    if (raiz is not null)
    {
        var real = OpcionesFerroSuite.Cargar(Path.Combine(raiz, "config", "appsettings.json"), m => aviso = m);
        var texto = File.ReadAllText(Path.Combine(raiz, "config", "appsettings.json"));
        Check("K8", "config/appsettings.json del repositorio: modo Real por defecto y sin claves", !real.BaseDatos.EsDemo
            && !texto.Contains("AIza") && !texto.Contains("ApiKey", StringComparison.OrdinalIgnoreCase) && !texto.Contains("Password", StringComparison.OrdinalIgnoreCase));
        var bdRepo = new BaseDatosSql(new OpcionesBaseDatos(), Path.Combine(raiz, "database"));
        var migraciones = new Migrador(bdRepo).Disponibles();
        Check("K9", "Las migraciones del repositorio se encuentran en orden (V001, V002…)",
            migraciones.Count >= 2 && migraciones[0].Version == 1 && migraciones[1].Version == 2,
            string.Join(", ", migraciones.Select(m => $"V{m.Version:000}__{m.Nombre}")));
        var v2 = File.ReadAllText(migraciones[1].Ruta);
        var gos = v2.Split('\n').Count(l => l.Trim().Equals("GO", StringComparison.OrdinalIgnoreCase));
        var lotes = ContarLotes(v2);
        Check("K10", "El divisor de lotes separa los scripts por GO (V002)", lotes >= gos && lotes <= gos + 1, $"{gos} GO, {lotes} lotes");
        var sqlTodo = string.Join("\n", Directory.GetFiles(Path.Combine(raiz, "database"), "*.sql", SearchOption.AllDirectories).Select(File.ReadAllText));
        Check("K11", "Los scripts no contienen contraseñas, hash ni usuarios con clave precargados",
            !System.Text.RegularExpressions.Regex.IsMatch(sqlTodo, @"(?i)(password\s*=|with\s+password|pwd\s*=)") && !sqlTodo.Contains("0x", StringComparison.Ordinal)
            && !sqlTodo.Contains("AQAAAA", StringComparison.Ordinal) && !System.Text.RegularExpressions.Regex.IsMatch(sqlTodo, @"(?i)INSERT\s+INTO\s+dbo\.Usuario\s*\([^)]*Clave"));
    }
    dir.Delete(true);
}

reg.Imprimir("Pruebas de FerroSuite (backend en memoria, datos SINTÉTICOS)");
Console.WriteLine();
foreach (var m in PruebasPronostico.Mediciones) Console.WriteLine("Medición | " + m);
return reg.Fallan == 0 ? 0 : 1;

static int ContarLotes(string script) => typeof(BaseDatosSql).Assembly.GetType("FerroSuite.Infraestructura.Persistencia.Sql")!
    .GetMethod("DividirLotes")!.Invoke(null, [script]) is System.Collections.ICollection c ? c.Count : -1;

// Busca la carpeta raíz de la solución (la que tiene FerroSuite.slnx) subiendo desde el ejecutable.
static string? BuscarRaiz()
{
    for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
        if (File.Exists(Path.Combine(d.FullName, "FerroSuite.slnx"))) return d.FullName;
    return null;
}

/// <summary>Protector reversible SOLO para pruebas (XOR): permite comprobar el flujo de ClaveGemini fuera de Windows.</summary>
sealed class ProtectorPrueba : FerroSuite.Infraestructura.IA.Gemini.IProtectorDatos
{
    public bool Disponible { get; init; } = true;
    public byte[] Proteger(byte[] d) => d.Select(b => (byte)(b ^ 0x5A)).Prepend((byte)1).ToArray();
    public byte[] Desproteger(byte[] d) => d.Skip(1).Select(b => (byte)(b ^ 0x5A)).ToArray();
}
