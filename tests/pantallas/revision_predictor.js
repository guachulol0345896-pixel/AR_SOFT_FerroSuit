// Revisión: el predictor FALLA al calcular (servidor con --modelo-falla; doble de prueba que lanza una excepción).
// Comprueba que las pantallas de pronóstico usan la regla de respaldo, lo dicen, y que el inventario sigue operando.
// Uso: servidor con --modelo-falla; CLAVE_BODEGA=... node tests/pantallas/revision_predictor.js
const { chromium } = require('playwright');
const BASE = 'http://127.0.0.1:18766/';
const shim = `window.chrome = window.chrome || {}; (function(){ const ls=[];
  window.chrome.webview = { postMessage(m){ fetch('/__msg',{method:'POST',body:JSON.stringify(m)}).then(r=>r.status===204?null:r.json()).then(d=>{ if(d) ls.forEach(f=>f({data:d})); }).catch(()=>{}); },
  addEventListener(t,f){ if(t==='message') ls.push(f); } }; })();`;
const res = []; const ok = (id, n, c, d = '') => res.push(`${c ? 'PASA ' : 'FALLA'} | ${id} | ${n}${d ? ' — ' + d : ''}`);
(async () => {
  const b = await chromium.launch(); const ctx = await b.newContext({ viewport: { width: 1366, height: 900 } }); await ctx.addInitScript(shim);
  const page = await ctx.newPage(); const errores = []; page.on('pageerror', (e) => errores.push(e.message));
  const txt = async (s) => ((await page.locator(s).first().textContent().catch(() => '')) || '').replace(/\s+/g, ' ').trim();
  const backend = (a, d) => page.evaluate(([a, d]) => new Promise((ok) => { const id = Math.floor(Math.random() * 1e6);
    window.chrome.webview.addEventListener('message', (e) => { if (e.data && e.data.id === id) ok(e.data); }); window.chrome.webview.postMessage({ id, accion: a, datos: d }); }), [a, d]);
  await page.goto(BASE + 'login.html'); await page.fill('#form-login [name=usuario]', 'bodega.prueba'); await page.fill('#form-login [name=clave]', process.env.CLAVE_BODEGA);
  await page.click('#form-login button[type=submit]'); await page.waitForURL('**/index.html');
  await page.goto(BASE + 'pronosticos.html'); await page.waitForSelector('#tabla-pronostico table', { timeout: 15000 }).catch(() => {});
  const r = (await backend('pronosticos.reposicion')).datos;
  ok('RP1', 'Predictor que falla: la tabla de pronósticos responde con la regla de respaldo para todos y avisa que el modelo falló',
    r.falloModelo === true && r.porRedNeuronal === 0 && (await txt('#estado-modelo')).length > 0 && (await txt('#tabla-pronostico')).includes('Regla de stock mínimo'), await txt('#estado-modelo'));
  await page.goto(BASE + 'ia.html?sku=SIN-001'); await page.waitForSelector('#ia-resultado:not([hidden])', { timeout: 15000 }).catch(() => {});
  ok('RP2', 'Pantalla de IA con el predictor fallando: muestra "Regla de respaldo" y el motivo (el modelo falló)', (await txt('#ia-fuente')).includes('Regla de respaldo') && /fall/i.test(await txt('#ia-motivo')), await txt('#ia-motivo'));
  const mov = await backend('movimientos.registrar', { sku: 'SIN-001', tipo: 'Entrada', cantidad: 1, motivo: 'Ajuste por conteo', bodegaId: 1 });
  ok('RP3', 'Con el predictor fallando, el inventario sigue operando (registrar un ajuste)', mov.ok === true, mov.mensaje);
  ok('RP4', 'Sin errores de JavaScript', errores.length === 0, errores.join(' | '));
  await b.close(); console.log(res.join('\n')); console.log(`\n${res.filter((x) => x.startsWith('PASA')).length}/${res.length} pasos correctos`);
  process.exit(res.some((x) => x.startsWith('FALLA')) ? 1 : 0);
})();
