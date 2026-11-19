// Recorrido en navegador (Chromium) de la pantalla de Inteligencia Artificial (ia.html) con el backend C# REAL
// (herramienta "FerroSuite.Pruebas servidor": mismo Puente, mismos servicios y el modelo ONNX real con ONNX Runtime;
// guarda en memoria). Datos SINTÉTICOS. Además de comprobar, guarda capturas para las evidencias en evidencias/.
// Uso:
//   dotnet run --project tests/FerroSuite.Pruebas -- servidor <carpeta frontend> [--sin-modelo]
//   CLAVE_ADMIN=... CLAVE_BODEGA=... CLAVE_VENDEDOR=... [MODO=sin-modelo] [CAPTURAS=<carpeta>] node tests/pantallas/pantalla_ia.js
const { chromium } = require('playwright');
const path = require('path');
const BASE = 'http://127.0.0.1:18766/';
const C = { admin: process.env.CLAVE_ADMIN, bodega: process.env.CLAVE_BODEGA, vendedor: process.env.CLAVE_VENDEDOR };
const SIN_MODELO = process.env.MODO === 'sin-modelo';
const CAPTURAS = process.env.CAPTURAS || path.join(__dirname, '..', '..', 'evidencias');
if (Object.values(C).some((v) => !v)) { console.error('Faltan CLAVE_ADMIN, CLAVE_BODEGA y CLAVE_VENDEDOR (las imprime la herramienta servidor).'); process.exit(2); }
const shim = `window.chrome = window.chrome || {}; (function(){ const ls=[];
  window.chrome.webview = { postMessage(m){ fetch('/__msg',{method:'POST',body:JSON.stringify(m)}).then(r=>r.status===204?null:r.json()).then(d=>{ if(d) ls.forEach(f=>f({data:d})); }); },
  addEventListener(t,f){ if(t==='message') ls.push(f); } }; })();`;
const res = []; const ok = (id, n, c, d = '') => res.push(`${c ? 'PASA ' : 'FALLA'} | ${id} | ${n}${d ? ' — ' + d : ''}`);
const num = (v) => new Intl.NumberFormat('es-CO', { maximumFractionDigits: 2 }).format(v);

(async () => {
  const b = await chromium.launch(); const ctx = await b.newContext({ viewport: { width: 1440, height: 900 } });
  await ctx.addInitScript(shim);
  const page = await ctx.newPage(); const errores = [];
  page.on('pageerror', (e) => errores.push(e.message)); page.on('console', (m) => { if (m.type() === 'error') errores.push(m.text()); });
  const txt = async (sel) => ((await page.locator(sel).first().textContent().catch(() => '')) || '').replace(/\s+/g, ' ').trim();
  const visible = (sel) => page.locator(sel).first().isVisible().catch(() => false);
  const ingresar = async (usuario, clave) => {
    await page.goto(BASE + 'login.html'); await page.waitForSelector('#form-login form');
    await page.fill('#form-login [name=usuario]', usuario); await page.fill('#form-login [name=clave]', clave);
    await page.click('#form-login button[type=submit]'); await page.waitForURL('**/index.html', { timeout: 5000 }).catch(() => {});
  };
  const salir = async () => { await page.click('#btn-salir'); await page.waitForURL('**/login.html', { timeout: 5000 }).catch(() => {}); };
  // Pide al backend por el mismo canal (para comparar lo que muestra la pantalla con lo que calcula C#).
  const backend = (accion, datos) => page.evaluate(([a, d]) => new Promise((ok) => {
    const id = 900000 + Math.floor(Math.random() * 1000);
    window.chrome.webview.addEventListener('message', (e) => { if (e.data && e.data.id === id) ok(e.data); });
    window.chrome.webview.postMessage({ id, accion: a, datos: d });
  }), [accion, datos]);
  const elegir = async (sku) => { await page.click(`#lista-productos [data-sku="${sku}"]`); await page.waitForSelector('#ia-resultado:not([hidden])', { timeout: 15000 }); };
  const captura = (nombre) => page.screenshot({ path: path.join(CAPTURAS, nombre), fullPage: true });
  // Captura solo una parte de la pantalla (por defecto, la zona de trabajo sin la cabecera).
  const recortar = (nombre, selector = 'main section.section') => page.locator(selector).first().screenshot({ path: path.join(CAPTURAS, nombre) });

  if (SIN_MODELO) {
    await ingresar('bodega.prueba', C.bodega);
    await page.goto(BASE + 'ia.html?sku=SIN-001'); await page.waitForSelector('#ia-resultado:not([hidden])', { timeout: 15000 });
    const d = (await backend('pronosticos.producto', { sku: 'SIN-001' })).datos;
    ok('IN1', 'Modelo no disponible: aviso visible, el producto usa la regla de respaldo y se explica el motivo; la pantalla sigue funcionando',
      (await txt('#estado-modelo')).includes('Modelo no disponible') && (await txt('#ia-fuente')).includes('Regla de respaldo')
      && (await txt('#ia-motivo')).includes('Modelo no disponible') && !d.modelo.disponible && d.recomendacion.esRespaldo, await txt('#ia-motivo'));
    const inv = await backend('productos.listar');
    ok('IN2', 'Con el modelo no disponible, el inventario sigue respondiendo', inv.ok === true);
    await recortar('ia_modelo_no_disponible.png');
    ok('IN3', 'Sin errores de JavaScript', errores.length === 0, errores.join(' | '));
    await b.close(); console.log(res.join('\n')); console.log(`\n${res.filter((r) => r.startsWith('PASA')).length}/${res.length} pasos correctos`);
    process.exit(res.some((r) => r.startsWith('FALLA')) ? 1 : 0);
  }

  // ---------- Encargado de bodega ----------
  await ingresar('bodega.prueba', C.bodega);
  await page.waitForSelector('.site-nav a[href="ia.html"]', { state: 'visible', timeout: 5000 }).catch(() => {});
  ok('IA1', 'Bodega: el menú muestra "Inteligencia artificial"', await visible('.site-nav a[href="ia.html"]'));
  await page.goto(BASE + 'ia.html'); await page.waitForSelector('#lista-productos [data-sku]', { timeout: 15000 });
  const lista = (await backend('pronosticos.reposicion')).datos.recomendaciones.map((r) => r.sku);
  const enPantalla = await page.locator('#lista-productos [data-sku]').evaluateAll((els) => els.map((e) => e.dataset.sku));
  ok('IA2', 'La lista de productos es la del backend, en el mismo orden (por riesgo); sin producto elegido se pide elegir uno',
    JSON.stringify(lista) === JSON.stringify(enPantalla) && (await visible('#ia-vacio')), enPantalla.join(', '));

  // Producto CON datos suficientes.
  await elegir('SIN-001');
  const d1 = (await backend('pronosticos.producto', { sku: 'SIN-001' })).datos; const r1 = d1.recomendacion;
  const barras = await page.locator('#ia-grafica svg .g-barra').count();
  const esperadas = d1.historial.filter((h) => h.estado === 'venta' || h.estado === 'sin_venta').length;
  ok('IA3', 'Producto con historial (SIN-001): la red neuronal es la fuente y la pantalla muestra exactamente los valores calculados en C#',
    (await txt('#ia-fuente')).includes('Red neuronal') && !r1.esRespaldo && (await txt('#ia-nombre')) === r1.nombre
    && (await txt('#kpi-stock .tarjeta-valor')).startsWith(num(r1.stockActual)) && (await txt('#kpi-demanda .tarjeta-valor')).startsWith(num(r1.consumoDiario))
    && (await txt('#kpi-dias .tarjeta-valor')) === `${num(r1.diasHastaAgotarse)} días` && (await txt('#kpi-entrega .tarjeta-valor')) === `${r1.diasEntrega} días`
    && (await txt('#kpi-riesgo .tarjeta-valor')) === r1.riesgoDesabastecimiento && !(await visible('#ia-motivo')),
    `consumo ${r1.consumoDiario}/día · ${r1.diasHastaAgotarse} días · entrega ${r1.diasEntrega} · riesgo ${r1.riesgoDesabastecimiento}`);
  ok('IA4', 'Gráfica: una barra por cada día con dato del backend (90 días), ventana del modelo marcada, línea del pronóstico y de la regla; tabla de 7 días',
    barras === esperadas && esperadas > 0 && (await page.locator('#ia-grafica svg .g-ventana').count()) === 1
    && (await page.locator('#ia-grafica svg .g-pronostico').count()) === 1 && (await page.locator('#ia-grafica svg .g-regla').count()) === 1
    && (await page.locator('#ia-proyeccion tbody tr').count()) === 7, `${barras} barras / ${esperadas} días con dato`);
  await captura('ia_producto_con_historial.png');

  // Producto con días agotado.
  await elegir('SIN-002');
  const d2 = (await backend('pronosticos.producto', { sku: 'SIN-002' })).datos;
  const agot = d2.historial.filter((h) => h.estado === 'agotado').length;
  ok('IA5', 'Producto con días agotado (SIN-002): los días sin stock se dibujan como "agotado", no como venta cero',
    agot > 0 && (await page.locator('#ia-grafica rect.g-agotado').count()) === agot, `${agot} días agotado`);
  await recortar('ia_producto_con_agotados.png', '#ia-resultado');

  // Producto SIN historial.
  await elegir('SIN-007');
  const d7 = (await backend('pronosticos.producto', { sku: 'SIN-007' })).datos; const r7 = d7.recomendacion;
  ok('IA6', 'Producto sin historial (SIN-007): regla de respaldo indicada con su motivo, "se agota en" no aplica y la gráfica lo explica',
    (await txt('#ia-fuente')).includes('Regla de respaldo') && (await txt('#ia-motivo')).includes('no tiene movimientos') && r7.esRespaldo
    && (await txt('#kpi-dias .tarjeta-valor')) === 'No aplica' && (await page.locator('#ia-grafica svg .g-barra').count()) === 0
    && (await txt('#ia-grafica')).includes('Sin ventas registradas') && (await txt('#kpi-riesgo .tarjeta-valor')) === r7.riesgoDesabastecimiento,
    await txt('#ia-motivo'));
  await captura('ia_producto_sin_historial.png');

  // Búsqueda.
  await page.fill('#buscar-producto', 'pint');
  const filtrados = await page.locator('#lista-productos [data-sku]').evaluateAll((els) => els.map((e) => e.dataset.sku));
  await page.fill('#buscar-producto', 'zzz');
  ok('IA7', 'Búsqueda por nombre o SKU; si nada coincide, se informa', filtrados.join() === 'SIN-008' && (await txt('#lista-productos')).includes('Ningún producto'));
  await page.fill('#buscar-producto', '');

  // Producto con historial corto y sin proveedor.
  await elegir('SIN-008');
  ok('IA8', 'Historial corto y sin proveedor (SIN-008): respaldo con motivo y sin botón de orden (explica por qué)',
    (await txt('#ia-motivo')).includes('Historial insuficiente') && !(await visible('#btn-borrador')) && (await txt('#ia-recomendacion')).length > 0, await txt('#ia-recomendacion'));

  // Estado de carga: se retrasa la respuesta del backend 1,5 s.
  let retraso = 1500;
  await page.route('**/__msg', async (route) => {
    const cuerpo = route.request().postData() || '';
    if (cuerpo.includes('pronosticos.producto') && retraso) await new Promise((r) => setTimeout(r, retraso));
    await route.continue();
  });
  await page.click('#lista-productos [data-sku="SIN-001"]'); await page.waitForTimeout(300);
  ok('IA9', 'Mientras el backend calcula se muestra "Consultando el modelo neuronal…" y se oculta el resultado anterior',
    (await visible('#ia-cargando')) && (await txt('#ia-cargando')).includes('SIN-001') && !(await visible('#ia-resultado')));
  await recortar('ia_cargando.png');
  await page.waitForSelector('#ia-resultado:not([hidden])', { timeout: 15000 });

  // Cambio rápido de producto: la respuesta lenta del primero no reemplaza al segundo.
  retraso = 0;
  await page.unroute('**/__msg');
  await page.route('**/__msg', async (route) => {
    const cuerpo = route.request().postData() || '';
    if (cuerpo.includes('pronosticos.producto') && cuerpo.includes('SIN-001')) await new Promise((r) => setTimeout(r, 2000));
    await route.continue();
  });
  await page.click('#lista-productos [data-sku="SIN-001"]'); await page.waitForTimeout(150);
  await page.click('#lista-productos [data-sku="SIN-002"]'); await page.waitForTimeout(3000);
  ok('IA10', 'Si se cambia de producto antes de que llegue la respuesta, solo se muestra el producto elegido (se descarta la respuesta vieja)',
    (await txt('#ia-nombre')) === d2.recomendacion.nombre && new URL(page.url()).searchParams.get('sku') === 'SIN-002' && (await txt('.ia-item.activo')).includes('SIN-002'));
  await page.unroute('**/__msg');

  // Error del backend (respuesta de error con el formato del Puente, inyectada en el navegador) -> mensaje y reintentar.
  await page.route('**/__msg', async (route) => {
    const m = JSON.parse(route.request().postData() || '{}');
    if (m.accion === 'pronosticos.producto') return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ id: m.id, ok: false, mensaje: 'Ocurrió un error inesperado. Referencia: PRUEBA-1.', errores: [] }) });
    return route.continue();
  });
  await page.click('#lista-productos [data-sku="SIN-001"]'); await page.waitForSelector('#ia-error:not([hidden])', { timeout: 5000 }).catch(() => {});
  const conError = (await visible('#ia-error')) && (await txt('#ia-error-texto')).includes('PRUEBA-1') && !(await visible('#ia-resultado'));
  await page.unroute('**/__msg');
  await page.click('#btn-reintentar'); await page.waitForSelector('#ia-resultado:not([hidden])', { timeout: 15000 }).catch(() => {});
  ok('IA11', 'Si el backend responde con error, se muestra el mensaje (sin datos inventados) y "Reintentar" vuelve a pedir el cálculo',
    conError && (await txt('#ia-nombre')) === r1.nombre);

  // Borrador de orden desde el producto sin historial (stock 0, mínimo 50).
  await elegir('SIN-007');
  const ordenesAntes = (await backend('ordenes.listar')).datos.length;
  await page.click('#btn-borrador');
  const sugerida = await page.inputValue('#borrador-cantidad');
  ok('IA12', 'Generar borrador abre una confirmación con producto, proveedor y la cantidad sugerida por el backend (editable)',
    (await visible('#ia-borrador')) && Number(sugerida) === r7.cantidadSugerida && (await txt('#borrador-proveedor')).includes(r7.nitProveedor), `sugerida ${sugerida}`);
  await page.fill('#borrador-cantidad', '60');
  await page.click('#btn-crear-borrador'); await page.waitForTimeout(700);
  await page.locator('#mensajes-ia .aviso').waitFor({ timeout: 5000 }).catch(() => {});
  await recortar('ia_borrador_orden.png', '.ia-detalle');
  const ordenes = (await backend('ordenes.listar')).datos;
  const nueva = ordenes.find((o) => o.detalles && o.detalles.some((x) => x.sku === 'SIN-007'));
  ok('IA13', 'El borrador se crea como orden "Pendiente de aprobación" con la cantidad confirmada; no se aprueba ni se envía, y el panel se cierra (no se puede enviar dos veces)',
    (await txt('#mensajes-ia')).includes('creada') && !(await visible('#ia-borrador')) && ordenes.length === ordenesAntes + 1 && nueva && nueva.estado.startsWith('Pendiente')
    && nueva.detalles.find((x) => x.sku === 'SIN-007').cantidad === 60 && !nueva.fechaAprobacion, nueva ? `${nueva.codigo} · ${nueva.estado}` : 'sin orden');
  await page.waitForTimeout(2000);
  ok('IA14', 'Tras crear el borrador se recalcula: la cantidad aparece como ya pedida y la sugerencia se descuenta',
    (await txt('#ia-recomendacion')).includes('60') && (await backend('pronosticos.producto', { sku: 'SIN-007' })).datos.recomendacion.enCamino === 60);
  await salir();

  // ---------- Administrador ----------
  await ingresar('admin.prueba', C.admin);
  await page.goto(BASE + 'ia.html?sku=SIN-002'); await page.waitForSelector('#ia-resultado:not([hidden])', { timeout: 15000 }).catch(() => {});
  ok('IA15', 'Administrador: abre la pantalla con ?sku= y ve el mismo pronóstico', (await txt('#ia-nombre')) === d2.recomendacion.nombre);
  await salir();

  // ---------- Vendedor: sin acceso ----------
  await ingresar('vendedor.prueba', C.vendedor);
  const enMenu = await visible('.site-nav a[href="ia.html"]');
  await page.goto(BASE + 'ia.html'); await page.waitForTimeout(1200);
  const directo = await backend('pronosticos.producto', { sku: 'SIN-001' });
  ok('IA16', 'Vendedor: no ve el menú, la pantalla dice "Sin acceso" y el backend rechaza la consulta aunque se envíe directo',
    !enMenu && (await txt('main')).includes('Sin acceso') && directo.ok === false && (directo.mensaje || '').includes('no permite'), directo.mensaje);
  await salir();

  ok('IA17', 'Sin errores de JavaScript en todo el recorrido', errores.length === 0, errores.join(' | '));
  await b.close();
  console.log(res.join('\n'));
  console.log(`\n${res.filter((r) => r.startsWith('PASA')).length}/${res.length} pasos correctos`);
  process.exit(res.some((r) => r.startsWith('FALLA')) ? 1 : 0);
})();
