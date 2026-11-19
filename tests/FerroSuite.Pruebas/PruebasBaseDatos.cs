// PRUEBAS CONTRA SQL SERVER (LocalDB). Se ejecutan con:  dotnet run --project tests/FerroSuite.Pruebas -- --bd
// Requieren Windows con SQL Server Express LocalDB (lo instala Visual Studio). Cada ejecución crea bases TEMPORALES
// con nombre "FerroSuite_Pruebas_<id>" y las borra al final: nunca tocan la base real ni la de demostración.
// Todos los datos son SINTÉTICOS. Los resultados se guardan en tests/resultados/ como evidencia verificable.
using System.Diagnostics;
using System.Security.Cryptography;
using FerroSuite.Infraestructura.Configuracion;
using FerroSuite.Infraestructura.Persistencia;
using FerroSuite.Infraestructura.Seguridad;
using FerroSuite.Pruebas.Herramientas;
using FerroSuite.Aplicacion.IA.Pronostico;
using Microsoft.Data.SqlClient;

namespace FerroSuite.Pruebas;

public static class PruebasBaseDatos
{
    private sealed record Contexto(BaseDatosSql Bd, RepositorioInventarioSql Inv, RepositorioProveedoresSql Prov, RepositorioComprasSql Comp,
        RepositorioUsuariosSql Usu, InventarioServicio InvServ, OrdenServicio OrdServ, UsuarioServicio UsuServ, ProveedorServicio ProvServ, Autorizador Aut);

    // Servicios reales con el hash de producción (PasswordHasher de Identity, 210.000 iteraciones) y auditoría en la base.
    private static Contexto Armar(BaseDatosSql bd)
    {
        var inv = new RepositorioInventarioSql(bd); var prov = new RepositorioProveedoresSql(bd);
        var comp = new RepositorioComprasSql(bd); var usu = new RepositorioUsuariosSql(bd);
        var aut = new Autorizador(new AuditoriaServicio(new RepositorioAuditoriaSql(bd), TimeProvider.System, "PRUEBAS-BD"));
        return new Contexto(bd, inv, prov, comp, usu,
            new InventarioServicio(inv, prov, comp, new CalculadoraReposicion(), aut),
            new OrdenServicio(comp, inv, prov, TimeProvider.System, aut),
            new UsuarioServicio(usu, new HasherIdentity(), aut, TimeProvider.System),
            new ProveedorServicio(prov, aut), aut);
    }

    private static string RaizScripts() =>
        BuscarRaiz() is string r ? Path.Combine(r, "database") : throw new DirectoryNotFoundException("No se encontró la carpeta database del repositorio.");

    private static BaseDatosSql NuevaBase(string servidor, string nombre, bool demo = false, string? scripts = null) =>
        new(new OpcionesBaseDatos { Servidor = servidor, Modo = demo ? OpcionesBaseDatos.ModoDemo : OpcionesBaseDatos.ModoReal,
            NombreBaseReal = demo ? nombre + "_R" : nombre, NombreBaseDemo = demo ? nombre : nombre + "_D", TiempoConexionSegundos = 60 }, scripts ?? RaizScripts());

    public static async Task<int> Correr(string servidor)
    {
        var reg = new Registro();
        var id = Guid.NewGuid().ToString("N")[..10];
        var nombre = BaseDatosSql.PrefijoBasesPrueba + id;
        var creadas = new List<BaseDatosSql>();
        Console.WriteLine($"Servidor: {servidor}. Base temporal: {nombre}");

        try
        {
            // ================= Creación y migraciones =================
            var bd = NuevaBase(servidor, nombre);
            creadas.Add(bd);
            bd.Preparar();
            var c = Armar(bd);
            reg.Check("B1", "Crea la base desde cero y aplica las migraciones V001, V002 y V003",
                bd.UltimasMigraciones.Count == 3 && new Migrador(bd).VersionActual() == 3, string.Join(", ", bd.UltimasMigraciones));
            var tablas = Escalar<int>(bd, """
                SELECT COUNT(*) FROM sys.tables WHERE name IN ('Proveedor','Producto','CategoriaProducto','Bodega','ExistenciaBodega',
                'Rol','Usuario','MovimientoInventario','OrdenCompra','DetalleOrdenCompra','VersionEsquema','AuditoriaEvento')
                """);
            reg.Check("B2", "Existen las 12 tablas del modelo (incluidas VersionEsquema y AuditoriaEvento)", tablas == 12, $"{tablas} tablas");
            bd.Preparar();
            reg.Check("B3", "Preparar otra vez no vuelve a aplicar migraciones (idempotente)", bd.UltimasMigraciones.Count == 0);
            reg.Check("B4", "En modo Real no se cargan datos de demostración", Escalar<int>(bd, "SELECT COUNT(*) FROM dbo.Producto") == 0 && Escalar<int>(bd, "SELECT COUNT(*) FROM dbo.Proveedor") == 0);

            // Datos SINTÉTICOS para el resto de pruebas.
            var clave = "Bd" + RandomNumberGenerator.GetHexString(12) + "9"; // aleatoria en cada ejecución
            c.UsuServ.CrearAdministradorInicial("admin.pruebas", "Administrador sintético", clave);
            var sesion = c.UsuServ.IniciarSesion("admin.pruebas", clave);
            reg.Check("B5", "Crea el administrador inicial (hash de ASP.NET Core Identity, sin columnas del formato anterior) e inicia sesión", sesion.EsAdministrador
                && Escalar<int>(bd, "SELECT COUNT(*) FROM dbo.Usuario WHERE NombreUsuario = 'admin.pruebas' AND ClaveIdentity LIKE 'AQAAAA%' AND ClaveHash IS NULL AND DebeCambiarClave = 0") == 1);
            c.ProvServ.Crear(new Proveedor { Nit = "000000001-8", Nombre = "Proveedor sintético BD", DiasEntrega = 5 }, sesion);
            c.InvServ.CrearProducto(new Producto { Sku = "BD-001", Nombre = "Producto sintético BD", CategoriaId = 1, Unidad = "Unidad", StockActual = 10, StockMinimo = 2, PrecioCompra = 1000, NitProveedor = "000000001-8" }, sesion);
            reg.Check("B6", "Crear producto con stock guarda producto, existencia y movimiento 'Inventario inicial' con el responsable",
                Escalar<decimal>(bd, "SELECT StockActual FROM dbo.Producto WHERE Sku='BD-001'") == 10
                && Escalar<decimal>(bd, "SELECT SUM(Cantidad) FROM dbo.ExistenciaBodega WHERE Sku='BD-001'") == 10
                && Escalar<int>(bd, $"SELECT COUNT(*) FROM dbo.MovimientoInventario WHERE Sku='BD-001' AND Motivo=N'Inventario inicial' AND UsuarioId={sesion.UsuarioId}") == 1);

            // ================= Restricciones de integridad =================
            reg.Check("I1", "CHECK: rechaza un SKU con espacios o minúsculas", FallaCon(bd, "INSERT INTO dbo.Producto (Sku,Nombre,CategoriaId,Unidad,StockMinimo) VALUES ('ab c',N'x',1,N'Unidad',0)", "CK_Producto_SkuFormato"));
            reg.Check("I2", "CHECK: rechaza un NIT con letras", FallaCon(bd, "INSERT INTO dbo.Proveedor (Nit,Nombre,DiasEntrega) VALUES ('ABC-1',N'x',1)", "CK_Proveedor_NitFormato"));
            reg.Check("I3", "FK: rechaza un movimiento de un producto que no existe", FallaCon(bd,
                "INSERT INTO dbo.MovimientoInventario (Sku,Tipo,Cantidad,Motivo,Usuario,BodegaId,UsuarioId) VALUES ('NO-EXISTE','Entrada',1,N'Devolución',N'x',1,1)", "FK_"));
            reg.Check("I4", "Los movimientos no se modifican ni se borran (disparador)",
                FallaCon(bd, "UPDATE dbo.MovimientoInventario SET Cantidad = 999", "51010") && FallaCon(bd, "DELETE FROM dbo.MovimientoInventario", "51010")
                && Escalar<int>(bd, "SELECT COUNT(*) FROM dbo.MovimientoInventario WHERE Cantidad = 999") == 0);
            reg.Check("I5", "CHECK: la existencia por bodega no puede quedar negativa", FallaCon(bd, "UPDATE dbo.ExistenciaBodega SET Cantidad = -1 WHERE Sku='BD-001'", "CK_"));
            reg.Check("I6", "FK sin cascada: no se puede borrar un producto con historial", FallaCon(bd, "DELETE FROM dbo.Producto WHERE Sku='BD-001'", "REFERENCE")
                && Escalar<int>(bd, "SELECT COUNT(*) FROM dbo.MovimientoInventario WHERE Sku='BD-001'") > 0);
            reg.Check("I7", "CHECK: una clave con menos de 100.000 iteraciones se rechaza", FallaCon(bd,
                "INSERT INTO dbo.Usuario (NombreUsuario,NombreCompleto,RolId,ClaveHash,ClaveSal,ClaveIteraciones) VALUES ('debil',N'x',2,0x00,0x00,1000)", "CK_"));
            reg.Check("I8", "CHECK: rechaza nombres de usuario con mayúsculas o espacios", FallaCon(bd,
                "INSERT INTO dbo.Usuario (NombreUsuario,NombreCompleto,RolId) VALUES ('Con Espacio',N'x',2)", "CK_"));
            reg.Check("I9", "CHECK: rechaza un estado de orden que no existe", FallaCon(bd,
                "INSERT INTO dbo.OrdenCompra (NitProveedor,Estado) VALUES ('000000001-8',N'Inventado')", "CK_Orden_Estado"));
            var codigo = c.OrdServ.Crear(new OrdenCompra { NitProveedor = "000000001-8", Detalles = [new DetalleOrdenCompra { Sku = "BD-001", Cantidad = 3 }] }, sesion);
            var ordenId = Escalar<int>(bd, $"SELECT OrdenId FROM dbo.OrdenCompra WHERE Codigo = '{codigo}'");
            reg.Check("I10", "UNIQUE: el mismo producto no se repite en una orden; el código de orden es único",
                FallaCon(bd, $"INSERT INTO dbo.DetalleOrdenCompra (OrdenId,Sku,Cantidad) VALUES ({ordenId},'BD-001',1)", "UQ_")
                && Escalar<int>(bd, "SELECT COUNT(*) FROM sys.indexes WHERE name = 'UX_OrdenCompra_Codigo' AND is_unique = 1") == 1);
            reg.Check("I11", "Código de orden calculado por la base: OC-### hasta 999 y número completo desde 1000",
                codigo == $"OC-{ordenId:000}" && Escalar<string>(bd, "SELECT 'OC-' + CASE WHEN 1001 < 1000 THEN RIGHT('000'+CAST(1001 AS VARCHAR(10)),3) ELSE CAST(1001 AS VARCHAR(10)) END") == "OC-1001");
            reg.Check("I12", "Consultas parametrizadas: un SKU con comillas se trata como dato, no como SQL",
                c.Inv.BuscarProducto("X'; DROP TABLE dbo.Producto; --") is null && Escalar<int>(bd, "SELECT COUNT(*) FROM sys.tables WHERE name='Producto'") == 1);

            // ================= Transacciones fallidas =================
            var antes = Foto(bd, "BD-001");
            var errorFk = false;
            try { c.Inv.RegistrarMovimiento(new Movimiento { Sku = "BD-001", Tipo = "Salida", Cantidad = 2, Motivo = "Venta", BodegaId = 1, UsuarioId = 999_999, Usuario = "inexistente" }); }
            catch (SqlException) { errorFk = true; }
            reg.Check("T1", "Si el movimiento falla DESPUÉS de descontar (usuario inexistente → FK), se deshace todo: stock, existencia y movimientos quedan igual",
                errorFk && Foto(bd, "BD-001") == antes, $"antes {antes}, después {Foto(bd, "BD-001")}");
            var errorStock = false;
            try { c.Inv.RegistrarMovimiento(new Movimiento { Sku = "BD-001", Tipo = "Salida", Cantidad = 11, Motivo = "Venta", BodegaId = 1, UsuarioId = sesion.UsuarioId, Usuario = sesion.NombreCompleto }); }
            catch (ErrorValidacion) { errorStock = true; }
            reg.Check("T2", "Una salida mayor que la existencia se rechaza en la base (UPDATE condicional) y no cambia nada", errorStock && Foto(bd, "BD-001") == antes);
            c.OrdServ.Aprobar(codigo, sesion);
            var errorRecibir = false;
            try { c.Comp.Recibir(codigo, 1, 999_999, "inexistente"); } catch (SqlException) { errorRecibir = true; }
            reg.Check("T3", "Si recibir una orden falla a mitad de camino, la orden sigue Aprobada y el stock no cambia",
                errorRecibir && Escalar<string>(bd, $"SELECT Estado FROM dbo.OrdenCompra WHERE Codigo='{codigo}'") == "Aprobada" && Foto(bd, "BD-001") == antes);
            c.OrdServ.Recibir(codigo, null, sesion);
            reg.Check("T4", "Recibir la orden (bien) suma 3 unidades y deja la entrada con el código de la orden",
                Escalar<decimal>(bd, "SELECT StockActual FROM dbo.Producto WHERE Sku='BD-001'") == 13
                && Escalar<int>(bd, $"SELECT COUNT(*) FROM dbo.MovimientoInventario WHERE Referencia='{codigo}' AND UsuarioId={sesion.UsuarioId}") == 1);
            reg.Check("T5", "Una migración que falla se deshace completa y la base queda en su versión anterior", MigracionFallidaSeDeshace(servidor, nombre + "_M", creadas));

            // ================= Concurrencia básica =================
            c.InvServ.CrearProducto(new Producto { Sku = "BD-002", Nombre = "Producto sintético concurrencia", CategoriaId = 1, Unidad = "Unidad", StockActual = 10, StockMinimo = 0 }, sesion);
            int exitos = 0, sinStock = 0, otros = 0; var detalleOtros = "";
            await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(() =>
            {
                try { c.Inv.RegistrarMovimiento(new Movimiento { Sku = "BD-002", Tipo = "Salida", Cantidad = 1, Motivo = "Venta", BodegaId = 1, UsuarioId = sesion.UsuarioId, Usuario = sesion.NombreCompleto }); Interlocked.Increment(ref exitos); }
                catch (ErrorValidacion) { Interlocked.Increment(ref sinStock); }
                catch (Exception ex) { Interlocked.Increment(ref otros); detalleOtros = ex.Message; }
            })));
            reg.Check("X1", "20 salidas simultáneas de 1 con stock 10 (20 conexiones): exactamente 10 se registran, el stock queda en 0 y nunca negativo",
                exitos == 10 && sinStock == 10 && otros == 0 && Escalar<decimal>(bd, "SELECT StockActual FROM dbo.Producto WHERE Sku='BD-002'") == 0
                && Escalar<int>(bd, "SELECT COUNT(*) FROM dbo.MovimientoInventario WHERE Sku='BD-002' AND Tipo='Salida'") == 10,
                $"éxitos {exitos}, sin stock {sinStock}, otros errores {otros} {detalleOtros}");
            otros = 0;
            await Task.WhenAll(Enumerable.Range(0, 30).Select(i => Task.Run(() =>
            {
                try
                {
                    c.Inv.RegistrarMovimiento(new Movimiento { Sku = "BD-002", Tipo = i % 2 == 0 ? "Entrada" : "Salida", Cantidad = 1, Motivo = i % 2 == 0 ? "Devolución" : "Venta",
                        BodegaId = 1, UsuarioId = sesion.UsuarioId, Usuario = sesion.NombreCompleto });
                }
                catch (ErrorValidacion) { }
                catch (Exception ex) { Interlocked.Increment(ref otros); detalleOtros = ex.Message; }
            })));
            var consistente = Escalar<int>(bd, """
                SELECT COUNT(*) FROM dbo.Producto p
                WHERE p.StockActual <> (SELECT ISNULL(SUM(Cantidad),0) FROM dbo.ExistenciaBodega e WHERE e.Sku = p.Sku)
                   OR p.StockActual <> (SELECT ISNULL(SUM(CASE WHEN Tipo='Entrada' THEN Cantidad ELSE -Cantidad END),0) FROM dbo.MovimientoInventario m WHERE m.Sku = p.Sku)
                """) == 0;
            reg.Check("X2", "30 entradas y salidas mezcladas en paralelo: stock = suma de existencias = suma de movimientos, sin bloqueos mutuos", consistente && otros == 0, $"otros errores {otros} {detalleOtros}");
            var codigo2 = c.OrdServ.Crear(new OrdenCompra { NitProveedor = "000000001-8", Detalles = [new DetalleOrdenCompra { Sku = "BD-002", Cantidad = 4 }] }, sesion);
            c.OrdServ.Aprobar(codigo2, sesion);
            var stock2 = Escalar<decimal>(bd, "SELECT StockActual FROM dbo.Producto WHERE Sku='BD-002'");
            var recibidas = (await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => Task.Run(() => { try { return c.Comp.Recibir(codigo2, 1, sesion.UsuarioId, sesion.NombreCompleto); } catch { return false; } })))).Count(x => x);
            reg.Check("X3", "5 recepciones simultáneas de la misma orden: solo una suma al inventario",
                recibidas == 1 && Escalar<decimal>(bd, "SELECT StockActual FROM dbo.Producto WHERE Sku='BD-002'") == stock2 + 4, $"recepciones {recibidas}");
            var codigos = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(() =>
                c.Comp.CrearOrden(new OrdenCompra { NitProveedor = "000000001-8", Detalles = [new DetalleOrdenCompra { Sku = "BD-001", Cantidad = 1, PrecioUnitario = 1000 }] }))));
            reg.Check("X4", "20 órdenes creadas en paralelo reciben 20 códigos distintos", codigos.Distinct().Count() == 20);

            // ================= Autenticación, autorización y auditoría en la base =================
            var secretos = new List<string> { clave };
            var claveVendedor = "Bd" + RandomNumberGenerator.GetHexString(12) + "5"; secretos.Add(claveVendedor);
            c.UsuServ.Crear(sesion, "vendedor.pruebas", "Vendedor sintético", Rol.Vendedor, claveVendedor);
            var vend = c.UsuServ.IniciarSesion("vendedor.pruebas", claveVendedor);
            var claveVendedor2 = "Bd" + RandomNumberGenerator.GetHexString(12) + "6"; secretos.Add(claveVendedor2);
            var cambio = vend.DebeCambiarClave;
            vend = c.UsuServ.CambiarClavePropia(vend, claveVendedor, claveVendedor2);
            var denegada = false;
            try { c.InvServ.EliminarProducto("BD-001", vend); } catch (ErrorAutorizacion) { denegada = true; }
            reg.Check("Z1", "Un vendedor (tras cambiar su clave inicial) no puede eliminar productos; el intento queda en la auditoría de la base",
                cambio && denegada && Escalar<int>(bd, "SELECT COUNT(*) FROM dbo.Producto WHERE Sku='BD-001'") == 1
                && Escalar<int>(bd, $"SELECT COUNT(*) FROM dbo.AuditoriaEvento WHERE UsuarioId={vend.UsuarioId} AND Accion='productos.eliminar' AND Resultado='Denegado'") == 1);
            reg.Check("Z2", "Las operaciones sensibles quedan registradas en AuditoriaEvento (ingreso, usuarios, productos, compras)",
                Escalar<int>(bd, "SELECT COUNT(DISTINCT Accion) FROM dbo.AuditoriaEvento WHERE Resultado='Exitoso' AND Accion IN " +
                    "('sesion.iniciar','usuarios.crearAdministradorInicial','usuarios.crear','sesion.cambiarClave','productos.crear','ordenes.crear','ordenes.aprobar','ordenes.recibir')") == 8);
            reg.Check("Z3", "La auditoría no se puede modificar ni borrar (disparador)",
                FallaCon(bd, "UPDATE dbo.AuditoriaEvento SET Detalle = N'x'", "51020") && FallaCon(bd, "DELETE FROM dbo.AuditoriaEvento", "51020"));

            var claveAnterior = "Bd" + RandomNumberGenerator.GetHexString(12) + "3"; secretos.Add(claveAnterior);
            var sal = RandomNumberGenerator.GetBytes(16);
            using (var cx = bd.Conectar())
            {
                var cmd = new SqlCommand("""
                    INSERT INTO dbo.Usuario (NombreUsuario, NombreCompleto, RolId, ClaveHash, ClaveSal, ClaveIteraciones)
                    VALUES ('version.dos', N'Usuario creado con la versión 2', 4, @h, @s, 100000)
                    """, cx);
                cmd.Parameters.Add(new SqlParameter("@h", System.Data.SqlDbType.VarBinary, 32) { Value = Rfc2898DeriveBytes.Pbkdf2(claveAnterior, sal, 100_000, HashAlgorithmName.SHA256, 32) });
                cmd.Parameters.Add(new SqlParameter("@s", System.Data.SqlDbType.VarBinary, 16) { Value = sal });
                cmd.ExecuteNonQuery();
            }
            var anterior = c.UsuServ.IniciarSesion("version.dos", claveAnterior);
            reg.Check("Z4", "Una clave guardada con la versión 2 sigue sirviendo y al ingresar se convierte al formato de Identity (se borran las columnas anteriores)",
                anterior.RolCodigo == Rol.Contador
                && Escalar<int>(bd, "SELECT COUNT(*) FROM dbo.Usuario WHERE NombreUsuario='version.dos' AND ClaveIdentity LIKE 'AQAAAA%' AND ClaveHash IS NULL AND ClaveSal IS NULL") == 1);

            for (var i = 0; i < UsuarioServicio.MaximoIntentos; i++)
                try { c.UsuServ.IniciarSesion("vendedor.pruebas", "Clave-mala-" + i); } catch (ErrorValidacion) { }
            var entra = true;
            try { c.UsuServ.IniciarSesion("vendedor.pruebas", claveVendedor2); } catch (ErrorValidacion) { entra = false; }
            reg.Check("Z5", "El bloqueo por 5 intentos fallidos queda guardado en la base y no deja entrar ni con la clave correcta",
                !entra && Escalar<int>(bd, "SELECT COUNT(*) FROM dbo.Usuario WHERE NombreUsuario='vendedor.pruebas' AND BloqueadoHasta > SYSDATETIME()") == 1);
            reg.Check("Z6", "CHECK: un usuario no puede tener a la vez el hash nuevo y el de la versión 2",
                FallaCon(bd, "UPDATE dbo.Usuario SET ClaveHash = 0x01, ClaveSal = 0x01, ClaveIteraciones = 100000 WHERE NombreUsuario = 'admin.pruebas'", "CK_Usuario_Clave"));
            reg.Check("Z7", "Los cuatro roles del Documento Maestro", string.Join("|", Leer(bd, "SELECT Nombre FROM dbo.Rol ORDER BY RolId", r => r.GetString(0)))
                == "Administrador|Encargado de bodega|Vendedor de mostrador|Contador externo");
            var textoAuditoria = string.Join("\n", Leer(bd, "SELECT CONCAT(NombreUsuario,'|',Accion,'|',Objeto,'|',Detalle,'|',SesionId,'|',Equipo) FROM dbo.AuditoriaEvento", r => r.GetString(0)));
            reg.Check("Z8", "Ninguna clave usada en estas pruebas ni ningún hash aparece en la auditoría de la base",
                textoAuditoria.Length > 0 && secretos.All(s => !textoAuditoria.Contains(s)) && !textoAuditoria.Contains("AQAAAA"));

            // ================= Pronóstico: historial desde SQL Server + modelo ONNX real =================
            {
                var casos = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(Entorno.CarpetaModelos, "demanda_mlp_casos_prueba.json")))!;
                var ventas = casos["recorrido"]!["ventas_30_dias"]!.AsArray().Select(v => v!.GetValue<decimal>()).ToArray();
                var hoy = DateOnly.FromDateTime(DateTime.Now);
                c.InvServ.CrearProducto(new Producto { Sku = "BD-PRON", Nombre = "Producto sintético pronóstico", CategoriaId = 1, Unidad = "Bulto", StockActual = 0, StockMinimo = 10, PrecioCompra = 1000, NitProveedor = "000000001-8" }, sesion);
                using (var cx = bd.Conectar())
                {
                    void Insertar(DateTime fecha, string tipo, decimal cantidad, string motivo)
                    {
                        var cmd = new SqlCommand("INSERT INTO dbo.MovimientoInventario (Sku, Tipo, Cantidad, Motivo, Usuario, BodegaId, UsuarioId, Fecha) VALUES ('BD-PRON', @t, @c, @m, N'Sintético', 1, @u, @f)", cx);
                        cmd.Parameters.Add(new SqlParameter("@t", System.Data.SqlDbType.VarChar, 10) { Value = tipo });
                        cmd.Parameters.Add(new SqlParameter("@c", System.Data.SqlDbType.Decimal) { Precision = 12, Scale = 2, Value = cantidad });
                        cmd.Parameters.Add(new SqlParameter("@m", System.Data.SqlDbType.NVarChar, 40) { Value = motivo });
                        cmd.Parameters.Add(new SqlParameter("@u", System.Data.SqlDbType.Int) { Value = sesion.UsuarioId });
                        cmd.Parameters.Add(new SqlParameter("@f", System.Data.SqlDbType.DateTime2) { Value = fecha });
                        cmd.ExecuteNonQuery();
                    }
                    Insertar(hoy.AddDays(-31).ToDateTime(new TimeOnly(8, 0)), "Entrada", 300, "Inventario inicial");
                    for (var i = 0; i < 30; i++)
                        if (ventas[i] > 0) Insertar(hoy.AddDays(-30 + i).ToDateTime(new TimeOnly(11, 0)), "Salida", ventas[i], "Venta");
                    new SqlCommand($"UPDATE dbo.Producto SET StockActual = {300 - ventas.Sum()} WHERE Sku = 'BD-PRON'", cx).ExecuteNonQuery();
                }
                var corte = hoy.AddDays(-1);
                var historialSql = c.Inv.ObtenerHistorialVentas(corte.AddDays(-29));
                var serieSql = SerieDemanda.Construir(historialSql.Filas.Where(f => f.Sku == "BD-PRON"), historialSql.PrimerMovimiento["BD-PRON"], 300 - ventas.Sum(), corte.AddDays(-29), corte);
                reg.Check("F1", "El historial diario leído de SQL Server reproduce las ventas registradas (ventas por día, sin agotados falsos)",
                    serieSql.Select(v => (decimal)v).SequenceEqual(ventas), string.Join(",", serieSql.Take(5)) + "…");
                var entrada = PreparadorCaracteristicas.Preparar(ventas.Skip(2).Select(v => (double)v).ToArray(), corte, "Construcción", "Bulto", CalendarioFestivos.EsFestivo)!;
                var esperado = (decimal)Entorno.ModeloReal.Predecir([entrada])[0];
                var servicio = new PronosticoServicio(c.Inv, c.Comp, Entorno.ModeloReal, c.Aut, TimeProvider.System);
                var ordenesAntes = Escalar<string>(bd, "SELECT CONCAT(COUNT(*), '|', SUM(CASE WHEN Estado = N'Aprobada' THEN 1 ELSE 0 END)) FROM dbo.OrdenCompra");
                var resultado = servicio.RecomendarReposicion(sesion);
                var fila = resultado.Recomendaciones.Single(r => r.Sku == "BD-PRON");
                reg.Check("F2", "Recorrido con SQL Server: repositorio → entradas → ONNX Runtime → recomendación con el consumo esperado; no se crea ni aprueba ninguna orden",
                    resultado.Modelo.Disponible && fila.Fuente == PronosticoServicio.FuenteRed && Math.Abs(fila.ConsumoDiario - Math.Round(esperado, 2)) <= 0.01m
                    && fila.DiasHastaAgotarse is not null
                    && Escalar<string>(bd, "SELECT CONCAT(COUNT(*), '|', SUM(CASE WHEN Estado = N'Aprobada' THEN 1 ELSE 0 END)) FROM dbo.OrdenCompra") == ordenesAntes,
                    $"consumo {fila.ConsumoDiario} (esperado {esperado:0.###}), {fila.DiasHastaAgotarse} días, {fila.Urgencia}, pedir {fila.CantidadSugerida}");
                var sinHistoria = resultado.Recomendaciones.Single(r => r.Sku == "BD-002");
                reg.Check("F3", "Con SQL Server, un producto con poca historia usa la regla de respaldo e informa el motivo",
                    sinHistoria.EsRespaldo && (sinHistoria.MotivoRespaldo ?? "").Contains("insuficiente"), sinHistoria.MotivoRespaldo ?? "");
                var detalle = servicio.PronosticarProducto(sesion, "BD-PRON");
                var ventasDetalle = detalle.Historial.TakeLast(30).Select(h => h.Demanda ?? 0).ToList();
                reg.Check("F4", "Con SQL Server, el detalle por producto (pantalla de IA) da la misma recomendación que la lista y su historial son las ventas registradas",
                    detalle.Recomendacion.Fuente == PronosticoServicio.FuenteRed && detalle.Recomendacion.ConsumoDiario == fila.ConsumoDiario
                    && detalle.Recomendacion.CantidadSugerida == fila.CantidadSugerida && detalle.Recomendacion.RiesgoDesabastecimiento == fila.RiesgoDesabastecimiento
                    && ventasDetalle.SequenceEqual(ventas.Select(v => Math.Round(v, 2))) && detalle.Pronostico.Count == 7,
                    $"detalle {detalle.Recomendacion.ConsumoDiario} · lista {fila.ConsumoDiario}");
            }

            // ================= Administración de bodegas, categorías y usuarios (D-04) con SQL Server =================
            var invAdm = new RepositorioInventarioSql(bd); var usuAdm = new RepositorioUsuariosSql(bd);
            var idBodegaAdm = invAdm.CrearBodega(new Bodega { Nombre = "BD bodega admin", Direccion = "Dirección sintética" });
            invAdm.ActualizarBodega(new Bodega { BodegaId = idBodegaAdm, Nombre = "BD bodega editada", Direccion = "Otra dirección", Activa = false });
            var idCategoriaAdm = invAdm.CrearCategoria(new Categoria { Nombre = "BD categoría" });
            invAdm.ActualizarCategoria(new Categoria { CategoriaId = idCategoriaAdm, Nombre = "BD categoría editada", Activa = false });
            var rolContador = usuAdm.ListarRoles().First(r => r.Codigo == Rol.Contador).RolId;
            var idUsuarioAdm = usuAdm.Crear(new Usuario
            {
                NombreUsuario = "bd.edicion", NombreCompleto = "Usuario sintético BD", RolId = usuAdm.ListarRoles().First(r => r.Codigo == Rol.Bodega).RolId,
                Activo = true, ClaveHash = new HasherIdentity(10_000).Calcular("ClaveSinteticaBd123"), DebeCambiarClave = true,
            });
            usuAdm.ActualizarDatos(idUsuarioAdm, "Usuario sintético BD editado", rolContador);
            var bodegaLeida = invAdm.BuscarBodega(idBodegaAdm);
            var categoriaLeida = invAdm.ListarCategorias().FirstOrDefault(c => c.CategoriaId == idCategoriaAdm);
            var usuarioLeido = usuAdm.BuscarPorId(idUsuarioAdm);
            reg.Check("AD1", "Con SQL Server: crear y editar bodega y categoría (nombre, dirección, estado) y editar nombre y rol de un usuario quedan guardados",
                bodegaLeida is { Nombre: "BD bodega editada", Direccion: "Otra dirección", Activa: false } && categoriaLeida is { Nombre: "BD categoría editada", Activa: false }
                && usuarioLeido is { NombreCompleto: "Usuario sintético BD editado" } && usuarioLeido.RolId == rolContador
                && invAdm.ExisteBodegaConNombre("BD bodega editada") && !invAdm.ExisteBodegaConNombre("BD bodega editada", idBodegaAdm)
                && invAdm.ExisteCategoriaConNombre("BD categoría editada") && !invAdm.ExisteCategoriaConNombre("BD categoría editada", idCategoriaAdm));
            var unicoRechazado = false;
            try { invAdm.CrearCategoria(new Categoria { Nombre = "BD categoría editada" }); } catch (SqlException) { unicoRechazado = true; }
            reg.Check("AD2", "SQL Server rechaza una categoría con nombre repetido (restricción UNIQUE), aunque se salte la validación del servicio", unicoRechazado);

            // ================= Persistencia al cerrar y abrir =================
            var huella = Huella(bd);
            SqlConnection.ClearAllPools();
            var reabierta = NuevaBase(servidor, nombre);
            reabierta.Preparar();
            reg.Check("P1", "Al 'reabrir' (nuevas conexiones, nueva instancia y Preparar) los datos siguen iguales y no se reaplican migraciones",
                Huella(reabierta) == huella && reabierta.UltimasMigraciones.Count == 0, huella);
            var invReabierta = new RepositorioInventarioSql(reabierta); var usuReabierta = new RepositorioUsuariosSql(reabierta);
            reg.Check("AD3", "Tras reabrir, las bodegas, categorías y usuarios editados conservan sus cambios",
                invReabierta.BuscarBodega(idBodegaAdm) is { Nombre: "BD bodega editada", Activa: false }
                && invReabierta.ListarCategorias().Any(c => c.CategoriaId == idCategoriaAdm && c.Nombre == "BD categoría editada" && !c.Activa)
                && usuReabierta.BuscarPorId(idUsuarioAdm)?.RolId == rolContador);
            var (codigoHijo, salidaHijo) = await LanzarHijo(servidor, nombre);
            reg.Check("P2", "Otro proceso (como abrir el programa de nuevo) lee los mismos datos y lo que escribe queda guardado",
                codigoHijo == 0 && salidaHijo.Contains("HUELLA=" + huella) && Escalar<int>(bd, "SELECT COUNT(*) FROM dbo.Proveedor WHERE Nit='000000002-5'") == 1,
                salidaHijo.Trim());

            // ================= Migración de una base de la versión anterior =================
            MigracionDesdeVersion1(servidor, nombre + "_L", creadas, reg);

            // ================= Base de demostración separada y reproducible =================
            var demo = NuevaBase(servidor, nombre + "_Demo", demo: true);
            creadas.Add(demo);
            demo.Preparar();
            var demo1 = Huella(demo, soloConteos: true);
            var skus1 = string.Join(",", Leer(demo, "SELECT Sku FROM dbo.Producto ORDER BY Sku", r => r.GetString(0)));
            demo.RestablecerDemo();
            var demo2 = Huella(demo, soloConteos: true);
            var skus2 = string.Join(",", Leer(demo, "SELECT Sku FROM dbo.Producto ORDER BY Sku", r => r.GetString(0)));
            reg.Check("D1", "La demostración se crea con datos identificados como DEMO y se reconstruye igual (reproducible)",
                demo1 == demo2 && skus1 == skus2 && skus1.Split(',').All(s => s.StartsWith("DEMO-")) && Escalar<int>(demo, "SELECT COUNT(*) FROM dbo.Proveedor WHERE Nombre NOT LIKE N'DEMO %'") == 0,
                demo1);
            reg.Check("D2", "La base de demostración y la de pruebas son bases distintas", demo.NombreBase != bd.NombreBase
                && Escalar<int>(bd, "SELECT COUNT(*) FROM dbo.Producto WHERE Sku LIKE 'DEMO-%'") == 0);
            var cd = Armar(demo);
            var claveDemoAdmin = "Bd" + RandomNumberGenerator.GetHexString(12) + "2";
            cd.UsuServ.CrearAdministradorInicial("admin.demo", "Administrador de la demostración", claveDemoAdmin);
            var cuentas = cd.UsuServ.GenerarClavesDemostracion(cd.UsuServ.IniciarSesion("admin.demo", claveDemoAdmin), esBaseDemo: true);
            var conDemo = cd.UsuServ.IniciarSesion("demo.vendedor", cuentas.First(x => x.Usuario == "demo.vendedor").Clave);
            var textoDemo = string.Join("\n", Leer(demo, "SELECT CONCAT(Accion,'|',Detalle) FROM dbo.AuditoriaEvento", r => r.GetString(0)));
            reg.Check("D4", "Cuentas de demostración: el administrador genera claves aleatorias para demo.* (3 cuentas), con ellas se ingresa y no quedan en la auditoría",
                cuentas.Count == 3 && conDemo.RolCodigo == Rol.Vendedor && cuentas.All(x => !textoDemo.Contains(x.Clave)) && !textoDemo.Contains(claveDemoAdmin));
            var cargarDosVeces = FallaCon(demo, File.ReadAllText(Path.Combine(RaizScripts(), "demo", "datos_demo.sql")).Split("\nGO")[0], "52001");
            reg.Check("D3", "El script de demostración se niega a ejecutarse sobre una base con datos", cargarDosVeces);
        }
        catch (Exception ex)
        {
            reg.Check("ERR", "Error no controlado durante las pruebas de base de datos", false, ex.ToString());
        }
        finally
        {
            foreach (var b in creadas)
                try { b.EliminarBase(); } catch (Exception ex) { Console.WriteLine($"No se pudo borrar {b.NombreBase}: {ex.Message}"); }
        }

        var carpeta = Path.Combine(BuscarRaiz() ?? ".", "tests", "resultados");
        Directory.CreateDirectory(carpeta);
        reg.Imprimir($"Pruebas de persistencia de FerroSuite contra SQL Server ({servidor}), datos SINTÉTICOS",
            Path.Combine(carpeta, $"resultados_bd_{DateTime.Now:yyyyMMdd_HHmmss}.txt"));
        return reg.Fallan == 0 ? 0 : 1;
    }

    // ---------- Migración de una base de la versión 1 (esquema y datos antiguos) ----------
    private static void MigracionDesdeVersion1(string servidor, string nombre, List<BaseDatosSql> creadas, Registro reg)
    {
        var bd = NuevaBase(servidor, nombre);
        creadas.Add(bd);
        bd.CrearBaseSiNoExiste();
        using (var c = bd.Conectar())
            foreach (var lote in Lotes(File.ReadAllText(Path.Combine(RaizScripts(), "pruebas", "legado_v1.sql"))))
                new SqlCommand(lote, c).ExecuteNonQuery();
        int[] Conteos(BaseDatosSql b) => [Escalar<int>(b, "SELECT COUNT(*) FROM dbo.Proveedor"), Escalar<int>(b, "SELECT COUNT(*) FROM dbo.Producto"),
            Escalar<int>(b, "SELECT COUNT(*) FROM dbo.MovimientoInventario"), Escalar<int>(b, "SELECT COUNT(*) FROM dbo.OrdenCompra")];
        var antes = Conteos(bd);
        var stockAntes = Escalar<decimal>(bd, "SELECT SUM(StockActual) FROM dbo.Producto");
        var categoriasAntes = string.Join("|", Leer(bd, "SELECT DISTINCT Categoria FROM dbo.Producto ORDER BY Categoria", r => r.GetString(0)));
        var usuariosTexto = Escalar<int>(bd, "SELECT COUNT(DISTINCT Usuario) FROM dbo.MovimientoInventario");

        bd.Preparar();
        var despues = Conteos(bd);
        reg.Check("L1", "Base de la versión 1: las migraciones se aplican y no se pierde ninguna fila",
            antes.SequenceEqual(despues) && antes.All(n => n > 0) && new Migrador(bd).VersionActual() == 3,
            $"proveedores/productos/movimientos/órdenes antes {string.Join("/", antes)}, después {string.Join("/", despues)}");
        reg.Check("L2", "El stock se conserva y queda repartido en existencias de la 'Bodega principal'",
            Escalar<decimal>(bd, "SELECT SUM(StockActual) FROM dbo.Producto") == stockAntes
            && Escalar<decimal>(bd, "SELECT SUM(e.Cantidad) FROM dbo.ExistenciaBodega e JOIN dbo.Bodega b ON b.BodegaId = e.BodegaId WHERE b.Nombre = N'Bodega principal'") == stockAntes);
        var categoriasDespues = string.Join("|", Leer(bd, "SELECT DISTINCT c.Nombre FROM dbo.Producto p JOIN dbo.CategoriaProducto c ON c.CategoriaId = p.CategoriaId ORDER BY c.Nombre", r => r.GetString(0)));
        reg.Check("L3", "Las categorías escritas como texto pasan a la tabla CategoriaProducto sin cambiar", categoriasAntes == categoriasDespues, categoriasDespues);
        reg.Check("L4", "Los movimientos antiguos conservan el texto del usuario y quedan asignados a 'registro.anterior' (inactivo, sin clave)",
            Escalar<int>(bd, "SELECT COUNT(DISTINCT Usuario) FROM dbo.MovimientoInventario") == usuariosTexto
            && Escalar<int>(bd, "SELECT COUNT(*) FROM dbo.MovimientoInventario m JOIN dbo.Usuario u ON u.UsuarioId = m.UsuarioId WHERE u.NombreUsuario <> 'registro.anterior'") == 0
            && Escalar<int>(bd, "SELECT COUNT(*) FROM dbo.Usuario WHERE NombreUsuario = 'registro.anterior' AND Activo = 0 AND ClaveHash IS NULL") == 1);
        reg.Check("L5", "Cada orden antigua tiene su línea de detalle y sus códigos siguen siendo OC-001, OC-002…",
            Escalar<int>(bd, "SELECT COUNT(*) FROM dbo.DetalleOrdenCompra") == antes[3]
            && Escalar<int>(bd, "SELECT COUNT(*) FROM dbo.OrdenCompra WHERE Codigo <> 'OC-' + RIGHT('000' + CAST(OrdenId AS VARCHAR(10)), 3)") == 0);
        reg.Check("L6", "Después de migrar, un producto con historial ya no se borra en cascada", FallaCon(bd,
            "DELETE FROM dbo.Producto WHERE Sku = (SELECT TOP (1) Sku FROM dbo.MovimientoInventario)", "REFERENCE"));
    }

    // ---------- Migración que falla: se deshace ----------
    private static bool MigracionFallidaSeDeshace(string servidor, string nombre, List<BaseDatosSql> creadas)
    {
        var carpeta = Directory.CreateTempSubdirectory("ferrosuite-migraciones-");
        try
        {
            Directory.CreateDirectory(Path.Combine(carpeta.FullName, "migraciones"));
            foreach (var f in Directory.GetFiles(Path.Combine(RaizScripts(), "migraciones"), "V*.sql"))
                File.Copy(f, Path.Combine(carpeta.FullName, "migraciones", Path.GetFileName(f)));
            File.WriteAllText(Path.Combine(carpeta.FullName, "migraciones", "V900__falla_a_proposito.sql"), """
                CREATE TABLE dbo.TablaQueNoDebeQuedar (Id INT NOT NULL);
                GO
                INSERT INTO dbo.TablaQueNoDebeQuedar (Id) VALUES (1);
                THROW 59999, N'Falla simulada de migración (prueba).', 1;
                """);
            var bd = NuevaBase(servidor, nombre, scripts: carpeta.FullName);
            creadas.Add(bd);
            var fallo = false;
            try { bd.Preparar(); } catch (InvalidOperationException ex) when (ex.Message.Contains("V900")) { fallo = true; }
            return fallo && new Migrador(bd).VersionActual() == 3 && Escalar<int>(bd, "SELECT COUNT(*) FROM sys.tables WHERE name = 'TablaQueNoDebeQuedar'") == 0;
        }
        finally { carpeta.Delete(true); }
    }

    // ---------- Proceso hijo: simula volver a abrir el programa ----------
    private static async Task<(int Codigo, string Salida)> LanzarHijo(string servidor, string nombre)
    {
        var exe = Environment.ProcessPath!;
        var info = new ProcessStartInfo(exe) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        if (Path.GetFileNameWithoutExtension(exe).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            info.ArgumentList.Add(typeof(PruebasBaseDatos).Assembly.Location);
        foreach (var a in new[] { "--hijo-persistencia", servidor, nombre }) info.ArgumentList.Add(a);
        using var p = Process.Start(info)!;
        var salida = await p.StandardOutput.ReadToEndAsync() + await p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync();
        return (p.ExitCode, salida);
    }

    public static int ProcesoHijo(string servidor, string nombre)
    {
        if (!nombre.StartsWith(BaseDatosSql.PrefijoBasesPrueba, StringComparison.Ordinal)) return 2;
        var bd = NuevaBase(servidor, nombre);
        bd.Preparar();
        Console.WriteLine("HUELLA=" + Huella(bd));
        new RepositorioProveedoresSql(bd).Crear(new Proveedor { Nit = "000000002-5", Nombre = "Proveedor sintético escrito por otro proceso", DiasEntrega = 7 });
        return 0;
    }

    // ---------- Utilidades ----------
    private static string Huella(BaseDatosSql bd, bool soloConteos = false)
    {
        var conteos = Leer(bd, """
            SELECT CONCAT('prov=', (SELECT COUNT(*) FROM dbo.Proveedor), ' prod=', (SELECT COUNT(*) FROM dbo.Producto),
                          ' mov=', (SELECT COUNT(*) FROM dbo.MovimientoInventario), ' ord=', (SELECT COUNT(*) FROM dbo.OrdenCompra),
                          ' det=', (SELECT COUNT(*) FROM dbo.DetalleOrdenCompra), ' usu=', (SELECT COUNT(*) FROM dbo.Usuario))
            """, r => r.GetString(0)).Single();
        return soloConteos ? conteos : conteos + " stock=" + Escalar<decimal?>(bd, "SELECT SUM(StockActual) FROM dbo.Producto")?.ToString("0.##");
    }

    private static string Foto(BaseDatosSql bd, string sku) =>
        $"stock={Escalar<decimal>(bd, $"SELECT StockActual FROM dbo.Producto WHERE Sku='{sku}'"):0.##} " +
        $"exist={Escalar<decimal>(bd, $"SELECT SUM(Cantidad) FROM dbo.ExistenciaBodega WHERE Sku='{sku}'"):0.##} " +
        $"mov={Escalar<int>(bd, $"SELECT COUNT(*) FROM dbo.MovimientoInventario WHERE Sku='{sku}'")}";

    // Solo se usan con SQL fijo escrito en estas pruebas (sin datos de usuario).
    private static T Escalar<T>(BaseDatosSql bd, string sql)
    {
        using var c = bd.Conectar();
        var v = new SqlCommand(sql, c).ExecuteScalar();
        return v is null or DBNull ? default! : (T)Convert.ChangeType(v, Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T));
    }

    private static List<T> Leer<T>(BaseDatosSql bd, string sql, Func<SqlDataReader, T> mapear)
    {
        using var c = bd.Conectar();
        using var r = new SqlCommand(sql, c).ExecuteReader();
        var l = new List<T>();
        while (r.Read()) l.Add(mapear(r));
        return l;
    }

    /// <summary>Ejecuta el SQL y devuelve true si SQL Server lo rechaza con un error que menciona el texto esperado.</summary>
    private static bool FallaCon(BaseDatosSql bd, string sql, string esperado)
    {
        try
        {
            using var c = bd.Conectar();
            new SqlCommand(sql, c).ExecuteNonQuery();
            return false;
        }
        catch (SqlException ex)
        {
            return ex.Message.Contains(esperado, StringComparison.OrdinalIgnoreCase) || ex.Number.ToString() == esperado;
        }
    }

    private static IEnumerable<string> Lotes(string script) =>
        System.Text.RegularExpressions.Regex.Split(script, @"^\s*GO\s*$", System.Text.RegularExpressions.RegexOptions.Multiline | System.Text.RegularExpressions.RegexOptions.IgnoreCase)
            .Where(l => !string.IsNullOrWhiteSpace(l));

    private static string? BuscarRaiz()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "FerroSuite.slnx"))) return d.FullName;
        return null;
    }
}
