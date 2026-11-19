// Recorrido en navegador (Chromium) de las pantallas REALES con el backend REAL vía la herramienta de prueba
// (FerroSuite.Pruebas servidor), que reemplaza a WebView2 y guarda en memoria. Datos SINTÉTICOS.
// Uso: iniciar "dotnet run --project tests/FerroSuite.Pruebas -- servidor <carpeta frontend>", copiar las claves
// aleatorias que imprime y ejecutar:
//   CLAVE_ADMIN=... CLAVE_BODEGA=... CLAVE_VENDEDOR=... CLAVE_CONTADOR=... node tests/pantallas/recorrido.js
const { chromium } = require('playwright');
const path = require('path');
// Capturas auxiliares: a CAPTURAS si se indica; si no, a la carpeta temporal (nunca dentro del repositorio).
const CAPTURAS = process.env.CAPTURAS || require('os').tmpdir();
const BASE = 'http://127.0.0.1:18766/';
const C = { admin: process.env.CLAVE_ADMIN, bodega: process.env.CLAVE_BODEGA, vendedor: process.env.CLAVE_VENDEDOR, contador: process.env.CLAVE_CONTADOR };
if (Object.values(C).some((v) => !v)) { console.error('Faltan CLAVE_ADMIN, CLAVE_BODEGA, CLAVE_VENDEDOR y CLAVE_CONTADOR (las imprime la herramienta servidor).'); process.exit(2); }
const shim = `window.chrome = window.chrome || {}; (function(){ const ls=[];
  window.chrome.webview = { postMessage(m){ fetch('/__msg',{method:'POST',body:JSON.stringify(m)}).then(r=>r.status===204?null:r.json()).then(d=>{ if(d) ls.forEach(f=>f({data:d})); }); },
  addEventListener(t,f){ if(t==='message') ls.push(f); } }; })();`;
const res = []; const ok = (id, n, c, d = '') => res.push(`${c ? 'PASA ' : 'FALLA'} | ${id} | ${n}${d ? ' — ' + d : ''}`);
(async () => {
  const b = await chromium.launch(); const ctx = await b.newContext({ viewport: { width: 1280, height: 900 } });
  await ctx.addInitScript(shim);
  const page = await ctx.newPage(); const errores = [];
  page.on('pageerror', e => errores.push(e.message)); page.on('console', m => { if (m.type() === 'error') errores.push(m.text()); });
  page.on('dialog', d => d.accept());
  const txt = async (sel) => ((await page.locator(sel).textContent().catch(() => '')) || '').trim();
  const visible = (sel) => page.locator(sel).first().isVisible().catch(() => false);
  const ingresar = async (usuario, clave, destino = '**/index.html') => {
    await page.goto(BASE + 'login.html'); await page.waitForSelector('#form-login form');
    await page.fill('#form-login [name=usuario]', usuario); await page.fill('#form-login [name=clave]', clave);
    await page.click('#form-login button[type=submit]');
    if (destino) await page.waitForURL(destino, { timeout: 5000 }).catch(() => {});
  };
  const salir = async () => { await page.click('#btn-salir'); await page.waitForURL('**/login.html', { timeout: 5000 }).catch(() => {}); };
  // Envía un mensaje directamente por el canal de WebView2, sin usar botones (como lo haría alguien que intenta saltarse la pantalla).
  const directo = (accion, datos) => page.evaluate(([a, d]) => new Promise((res) => {
    const id = 900000 + Math.floor(Math.random() * 1000);
    window.chrome.webview.addEventListener('message', (e) => { if (e.data && e.data.id === id) res(e.data); });
    window.chrome.webview.postMessage({ id, accion: a, datos: d });
  }), [accion, datos]);

  // ---------- Ingreso ----------
  await page.goto(BASE + 'inventario.html');
  await page.waitForURL('**/login.html', { timeout: 5000 }).catch(() => {});
  ok('E1', 'Sin sesión, abrir Inventario lleva a la pantalla de ingreso', page.url().endsWith('login.html'));
  await ingresar('admin.prueba', 'Clave-equivocada-1', null); await page.waitForTimeout(800);
  ok('E2', 'Clave incorrecta: mensaje genérico y no entra', (await txt('#form-login .form-mensajes')).includes('No se pudo iniciar sesión') && page.url().endsWith('login.html'));

  // ---------- Vendedor de mostrador: solo stock, ventas y devoluciones ----------
  await ingresar('vendedor.prueba', C.vendedor);
  await page.waitForSelector('.nav-usuario', { timeout: 5000 }).catch(() => {});
  ok('E3', 'Vendedor: el menú solo muestra Inicio e Inventario', (await visible('.site-nav a[href="inventario.html"]')) && !(await visible('.site-nav a[href="ordenes.html"]'))
    && !(await visible('.site-nav a[href="proveedores.html"]')) && !(await visible('.site-nav a[href="usuarios.html"]')), (await txt('.nav-usuario')));
  await page.goto(BASE + 'inventario.html'); await page.waitForSelector('#tabla-productos table');
  await page.waitForFunction(() => document.querySelectorAll('#form-movimiento [name=motivo] option').length > 0 && document.body.classList.contains('permisos-listos'));
  const motivos = await page.locator('#form-movimiento [name=motivo] option').allTextContents();
  ok('E4', 'Vendedor: solo motivos Venta y Devolución; sin formulario de productos, historial, asistente ni botones de eliminar',
    motivos.join('|') === 'Venta|Devolución' && !(await visible('#form-producto')) && !(await visible('#tabla-movimientos'))
    && !(await visible('#asistente')) && (await page.locator('[data-eliminar]').count()) === 0, motivos.join('|'));
  ok('E5', 'Vendedor: no ve precios de compra ni valor en bodega', !(await txt('#tabla-productos')).includes('$') && !(await txt('#resumen-inventario')).includes('Valor'));
  const m = '#form-movimiento';
  await page.selectOption(`${m} [name=sku]`, 'SIN-001'); await page.selectOption(`${m} [name=tipo]`, 'Salida');
  await page.fill(`${m} [name=cantidad]`, '2.5'); await page.selectOption(`${m} [name=motivo]`, 'Venta');
  await page.click(`${m} button[type=submit]`); await page.waitForTimeout(900);
  ok('E6', 'Vendedor registra una venta de 2,5 m', /97[,.]5/.test(await txt(`${m} .form-mensajes`)), await txt(`${m} .form-mensajes`));
  const r1 = await directo('usuarios.crear', { nombreUsuario: 'intruso.uno', nombreCompleto: 'X', rolCodigo: 'ADMIN', clave: 'ClaveIntrusa123' });
  const r2 = await directo('movimientos.registrar', { sku: 'SIN-001', tipo: 'Salida', cantidad: 90, motivo: 'Daño o pérdida', bodegaId: 1 });
  const r3 = await directo('productos.eliminar', { sku: 'SIN-002' });
  ok('E7', 'Vendedor enviando mensajes directos por WebView2 (crear administrador, dar de baja stock, eliminar producto): el backend los rechaza',
    !r1.ok && !r2.ok && !r3.ok && [r1, r2, r3].every((r) => r.mensaje.includes('no permite')));
  await page.goto(BASE + 'usuarios.html'); await page.waitForTimeout(800);
  ok('E8', 'Vendedor abriendo la pantalla de usuarios por su dirección: "Sin acceso"', (await txt('main')).includes('Sin acceso'));
  const rp = await directo('pronosticos.reposicion', null);
  ok('E28', 'Vendedor: sin menú de Pronósticos y el backend rechaza la consulta directa', !(await visible('.site-nav a[href="pronosticos.html"]')) && !rp.ok && rp.mensaje.includes('no permite'));
  await salir();

  // ---------- Encargado de bodega ----------
  await ingresar('bodega.prueba', C.bodega);
  await page.goto(BASE + 'inventario.html'); await page.waitForSelector('#tabla-productos table');
  await page.waitForFunction(() => document.querySelectorAll('#form-movimiento [name=motivo] option').length > 0 && document.body.classList.contains('permisos-listos'));
  const motivosBodega = await page.locator('#form-movimiento [name=motivo] option').allTextContents();
  ok('E9', 'Bodega: motivos de ajuste (sin Venta), historial visible y sin formulario de productos',
    !motivosBodega.includes('Venta') && motivosBodega.includes('Ajuste por conteo') && (await visible('#tabla-movimientos')) && !(await visible('#form-producto')), motivosBodega.join('|'));
  await page.selectOption(`${m} [name=sku]`, 'SIN-001'); await page.selectOption(`${m} [name=tipo]`, 'Salida');
  await page.fill(`${m} [name=cantidad]`, '0.5'); await page.selectOption(`${m} [name=motivo]`, 'Ajuste por conteo');
  await page.click(`${m} button[type=submit]`); await page.waitForTimeout(900);
  const hist = await txt('#tabla-movimientos');
  ok('E10', 'Bodega registra un ajuste; el historial muestra bodega y responsable de cada movimiento', hist.includes('Usuario sintético bodega') && hist.includes('Usuario sintético vendedor') && hist.includes('Bodega principal'));
  await page.goto(BASE + 'ordenes.html'); await page.waitForSelector('#form-orden form'); await page.waitForTimeout(500);
  await page.selectOption('#form-orden [name=sku]', 'SIN-001'); await page.selectOption('#form-orden [name=nitProveedor]', '000000001-8');
  await page.fill('#form-orden [name=cantidad]', '5'); await page.click('#form-orden button[type=submit]'); await page.waitForTimeout(800);
  ok('E11', 'Bodega crea una orden pero no ve el botón Aprobar', (await txt('#tabla-ordenes')).includes('OC-001') && (await page.locator('[data-aprobar]').count()) === 0);
  const r4 = await directo('ordenes.aprobar', { codigo: 'OC-001' });
  ok('E12', 'Bodega intentando aprobar su propia orden con un mensaje directo: rechazado', !r4.ok && r4.mensaje.includes('no permite'));
  // ---------- Pronóstico con la red neuronal (bodega) ----------
  await page.goto(BASE + 'pronosticos.html');
  await page.waitForSelector('#tabla-pronostico table', { timeout: 15000 }).catch(() => {});
  await page.uncheck('#solo-accion'); await page.waitForTimeout(300);
  const tablaPron = await txt('#tabla-pronostico');
  ok('E24', 'Pronósticos: el modelo neuronal está activo y los productos con historia se pronostican con la red',
    (await txt('#estado-modelo')).includes('Modelo neuronal activo') && tablaPron.includes('Red neuronal') && tablaPron.includes('SIN-002'), (await txt('#estado-modelo')).slice(0, 90));
  await page.screenshot({ path: path.join(CAPTURAS, 'pronosticos.png'), fullPage: true });
  ok('E25', 'Cada fila muestra consumo diario, días hasta agotarse, entrega del proveedor y la explicación', tablaPron.includes('/día') && tablaPron.includes('días') && tablaPron.includes('El proveedor tarda'));
  const boton = page.locator('[data-preparar]').first();
  if (await boton.count()) {
    const skuPrep = await boton.getAttribute('data-preparar');
    const ordenesAntes = (await directo('ordenes.listar')).datos.length;
    await boton.click();
    await page.waitForURL('**/ordenes.html?*', { timeout: 5000 }).catch(() => {});
    await page.waitForTimeout(800);
    const prellenado = await page.locator('#form-orden [name=sku]').inputValue();
    const cantidad = await page.locator('#form-orden [name=cantidad]').inputValue();
    const ordenesDespues = (await directo('ordenes.listar')).datos.length;
    ok('E26', '"Preparar orden" solo llena el formulario: no crea ni aprueba nada hasta que el usuario lo decide',
      prellenado === skuPrep && Number(cantidad) > 0 && ordenesDespues === ordenesAntes && (await txt('#form-orden .form-mensajes')).includes('pendiente de aprobación'), `${skuPrep} × ${cantidad}`);
    await page.click('#form-orden button[type=submit]'); await page.waitForTimeout(900);
    const lista = (await directo('ordenes.listar')).datos;
    ok('E27', 'Al generarla, la orden queda "Pendiente de aprobación"', lista.length === ordenesAntes + 1 && lista[lista.length - 1].estado === 'Pendiente de aprobación');
  } else {
    ok('E26b', '"Preparar orden" disponible en alguna recomendación', false, 'no hubo recomendaciones con cantidad');
  }
  await salir();

  // ---------- Administrador: usuarios, auditoría, catálogo y compras ----------
  await ingresar('admin.prueba', C.admin);
  await page.waitForSelector('.nav-usuario', { timeout: 5000 }).catch(() => {});
  ok('E13', 'Administrador: el menú incluye Usuarios', await visible('.site-nav a[href="usuarios.html"]'));
  await page.goto(BASE + 'usuarios.html'); await page.waitForSelector('#tabla-usuarios table');
  await page.waitForFunction(() => document.querySelectorAll('#form-usuario [name=rolCodigo] option').length === 4);
  const claveInicial = 'Inicial' + Math.random().toString(36).slice(2, 10) + '9';
  await page.fill('#form-usuario [name=nombreUsuario]', 'nuevo.cajero'); await page.fill('#form-usuario [name=nombreCompleto]', 'Cajero sintético');
  await page.selectOption('#form-usuario [name=rolCodigo]', 'VENDEDOR'); await page.fill('#form-usuario [name=clave]', claveInicial);
  await page.click('#form-usuario button[type=submit]'); await page.waitForTimeout(900);
  ok('E14', 'Administrador crea un usuario (queda con obligación de cambiar la clave)', (await txt('#tabla-usuarios')).includes('nuevo.cajero') && (await txt('#tabla-usuarios')).includes('Debe cambiar'));
  const auditoria = await txt('#tabla-auditoria');
  ok('E15', 'La pantalla de auditoría muestra la creación del usuario y los accesos denegados, sin claves',
    auditoria.includes('usuarios.crear') && auditoria.includes('Denegado') && !auditoria.includes(claveInicial) && !auditoria.includes(C.vendedor));
  await page.goto(BASE + 'ordenes.html'); await page.waitForSelector('[data-aprobar="OC-001"]');
  await page.click('[data-aprobar="OC-001"]'); await page.waitForTimeout(800);
  await page.click('[data-recibir="OC-001"]'); await page.waitForTimeout(800);
  ok('E16', 'Administrador aprueba la orden y se recibe la mercancía', (await txt('#tabla-ordenes')).includes('Recibida'));
  await page.goto(BASE + 'inventario.html'); await page.waitForSelector('#tabla-productos table');
  await page.waitForFunction(() => document.querySelectorAll('#form-producto [name=categoriaId] option').length > 0);
  const f = '#form-producto';
  await page.fill(`${f} [name=sku]`, 'sin-010'); await page.fill(`${f} [name=nombre]`, 'Llave sintética');
  await page.selectOption(`${f} [name=categoriaId]`, { label: 'Plomería' }); await page.selectOption(`${f} [name=unidad]`, 'Unidad');
  await page.fill(`${f} [name=stockActual]`, '0'); await page.fill(`${f} [name=stockMinimo]`, '5'); await page.fill(`${f} [name=precioCompra]`, '15000');
  await page.click(`${f} button[type=submit]`); await page.waitForTimeout(900);
  await page.click('[data-eliminar="SIN-010"]'); await page.waitForTimeout(900);
  ok('E17', 'Administrador crea y elimina un producto', !(await txt('#tabla-productos')).includes('SIN-010') && (await txt('#mensajes-catalogo')).includes('eliminado'));
  await page.goto(BASE + 'proveedores.html'); await page.waitForSelector('#tabla-proveedores table');
  await page.fill('#form-proveedor [name=nombre]', 'Proveedor Sintético C'); await page.fill('#form-proveedor [name=nit]', '000000003-1'); await page.fill('#form-proveedor [name=diasEntrega]', '4');
  await page.click('#form-proveedor button[type=submit]'); await page.waitForTimeout(800);
  ok('E18', 'NIT con dígito de verificación incorrecto: muestra el error', (await txt('#form-proveedor .form-mensajes')).includes('verificación'));
  await salir();

  // ---------- Contador ----------
  await ingresar('contador.prueba', C.contador);
  await page.goto(BASE + 'ordenes.html'); await page.waitForSelector('#tabla-ordenes table'); await page.waitForTimeout(500);
  ok('E19', 'Contador: ve órdenes y "Marcar pagada", pero no aprobar ni crear órdenes', (await page.locator('[data-pagar="OC-001"]').count()) === 1
    && (await page.locator('[data-aprobar]').count()) === 0 && !(await visible('#form-orden')));
  await page.click('[data-pagar="OC-001"]'); await page.waitForTimeout(800);
  ok('E20', 'Contador marca la orden como pagada', (await txt('#tabla-ordenes')).includes('Pagada'));
  await salir();

  // ---------- Usuario nuevo: cambio obligatorio de clave ----------
  await ingresar('nuevo.cajero', claveInicial, null); await page.waitForTimeout(900);
  ok('E21', 'Con la clave inicial, el usuario nuevo debe cambiarla antes de entrar', await visible('#form-cambio-clave form'));
  const nueva = 'Definitiva' + Math.random().toString(36).slice(2, 10) + '7';
  await page.fill('#form-cambio-clave [name=actual]', claveInicial); await page.fill('#form-cambio-clave [name=nueva]', nueva); await page.fill('#form-cambio-clave [name=repetir]', nueva);
  await page.click('#form-cambio-clave button[type=submit]');
  await page.waitForURL('**/index.html', { timeout: 5000 }).catch(() => {});
  await page.waitForSelector('.nav-usuario', { timeout: 5000 }).catch(() => {});
  ok('E22', 'Tras cambiarla entra con su rol (Vendedor de mostrador)', page.url().endsWith('index.html') && (await txt('.nav-usuario')).includes('Vendedor'));
  await page.screenshot({ path: path.join(CAPTURAS, 'inicio_vendedor.png'), fullPage: false });

  ok('E23', 'Sin errores de JavaScript en consola durante el recorrido', errores.length === 0, errores.slice(0, 3).join(' || '));
  await b.close(); console.log(res.join('\n'));
  console.log(`\nTotal: ${res.filter(r => r.startsWith('PASA')).length} pasan, ${res.filter(r => r.startsWith('FALLA')).length} fallan`);
})();
