# Recorrido de pantallas en navegador (opcional)

`recorrido.js` prueba las pantallas reales con el backend real en Chromium (Playwright), usando la herramienta
`FerroSuite.Pruebas servidor`, que reemplaza a WebView2 por un servidor HTTP local de prueba. Datos sintéticos.

```bash
dotnet run --project tests/FerroSuite.Pruebas -- servidor src/FerroSuite.Presentacion.Escritorio/frontend
node tests/pantallas/recorrido.js        # requiere: npm i playwright
```

`pantalla_ia.js` recorre la pantalla de Inteligencia Artificial (17 pasos) y guarda las capturas de
`evidencias/`. Con `--sin-modelo` en el servidor y `MODO=sin-modelo` comprueba la pantalla sin el modelo (3 pasos):

```bash
dotnet run --project tests/FerroSuite.Pruebas -- servidor src/FerroSuite.Presentacion.Escritorio/frontend [--sin-modelo]
CLAVE_ADMIN=... CLAVE_BODEGA=... CLAVE_VENDEDOR=... [MODO=sin-modelo] node tests/pantallas/pantalla_ia.js
```

El servidor de prueba siembra historial **sintético** de 90 días: SIN-001 (ventas diarias con patrón semanal),
SIN-002 (agotada 6 días), SIN-007 (sin ningún movimiento) y SIN-008 (12 días de ventas, sin proveedor).

`revision.js` (35 pasos, incluye bodegas, categorías y edición de usuarios) y `revision_predictor.js` (4 pasos, servidor con `--modelo-falla`) son parte de la revisión
integral; `tests/revision/ejecutar_revision.sh` ejecuta todo y guarda los registros (ver `docs/informe_pruebas.md`).

No reemplaza la prueba en Windows con WebView2 y SQL Server.
