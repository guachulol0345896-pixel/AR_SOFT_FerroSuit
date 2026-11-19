# Auditoría técnica independiente — FerroSuite ERP 0.5.0

| | |
|---|---|
| Fecha | 18 de noviembre de 2026 |
| Alcance | Todo el repositorio: código C#, pantallas, migraciones SQL, red neuronal, configuración, pruebas y evidencias |
| Entorno | Linux, .NET SDK 10.0.115, Python 3.13, Chromium (Playwright). Sin Windows, sin SQL Server y sin acceso a nuget.org |
| Correcciones de código | **Ninguna** (solo diagnóstico) |

Estados de los componentes:
- **VERIFICADO**: lo ejecuté o lo revisé en esta auditoría y es correcto.
- **FALLÓ**: lo ejecuté o lo revisé y es incorrecto.
- **NO PROBADO**: no se puede ejecutar en este entorno. **Que algo aparezca en el README o en el informe anterior no lo vuelve VERIFICADO.**

Registros de esta auditoría: `evidencias/auditoria_independiente/`.
- `01`–`07` y `11`: volví a ejecutar las suites con el script del repositorio.
- `manipulacion_mensajes.js` y `.txt`: prueba nueva, escrita en esta auditoría, que manda mensajes alterados al `Puente` real.

## 1. Resumen ejecutivo

**Lo que funciona:**
- El backend, las reglas, la seguridad del `Puente` y la red neuronal funcionan y están respaldados por evidencia que **volví a ejecutar**:
  - 143/143 pruebas en memoria;
  - pantallas: 28/28, 17/17, 3/3, 29/29 y 4/4;
  - 15/16 manipulaciones de mensajes rechazadas o neutralizadas. La que «falla» es una incoherencia de documentación, no una fuga (D-05).
- Cada uno de los **104 identificadores de prueba** citados en `INFORME_FINAL_FERROSUITE.md` existe en los registros y pasa. La única excepción es S7, que el propio informe declara como fallida.

**Lo que no se ha comprobado:**
- La **aplicación de escritorio en Windows** no se ha ejecutado nunca.
- La **compilación con los paquetes NuGet reales** tampoco.
- La **persistencia en SQL Server** tampoco: las 51 pruebas están escritas, pero ninguna se ha corrido.

Para una entrega que se presenta como «programa de escritorio con SQL Server», esa es la **brecha principal**. Además
encontré dos riesgos de arranque en Windows sin cubrir (D-02 y D-03).

## 2. Estado por componente

| # | Componente | Estado | Evidencia o motivo |
|---|---|---|---|
| 1 | Compilación de Dominio, Aplicación, Infraestructura, Comunicación y Pruebas | **VERIFICADO** (0 errores, 0 advertencias) | `01_compilacion.txt`. Con sustitutos: SqlClient mínimo, Identity desde el marco de ASP.NET Core, biblioteca administrada antigua de ONNX Runtime |
| 2 | Código de la ventana (`VentanaPrincipal.cs`, `Program.cs`) | **VERIFICADO solo como compilación** | Compila con WinForms de Mono y las DLL de **WebView2 1.0.3856.49**; el proyecto pide 1.0.3967.48 |
| 3 | Solución completa con los paquetes reales (`FerroSuite.slnx`) | **NO PROBADO** | `NETSDK1100` y sin acceso a NuGet (`evidencias/revision/01b_…`). Los cuatro paquetes y versiones existen en nuget.org (comprobado por búsqueda web) |
| 4 | WinForms carga WebView2 y navega a las pantallas | **NO PROBADO** | Requiere Windows. Revisión de código: ver D-02 y D-03 |
| 5 | Pantallas ↔ backend C# (mensajes `{id, accion, datos}`) | **VERIFICADO** en Chromium con el `Puente` real | `03`, `04`, `06`. Con WebView2 real: NO PROBADO |
| 6 | Recursos copiados junto al ejecutable (48: 41 de frontend, 4 SQL, configuración, modelo y normalización) | **VERIFICADO** | Evaluación de MSBuild en `01_compilacion.txt` |
| 7 | Inicio y cierre de sesión, bloqueo, vencimiento, cambio obligatorio de clave | **VERIFICADO** | `02` (A1-A9, U), `06` (R1, R2) |
| 8 | Hash de claves (Identity V3, PBKDF2-HMAC-SHA512, 210 000 iteraciones, mínimo 10 caracteres) | **VERIFICADO** (código) | `HasherIdentity.cs`, `UsuarioServicio.cs:9`; pruebas H y K |
| 9 | Permisos en el backend (37 acciones × 4 roles + sin sesión) | **VERIFICADO** | `02` (MZ, D1) |
| 10 | Mensajes manipulados desde JavaScript (motivo, tipo, usuario, estado, precio, acciones prohibidas, JSON malformado, límites) | **VERIFICADO** (15/16) | `manipulacion_mensajes.txt`. La comprobación que no pasa es D-05 (documentación) |
| 11 | Administrador: productos, proveedores (con edición), usuarios | **VERIFICADO** | `06` (R3-R8, R20-R22) |
| 12 | Administrador: bodegas y categorías; editar nombre o rol de un usuario | **FALLÓ** | Sin pantalla ni acción (D-04) |
| 13 | Vendedor: stock, ventas y devoluciones, sin costos | **VERIFICADO** | `02` (L1, L2), `03` (E3-E8), T1-T3 |
| 14 | Bodega: entradas, ajustes, órdenes y recepción | **VERIFICADO** | `03` (E9-E12), `06` (R9-R16), T4, T6, T7 |
| 15 | Contador: valores, órdenes y pagos | **VERIFICADO** | `02` (L4, O9), `03` (E19, E20), T8 |
| 16 | Consultas y reportes | **VERIFICADO** como pantallas de consulta (resumen, historial, auditoría, pronósticos). **No hay reportes exportables** (D-11) | `06` (R11, R17-R19) |
| 17 | Migraciones V001-V003: claves foráneas, CHECK, UNIQUE, disparadores que impiden modificar movimientos y auditoría | **VERIFICADO** (revisión de código) | Ver §4 |
| 18 | Transacciones y concurrencia en SQL (salida condicionada, recepción con cambio de estado condicionado, UPDLOCK) | **VERIFICADO** (código). Ejecución en SQL Server: **NO PROBADO** | `OperacionesStockSql.cs`, `RepositorioComprasSql.cs`. En memoria: X1, X2 |
| 19 | Persistencia tras reiniciar el programa en SQL Server | **NO PROBADO** | Las pruebas P1-P2 de `--bd` no se han ejecutado |
| 20 | Consultas parametrizadas | **VERIFICADO** (código) | No hay SQL armado con datos del usuario; todo pasa por `Sql.VarChar` y equivalentes |
| 21 | Operaciones duplicadas | **VERIFICADO** | `06` (R7, R12, R13, R26), `02` (X1, X2). Ver D-08 sobre los botones de fila |
| 22 | Mensajes de error sin detalles internos | **VERIFICADO**, salvo el arranque (D-06) y el JSON con claves repetidas (D-07) | `02` (W4, CM5), `06` (R23-R25) |
| 23 | Registro de auditoría sin claves | **VERIFICADO** | `02` (AU1-AU4). Ver D-09 (fallas de escritura silenciosas) |
| 24 | Clave de Gemini | **FALLÓ** (texto plano en el equipo del usuario) | D-01 |
| 25 | Red neuronal PyTorch (MLP 48 → 64 → 32 → 1) y scripts de entrenamiento | **VERIFICADO** | `ml/`. Reentrenamiento reproducible (`evidencias/modelo/`) |
| 26 | Separación cronológica sin fugas; normalización solo con entrenamiento | **VERIFICADO** | `ml/resultados/verificacion_preparacion.json`: las 10 comprobaciones booleanas en `true` (`todas_correctas`) |
| 27 | Modelo ONNX y normalización; carga verificada en C# | **VERIFICADO** con el ONNX Runtime nativo 1.29.0; con el paquete NuGet: **NO PROBADO** | `02` (PI1), huella SHA-256 en `01` |
| 28 | Mismas características en C# que en Python | **VERIFICADO** | `02` (PF1-PF5: 35 casos, diferencia 0; PI2) |
| 29 | La salida del modelo se usa para el riesgo y la reposición | **VERIFICADO** | `02` (PI3, PD1, PD9), `04` (IA3: 6,37/día de la red), `05` (respaldo con 5,9/día) |
| 30 | Respaldo con reglas (historial insuficiente, modelo ausente o con falla) | **VERIFICADO** | `02` (PU5-PU8, PD4, PD6, PD7), `05`, `07` |
| 31 | Predicción sin internet ni Gemini | **VERIFICADO** | `evidencias/revision/sin_red/` (143/143) |
| 32 | Métricas MAE y RMSE | **VERIFICADO** como cálculo; **no** como precisión en producción | Datos sintéticos (ver §5) |
| 33 | Evidencias del informe final | **VERIFICADO** | 104 identificadores cruzados con los registros |

## 3. Defectos, ordenados por prioridad para la entrega académica

| Prioridad | ID | Gravedad | Ubicación | Defecto | Causa probable | Corrección recomendada |
|---|---|---|---|---|---|---|
| 1 | **D-00** | **Alta** (brecha de verificación) | Toda la aplicación de escritorio y `tests/FerroSuite.Pruebas/PruebasBaseDatos.cs` | Nunca se ejecutaron en Windows la compilación completa, el arranque de la ventana ni las 51 pruebas de SQL Server. El programa se presenta como «escritorio + SQL Server» sin una sola ejecución de esas dos piezas | Desarrollo en un entorno sin Windows ni SQL Server | En Windows con VS 2026 y LocalDB: `dotnet build FerroSuite.slnx -c Release`; `dotnet run --project tests/FerroSuite.Pruebas -- --bd` (esperado 51/51); F5 y capturas de la ventana. Guardar todo en `evidencias/windows/` |
| 2 | **D-02** | **Media-Alta** | `VentanaPrincipal.cs:151` (`EnsureCoreWebView2Async()` sin entorno) | WebView2 crea su carpeta de datos junto al ejecutable (`FerroSuite.exe.WebView2`). Si el programa se copia a una carpeta sin permiso de escritura (`C:\Program Files`, una unidad de red o de solo lectura), WebView2 no arranca | No se indica `userDataFolder` | `CoreWebView2Environment.CreateAsync(null, Path.Combine(Environment.GetFolderPath(LocalApplicationData), "FerroSuite", "WebView2"))` y `EnsureCoreWebView2Async(entorno)` |
| 3 | **D-03** | **Media-Alta** | `VentanaPrincipal.cs:77`, `153-159`; `Program.cs:11-15` | Solo se captura `WebView2RuntimeNotFoundException`. Cualquier otra falla de WebView2 (acceso denegado, runtime dañado) escapa de un `async void` (`Load`) sin manejador global: WinForms muestra el cuadro de excepción no controlada o el programa se cierra | Manejo de errores incompleto en el arranque | Capturar `Exception` en `IniciarAsync` con un mensaje claro; registrar `Application.ThreadException` y `AppDomain.UnhandledException` en `Program.Main` |
| 4 | **D-01** | **Media** (seguridad) | `src/FerroSuite.Infraestructura/IA/Gemini/ClaveGemini.cs:13, 22, 40` | La clave de Gemini se guarda en texto plano en `%LocalAppData%\FerroSuite\clave-gemini.txt` | Falta cifrado | `ProtectedData.Protect`/`Unprotect` (DPAPI, `DataProtectionScope.CurrentUser`) y migrar el archivo existente |
| 5 | **D-04** | **Media** (funcional) | `Puente.cs:61, 221` (`bodegas.crear` sin pantalla); `inventario.html`/`inventario.js`; `usuarios.js`; `InventarioServicio`, `UsuarioServicio` | CRUD del administrador incompleto: no se pueden crear ni editar bodegas desde la pantalla; las categorías no tienen ninguna acción; no se edita el nombre ni el rol de un usuario | Alcance no terminado | Formulario de bodegas que use la acción existente; acciones y formularios de categorías; `usuarios.actualizar` (nombre y rol) con auditoría |
| 6 | **D-05** | **Media** (coherencia) | `README.md` §8 (fila Bodega: «No puede… ver valores en pesos»); `docs/seguridad.md`; `InventarioServicio.cs:56-61`; `AsistenteInventario.cs:52-55, 83-84` | La documentación dice que bodega no ve valores en pesos, pero el backend le envía `precioCompra` en `productos.listar` (a propósito, porque crea órdenes) y el asistente le muestra costos por proveedor. Prueba T5 | Decisión de diseño no reflejada en la documentación | Decidir: (a) documentar que bodega ve el **precio unitario de compra**, pero no el valor de bodega ni las cuentas por pagar; o (b) ocultar `precioCompra` a bodega y tomar el precio en el servidor (ya lo hace al crear la orden, T6) |
| 7 | **D-06** | **Baja-Media** | `VentanaPrincipal.cs:172-174` | Si falla la preparación de la base, se muestra `ex.Message` (detalle técnico) y se recomienda la carga «Desarrollo de ASP.NET y web». Con VS 2026 lo correcto es el componente «SQL Server Express LocalDB» | Texto heredado; política de errores no aplicada en el arranque | Texto fijo con un número de referencia; registrar el detalle; corregir la indicación de instalación |
| 8 | **D-07** | **Baja** | `Puente.cs:130-132` | Un mensaje con la clave `accion` repetida provoca «Ocurrió un error inesperado» en vez de «formato incorrecto». No salta permisos (T9) | `JsonObject` lanza `ArgumentException` con propiedades duplicadas, y solo se captura `JsonException` | Capturar `ArgumentException` como error de formato, o validar con `JsonDocument` antes de convertir |
| 9 | **D-08** | **Baja** | `js/controllers/ordenes.js:35` y botones de fila en `proveedores.js` y `usuarios.js` | Los botones de fila no se desactivan mientras responde el backend: un doble clic envía dos solicitudes. El backend rechaza la segunda (R13), así que no hay duplicado | El formulario se protege, las filas no | Desactivar el botón hasta recibir la respuesta |
| 10 | **D-09** | **Baja** (seguridad) | `src/FerroSuite.Aplicacion/Seguridad/AuditoriaServicio.cs:31-32` | Si no se puede escribir la auditoría (base caída), el evento se pierde sin aviso; solo queda en `Debug.WriteLine` | Diseño para no bloquear la operación | Registro local de respaldo (archivo en `%LocalAppData%`) y reintento |
| 11 | **D-10** | **Baja** | `ml/exportar_onnx.py:31` | El `.onnx` guarda en sus metadatos la ruta de la carpeta donde se entrenó, así que su SHA-256 cambia según la carpeta | El exportador `dynamo` guarda el rastro de llamadas en los metadatos de los nodos | Limpiar los `metadata_props` de los nodos antes de guardar, o documentarlo |
| 12 | **D-11** | **Baja** (alcance) | — | No hay reportes exportables (PDF o Excel) para el contador; solo pantallas de consulta | Fuera del alcance implementado | Aclararlo en la entrega o implementarlo en otra versión |
| 13 | **D-12** | **Baja** (privacidad) | `AsistenteInventario.cs:52-55, 83-84` | Con la clave de Gemini configurada, se envían a un servicio externo nombres de proveedores y costos | Diseño del asistente opcional | Avisar en *Opciones → Clave de Gemini* qué datos salen del equipo; permitir desactivarlo (ya existe `Gemini.Habilitado`) |
| 14 | **D-13** | **Informativa** | `~/verif` (fuera del repositorio) | La compilación de la ventana usó WebView2 1.0.3856.49 y la de Infraestructura usó sustitutos de SqlClient y de ONNX Runtime. La compatibilidad exacta con las API de los paquetes reales está **NO PROBADA** | Sin acceso a NuGet | Lo cubre D-00 |

## 4. Base de datos (revisión de código)

- **Relaciones:** cada tabla de detalle tiene su clave foránea:
  - `Producto` → `Proveedor`, `CategoriaProducto`;
  - `MovimientoInventario` → `Producto`, `Bodega`, `Usuario`;
  - `ExistenciaBodega` → `Producto`, `Bodega`;
  - `DetalleOrdenCompra` → `OrdenCompra`, `Producto`;
  - `Usuario` → `Rol`;
  - `AuditoriaEvento` → `Usuario`.

  V002 quita el `ON DELETE CASCADE` de V001 sobre los movimientos.
- **Integridad:**
  - CHECK de stock ≥ 0, cantidades > 0, unidades, estados de orden y pago coherente, formato de SKU y NIT, clave no vacía.
  - UNIQUE en nombres de usuario, categorías, bodegas y código de orden, y en (orden, SKU).
  - Disparadores `INSTEAD OF UPDATE, DELETE` que hacen inmodificables los movimientos y la auditoría.
- **Transacciones:**
  - Movimiento = existencia → total → registro, en una transacción y siempre en el mismo orden.
  - La salida usa un `UPDATE … WHERE Cantidad >= @cantidad` (no puede quedar negativa con salidas simultáneas).
  - La recepción cambia el estado con condición antes de sumar stock (una sola recepción).
  - El pago es condicionado.
- **Riesgo de diseño (no es un defecto):** el stock se guarda dos veces (`Producto.StockActual` y `ExistenciaBodega`). Siempre se actualizan en la misma transacción. La prueba I de `--bd` que lo comprueba en SQL está **NO PROBADA**.
- **Pruebas en memoria y en SQL:** las 143 pruebas usan `AlmacenMemoria`, con la misma interfaz que los repositorios SQL. Las 51 de `PruebasBaseDatos.cs` son las únicas que ejercitan SQL Server y están **NO PROBADAS**.

## 5. Red neuronal

**Datos y entrenamiento:**
- 7 scripts de flujo (más `ejecutar_todo.py`) y el paquete `ferrosuite_demanda/`.
- Semilla fija.
- Mejor época: 15.

**Métricas** (`metricas.json`, prueba con 3 418 muestras sintéticas):

| Método | MAE | RMSE |
|---|---:|---:|
| Red MLP | 2,427 | 6,300 |
| Promedio móvil de 28 días | 2,498 | 6,383 |
| Regla de 30 días | 2,674 | 6,797 |
| Promedio de 7 días | 3,229 | 8,536 |
| Ingenuo | 6,792 | 19,543 |
| Referencia (media verdadera) | 2,210 | 5,595 |

- La mejora frente a la regla es del 9,2 % en MAE.
- Frente al promedio móvil de 28 días, la mejora es **solo del 2,8 %**.
- Al ser datos sintéticos generados con patrones conocidos, **no sirven como evidencia de precisión en producción**.

**Equivalencia entre C# y Python:**
- Las 48 características dan diferencia 0 en 35 casos.
- La salida de ONNX en C# coincide con la de Python.
- El orden de las entradas viene del `.json` de normalización y se valida al cargar.

**Uso real:** `PronosticoServicio` usa el consumo de la red para:
- los días hasta agotarse;
- la urgencia y el riesgo;
- la cantidad sugerida.

Sin modelo, el mismo producto pasa de 6,37/día (red) a 5,9/día (regla) y la pantalla lo indica.

**Respaldo y sin internet:** VERIFICADOS.

## 6. Seguridad de la comunicación WebView2 ↔ C#

- Solo se aceptan mensajes desde `https://ferrosuite.example/`; la navegación a otros sitios se cancela y no se abren ventanas nuevas.
- Mensajes limitados a 64 KB y profundidad 16. Las acciones deben coincidir con un formato y estar en una lista.
- Todo se deniega por defecto.
- Con DevTools deshabilitado en Release.
- **Mensajes manipulados** (`manipulacion_mensajes.txt`):
  - cambiar `tipo` o `motivo` no da a un rol operaciones de otro;
  - `usuarioId` y `usuario` se toman de la sesión, nunca del mensaje;
  - `estado`, `pagada` y `precioUnitario` se ignoran al crear órdenes;
  - acciones prohibidas, JSON profundo o grande, cantidades absurdas y límites enormes se rechazan o se acotan;
  - el administrador no puede desactivarse a sí mismo.
- **Inyección de HTML:** todo dato que se muestra pasa por `esc()` o `textContent`, incluido el texto de Gemini. No hay CSP declarada, lo que es aceptable porque no se carga contenido externo.

## 7. Lista priorizada de archivos que deben corregirse

| Prioridad | Archivo | Defectos |
|---|---|---|
| 1 | *(ejecución en Windows, sin cambio de código)* → `evidencias/windows/` y `tests/resultados/` | D-00 |
| 2 | `src/FerroSuite.Presentacion.Escritorio/VentanaPrincipal.cs` | D-02, D-03, D-06 |
| 3 | `src/FerroSuite.Presentacion.Escritorio/Program.cs` | D-03 |
| 4 | `src/FerroSuite.Infraestructura/IA/Gemini/ClaveGemini.cs` | D-01 |
| 5 | `README.md` §8 y `docs/seguridad.md` (o `src/FerroSuite.Aplicacion/Inventario/InventarioServicio.cs` y `AsistenteInventario.cs`) | D-05 |
| 6 | `src/FerroSuite.Presentacion.Escritorio/frontend/inventario.html`, `js/controllers/inventario.js`, `js/controllers/usuarios.js`, `js/services/*`; `Puente.cs`; `InventarioServicio.cs`; `UsuarioServicio.cs` | D-04 |
| 7 | `src/FerroSuite.Presentacion.Comunicacion/Puente.cs` | D-07 |
| 8 | `frontend/js/controllers/ordenes.js`, `proveedores.js`, `usuarios.js` | D-08 |
| 9 | `src/FerroSuite.Aplicacion/Seguridad/AuditoriaServicio.cs` | D-09 |
| 10 | `ml/exportar_onnx.py` | D-10 |
| 11 | `INFORME_FINAL_FERROSUITE.md` y `README.md` | Actualizar con este diagnóstico: D-02, D-03, D-05 y D-07 a D-12 son nuevos; los defectos D-01 a D-08 del informe anterior conservan su numeración allí |

> **Numeración:** esta auditoría usa su propia numeración (D-00 a D-13). Equivalencias con el informe anterior:
> - D-01 de aquí = D-05 anterior (clave de Gemini);
> - D-04 = D-02 anterior;
> - D-06 = D-08 anterior;
> - D-08 = D-07 anterior.
>
> Los demás son hallazgos nuevos.
