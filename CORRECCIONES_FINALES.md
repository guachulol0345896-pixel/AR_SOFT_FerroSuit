# Correcciones finales — FerroSuite ERP 0.5.0

**Fecha:** 18 de noviembre de 2026.

**Base:** [AUDITORIA_TECNICA_INDEPENDIENTE.md](AUDITORIA_TECNICA_INDEPENDIENTE.md). Se usa su numeración de defectos (D-00 a D-13).

**Entorno:** Linux, .NET SDK 10.0.115, Python 3.13 y Chromium. **Sin Windows ni SQL Server.**

Reglas de este documento:
- **VERIFICADO**: la prueba se ejecutó y está su registro.
- **NO PROBADO**: no se pudo ejecutar aquí; no se presenta como aprobado.
- No se modificó el Documento Maestro. No se usó Git.
- La arquitectura de cuatro capas se conserva, y la red neuronal no se reentrenó ni se reexportó.

## 1. Resumen

| Defecto | Gravedad | Estado | Prueba |
|---|---|---|---|
| D-00 Ejecución en Windows y SQL Server | Alta | **NO PROBADO** (requiere Windows) | Ver §4 |
| D-01 Clave de Gemini en texto plano | Media | **Corregido** | GK1-GK5: VERIFICADO con un protector de prueba. DPAPI real: NO PROBADO |
| D-02 Carpeta de datos de WebView2 junto al ejecutable | Media-Alta | **Corregido** | Compila (Debug y Release). Ejecución: NO PROBADO |
| D-03 Arranque sin manejo general de errores | Media-Alta | **Corregido** | Compila. Registro técnico: VERIFICADO (C1). Manejadores en ejecución: NO PROBADO |
| D-04 CRUD administrativo incompleto (bodegas, categorías, edición de usuarios) | Media | **Corregido** | CA1-CA5, MZ (43 acciones), R30-R35: VERIFICADO. SQL AD1-AD3: NO PROBADO |
| D-05 Documentación de valores visibles para bodega | Media | **Corregido** (documentación) | T5: VERIFICADO |
| D-06 Mensaje técnico al fallar la base al arrancar | Baja-Media | **Corregido** | Compila. Ejecución: NO PROBADO |
| D-07 JSON con propiedades repetidas → «error inesperado» | Baja | **Corregido** | CM7, T9: VERIFICADO |
| D-08 Botones de fila sin bloqueo durante la solicitud | Baja | **Corregido** | R13 y R30 (doble clic = una solicitud): VERIFICADO |
| D-09 Auditoría perdida sin rastro si falla la base | Baja | **Corregido** | Escritura en el registro técnico: VERIFICADO (C1). El camino de respaldo de la auditoría: sin prueba propia |
| D-10 Ruta de entrenamiento en los metadatos del `.onnx` | Baja | **Pendiente** (decisión) | Corregirlo exige reexportar y cambiar la huella del modelo entregado |
| D-11 Sin reportes exportables | Baja (alcance) | **Pendiente** | Funcionalidad nueva, fuera de esta etapa |
| D-12 Aviso de privacidad de Gemini | Baja | **Corregido** | Texto en el diálogo y en el README. Compila |
| D-13 Compilación con sustitutos de paquetes | Informativa | Sin cambio | La cubre D-00 |

## 2. Detalle por defecto

### D-02 y D-03 — Arranque de WebView2 y errores no controlados

- **Archivos:**
  - `src/FerroSuite.Presentacion.Escritorio/VentanaPrincipal.cs`
  - `src/FerroSuite.Presentacion.Escritorio/Program.cs`
  - `src/FerroSuite.Presentacion.Escritorio/RegistroTecnico.cs` (nuevo)
- **Causa:**
  - `EnsureCoreWebView2Async()` sin entorno crea la carpeta de datos junto a `FerroSuite.exe`; falla si esa carpeta no admite escritura.
  - Solo se capturaba `WebView2RuntimeNotFoundException`.
  - No había manejadores globales.
  - `Debug.WriteLine` no deja nada en Release.
- **Solución:**
  - `CoreWebView2Environment.CreateAsync(null, %LocalAppData%\FerroSuite\WebView2)`.
  - Captura general con un mensaje claro y un número de referencia.
  - `Application.ThreadException`, `AppDomain.UnhandledException` y `TaskScheduler.UnobservedTaskException` registran y avisan sin detalles técnicos.
  - Respuesta del `Puente` protegida con `try/catch`.
  - **Registro técnico** en `%LocalAppData%\FerroSuite\registros\ferrosuite-AAAAMMDD.log` (14 días). Todos los `Debug.WriteLine` del programa pasaron a `Trace.WriteLine`, que sí funciona en Release.
- **Pruebas:**
  - El código de la ventana compila en Debug y Release, 0 errores y 0 advertencias (`evidencias/revision/01_compilacion.txt`).
  - `RegistroTecnico` compilado en Release crea el archivo y registra la referencia, la pila y los mensajes de `Trace` (`evidencias/correcciones/C1_registro_tecnico.txt`).
  - **Arranque en Windows: NO PROBADO.**

### D-06 — Mensajes de arranque

- **Archivo:** `VentanaPrincipal.cs`.
- **Causa:** se mostraba `ex.Message` y se indicaba una carga de trabajo de Visual Studio equivocada.
- **Solución:**
  - Texto fijo con la instalación correcta (componente *SQL Server Express LocalDB*, `sqllocaldb start MSSQLLocalDB`) y un número de referencia; el detalle va al registro técnico.
  - Lo mismo para «No se pudo leer la lista de usuarios» y las operaciones del menú *Opciones*.
- **Prueba:** compila. Ejecución en Windows: NO PROBADO.

### D-01 y D-12 — Clave de Gemini

**Evaluación:** Gemini se **conserva como opcional**.
- El pronóstico no lo usa: las pruebas PI y PD pasan sin red.
- Sin clave, el asistente responde con reglas.
- No agrega paquetes NuGet: usa `HttpClient`.

Si el equipo decide retirarlo, hay que quitar o ajustar:

| Capa | Archivos y elementos |
|---|---|
| Infraestructura | `src/FerroSuite.Infraestructura/IA/Gemini/` (`ClienteGemini.cs`, `ClaveGemini.cs`, `ProtectorDpapi.cs`) |
| Aplicación | El puerto `IGeneradorTexto` y su uso en `AsistenteInventario.cs`. Las reglas de respaldo del asistente se pueden conservar |
| Ventana | Menús *Clave de Gemini…* y *Probar conexión* en `VentanaPrincipal.cs` |
| Configuración | Sección `Gemini` de `config/appsettings.json` |
| Pruebas | G1-G6, GK1-GK5, AS1-AS5 |

Las acciones `ia.*` del `Puente`, `asistente.js`, `panelAsistente.js` e `iaService.js` siguen funcionando con reglas aunque se retire Gemini.

- **Archivos:**
  - `src/FerroSuite.Infraestructura/IA/Gemini/ClaveGemini.cs`
  - `src/FerroSuite.Infraestructura/IA/Gemini/ProtectorDpapi.cs` (nuevo)
  - `VentanaPrincipal.cs` (diálogo)
  - `.gitignore`
- **Causa:** `File.WriteAllText` de la clave en `clave-gemini.txt`.
- **Solución:**
  - **DPAPI** de Windows (`CryptProtectData`/`CryptUnprotectData` de `crypt32.dll`), ámbito del usuario, con entropía propia y sin paquetes nuevos.
  - Se guarda en `clave-gemini.dat` con formato `DPAPI1:<base64>`.
  - El archivo antiguo en texto plano se **cifra y se borra** al leerlo.
  - Fuera de Windows **no se guarda** (se pide `GEMINI_API_KEY`); nunca se escribe en texto plano.
  - El diálogo avisa qué datos envía el asistente a Google.
- **Pruebas** (`evidencias/revision/02_pruebas_en_memoria.txt`), con un protector reversible de prueba:
  - GK1: archivo sin la clave en claro y lectura correcta.
  - GK2: migración del archivo antiguo.
  - GK3: sin DPAPI no se escribe nada.
  - GK4: borrar.
  - GK5: el protector real solo se declara disponible en Windows.
  - **La ida y vuelta con DPAPI real: NO PROBADO** (requiere Windows).

### D-04 — Bodegas, categorías y edición de usuarios

- **Archivos:**

  | Capa | Archivos |
  |---|---|
  | Aplicación | `Puertos/IRepositorioInventario.cs`, `Puertos/IRepositorioUsuarios.cs`, `Inventario/InventarioServicio.cs`, `Usuarios/UsuarioServicio.cs` |
  | Infraestructura | `Persistencia/RepositorioInventarioSql.cs`, `Persistencia/RepositorioUsuariosSql.cs` |
  | Comunicación | `Puente.cs` |
  | Pantallas | `inventario.html`, `usuarios.html`, `css/components.css`, `js/controllers/catalogos.js` (nuevo), `js/services/catalogosService.js` (nuevo), `js/controllers/inventario.js`, `js/controllers/usuarios.js`, `js/services/usuariosService.js` |
  | Pruebas | `tests/FerroSuite.Pruebas/Program.cs` (CA1-CA5), `Dobles/AlmacenMemoria.cs`, `Herramientas/PruebasSeguridad.cs` (matriz), `PruebasBaseDatos.cs` (AD1-AD3), `tests/pantallas/revision.js` (R30-R35) |

- **Solución:**
  - Acciones nuevas en el `Puente`, todas con permiso de administrador y denegadas al resto:
    - `bodegas.listarTodas`, `bodegas.actualizar` (`bodegas.gestionar`);
    - `categorias.listarTodas`, `categorias.crear`, `categorias.actualizar` (`productos.gestionar`);
    - `usuarios.actualizar` (`usuarios.gestionar`).
  - Reglas en los servicios:
    - nombres únicos;
    - no desactivar una bodega con mercancía ni la última activa;
    - no desactivar una categoría con productos activos;
    - no cambiar el propio rol;
    - siempre debe quedar un administrador activo.
  - Cada cambio queda en la auditoría.
  - Persistencia con SQL parametrizado.
  - No hizo falta una migración: las columnas ya existían en V002.
  - Pantallas: sección *Bodegas y categorías* en Inventario (crear, editar, activar o desactivar) y botón *Editar* en Usuarios (nombre y rol; no pide clave).
- **Pruebas:**
  - CA1-CA5: 5/5.
  - Matriz MZ con 43 acciones × 4 roles + sin sesión.
  - En navegador: R30-R35, 6/6. Incluyen el doble clic con una sola solicitud, el mensaje al intentar desactivar una bodega con mercancía y que bodega no ve la sección y el backend la rechaza.
  - SQL Server AD1-AD3 (guardar, restricción UNIQUE, persistencia tras reabrir): **escritas, NO PROBADAS**.
  - Capturas: `evidencias/correcciones/admin_bodegas_categorias.png` y `admin_editar_usuario.png` (Chromium).

### D-05 — Qué valores ve el encargado de bodega

- **Archivos:** `README.md` §8, `docs/seguridad.md`.
- **Causa:** la documentación decía que bodega no ve valores en pesos, pero el backend le envía el precio unitario de compra (a propósito, para crear órdenes).
- **Solución:** se documenta el comportamiento real: ve el precio unitario, pero no el valor total ni las cuentas por pagar. El código no cambió.
- **Prueba:** T5 en `evidencias/revision/07b_manipulacion_mensajes.txt` verifica precisamente eso.

### D-07 — JSON con propiedades repetidas

- **Archivo:** `Puente.cs`.
- **Causa:** `JsonObject` lanzaba `ArgumentException` con claves repetidas.
- **Solución:** `JsonDocumentOptions.AllowDuplicateProperties = false`: el mensaje se rechaza como «formato incorrecto».
- **Pruebas:** CM7 en la suite en memoria y T9 en los mensajes manipulados.

### D-08 — Botones de fila

- **Archivos:** `js/components/util.js` (nuevo `FerroSuite.util.ocupado`), `js/controllers/ordenes.js`, `proveedores.js`, `usuarios.js`, `inventario.js`, `catalogos.js`.
- **Solución:** el botón queda desactivado y muestra «Procesando…» hasta que el backend responde. Los errores se muestran en el aviso de la pantalla.
- **Pruebas:**
  - R13: doble clic en *Aprobar* = 1 solicitud (antes, 2).
  - R30: doble clic en *Desactivar bodega* = 1 solicitud.

### D-09 — Auditoría que no se pudo guardar

- **Archivo:** `src/FerroSuite.Aplicacion/Seguridad/AuditoriaServicio.cs`.
- **Solución:** si falla la escritura, el evento completo (ya sanitizado, sin claves) se escribe en el registro técnico con la marca `AUDITORÍA NO GUARDADA`.
- **Prueba:** C1 comprueba que `Trace` llega al archivo. Este camino de respaldo no tiene una prueba propia.

## 3. Red neuronal (sin cambios en el modelo)

- No se reentrenó ni se reexportó: la huella sigue siendo `08eb91cf…5cce`.
- Nueva comprobación de solo lectura, `tests/revision/equivalencia_modelo.py`, 6/6:
  - huella;
  - orden de las 48 entradas (parámetros = casos de C#);
  - ONNX reproduce los 35 casos de C# (diferencia 0) y el recorrido SINT-001 (3,6096);
  - PyTorch = ONNX en 3 418 muestras (diferencia máx. 7,6·10⁻⁶);
  - salidas no negativas.

  Registro: `evidencias/revision/12_equivalencia_modelo.txt`.
- En la suite C# pasan:
  - PF1-PF5 (características = Python);
  - PI1-PI6 (carga, equivalencia, recorrido, falla);
  - PD1-PD10 (detalle; producto sin historial con respaldo y motivo);
  - PU1-PU8 (recomendaciones).
- Sin internet: 154/154, 17/17 y 35/35 en un espacio de red aislado (`evidencias/revision/sin_red/`).

## 4. Resultados de pruebas tras las correcciones

| Prueba | Resultado | Registro (`evidencias/revision/`) |
|---|---|---|
| Compilación de Dominio, Aplicación, Infraestructura, Comunicación y Pruebas + código de la ventana (Debug y Release) | 0 errores, 0 advertencias | `01_compilacion.txt` |
| Solución completa con los paquetes NuGet reales | **NO PROBADO** | `01b_compilacion_solucion_completa.txt` |
| Pruebas en memoria | **154/154** (eran 143; nuevas: CA1-CA5, GK1-GK5, CM7) | `02_pruebas_en_memoria.txt` |
| Recorrido general | 28/28 | `03_recorrido_general.txt` |
| Pantalla de IA (con modelo / sin modelo) | 17/17 / 3/3 | `04_…`, `05_…` |
| Revisión integral | **35/35** (eran 29; nuevas: R30-R35) | `06_revision_integral.txt` |
| Predictor con falla | 4/4 | `07_predictor_con_falla.txt` |
| Mensajes manipulados al `Puente` | **16/16** | `07b_manipulacion_mensajes.txt` |
| Sin internet | 154/154 · 17/17 · 35/35 | `sin_red/` |
| Verificaciones estáticas | **19/19** (era 18/19; ahora toda acción tiene pantalla) | `11_verificaciones_estaticas.txt` |
| Equivalencia PyTorch ↔ ONNX ↔ C# | 6/6 | `12_equivalencia_modelo.txt` |
| **SQL Server LocalDB** (54 pruebas: las 51 anteriores + AD1-AD3) | **NO PROBADO** | — |
| **Ventana en Windows** (arranque, DPAPI, manejadores) | **NO PROBADO** | — |

**Sin regresiones:** todas las suites que existían antes siguen pasando.

## 5. Pendiente

1. **Ejecutar en Windows** (Visual Studio 2026, SQL Server Express LocalDB, WebView2 Runtime):
   - `dotnet build FerroSuite.slnx -c Release`;
   - `dotnet run --project tests/FerroSuite.Pruebas` (esperado 154);
   - `dotnet run --project tests/FerroSuite.Pruebas -- --bd` (esperado 54; guarda su resultado en `tests/resultados/`);
   - F5 para comprobar el arranque, la creación del administrador, las bodegas y categorías y la edición de usuarios;
   - guardar una clave de Gemini, comprobar que `clave-gemini.dat` no contiene la clave en claro y que se vuelve a leer al reabrir;
   - copiar el programa a una carpeta sin permiso de escritura y comprobar que arranca;
   - guardar las capturas en `evidencias/windows/`.
2. **D-10:** limpiar los metadatos del `.onnx` exige reexportar (cambia la huella y los casos de referencia). Se dejó así para conservar el modelo entregado.
3. **D-11:** reportes exportables (PDF o Excel), si el curso los exige.
4. Decisiones del equipo:
   - licencia del proyecto (no hay `LICENSE`);
   - URL definitiva del repositorio (pie de las pantallas);
   - conservar o retirar Gemini (§2).
