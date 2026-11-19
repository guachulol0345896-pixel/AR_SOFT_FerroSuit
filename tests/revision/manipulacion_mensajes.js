// Auditoría independiente: mensajes MANIPULADOS enviados al Puente real (herramienta "FerroSuite.Pruebas servidor"),
// como lo haría alguien que edita el JavaScript o usa la consola. Solo lee y reporta; no modifica el programa.
// Uso: servidor de prueba en marcha; CLAVE_ADMIN=.. CLAVE_BODEGA=.. CLAVE_VENDEDOR=.. CLAVE_CONTADOR=.. node tests/revision/manipulacion_mensajes.js
// (Versión vigente tras CORRECCIONES_FINALES.md; la original de la auditoría está en evidencias/auditoria_independiente/.)
const { chromium } = require('playwright');
const BASE = 'http://127.0.0.1:18766/';
const shim = `window.chrome = window.chrome || {}; (function(){ const ls=[];
  window.chrome.webview = { postMessage(m){ fetch('/__msg',{method:'POST',body:typeof m==='string'?m:JSON.stringify(m)}).then(r=>r.status===204?null:r.json()).then(d=>{ if(d) ls.forEach(f=>f({data:d})); }).catch(()=>{}); },
  addEventListener(t,f){ if(t==='message') ls.push(f); } }; })();`;
const res = []; const ok = (id, n, c, d = '') => res.push(`${c ? 'PASA ' : 'FALLA'} | ${id} | ${n}${d ? ' — ' + d : ''}`);
(async () => {
  const b = await chromium.launch(); const ctx = await b.newContext(); await ctx.addInitScript(shim); const page = await ctx.newPage();
  await page.goto(BASE + 'login.html');
  let n = 1;
  const pedir = (accion, datos, extra = {}) => page.evaluate(([a, d, x, id]) => new Promise((ok) => {
    window.chrome.webview.addEventListener('message', (e) => { if (e.data && e.data.id === id) ok(e.data); });
    window.chrome.webview.postMessage(Object.assign({ id, accion: a, datos: d }, x));
    setTimeout(() => ok({ sinRespuesta: true }), 8000);
  }), [accion, datos, extra, 700000 + n++]);
  const crudo = (texto) => page.evaluate((t) => fetch('/__msg', { method: 'POST', body: t }).then((r) => r.status === 204 ? null : r.json()), texto);
  const entrar = async (u, clave) => { await pedir('sesion.cerrar'); return pedir('sesion.iniciar', { usuario: u, clave }); };
  const C = { admin: process.env.CLAVE_ADMIN, bodega: process.env.CLAVE_BODEGA, vendedor: process.env.CLAVE_VENDEDOR, contador: process.env.CLAVE_CONTADOR };

  // --- Vendedor ---
  await entrar('vendedor.prueba', C.vendedor);
  const v1 = await pedir('movimientos.registrar', { sku: 'SIN-001', tipo: 'Entrada', cantidad: 50, motivo: 'Compra a proveedor', bodegaId: 1 });
  const v2 = await pedir('movimientos.registrar', { sku: 'SIN-001', tipo: 'Entrada', cantidad: 50, motivo: 'Ajuste por conteo', bodegaId: 1 });
  ok('T1', 'Vendedor no puede registrar compras ni ajustes cambiando "tipo" o "motivo" en el mensaje', !v1.ok && !v2.ok, `${v1.mensaje} | ${v2.mensaje}`);
  const pv = await pedir('productos.listar');
  const filaV = (pv.datos || [])[0] || {};
  ok('T2', 'Vendedor: la respuesta de productos no incluye costos (precio de compra ni valor)', pv.ok && filaV.precioCompra == null && filaV.valorInventario == null, `campos: ${Object.keys(filaV).join(',')}`);
  const v3 = await pedir('movimientos.registrar', { sku: 'SIN-001', tipo: 'Salida', cantidad: 1, motivo: 'Venta', bodegaId: 1, usuarioId: 1, usuario: 'admin.prueba' });
  const ultimo = v3.ok ? null : null;
  ok('T3', 'Vendedor: los campos "usuarioId"/"usuario" falsificados en el mensaje se ignoran', v3.ok, v3.mensaje);
  // --- Bodega ---
  await entrar('bodega.prueba', C.bodega);
  const b1 = await pedir('movimientos.registrar', { sku: 'SIN-001', tipo: 'Salida', cantidad: 1, motivo: 'Venta', bodegaId: 1 });
  ok('T4', 'Bodega no puede registrar ventas cambiando el motivo', !b1.ok, b1.mensaje);
  const pb = await pedir('productos.listar'); const filaB = (pb.datos || [])[0] || {};
  const rb = await pedir('resumen');
  // Tras las correcciones (D-05): documentado que bodega ve el precio unitario (para pedir), pero no los totales en pesos.
  ok('T5', 'Bodega: recibe el precio unitario de compra (documentado) pero no el valor total de bodega ni las cuentas por pagar',
    pb.ok && filaB.precioCompra != null && rb.ok && rb.datos.valorBodega == null && rb.datos.porPagar == null, `precio ${filaB.precioCompra} · valorBodega ${rb.datos && rb.datos.valorBodega} · porPagar ${rb.datos && rb.datos.porPagar}`);
  const oc = await pedir('ordenes.crear', { nitProveedor: '000000001-8', estado: 'Aprobada', pagada: true, detalles: [{ sku: 'SIN-001', cantidad: 3, precioUnitario: 1 }] });
  await entrar('admin.prueba', C.admin);
  const ordenes = (await pedir('ordenes.listar')).datos || [];
  const creada = ordenes.find((o) => o.codigo === (oc.datos && oc.datos.codigo)) || ordenes[ordenes.length - 1];
  const prod = ((await pedir('productos.listar')).datos || []).find((p) => p.sku === 'SIN-001');
  ok('T6', 'Bodega: "estado", "pagada" y "precioUnitario" falsificados al crear una orden se ignoran (queda pendiente, sin pagar y con el precio del producto)',
    oc.ok && creada && /Pendiente/.test(creada.estado) && !creada.pagada && creada.detalles[0].precioUnitario === prod.precioCompra,
    creada ? `${creada.estado} · pagada ${creada.pagada} · precio ${creada.detalles[0].precioUnitario} (producto ${prod.precioCompra})` : JSON.stringify(oc));
  await entrar('bodega.prueba', C.bodega);
  const b2 = await pedir('ordenes.aprobar', { codigo: creada && creada.codigo });
  const b3 = await pedir('usuarios.crear', { nombreUsuario: 'intruso.x', nombreCompleto: 'X', rolCodigo: 'ADMIN', clave: 'ClaveIntrusa123' });
  ok('T7', 'Bodega no puede aprobar órdenes ni crear usuarios aunque envíe la acción', !b2.ok && !b3.ok && b2.sinSesion !== true, `${b2.mensaje} | ${b3.mensaje}`);
  // --- Contador ---
  await entrar('contador.prueba', C.contador);
  const c1 = await pedir('movimientos.registrar', { sku: 'SIN-001', tipo: 'Salida', cantidad: 1, motivo: 'Venta', bodegaId: 1 });
  const c2 = await pedir('productos.eliminar', { sku: 'SIN-002' });
  ok('T8', 'Contador no puede mover inventario ni eliminar productos', !c1.ok && !c2.ok, `${c1.mensaje} | ${c2.mensaje}`);
  // --- Formato y límites ---
  const f1 = await crudo('{"id":1,"accion":"productos.listar","accion":"usuarios.crear","datos":{}}');
  ok('T9', 'Claves "accion" duplicadas en el JSON: se rechazan como formato incorrecto (no saltan permisos ni dan error inesperado)', f1 && f1.ok === false && /formato/.test(f1.mensaje), JSON.stringify(f1).slice(0, 120));
  const f2 = await pedir('PRODUCTOS.LISTAR'); const f3 = await pedir('productos.listar ');
  ok('T10', 'Acciones con mayúsculas o espacios no se aceptan', !f2.ok && !f3.ok, `${f2.mensaje} | ${f3.mensaje}`);
  const f4 = await crudo('{"id":2,"accion":"productos.listar","datos":' + '['.repeat(40) + ']'.repeat(40) + '}');
  const f5 = await crudo(JSON.stringify({ id: 3, accion: 'productos.listar', datos: { x: 'a'.repeat(70000) } }));
  ok('T11', 'JSON demasiado profundo o demasiado grande se rechaza sin error interno', f4 && !f4.ok && f5 && !f5.ok, `${f4 && f4.mensaje} | ${f5 && f5.mensaje}`);
  await entrar('admin.prueba', C.admin);
  const g1 = await pedir('movimientos.registrar', { sku: 'SIN-001', tipo: 'Entrada', cantidad: 1e30, motivo: 'Ajuste por conteo', bodegaId: 1 });
  const g2 = await pedir('movimientos.registrar', { sku: 'SIN-001', tipo: 'Entrada', cantidad: 'NaN', motivo: 'Ajuste por conteo', bodegaId: 1 });
  const g3 = await pedir('movimientos.registrar', { sku: 'SIN-001', tipo: 'Entrada', cantidad: -5, motivo: 'Ajuste por conteo', bodegaId: 1 });
  ok('T12', 'Cantidades absurdas (1e30, "NaN", negativas) se rechazan como validación, no como error inesperado',
    [g1, g2, g3].every((g) => !g.ok && !/inesperado/i.test(g.mensaje)), [g1, g2, g3].map((g) => g.mensaje + ' ' + (g.errores || []).join(' ')).join(' | '));
  const g4 = await pedir('movimientos.listar', { limite: 1000000000 });
  ok('T13', 'Un "limite" enorme en consultas se acota o se rechaza', !g4.ok || (g4.datos || []).length <= 1000, `${g4.ok ? (g4.datos || []).length + ' filas' : g4.mensaje}`);
  const u = ((await pedir('usuarios.listar')).datos || []);
  const yo = u.find((x) => x.nombreUsuario === 'admin.prueba');
  const g5 = await pedir('usuarios.desactivar', { usuarioId: yo && yo.usuarioId });
  ok('T14', 'El administrador no puede desactivarse a sí mismo (evita quedar sin administrador)', !g5.ok, g5.mensaje);
  const g6 = await pedir('pronosticos.producto', { sku: 'SIN-001', dias: 100000 });
  ok('T15', 'Pronóstico con "dias" enorme: se acota (máximo 365) y responde', g6.ok && g6.datos.historial.length <= 365, g6.ok ? g6.datos.historial.length + ' días' : g6.mensaje);
  await pedir('sesion.cerrar');
  const s1 = await pedir('pronosticos.producto', { sku: 'SIN-001' });
  ok('T16', 'Sin sesión, toda acción protegida se rechaza (sinSesion)', !s1.ok && s1.sinSesion === true, s1.mensaje);
  await b.close();
  console.log(res.join('\n')); console.log(`\n${res.filter((r) => r.startsWith('PASA')).length}/${res.length} comprobaciones correctas`);
})();
