// PRUEBAS DE AUTENTICACIÓN, AUTORIZACIÓN Y AUDITORÍA (backend real sobre memoria, datos SINTÉTICOS).
// Los mensajes se envían directamente al Puente, igual que si alguien los mandara por WebView2 sin usar los botones.
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using FerroSuite.Infraestructura.Seguridad;
using FerroSuite.Presentacion.Comunicacion;
using static FerroSuite.Pruebas.Herramientas.Entorno;

namespace FerroSuite.Pruebas.Herramientas;

public static class PruebasSeguridad
{
    /// <summary>
    /// Matriz ESPERADA de acciones por rol, escrita aparte de la matriz del programa (Permisos.cs) a propósito:
    /// si alguien cambia los permisos o agrega una acción sin revisar esta tabla, la prueba falla.
    /// </summary>
    private static readonly Dictionary<string, HashSet<string>> Esperado = new()
    {
        [Rol.Bodega] = ["sesion.cambiarClave", "ia.estado", "categorias.listar", "bodegas.listar", "productos.listar", "movimientos.listar",
                        "movimientos.registrar", "resumen", "proveedores.listar", "ordenes.listar", "ordenes.crear", "ordenes.recibir",
                        "ia.reposicion", "ia.preguntar", "ia.recordatorios", "pronosticos.reposicion", "pronosticos.producto"],
        [Rol.Vendedor] = ["sesion.cambiarClave", "ia.estado", "categorias.listar", "bodegas.listar", "productos.listar", "movimientos.registrar", "resumen"],
        [Rol.Contador] = ["sesion.cambiarClave", "ia.estado", "categorias.listar", "bodegas.listar", "productos.listar", "movimientos.listar", "resumen",
                          "proveedores.listar", "ordenes.listar", "ordenes.pagar", "ia.recordatorios"],
    };

    private static readonly string[] TodasLasAcciones =
    [
        "sesion.cambiarClave", "ia.estado", "categorias.listar", "bodegas.listar", "bodegas.crear", "productos.listar", "productos.crear",
        "productos.actualizar", "productos.eliminar", "movimientos.listar", "movimientos.registrar", "resumen", "proveedores.listar",
        "proveedores.crear", "proveedores.actualizar", "proveedores.activar", "proveedores.desactivar", "ordenes.listar", "ordenes.crear",
        "ordenes.aprobar", "ordenes.rechazar", "ordenes.recibir", "ordenes.pagar", "ia.reposicion", "ia.preguntar", "ia.recordatorios",
        "pronosticos.reposicion", "pronosticos.producto",
        "roles.listar", "usuarios.listar", "usuarios.crear", "usuarios.activar", "usuarios.desactivar", "usuarios.restablecerClave",
        "usuarios.desbloquear", "auditoria.listar", "datos.restablecer",
        "bodegas.listarTodas", "bodegas.actualizar", "categorias.listarTodas", "categorias.crear", "categorias.actualizar", "usuarios.actualizar",
    ];

    public static async Task Correr(Registro reg)
    {
        void Check(string id, string nombre, bool cond, string detalle = "") => reg.Check(id, nombre, cond, detalle);

        // ================= Hash de claves (ASP.NET Core Identity PasswordHasher) =================
        {
            var clave = Entorno.ClaveAleatoria();
            var h1 = HasherRapido.Calcular(clave);
            var h2 = HasherRapido.Calcular(clave);
            Check("H1", "Hash con PasswordHasher de Identity (formato V3, con sal: dos hash de la misma clave son distintos) y verificación",
                h1.StartsWith("AQAAAA") && h1 != h2 && HasherRapido.Verificar(h1, clave) == ResultadoClave.Correcta
                && HasherRapido.Verificar(h1, clave + "x") == ResultadoClave.Incorrecta);
            var produccion = new HasherIdentity();
            var bytes = Convert.FromBase64String(produccion.Calcular(clave));
            uint Leer(int i) => (uint)(bytes[i] << 24 | bytes[i + 1] << 16 | bytes[i + 2] << 8 | bytes[i + 3]);
            Check("H2", "Parámetros de producción: PBKDF2-HMAC-SHA512, 210.000 iteraciones, sal de 16 bytes, subclave de 32 bytes",
                bytes[0] == 0x01 && Leer(1) == 2 && Leer(5) == 210_000 && Leer(9) == 16 && bytes.Length == 13 + 16 + 32,
                $"prf={Leer(1)} iteraciones={Leer(5)} sal={Leer(9)}");
            Check("H3", "Un hash con menos iteraciones que las vigentes se marca para recalcular", new HasherIdentity(20_000).Verificar(h1, clave) == ResultadoClave.CorrectaDebeActualizarse);
            var sal = RandomNumberGenerator.GetBytes(16);
            var anterior = HasherIdentity.FormatoAnterior(Rfc2898DeriveBytes.Pbkdf2(clave, sal, 100_000, HashAlgorithmName.SHA256, 32), sal, 100_000);
            Check("H4", "Las claves de la versión 2 se verifican una vez y piden conversión; un hash dañado nunca da acceso",
                HasherRapido.Verificar(anterior, clave) == ResultadoClave.CorrectaDebeActualizarse && HasherRapido.Verificar(anterior, "otra") == ResultadoClave.Incorrecta
                && HasherRapido.Verificar("no-es-base64-%%%", clave) == ResultadoClave.Incorrecta && HasherRapido.Verificar("", clave) == ResultadoClave.Incorrecta);

            var e = Entorno.Nuevo();
            var u = e.A.U.First(x => x.NombreUsuario == "bodega.prueba");
            u.ClaveHash = HasherIdentity.FormatoAnterior(Rfc2898DeriveBytes.Pbkdf2(Claves["bodega.prueba"], sal, 100_000, HashAlgorithmName.SHA256, 32), sal, 100_000);
            var r = await e.Ingresar("bodega.prueba");
            Check("H5", "Al ingresar con una clave de la versión 2, el hash se convierte al formato de Identity y queda en la auditoría",
                Entorno.Ok(r) && u.ClaveHash.StartsWith("AQAAAA") && e.A.A.Any(x => x.Accion == "usuarios.actualizarHash"));
        }

        // ================= Matriz de permisos: cada rol × cada acción, enviada directamente al Puente =================
        {
            var acciones = Puente.PermisoPorAccion.Keys.ToHashSet();
            Check("MZ0", "Toda acción del Puente está revisada en la matriz esperada de las pruebas (y no hay acciones sin permiso definido)",
                acciones.SetEquals(TodasLasAcciones), string.Join(", ", acciones.Except(TodasLasAcciones).Concat(TodasLasAcciones.Except(acciones))));

            foreach (var (usuario, rol) in new[] { ("admin.prueba", Rol.Administrador), ("bodega.prueba", Rol.Bodega), ("vendedor.prueba", Rol.Vendedor), ("contador.prueba", Rol.Contador) })
            {
                var e = Entorno.Nuevo();
                await e.Ingresar(usuario);
                var errores = new List<string>();
                foreach (var accion in TodasLasAcciones)
                {
                    var permitido = rol == Rol.Administrador || Esperado[rol].Contains(accion);
                    // Datos vacíos: si el rol tiene permiso, la acción falla por validación (no por permiso) y no cambia nada.
                    var r = await e.Pedir(accion, new { });
                    if (permitido && Entorno.Denegado(r)) errores.Add($"{accion}: denegada y debía permitirse");
                    if (!permitido && !Entorno.Denegado(r)) errores.Add($"{accion}: NO se denegó ({r["mensaje"]})");
                    if (r["sinSesion"]?.GetValue<bool>() == true) { await e.Ingresar(usuario); }
                }
                var denegadas = TodasLasAcciones.Count(a => rol != Rol.Administrador && !Esperado[rol].Contains(a));
                var auditadas = e.A.A.Count(x => x.Resultado == ResultadosAuditoria.Denegado && x.RolCodigo == rol);
                Check($"MZ-{rol}", $"Rol {rol}: ejecuta solo sus {(rol == Rol.Administrador ? TodasLasAcciones.Length : Esperado[rol].Count)} acciones; las otras {denegadas} se rechazan en el backend y quedan auditadas",
                    errores.Count == 0 && auditadas >= denegadas, errores.Count > 0 ? string.Join(" || ", errores) : $"{auditadas} denegaciones auditadas");
            }

            var p = Entorno.Nuevo();
            var sin = new List<string>();
            foreach (var accion in TodasLasAcciones)
                if ((await p.Pedir(accion, new { }))["sinSesion"]?.GetValue<bool>() != true) sin.Add(accion);
            Check("MZ-SIN", "Sin sesión, ninguna acción protegida se ejecuta (todas piden ingresar)", sin.Count == 0, string.Join(", ", sin));
        }

        // ================= Defensa en profundidad: los servicios también exigen permiso =================
        {
            var e = Entorno.Nuevo();
            var vendedor = e.SesionDe("vendedor.prueba");
            var bodega = e.SesionDe("bodega.prueba");
            bool Rechaza(Action a) { try { a(); return false; } catch (ErrorAutorizacion) { return true; } }
            var ejecutado = false;
            Check("D1", "Llamando a los servicios sin pasar por el Puente: eliminar productos, aprobar compras, crear usuarios y cambiar configuración se rechazan sin el rol",
                Rechaza(() => e.Inv.EliminarProducto("SIN-001", vendedor)) && Rechaza(() => e.Ord.Aprobar("OC-001", bodega))
                && Rechaza(() => e.Usuarios.Crear(bodega, "otro.usuario", "Otro", Rol.Administrador, Entorno.ClaveAleatoria()))
                && Rechaza(() => e.Adm.CambiarConfiguracion(vendedor, "configuracion.prueba", "Prueba", () => ejecutado = true))
                && Rechaza(() => e.Inv.CrearProducto(new Producto { Sku = "SIN-099" }, null)) && !ejecutado
                && e.A.P.Any(x => x.Sku == "SIN-001" && x.Activo) && e.A.U.All(u => u.NombreUsuario != "otro.usuario"));
            var admin = e.SesionDe("admin.prueba");
            e.Adm.CambiarConfiguracion(admin, "configuracion.claveGemini", "Clave de Gemini reemplazada", () => ejecutado = true);
            var ev = e.A.A.Last();
            Check("D2", "El administrador sí cambia la configuración y queda en la auditoría (sin el valor secreto)",
                ejecutado && ev.Accion == "configuracion.claveGemini" && ev.Resultado == ResultadosAuditoria.Exitoso && ev.Detalle == "Clave de Gemini reemplazada");
        }

        // ================= Ingreso, bloqueo y sesión =================
        {
            var e = Entorno.Nuevo();
            var r1 = await e.Pedir("sesion.iniciar", new { usuario = "admin.prueba", clave = "Clave-equivocada-1" });
            var r2 = await e.Pedir("sesion.iniciar", new { usuario = "no.existe", clave = "Clave-equivocada-1" });
            e.A.U.First(x => x.NombreUsuario == "contador.prueba").Activo = false;
            var r3 = await e.Pedir("sesion.iniciar", new { usuario = "contador.prueba", clave = Claves["contador.prueba"] });
            Check("A1", "Clave incorrecta, usuario inexistente y usuario inactivo dan exactamente el mismo mensaje",
                !Entorno.Ok(r1) && Entorno.Errores(r1) == Entorno.Errores(r2) && Entorno.Errores(r2) == Entorno.Errores(r3));
            var larga = await e.Pedir("sesion.iniciar", new { usuario = new string('a', 500), clave = new string('b', 5000) });
            Check("A2", "Datos de ingreso desmedidos se rechazan sin error interno", !Entorno.Ok(larga) && !Entorno.Errores(larga).Contains("inesperado"));

            for (var i = 0; i < UsuarioServicio.MaximoIntentos; i++) await e.Pedir("sesion.iniciar", new { usuario = "vendedor.prueba", clave = "Clave-equivocada-" + i });
            var bloqueada = await e.Ingresar("vendedor.prueba");
            Check("A3", "Después de 5 intentos fallidos la cuenta se bloquea: ni con la clave correcta entra, y el bloqueo queda auditado",
                !Entorno.Ok(bloqueada) && e.A.U.First(x => x.NombreUsuario == "vendedor.prueba").BloqueadoHasta is not null
                && e.A.A.Any(x => x.Accion == "usuarios.bloqueo") && e.A.A.Any(x => x.Accion == "sesion.iniciar" && x.Resultado == ResultadosAuditoria.Denegado));
            e.Reloj.Avanzar(TimeSpan.FromMinutes(16));
            Check("A4", "Pasados 15 minutos, la clave correcta vuelve a funcionar", Entorno.Ok(await e.Ingresar("vendedor.prueba")));

            await e.Ingresar("admin.prueba");
            e.Reloj.Avanzar(TimeSpan.FromMinutes(25));
            var sigue = await e.Pedir("resumen");
            e.Reloj.Avanzar(TimeSpan.FromMinutes(25));
            var sigue2 = await e.Pedir("resumen");
            e.Reloj.Avanzar(TimeSpan.FromMinutes(31));
            var vencida = await e.Pedir("resumen");
            Check("A5", "La sesión vence tras 30 minutos sin actividad (la actividad la mantiene) y el vencimiento se audita",
                Entorno.Ok(sigue) && Entorno.Ok(sigue2) && !Entorno.Ok(vencida) && vencida["sinSesion"]?.GetValue<bool>() == true && e.A.A.Any(x => x.Accion == "sesion.vencida"));
            await e.Ingresar("admin.prueba");
            for (var i = 0; i < 26; i++) { e.Reloj.Avanzar(TimeSpan.FromMinutes(28)); await e.Pedir("resumen"); }
            Check("A6", "La sesión vence a las 12 horas aunque haya actividad", (await e.Pedir("resumen"))["sinSesion"]?.GetValue<bool>() == true);

            var actual = await e.Ingresar("contador.prueba".Replace("contador", "bodega"));
            var permisos = actual["datos"]!["permisos"]!.AsArray().Select(x => x!.ToString()).ToHashSet();
            Check("A7", "El ingreso devuelve los permisos del rol (para la pantalla) y nada de la clave ni del identificador de sesión",
                permisos.SetEquals(Permisos.DelRol(Rol.Bodega)) && !actual.ToJsonString().Contains("claveHash", StringComparison.OrdinalIgnoreCase)
                && !actual.ToJsonString().Contains(Claves["bodega.prueba"])
                && actual["datos"]!["sesionId"] is null);
            await e.Ingresar("admin.prueba");
            Check("A8", "Un nuevo ingreso reemplaza la sesión anterior y lo registra", e.Sesiones.Actual()?.NombreUsuario == "admin.prueba"
                && e.A.A.Any(x => x.Accion == "sesion.cerrar" && x.NombreUsuario == "bodega.prueba"));
            await e.Pedir("sesion.cerrar");
            Check("A9", "Cerrar sesión: el backend vuelve a pedir ingresar", (await e.Pedir("resumen"))["sinSesion"]?.GetValue<bool>() == true);
        }

        // ================= Mínimo privilegio en los datos y en los movimientos =================
        {
            var e = Entorno.Nuevo();
            await e.Ingresar("vendedor.prueba");
            var venta = await e.Pedir("movimientos.registrar", new { sku = "SIN-001", tipo = "Salida", cantidad = 1, motivo = "Venta", bodegaId = 1 });
            var devolucion = await e.Pedir("movimientos.registrar", new { sku = "SIN-001", tipo = "Entrada", cantidad = 1, motivo = "Devolución", bodegaId = 1 });
            var ajuste = await e.Pedir("movimientos.registrar", new { sku = "SIN-001", tipo = "Salida", cantidad = 50, motivo = "Ajuste por conteo", bodegaId = 1 });
            var dano = await e.Pedir("movimientos.registrar", new { sku = "SIN-001", tipo = "Salida", cantidad = 50, motivo = "Daño o pérdida", bodegaId = 1 });
            Check("L1", "El vendedor registra ventas y devoluciones, pero no ajustes ni bajas por daño", Entorno.Ok(venta) && Entorno.Ok(devolucion) && Entorno.Denegado(ajuste) && Entorno.Denegado(dano)
                && e.A.P.First(x => x.Sku == "SIN-001").StockActual == 100);
            var lista = (await e.Pedir("productos.listar"))["datos"]!.AsArray();
            var resumen = (await e.Pedir("resumen"))["datos"]!;
            Check("L2", "El vendedor no recibe costos: sin precio de compra, valor en bodega ni cuentas por pagar",
                lista.All(x => x!["precioCompra"] is null) && resumen["valorBodega"] is null && resumen["porPagar"] is null);
            await e.Ingresar("bodega.prueba");
            var ventaBodega = await e.Pedir("movimientos.registrar", new { sku = "SIN-001", tipo = "Salida", cantidad = 1, motivo = "Venta", bodegaId = 1 });
            var ajusteBodega = await e.Pedir("movimientos.registrar", new { sku = "SIN-001", tipo = "Salida", cantidad = 1, motivo = "Ajuste por conteo", bodegaId = 1 });
            var inicial = await e.Pedir("movimientos.registrar", new { sku = "SIN-001", tipo = "Entrada", cantidad = 1, motivo = "Inventario inicial", bodegaId = 1 });
            Check("L3", "El encargado de bodega registra ajustes pero no ventas; nadie registra 'Inventario inicial' a mano", Entorno.Denegado(ventaBodega) && Entorno.Ok(ajusteBodega) && !Entorno.Ok(inicial));
            await e.Ingresar("contador.prueba");
            resumen = (await e.Pedir("resumen"))["datos"]!;
            lista = (await e.Pedir("productos.listar"))["datos"]!.AsArray();
            Check("L4", "El contador sí ve valores y costos, pero no puede mover inventario",
                resumen["valorBodega"] is not null && lista.Any(x => x!["precioCompra"] is not null)
                && Entorno.Denegado(await e.Pedir("movimientos.registrar", new { sku = "SIN-001", tipo = "Salida", cantidad = 1, motivo = "Venta", bodegaId = 1 })));
        }

        // ================= Usuarios: creación, clave inicial, restablecimiento =================
        {
            var e = Entorno.Nuevo();
            await e.Ingresar("admin.prueba");
            var corta = await e.Pedir("usuarios.crear", new { nombreUsuario = "nuevo.usuario", nombreCompleto = "Nuevo sintético", rolCodigo = Rol.Bodega, clave = "Corta123" });
            var conNombre = await e.Pedir("usuarios.crear", new { nombreUsuario = "nuevo.usuario", nombreCompleto = "Nuevo sintético", rolCodigo = Rol.Bodega, clave = "xnuevo.usuario9" });
            var reservado = await e.Pedir("usuarios.crear", new { nombreUsuario = "demo.falso", nombreCompleto = "X", rolCodigo = Rol.Bodega, clave = Entorno.ClaveAleatoria() });
            var rolFalso = await e.Pedir("usuarios.crear", new { nombreUsuario = "otro.usuario", nombreCompleto = "X", rolCodigo = "SUPERADMIN", clave = Entorno.ClaveAleatoria() });
            Check("U1", "Política de claves y validaciones: mínimo 10 caracteres, sin contener el usuario; nombres reservados y roles inexistentes se rechazan",
                !Entorno.Ok(corta) && Entorno.Errores(corta).Contains("10") && !Entorno.Ok(conNombre) && Entorno.Errores(conNombre).Contains("nombre de usuario")
                && !Entorno.Ok(reservado) && Entorno.Errores(reservado).Contains("reservado") && !Entorno.Ok(rolFalso) && Entorno.Errores(rolFalso).Contains("rol"));
            var inicialClave = Entorno.ClaveAleatoria();
            var creado = await e.Pedir("usuarios.crear", new { nombreUsuario = "Nuevo.Usuario", nombreCompleto = "Nuevo sintético", rolCodigo = Rol.Vendedor, clave = inicialClave });
            var nuevo = e.A.U.FirstOrDefault(x => x.NombreUsuario == "nuevo.usuario");
            Check("U2", "Crear usuario: clave guardada solo como hash de Identity y obligación de cambiarla", Entorno.Ok(creado) && nuevo is { DebeCambiarClave: true }
                && nuevo.ClaveHash!.StartsWith("AQAAAA") && !nuevo.ClaveHash.Contains(inicialClave));
            var lista = await e.Pedir("usuarios.listar");
            Check("U3", "La lista de usuarios no expone hash, intentos ni el usuario técnico de registros anteriores",
                Entorno.Ok(lista) && !lista.ToJsonString().Contains("AQAAAA") && !lista.ToJsonString().Contains("claveHash", StringComparison.OrdinalIgnoreCase)
                && !lista.ToJsonString().Contains("intentosFallidos", StringComparison.OrdinalIgnoreCase));

            var ingreso = await e.Pedir("sesion.iniciar", new { usuario = "nuevo.usuario", clave = inicialClave });
            var antes = await e.Pedir("productos.listar");
            var malActual = await e.Pedir("sesion.cambiarClave", new { actual = "otra-cosa-123", nueva = Entorno.ClaveAleatoria() });
            var igual = await e.Pedir("sesion.cambiarClave", new { actual = inicialClave, nueva = inicialClave });
            var definitiva = Entorno.ClaveAleatoria();
            var cambio = await e.Pedir("sesion.cambiarClave", new { actual = inicialClave, nueva = definitiva });
            var despues = await e.Pedir("productos.listar");
            Check("U4", "Con la clave inicial solo puede cambiarla: nada más se ejecuta hasta hacerlo; exige la clave actual y una distinta",
                Entorno.Ok(ingreso) && ingreso["datos"]!["debeCambiarClave"]!.GetValue<bool>() && !Entorno.Ok(antes) && Entorno.Errores(antes).Contains("cambiar tu clave")
                && !Entorno.Ok(malActual) && !Entorno.Ok(igual) && Entorno.Ok(cambio) && Entorno.Ok(despues) && !e.A.U.First(x => x.NombreUsuario == "nuevo.usuario").DebeCambiarClave);

            await e.Ingresar("admin.prueba");
            var reset = await e.Pedir("usuarios.restablecerClave", new { usuarioId = nuevo!.UsuarioId });
            var temporal = reset["datos"]?["claveTemporal"]?.ToString() ?? "";
            lock (ClavesUsadas) ClavesUsadas.Add(temporal);
            var vieja = await e.Pedir("sesion.iniciar", new { usuario = "nuevo.usuario", clave = definitiva });
            var conTemporal = await e.Pedir("sesion.iniciar", new { usuario = "nuevo.usuario", clave = temporal });
            Check("U5", "Restablecer clave: genera una temporal aleatoria (se muestra una vez), la anterior deja de servir y obliga a cambiarla",
                Entorno.Ok(reset) && temporal.Length >= 16 && !Entorno.Ok(vieja) && Entorno.Ok(conTemporal) && conTemporal["datos"]!["debeCambiarClave"]!.GetValue<bool>());

            await e.Ingresar("admin.prueba");
            var admin = e.A.U.First(x => x.NombreUsuario == "admin.prueba");
            var propio = await e.Pedir("usuarios.desactivar", new { usuarioId = admin.UsuarioId });
            Check("U6", "El administrador no puede desactivarse a sí mismo (siempre queda al menos uno)", !Entorno.Ok(propio) && admin.Activo);
            for (var i = 0; i < 5; i++) await e.Pedir("sesion.iniciar", new { usuario = "bodega.prueba", clave = "Clave-mala-" + i });
            await e.Ingresar("admin.prueba");
            var bodega = e.A.U.First(x => x.NombreUsuario == "bodega.prueba");
            var desbloqueo = await e.Pedir("usuarios.desbloquear", new { usuarioId = bodega.UsuarioId });
            Check("U7", "El administrador puede desbloquear una cuenta", Entorno.Ok(desbloqueo) && bodega.BloqueadoHasta is null && Entorno.Ok(await e.Ingresar("bodega.prueba")));
            var primera = false;
            try { e.Usuarios.CrearAdministradorInicial("otro.admin", "Otro", Entorno.ClaveAleatoria()); } catch (ErrorValidacion) { primera = true; }
            Check("U8", "El 'administrador inicial' solo se puede crear cuando no existe ninguno", primera && e.A.U.All(u => u.NombreUsuario != "otro.admin"));
        }

        // ================= Cuentas de demostración y reconstrucción de la demo =================
        {
            var real = Entorno.Nuevo();
            real.A.AgregarSinClave("demo.vendedor", 3);
            var admin = real.SesionDe("admin.prueba");
            var enReal = false;
            try { real.Usuarios.GenerarClavesDemostracion(admin, esBaseDemo: false); } catch (ErrorValidacion) { enReal = true; }
            var restablecerReal = await (await Ingresado(real, "admin.prueba")).Pedir("datos.restablecer");
            Check("C1", "En la base real no se generan cuentas de demostración ni se reconstruye nada, ni siquiera el administrador",
                enReal && real.A.U.First(u => u.NombreUsuario == "demo.vendedor").ClaveHash is null && !Entorno.Ok(restablecerReal) && real.Mant.VecesRestablecido == 0);

            var demo = Entorno.Nuevo(demo: true);
            demo.A.AgregarSinClave("demo.vendedor", 3);
            demo.A.AgregarSinClave("demo.bodega", 2);
            var sinPermiso = false;
            try { demo.Usuarios.GenerarClavesDemostracion(demo.SesionDe("bodega.prueba"), true); } catch (ErrorAutorizacion) { sinPermiso = true; }
            var cuentas = demo.Usuarios.GenerarClavesDemostracion(demo.SesionDe("admin.prueba"), true);
            lock (ClavesUsadas) foreach (var c in cuentas) ClavesUsadas.Add(c.Clave);
            var entra = await demo.Pedir("sesion.iniciar", new { usuario = "demo.vendedor", clave = cuentas.First(c => c.Usuario == "demo.vendedor").Clave });
            Check("C2", "En modo Demo, solo el administrador genera claves aleatorias para las cuentas demo.* (procedimiento local) y con ellas se ingresa",
                sinPermiso && cuentas.Count == 2 && cuentas.All(c => c.Clave.Length >= 16) && Entorno.Ok(entra));
            await demo.Ingresar("admin.prueba");
            var reconstruir = await demo.Pedir("datos.restablecer");
            Check("C3", "En modo Demo el administrador reconstruye la base, queda auditado y la sesión se cierra",
                Entorno.Ok(reconstruir) && demo.Mant.VecesRestablecido == 1 && demo.A.A.Any(x => x.Accion == "datos.restablecer" && x.Resultado == ResultadosAuditoria.Exitoso)
                && (await demo.Pedir("resumen"))["sinSesion"]?.GetValue<bool>() == true);
        }

        // ================= Canal de mensajes de WebView2 y errores seguros =================
        {
            var e = Entorno.Nuevo();
            await e.Ingresar("admin.prueba");
            var externo = await e.Pedir("usuarios.crear", new { nombreUsuario = "intruso.web", nombreCompleto = "X", rolCodigo = Rol.Administrador, clave = Entorno.ClaveAleatoria() }, "https://sitio-malicioso.example/");
            var http = await e.Pedir("productos.listar", null, "http://ferrosuite.example/");
            var parecido = await e.Pedir("productos.listar", null, "https://ferrosuite.example.atacante.com/");
            var nulo = await e.Pedir("productos.listar", null, null);
            var propio = await e.Pedir("productos.listar", null, "https://ferrosuite.example/inventario.html");
            Check("W1", "Solo se aceptan mensajes de las pantallas propias (https://ferrosuite.example); otras páginas se rechazan y se auditan, aunque haya sesión de administrador",
                !Entorno.Ok(externo) && !Entorno.Ok(http) && !Entorno.Ok(parecido) && !Entorno.Ok(nulo) && Entorno.Ok(propio) && e.A.U.All(u => u.NombreUsuario != "intruso.web")
                && e.A.A.Count(x => x.Accion == "puente.origen" && x.Resultado == ResultadosAuditoria.Denegado) == 4);
            var grande = await e.P.AtenderAsync(JsonSerializer.Serialize(new { id = 1, accion = "productos.listar", datos = new string('x', 70_000) }), Puente.OrigenPermitido);
            var profundo = await e.P.AtenderAsync("{\"id\":1,\"accion\":\"productos.listar\",\"datos\":" + string.Concat(Enumerable.Repeat("[", 40)) + string.Concat(Enumerable.Repeat("]", 40)) + "}", Puente.OrigenPermitido);
            Check("W2", "Mensajes de más de 64 KB o con anidamiento excesivo se rechazan", grande!.Contains("demasiado grande") && profundo!.Contains("formato incorrecto"));
            var inventada = await e.Pedir("usuarios.borrarTodo");
            var inyectada = await e.Pedir("productos.listar; DROP TABLE dbo.Usuario");
            var noTexto = JsonNode.Parse((await e.P.AtenderAsync("{\"id\":1,\"accion\":{\"x\":1}}", Puente.OrigenPermitido))!)!;
            Check("W3", "Acciones inventadas o con texto extra se rechazan (denegar por defecto), incluso al administrador",
                !Entorno.Ok(inventada) && Entorno.Errores(inventada).Contains("no reconocida") && !Entorno.Ok(inyectada) && !Entorno.Ok(noTexto));
            e.A.FallaInesperada = true;
            var falla = await e.Pedir("categorias.listar");
            e.A.FallaInesperada = false;
            Check("W4", "Un error inesperado muestra solo un número de referencia (sin detalles internos) y queda en la auditoría",
                !Entorno.Ok(falla) && Entorno.Errores(falla).Contains("Referencia") && !falla.ToJsonString().Contains("SECRETO") && !falla.ToJsonString().Contains("Server=")
                && e.A.A.Any(x => x.Accion == "error.inesperado" && !(x.Detalle ?? "").Contains("SECRETO")));
            var raro = await e.Pedir("proveedores.crear", new { nombre = "Prov\u0000 con\u001b control\nfalso", nit = "000000003", diasEntrega = 3 });
            var ev = e.A.A.LastOrDefault(x => x.Accion == "proveedores.crear");
            Check("W5", "La auditoría limpia caracteres de control (nadie puede falsificar líneas del registro)", Entorno.Ok(raro) && ev is not null
                && e.A.A.All(x => !(x.Detalle ?? "").Any(char.IsControl) && !(x.Objeto ?? "").Any(char.IsControl) && !x.NombreUsuario.Any(char.IsControl)));
        }

        // ================= Auditoría de operaciones sensibles, sin secretos =================
        {
            var e = Entorno.Nuevo();
            await e.Ingresar("admin.prueba");
            await e.Pedir("productos.crear", new { sku = "AUD-001", nombre = "Auditado", categoriaId = 1, unidad = "Unidad", stockActual = 0, stockMinimo = 0 });
            await e.Pedir("productos.eliminar", new { sku = "AUD-001" });
            await e.Pedir("ordenes.crear", new { sku = "SIN-001", nitProveedor = "000000001-8", cantidad = 1 });
            await e.Pedir("ordenes.aprobar", new { codigo = "OC-001" });
            await e.Pedir("usuarios.crear", new { nombreUsuario = "auditado.uno", nombreCompleto = "Auditado", rolCodigo = Rol.Contador, clave = Entorno.ClaveAleatoria() });
            var acciones = e.A.A.Where(x => x.Resultado == ResultadosAuditoria.Exitoso).Select(x => x.Accion).ToHashSet();
            var crearUsuario = e.A.A.Last(x => x.Accion == "usuarios.crear");
            Check("AU1", "Crear usuarios, crear y eliminar productos, crear y aprobar compras quedan registrados con quién, cuándo, sesión y equipo",
                new[] { "sesion.iniciar", "productos.crear", "productos.eliminar", "ordenes.crear", "ordenes.aprobar", "usuarios.crear" }.All(acciones.Contains)
                && crearUsuario.NombreUsuario == "admin.prueba" && crearUsuario.SesionId?.Length == 32 && crearUsuario.Equipo == "EQUIPO-PRUEBA" && crearUsuario.Objeto == "Usuario auditado.uno");
            var auditoria = await e.Pedir("auditoria.listar", new { limite = 50 });
            await e.Ingresar("contador.prueba");
            var auditoriaContador = await e.Pedir("auditoria.listar");
            Check("AU2", "Solo el administrador consulta la auditoría", Entorno.Ok(auditoria) && auditoria["datos"]!.AsArray().Count > 0 && Entorno.Denegado(auditoriaContador));
            Check("AU3", "No hay operación que modifique o borre la auditoría (no existe ninguna acción para eso)",
                Puente.PermisoPorAccion.Keys.Where(a => a.StartsWith("auditoria.")).SequenceEqual(["auditoria.listar"]));
        }

        // Esta revisión va al final: recorre TODA la auditoría generada por las pruebas anteriores de este archivo.
        // (Cada entorno tiene su propia auditoría; aquí se repite un recorrido completo en uno nuevo con todas las operaciones de clave.)
        {
            var e = Entorno.Nuevo(demo: true);
            e.A.AgregarSinClave("demo.contador", 4);
            await e.Ingresar("admin.prueba");
            await e.Pedir("sesion.iniciar", new { usuario = "admin.prueba", clave = "Mala-clave-123" });
            await e.Ingresar("admin.prueba");
            var inicial = Entorno.ClaveAleatoria();
            await e.Pedir("usuarios.crear", new { nombreUsuario = "secreto.uno", nombreCompleto = "Secreto", rolCodigo = Rol.Bodega, clave = inicial });
            var id = e.A.U.First(x => x.NombreUsuario == "secreto.uno").UsuarioId;
            var temporal = (await e.Pedir("usuarios.restablecerClave", new { usuarioId = id }))["datos"]!["claveTemporal"]!.ToString();
            await e.Pedir("sesion.iniciar", new { usuario = "secreto.uno", clave = temporal });
            var nueva = Entorno.ClaveAleatoria();
            await e.Pedir("sesion.cambiarClave", new { actual = temporal, nueva });
            await e.Ingresar("admin.prueba");
            var demo = e.Usuarios.GenerarClavesDemostracion(e.Sesiones.Actual(), true);
            e.Adm.CambiarConfiguracion(e.Sesiones.Actual(), "configuracion.claveGemini", "Clave de Gemini reemplazada", () => { });
            var secretos = new List<string> { inicial, temporal, nueva, "Mala-clave-123", Claves["admin.prueba"] };
            secretos.AddRange(demo.Select(d => d.Clave));
            lock (ClavesUsadas) secretos.AddRange(ClavesUsadas);
            var texto = string.Join("\n", e.A.A.Select(x => $"{x.NombreUsuario}|{x.Accion}|{x.Objeto}|{x.Detalle}|{x.SesionId}|{x.Equipo}"));
            var fugas = secretos.Where(s => s.Length > 0 && texto.Contains(s)).ToList();
            Check("AU4", $"Ninguna de las {secretos.Distinct().Count()} claves usadas (iniciales, temporales, nuevas, erróneas, de demostración) ni ningún hash aparece en la auditoría",
                e.A.A.Count > 8 && fugas.Count == 0 && !texto.Contains("AQAAAA"), fugas.Count > 0 ? "FUGA" : $"{e.A.A.Count} eventos revisados");
        }
    }

    private static async Task<Entorno> Ingresado(Entorno e, string usuario)
    {
        await e.Ingresar(usuario);
        return e;
    }
}
