// REVISIÓN INTEGRAL en navegador (Chromium + Playwright) de las pantallas reales con el backend C# real
// (herramienta "FerroSuite.Pruebas servidor": mismo Puente y servicios; datos en memoria y SINTÉTICOS).
// Completa a recorrido.js y pantalla_ia.js con: cierre de sesión, CRUD administrativo por pantalla, entradas y salidas,
// ciclo completo de órdenes, consultas (resumen, historial, auditoría), envíos dobles, fallas de base de datos y de
// comunicación (simuladas en el servidor de prueba), solicitudes fuera del equipo y recursos rotos.
// Uso: servidor en marcha; CLAVE_ADMIN=... CLAVE_BODEGA=... node tests/pantallas/revision.js
const { chromium } = require('playwright');
const BASE = 'http://127.0.0.1:18766/';
const C = { admin: process.env.CLAVE_ADMIN, bodega: process.env.CLAVE_BODEGA };
if (!C.admin || !C.bodega) { console.error('Faltan CLAVE_ADMIN y CLAVE_BODEGA.'); process.exit(2); }
const shim = `window.chrome = window.chrome || {}; (function(){ const ls=[];
  window.chrome.webview = { postMessage(m){ fetch('/__msg',{method:'POST',body:JSON.stringify(m)}).then(r=>r.status===204?null:r.json()).then(d=>{ if(d) ls.forEach(f=>f({data:d})); }).catch(()=>{}); },
  addEventListener(t,f){ if(t==='message') ls.push(f); } }; })();`;
const res = []; const ok = (id, n, c, d = '') => res.push(`${c ? 'PASA ' : 'FALLA'} | ${id} | ${n}${d ? ' — ' + d : ''}`);
const control = (accion) => fetch(BASE + '__control?accion=' + accion, { method: 'POST' }).then((r) => r.status);

(async () => {
  const b = await chromium.launch(); const ctx = await b.newContext({ viewport: { width: 1366, height: 900 } });
  await ctx.addInitScript(shim);
  const page = await ctx.newPage(); const errores = []; const externas = []; const rotas = []; const mensajes = [];
  page.on('pageerror', (e) => errores.push(e.message)); page.on('console', (m) => { if (m.type() === 'error' && !/Failed to load resource.*(503|__msg)/.test(m.text())) errores.push(m.text()); });
  page.on('request', (r) => { const u = new URL(r.url()); if (!['127.0.0.1', 'localhost'].includes(u.hostname) && u.protocol !== 'data:') externas.push(r.url()); });
  page.on('response', (r) => { if (r.status() >= 400 && !r.url().includes('/__')) rotas.push(`${r.status()} ${r.url()}`); });
  page.on('request', (r) => { if (r.url().endsWith('/__msg')) { try { mensajes.push(JSON.parse(r.postData()).accion); } catch { } } });
  page.on('dialog', (d) => d.accept());
  const txt = async (sel) => ((await page.locator(sel).first().textContent().catch(() => '')) || '').replace(/\s+/g, ' ').trim();
  const visible = (sel) => page.locator(sel).first().isVisible().catch(() => false);
  const backend = (accion, datos) => page.evaluate(([a, d]) => new Promise((ok) => {
    const id = 900000 + Math.floor(Math.random() * 99999);
    window.chrome.webview.addEventListener('message', (e) => { if (e.data && e.data.id === id) ok(e.data); });
    window.chrome.webview.postMessage({ id, accion: a, datos: d });
  }), [accion, datos]);
  const ingresar = async (usuario, clave) => {
    await page.goto(BASE + 'login.html'); await page.waitForSelector('#form-login form');
    await page.fill('#form-login [name=usuario]', usuario); await page.fill('#form-login [name=clave]', clave);
    await page.click('#form-login button[type=submit]'); await page.waitForURL('**/index.html', { timeout: 5000 }).catch(() => {});
  };
  const listo = () => page.waitForFunction(() => document.body.classList.contains('permisos-listos'), null, { timeout: 8000 }).catch(() => {});
  const contar = (accion) => mensajes.filter((m) => m === accion).length;

  // ---------- R1-R2: inicio y cierre de sesión ----------
  await ingresar('admin.prueba', C.admin);
  const s1 = await backend('sesion.actual');
  ok('R1', 'Inicio de sesión del administrador: entra a Inicio y el backend tiene la sesión con su rol', page.url().endsWith('index.html') && s1.ok && s1.datos && /admin/i.test(JSON.stringify(s1.datos)), s1.datos && s1.datos.rol);
  await page.click('#btn-salir'); await page.waitForURL('**/login.html', { timeout: 5000 }).catch(() => {});
  const s2 = await backend('sesion.actual');
  await page.goto(BASE + 'ordenes.html'); await page.waitForURL('**/login.html', { timeout: 5000 }).catch(() => {});
  const s3 = await backend('productos.listar');
  ok('R2', 'Cierre de sesión: vuelve a ingreso, el backend ya no tiene sesión, abrir una pantalla pide ingresar y las acciones se rechazan',
    page.url().endsWith('login.html') && s2.ok && s2.datos == null && s3.ok === false && s3.sinSesion === true, s3.mensaje);

  // ---------- R3-R10: CRUD administrativo y registro de productos y proveedores ----------
  await ingresar('admin.prueba', C.admin);
  await page.goto(BASE + 'proveedores.html'); await listo(); await page.waitForSelector('#form-proveedor form');
  const fp = '#form-proveedor';
  await page.fill(`${fp} [name=nombre]`, 'Proveedor Sintético Revisión'); await page.fill(`${fp} [name=nit]`, '000.000.003-2');
  await page.fill(`${fp} [name=telefono]`, '6010000000'); await page.fill(`${fp} [name=diasEntrega]`, '5');
  await page.click(`${fp} button[type=submit]`); await page.waitForTimeout(900);
  let provs = (await backend('proveedores.listar')).datos;
  const provsTotal = async () => (await backend('proveedores.listar')).datos.length;
  const pNuevo = provs.find((p) => p.nit === '000000003-2');
  ok('R3', 'Registrar proveedor desde la pantalla (NIT con puntos se normaliza) y aparece en la tabla',
    !!pNuevo && pNuevo.diasEntrega === 5 && (await txt('#tabla-proveedores')).includes('Proveedor Sintético Revisión'), await txt(`${fp} .form-mensajes`));
  await page.click(`[data-desactivar="000000003-2"]`); await page.waitForTimeout(800);
  const inactivo = (await backend('proveedores.listar')).datos.find((p) => p.nit === '000000003-2');
  await page.click(`[data-activar="000000003-2"]`); await page.waitForTimeout(800);
  const activo = (await backend('proveedores.listar')).datos.find((p) => p.nit === '000000003-2');
  ok('R4', 'Desactivar y volver a activar un proveedor desde la pantalla', inactivo && inactivo.activo === false && activo && activo.activo === true);
  await page.click('[data-editar="000000003-2"]'); await page.waitForTimeout(300);
  const nitBloqueado = await page.locator(`${fp} [name=nit]`).evaluate((e) => e.readOnly);
  await page.fill(`${fp} [name=nombre]`, 'Proveedor Sintético Revisión (editado)'); await page.fill(`${fp} [name=diasEntrega]`, '7');
  await page.click(`${fp} button[type=submit]`); await page.waitForTimeout(900);
  const pEditado = (await backend('proveedores.listar')).datos.find((p) => p.nit === '000000003-2');
  ok('R5', 'Editar proveedor desde la pantalla: cambia nombre y días de entrega; el NIT no se edita',
    nitBloqueado && pEditado.nombre.endsWith('(editado)') && pEditado.diasEntrega === 7 && (await provsTotal()) === provs.length, `${pEditado.nombre} · ${pEditado.diasEntrega} días`);

  await page.goto(BASE + 'inventario.html'); await listo(); await page.waitForSelector('#form-producto form');
  await page.waitForFunction(() => document.querySelectorAll('#form-producto [name=nitProveedor] option').length > 1);
  const fpr = '#form-producto';
  await page.fill(`${fpr} [name=sku]`, 'rev-001'); await page.fill(`${fpr} [name=nombre]`, 'Producto sintético de revisión');
  await page.selectOption(`${fpr} [name=unidad]`, 'Unidad'); await page.fill(`${fpr} [name=stockActual]`, '12');
  await page.fill(`${fpr} [name=stockMinimo]`, '4'); await page.fill(`${fpr} [name=precioCompra]`, '2500');
  await page.selectOption(`${fpr} [name=nitProveedor]`, '000000003-2');
  const antesCrear = contar('productos.crear');
  await page.dblclick(`${fpr} button[type=submit]`); await page.click(`${fpr} button[type=submit]`).catch(() => {});
  await page.waitForTimeout(1200);
  let prods = (await backend('productos.listar')).datos;
  const rev = prods.filter((p) => p.sku === 'REV-001');
  ok('R6', 'Registrar producto desde la pantalla: SKU en mayúsculas, stock inicial y proveedor nuevo', rev.length === 1 && rev[0].stockActual === 12 && rev[0].nitProveedor === '000000003-2');
  ok('R7', 'Doble clic y clic extra en "Guardar producto": un solo producto (el botón se desactiva mientras responde)',
    rev.length === 1, `${contar('productos.crear') - antesCrear} envío(s); segundo envío, si lo hubo, rechazado por SKU repetido`);
  await page.click('[data-editar="REV-001"]'); await page.waitForTimeout(400);
  await page.fill(`${fpr} [name=nombre]`, 'Producto sintético de revisión (editado)'); await page.fill(`${fpr} [name=stockMinimo]`, '6');
  const stockBloqueado = await page.locator(`${fpr} [name=stockActual]`).isDisabled();
  await page.click(`${fpr} button[type=submit]`); await page.waitForTimeout(900);
  const editado = (await backend('productos.listar')).datos.find((p) => p.sku === 'REV-001');
  ok('R8', 'Editar producto desde la pantalla: cambia nombre y mínimo; el stock no se cambia editando', editado.nombre.endsWith('(editado)') && editado.stockMinimo === 6 && editado.stockActual === 12 && stockBloqueado, `campo de stock ${stockBloqueado ? 'bloqueado' : 'editable'} al editar`);

  // ---------- R9-R11: entradas y salidas ----------
  const fm = '#form-movimiento';
  await page.selectOption(`${fm} [name=sku]`, 'REV-001'); await page.selectOption(`${fm} [name=tipo]`, 'Entrada');
  await page.fill(`${fm} [name=cantidad]`, '8'); await page.selectOption(`${fm} [name=motivo]`, 'Compra a proveedor');
  await page.click(`${fm} button[type=submit]`); await page.waitForTimeout(900);
  const trasEntrada = (await backend('productos.listar')).datos.find((p) => p.sku === 'REV-001').stockActual;
  await page.selectOption(`${fm} [name=sku]`, 'REV-001'); await page.selectOption(`${fm} [name=tipo]`, 'Salida');
  await page.fill(`${fm} [name=cantidad]`, '5'); await page.selectOption(`${fm} [name=motivo]`, 'Venta');
  await page.click(`${fm} button[type=submit]`); await page.waitForTimeout(900);
  const trasSalida = (await backend('productos.listar')).datos.find((p) => p.sku === 'REV-001').stockActual;
  const movs = (await backend('movimientos.listar', { limite: 50 })).datos.filter((m) => m.sku === 'REV-001');
  ok('R9', 'Entrada (compra, 8) y salida (venta, 5) desde la pantalla: 12 → 20 → 15 y quedan en el historial con responsable',
    trasEntrada === 20 && trasSalida === 15 && movs.length === 3 && movs.every((m) => m.usuario), `stock ${trasEntrada} → ${trasSalida}; ${movs.length} movimientos`);
  await page.selectOption(`${fm} [name=sku]`, 'REV-001'); await page.selectOption(`${fm} [name=tipo]`, 'Salida');
  await page.fill(`${fm} [name=cantidad]`, '500'); await page.selectOption(`${fm} [name=motivo]`, 'Venta');
  await page.click(`${fm} button[type=submit]`); await page.waitForTimeout(900);
  ok('R10', 'Salida mayor al stock: la pantalla muestra el error y el stock no cambia',
    (await txt(`${fm} .form-mensajes`)).length > 0 && (await backend('productos.listar')).datos.find((p) => p.sku === 'REV-001').stockActual === 15, await txt(`${fm} .form-mensajes`));
  await page.reload(); await listo(); await page.waitForSelector('#tabla-movimientos table', { timeout: 8000 }).catch(() => {});
  ok('R11', 'Consulta del historial de movimientos (reporte): la tabla muestra los movimientos del producto', (await txt('#tabla-movimientos')).includes('Producto sintético de revisión'));

  // ---------- R12-R16: órdenes de compra ----------
  await page.goto(BASE + 'ordenes.html'); await listo(); await page.waitForSelector('#form-orden form');
  await page.waitForFunction(() => document.querySelectorAll('#form-orden [name=sku] option').length > 1);
  const fo = '#form-orden';
  const ordenesAntes = (await backend('ordenes.listar')).datos.length;
  await page.selectOption(`${fo} [name=sku]`, 'REV-001'); await page.selectOption(`${fo} [name=nitProveedor]`, '000000003-2');
  await page.fill(`${fo} [name=cantidad]`, '10');
  await page.dblclick(`${fo} button[type=submit]`); await page.waitForTimeout(1200);
  let ordenes = (await backend('ordenes.listar')).datos;
  const nuevas = ordenes.filter((o) => o.detalles.some((d) => d.sku === 'REV-001'));
  ok('R12', 'Generar orden desde la pantalla (con doble clic): una sola orden, pendiente de aprobación', ordenes.length === ordenesAntes + 1 && nuevas.length === 1 && nuevas[0].estado.startsWith('Pendiente'),
    nuevas.map((o) => o.codigo + ' ' + o.estado).join(', '));
  const cod = nuevas[0].codigo;
  const aprobarAntes = contar('ordenes.aprobar');
  await page.dblclick(`[data-aprobar="${cod}"]`); await page.waitForTimeout(1200);
  const aprobada = (await backend('ordenes.listar')).datos.find((o) => o.codigo === cod);
  const audit = (await backend('auditoria.listar', { limite: 50 })).datos.filter((e) => (e.accion || '').includes('ordenes.aprobar') && JSON.stringify(e).includes(cod) && /xito|Exitoso|ok/i.test(JSON.stringify(e)));
  ok('R13', 'Aprobar orden (con doble clic): el botón queda ocupado, se envía UNA sola solicitud y la orden queda aprobada con fecha de entrega esperada',
    aprobada.estado === 'Aprobada' && !!aprobada.fechaEntregaEsperada && contar('ordenes.aprobar') - aprobarAntes === 1, `${contar('ordenes.aprobar') - aprobarAntes} solicitud(es) de aprobación enviadas; mensaje: ${await txt('#mensajes-ordenes')}`);
  await page.click(`[data-recibir="${cod}"]`); await page.waitForTimeout(1200);
  const recibida = (await backend('ordenes.listar')).datos.find((o) => o.codigo === cod);
  const stockRecibido = (await backend('productos.listar')).datos.find((p) => p.sku === 'REV-001').stockActual;
  ok('R14', 'Recibir la orden aprobada: suma 10 al stock (15 → 25)', recibida.estado === 'Recibida' && stockRecibido === 25, `stock ${stockRecibido}`);
  await page.click(`[data-pagar="${cod}"]`); await page.waitForTimeout(900);
  ok('R15', 'Marcar pagada la orden recibida', (await backend('ordenes.listar')).datos.find((o) => o.codigo === cod).pagada === true);
  await page.selectOption(`${fo} [name=sku]`, 'REV-001'); await page.selectOption(`${fo} [name=nitProveedor]`, '000000003-2');
  await page.fill(`${fo} [name=cantidad]`, '3'); await page.click(`${fo} button[type=submit]`); await page.waitForTimeout(900);
  const cod2 = (await backend('ordenes.listar')).datos.filter((o) => o.detalles.some((d) => d.sku === 'REV-001') && o.estado.startsWith('Pendiente'))[0].codigo;
  await page.click(`[data-rechazar="${cod2}"]`); await page.waitForTimeout(900);
  ok('R16', 'Rechazar una orden pendiente', (await backend('ordenes.listar')).datos.find((o) => o.codigo === cod2).estado === 'Rechazada');

  // ---------- R17-R19: consultas / reportes ----------
  await page.goto(BASE + 'index.html'); await listo(); await page.waitForSelector('#resumen-inicio .tarjeta-valor', { timeout: 8000 }).catch(() => {});
  const resumen = (await backend('resumen')).datos;
  const tarjetas = await page.locator('#resumen-inicio .tarjeta-valor').allTextContents();
  ok('R17', 'Resumen de Inicio: las cifras de la pantalla son las del backend (productos, en riesgo, proveedores, órdenes pendientes)',
    tarjetas.slice(0, 4).map((t) => t.trim()).join('|') === [resumen.productos, resumen.enRiesgo, resumen.proveedores, resumen.ordenesPendientes].join('|'), tarjetas.join('|'));
  await page.goto(BASE + 'usuarios.html'); await listo(); await page.waitForSelector('#tabla-auditoria table', { timeout: 8000 }).catch(() => {});
  const aud = await txt('#tabla-auditoria');
  ok('R18', 'Consulta de auditoría (administrador): muestra la aprobación y la creación de productos de esta revisión, sin claves',
    aud.includes('ordenes.aprobar') && aud.includes('productos.crear') && !aud.includes(C.admin) && !aud.includes(C.bodega));
  await page.goto(BASE + 'ordenes.html'); await listo(); await page.waitForSelector('#resumen-ordenes .tarjeta-valor', { timeout: 8000 }).catch(() => {});
  ok('R19', 'Resumen de órdenes visible', (await page.locator('#resumen-ordenes .tarjeta-valor').count()) > 0, await txt('#resumen-ordenes'));

  // ---------- R20-R22: CRUD de usuarios ----------
  await page.goto(BASE + 'usuarios.html'); await listo(); await page.waitForSelector('#form-usuario form');
  await page.waitForFunction(() => document.querySelectorAll('#form-usuario [name=rolCodigo] option').length > 0);
  const fu = '#form-usuario';
  await page.fill(`${fu} [name=nombreUsuario]`, 'revision.bodega'); await page.fill(`${fu} [name=nombreCompleto]`, 'Usuario sintético de revisión');
  await page.selectOption(`${fu} [name=rolCodigo]`, 'BODEGA'); await page.fill(`${fu} [name=clave]`, 'ClaveRevision' + Math.floor(Math.random() * 1e6));
  await page.click(`${fu} button[type=submit]`); await page.waitForTimeout(900);
  let usuarios = (await backend('usuarios.listar')).datos;
  const uRev = usuarios.find((u) => u.nombreUsuario === 'revision.bodega');
  ok('R20', 'Crear usuario con rol de bodega desde la pantalla', !!uRev && /bodega/i.test(JSON.stringify(uRev)));
  await page.click(`[data-desactivar="${uRev.usuarioId}"]`); await page.waitForTimeout(800);
  const uInactivo = (await backend('usuarios.listar')).datos.find((u) => u.usuarioId === uRev.usuarioId);
  await page.click(`[data-activar="${uRev.usuarioId}"]`); await page.waitForTimeout(800);
  const uActivo = (await backend('usuarios.listar')).datos.find((u) => u.usuarioId === uRev.usuarioId);
  ok('R21', 'Desactivar y activar un usuario desde la pantalla', uInactivo.activo === false && uActivo.activo === true);
  await page.click(`[data-restablecer="${uRev.usuarioId}"]`); await page.waitForTimeout(900);
  ok('R22', 'Restablecer la clave de un usuario: se muestra una clave temporal una sola vez', /temporal/i.test(await txt('#mensajes-usuarios')));

  // ---------- R23-R25: fallas de base de datos y de comunicación ----------
  await page.goto(BASE + 'inventario.html'); await listo(); await page.waitForSelector('#tabla-productos table', { timeout: 8000 }).catch(() => {});
  await control('bd-caida');
  const rCaida = await backend('productos.listar');
  await page.reload(); await page.waitForTimeout(2500);
  const pantallaCaida = await txt('main');
  await control('bd-ok');
  const rRecupera = await backend('productos.listar');
  ok('R23', 'Base de datos caída (DbException en el repositorio): mensaje claro con referencia, sin detalles internos; al volver la base, todo responde',
    rCaida.ok === false && /base de datos/i.test(JSON.stringify(rCaida)) && !/at FerroSuite|StackTrace|Exception/.test(JSON.stringify(rCaida))
    && /base de datos/i.test(pantallaCaida) && rRecupera.ok === true, (rCaida.errores || []).join(' ') || rCaida.mensaje);
  await control('desconectar');
  const t0 = Date.now();
  await page.goto(BASE + 'inventario.html');
  await page.waitForFunction(() => /comunicación con el backend|Sin acceso/.test(document.body.innerText), null, { timeout: 15000 }).catch(() => {});
  const textoInv = await txt('main');
  const sinRespuesta = /comunicación con el backend/.test(await txt('body')) && !/Sin acceso/.test(textoInv);
  await page.goto(BASE + 'index.html'); await page.waitForTimeout(12000);
  const avisoInicio = /comunicación con el backend/.test(await txt('body'));
  await control('conectar');
  ok('R24', 'Backend sin respuesta al abrir una pantalla: tras el tiempo de espera avisa que no hay comunicación (no muestra "Sin acceso" ni se queda en blanco)',
    sinRespuesta && avisoInicio, `Inventario muestra: «${textoInv.slice(0, 90)}»; Inicio ${avisoInicio ? 'avisa' : 'no muestra ningún aviso'}`);
  await ingresar('admin.prueba', C.admin);
  ok('R25', 'Recuperación tras la desconexión: se vuelve a ingresar y a operar', (await backend('resumen')).ok === true);

  // ---------- R26: borrador de orden desde IA, doble envío ----------
  await page.goto(BASE + 'ia.html?sku=SIN-007'); await page.waitForSelector('#ia-resultado:not([hidden])', { timeout: 15000 });
  const antesIA = (await backend('ordenes.listar')).datos.length;
  await page.click('#btn-borrador'); await page.fill('#borrador-cantidad', '20');
  await page.click('#btn-crear-borrador'); await page.waitForTimeout(400);
  const botonVisible = await visible('#btn-crear-borrador');
  if (botonVisible) { await page.click('#btn-crear-borrador').catch(() => {}); }
  await page.waitForTimeout(1500);
  const despuesIA = (await backend('ordenes.listar')).datos.length;
  ok('R26', 'Borrador de orden desde Inteligencia artificial: un segundo clic en "Crear borrador" después de crearlo no duplica la orden',
    despuesIA === antesIA + 1, `${despuesIA - antesIA} orden(es) creadas`);

  // ---------- R30-R35: administración de bodegas, categorías y usuarios (D-04) ----------
  await page.goto(BASE + 'inventario.html'); await listo();
  await page.waitForSelector('#tabla-bodegas table', { timeout: 8000 }).catch(() => {});
  await page.fill('#form-bodega [name=nombre]', 'Bodega sintética UI'); await page.fill('#form-bodega [name=direccion]', 'Carrera 1');
  await page.click('#form-bodega button[type=submit]'); await page.waitForTimeout(900);
  let bodegasTodas = (await backend('bodegas.listarTodas')).datos;
  const bUi = bodegasTodas.find((b) => b.nombre === 'Bodega sintética UI');
  await page.click(`[data-editar-bodega="${bUi.bodegaId}"]`); await page.waitForTimeout(200);
  await page.fill('#form-bodega [name=nombre]', 'Bodega sintética UI editada');
  await page.click('#form-bodega button[type=submit]'); await page.waitForTimeout(900);
  const antesEstado = contar('bodegas.actualizar');
  await page.dblclick(`[data-estado-bodega="${bUi.bodegaId}"]`); await page.waitForTimeout(1200);
  const bFinal = (await backend('bodegas.listarTodas')).datos.find((b) => b.bodegaId === bUi.bodegaId);
  const opcionesMov = await page.locator('#form-movimiento [name=bodegaId] option').allTextContents();
  ok('R30', 'Bodegas desde la pantalla: crear, editar el nombre y desactivar (doble clic = una solicitud); la inactiva deja de ofrecerse en Movimientos',
    !!bUi && bFinal.nombre === 'Bodega sintética UI editada' && bFinal.activa === false && contar('bodegas.actualizar') - antesEstado === 1
    && !opcionesMov.some((o) => o.includes('Bodega sintética UI')), `${bFinal.nombre} · activa ${bFinal.activa}`);
  const principalUi = bodegasTodas.find((b) => b.nombre === 'Bodega principal');
  await page.click(`[data-estado-bodega="${principalUi.bodegaId}"]`); await page.waitForTimeout(900);
  ok('R31', 'Desactivar la bodega con mercancía: la pantalla muestra el motivo y la bodega sigue activa',
    /mercancía/.test(await txt('#mensajes-catalogos')) && (await backend('bodegas.listarTodas')).datos.find((b) => b.bodegaId === principalUi.bodegaId).activa,
    await txt('#mensajes-catalogos'));
  await page.fill('#form-categoria [name=nombre]', 'Ferretería sintética UI');
  await page.click('#form-categoria button[type=submit]'); await page.waitForTimeout(1200);
  const catsProducto = await page.locator('#form-producto [name=categoriaId] option').allTextContents();
  const catUi = (await backend('categorias.listarTodas')).datos.find((c) => c.nombre === 'Ferretería sintética UI');
  await page.click(`[data-editar-categoria="${catUi.categoriaId}"]`); await page.waitForTimeout(200);
  await page.fill('#form-categoria [name=nombre]', 'Ferretería sintética UI editada');
  await page.click('#form-categoria button[type=submit]'); await page.waitForTimeout(900);
  ok('R32', 'Categorías desde la pantalla: crear (aparece de inmediato en el formulario de productos) y renombrar',
    catsProducto.includes('Ferretería sintética UI') && (await backend('categorias.listarTodas')).datos.some((c) => c.nombre === 'Ferretería sintética UI editada'));
  const catConProductos = (await backend('categorias.listarTodas')).datos.find((c) => c.nombre === 'Construcción');
  await page.click(`[data-estado-categoria="${catConProductos.categoriaId}"]`); await page.waitForTimeout(900);
  ok('R33', 'Desactivar una categoría con productos activos: se explica el motivo y no se desactiva',
    /producto/.test(await txt('#mensajes-catalogos')) && (await backend('categorias.listarTodas')).datos.find((c) => c.categoriaId === catConProductos.categoriaId).activa,
    await txt('#mensajes-catalogos'));
  await page.goto(BASE + 'usuarios.html'); await listo(); await page.waitForSelector('#tabla-usuarios table', { timeout: 8000 }).catch(() => {});
  const uEdit = (await backend('usuarios.listar')).datos.find((u) => u.nombreUsuario === 'revision.bodega');
  await page.click(`[data-editar="${uEdit.usuarioId}"]`); await page.waitForTimeout(300);
  const usuarioBloqueado = await page.locator('#form-usuario [name=nombreUsuario]').evaluate((e) => e.readOnly);
  const claveOculta = await page.locator('#form-usuario [name=clave]').evaluate((e) => e.closest('label').hidden);
  await page.fill('#form-usuario [name=nombreCompleto]', 'Usuario sintético de revisión (editado)');
  await page.selectOption('#form-usuario [name=rolCodigo]', 'CONTADOR');
  await page.click('#form-usuario button[type=submit]'); await page.waitForTimeout(900);
  const uDespues = (await backend('usuarios.listar')).datos.find((u) => u.usuarioId === uEdit.usuarioId);
  ok('R34', 'Editar usuario desde la pantalla: nombre y rol cambian; el nombre de usuario no se edita y no se pide clave',
    usuarioBloqueado && claveOculta && uDespues.nombreCompleto.endsWith('(editado)') && uDespues.rolCodigo === 'CONTADOR', `${uDespues.nombreCompleto} · ${uDespues.rolCodigo}`);
  await page.click('#btn-salir'); await page.waitForURL('**/login.html', { timeout: 5000 }).catch(() => {});
  await ingresar('bodega.prueba', C.bodega);
  await page.goto(BASE + 'inventario.html'); await listo(); await page.waitForTimeout(500);
  const directoBodega = await backend('categorias.crear', { nombre: 'Intento bodega' });
  ok('R35', 'Bodega no ve la administración de bodegas y categorías y el backend le rechaza la acción directa',
    !(await visible('#admin-catalogos')) && directoBodega.ok === false && /no permite/.test(directoBodega.mensaje), directoBodega.mensaje);
  await page.click('#btn-salir'); await page.waitForURL('**/login.html', { timeout: 5000 }).catch(() => {});
  await ingresar('admin.prueba', C.admin);

  // ---------- R27-R29: recursos, red y consola ----------
  for (const p of ['index', 'inventario', 'proveedores', 'ordenes', 'pronosticos', 'ia', 'usuarios']) { await page.goto(BASE + p + '.html'); await listo(); await page.waitForTimeout(600); }
  ok('R27', 'Ninguna pantalla pide recursos fuera del equipo (fuentes, scripts o imágenes de internet)', externas.length === 0, externas.slice(0, 5).join(' '));
  ok('R28', 'Ningún recurso de las pantallas responde 404 u otro error (rutas de scripts, estilos y páginas)', rotas.length === 0, rotas.slice(0, 5).join(' '));
  ok('R29', 'Sin errores de JavaScript en toda la revisión', errores.length === 0, errores.slice(0, 3).join(' | '));
  await b.close();
  console.log(res.join('\n'));
  console.log(`\n${res.filter((r) => r.startsWith('PASA')).length}/${res.length} pasos correctos`);
  process.exit(res.some((r) => r.startsWith('FALLA')) ? 1 : 0);
})();
