# Auditoría del repositorio original de FerroSuite

| Dato | Valor |
|---|---|
| Repositorio | `guachulol0345896-pixel/AR_SOFT_FerroSuite`, rama `master` |
| Versión auditada | commit `e4d2de1` ("Add files via upload", 4 de noviembre de 2026) |
| Fecha de la auditoría | 17 de noviembre de 2026 |
| Tipo | Solo lectura. Se clonó en una carpeta aparte y se trabajó sobre una copia. No se modificó ningún archivo ni se ejecutó ninguna operación Git que escriba datos. |

## 0. Método y alcance de la verificación

| Qué se verificó | Cómo | Resultado |
|---|---|---|
| Compilación de la solución real (`FerroSuite.slnx`) | `dotnet build` (SDK 10.0.115) | **No se pudo:** NuGet está bloqueado en este entorno (error NU1301). No es un error del código. |
| Compilación del backend (Modelos, Servicios, Datos, Comunicación, Configuración) | Proyecto net10.0 aparte que enlaza los archivos originales, con un sustituto de las clases de `Microsoft.Data.SqlClient` que solo sirve para compilar | **Compila, 0 errores y 0 advertencias** |
| Compilación de la ventana (`VentanaPrincipal.cs`, `Program.cs`) | net48 con Windows Forms de Mono y las DLL reales de WebView2 (paquete PyPI `pywebview` 6.2.1); sustitutos de las clases del backend | **Compila.** El único ajuste fue agregar `using System.Net.Http`, que en .NET 10 ya viene incluido |
| Lógica del backend | 35 pruebas propias que llaman al `Puente` original, con un almacenamiento en memoria y datos **sintéticos** | **35 de 35 pasan** (8 de ellas confirman hallazgos de este informe) |
| `ClienteGemini` | Contra un servidor HTTP **falso** local, no contra Gemini real | Lectura de respuestas, errores y tiempo agotado: correctos |
| Frontend junto al backend | Chromium (Playwright) con las pantallas originales; en lugar de WebView2, un puente HTTP de prueba | 14 de 14 recorridos correctos. La comprobación E3 falló una vez por un error de mi prueba; al corregirla se confirmó que funciona |
| JavaScript | `node --check` en los 23 archivos | Sin errores de sintaxis |
| Scripts SQL | Análisis con `sqlglot` en modo T-SQL (no se ejecutaron) | 10 lotes analizados sin error |
| Claves o secretos en el historial | Búsqueda en los 20 commits y en los .docx y .pdf | No se encontraron claves |

**Lo que NO se pudo verificar aquí, y por eso este informe no afirma que funcione:**
- la ejecución sobre SQL Server LocalDB: creación de la base, restricciones, transacciones, código calculado de las órdenes;
- la ventana real de Windows con WebView2;
- la conexión real con Gemini;
- el paquete NuGet real de WebView2 y de SqlClient.

El PDF `docs/Frontend enviando solicitud de registro.pdf` trae capturas del programa funcionando en Windows y de la tabla `Producto` vista en SQL Server. Es evidencia aportada por el equipo, no algo que yo haya verificado.

---

## 1. Estructura completa de carpetas y proyectos

```text
AR_SOFT_FerroSuite/            (152 archivos, ~12 MB)
├── FerroSuite.slnx            Solución: contiene SOLO FerroSuite.IA
├── FerroSuite.IA/             Proyecto WinExe net10.0-windows (WinForms + WebView2), AssemblyName "FerroSuite"
│   ├── Program.cs, VentanaPrincipal.cs, Configuracion.cs
│   ├── Comunicacion/Puente.cs
│   ├── Servicios/  (Inventario, Proveedor, Orden, CalculadoraReposicion, GeneradorRecordatorios, AsistenteInventario, ClienteGemini)
│   ├── Modelos/    (Entidades.cs, Contratos.cs)
│   ├── Datos/      (IAlmacenDatos, AlmacenSqlServer, BaseDatos)
│   ├── BaseDatos/  (crear_base_datos.sql, datos_prueba.sql)
│   ├── FerroSuite.IA.csproj, FerroSuite.IA.csproj.user, README.md
├── frontend/                  5 pantallas HTML, 4 CSS y 23 JS (los copia el .csproj junto al ejecutable)
├── FerroSuite.web/            Plantilla de ASP.NET Core Razor Pages SIN MODIFICAR. No está en la solución
│   └── wwwroot/lib/           Bootstrap, jQuery y jQuery Validation (unos 60 archivos de librerías)
├── docs/                      3 .md, 2 .docx, 1 .pdf, 6 imágenes
├── diagrams/                  2 PNG y un README vacío (1 byte)
├── README.md, .gitignore, .gitattributes
```

Proyectos: hay **uno activo** (`FerroSuite.IA`) y **uno huérfano** (`FerroSuite.web`). No hay proyecto de pruebas, ni `LICENSE`, ni `.github/workflows`.

## 2. Archivos realmente utilizados por la aplicación

| Usado | Archivos | Cómo se comprobó |
|---|---|---|
| Sí, se compila | Los 16 `.cs` de `FerroSuite.IA/` | El SDK compila todos los `.cs` de la carpeta del proyecto |
| Sí, se copia y ejecuta | `BaseDatos/*.sql` | `<None Update="BaseDatos\*.sql">` en el `.csproj`; los ejecuta `BaseDatos.Preparar()` |
| Sí, se copia y se carga | Los 5 HTML, los 4 CSS y los 23 JS de `frontend/` | `<Content Include="..\frontend\**\*">`. Cada archivo JS y CSS está referenciado por al menos un HTML (ninguno sobra) |
| No | Todo `FerroSuite.web/` | No está en `FerroSuite.slnx` y ningún archivo lo referencia |
| No (Visual Studio) | `FerroSuite.IA.csproj.user` | Archivo personal del IDE; `.gitignore` ya lo excluye, pero se subió por la web |
| Documentación | `docs/`, `diagrams/`, los README | No forman parte del programa |

Observación: las 5 páginas cargan los mismos 3 servicios (`inventarioService`, `proveedoresService`, `ordenesService`) y todos los componentes, aunque no los usen. No es un error, pero sí es carga innecesaria.

## 3. Código funcional que podemos recuperar

| Pieza | Por qué vale la pena | Evidencia |
|---|---|---|
| Canal pantalla ↔ backend (`api.js`, `Puente.cs`) | Mensajes `{id, accion, datos}` → `{id, ok, mensaje, datos, errores}`, con tiempo de espera y manejo de errores tipificado | Pruebas C1 a C4 y recorrido en navegador E1 a E14 |
| Validaciones de `InventarioServicio`, `ProveedorServicio` y `OrdenServicio` | SKU único, enteros o decimales según la unidad, coherencia entre motivo y tipo, stock suficiente, formato de NIT | P1 a P5, M1 a M4, V1, V2, O2, O3, O5 |
| `IAlmacenDatos` | Separa los servicios del motor de base de datos y permite probarlos sin SQL Server | Las 35 pruebas usan otra implementación sin cambiar los servicios |
| `AlmacenSqlServer` | Todas las consultas usan parámetros; el movimiento va en una transacción | Revisión de código (no ejecutado sobre SQL Server) |
| `CalculadoraReposicion` y `GeneradorRecordatorios` | Reglas claras; sirven como punto de comparación (*baseline*) y como respaldo de la red neuronal | A1 a A3 |
| Componentes del frontend (`tabla`, `formulario`, `aviso`, `util.esc`, etc.) | Reutilizables y escapan el texto antes de mostrarlo | Recorrido en navegador sin errores de consola (E14) |
| Estilos `frontend/css/` | Diseño coherente y adaptable a celular | Capturas del recorrido |
| `ClienteGemini` | Bien escrito (clave en el encabezado, tiempo límite, respaldo). Solo se conservaría si se quiere una explicación en texto **opcional** | G1 a G5 |

## 4. Funciones incompletas, duplicadas o con errores

Gravedad: **A** = alta, **M** = media, **B** = baja.

| ID | Gravedad | Dónde | Problema | Evidencia |
|---|---|---|---|---|
| E-01 | A | `crear_base_datos.sql:70` | El código de orden `'OC-' + RIGHT('000'+id, 3)` se repite desde la orden 1.000: la 1.001 vuelve a ser `OC-001`. No hay restricción `UNIQUE` y la orden se busca, aprueba y paga **por código** (`AlmacenSqlServer`, `UPDATE … WHERE Codigo`), así que se puede aprobar o pagar **la orden equivocada o varias a la vez** | Prueba O6 (misma fórmula; no ejecutada en SQL Server) |
| E-02 | A | `crear_base_datos.sql:60` + `InventarioServicio.EliminarProducto` | `ON DELETE CASCADE` borra el historial de movimientos al eliminar un producto sin órdenes. Contradice el RNF08 (los movimientos no se borran) | Revisión de código |
| E-03 | M | `AlmacenSqlServer.cs:125-149` | El stock se calcula en C# antes de la transacción y luego se escribe como valor absoluto (`SET StockActual = @stock`). Si dos movimientos llegan a la vez, uno se pierde. La pantalla de Inventario envía varias solicitudes en paralelo | Revisión de código |
| E-04 | M | `AlmacenSqlServer.cs:267` | El valor de una orden se calcula con el precio **actual** del producto, que no se guarda en la orden. Si cambia el precio, cambian el valor y la deuda de órdenes viejas | Prueba O4 (50.000 → 100.000) |
| E-05 | M | `OrdenServicio.cs:25` | Una orden acepta 2,5 varillas, pero un movimiento lo rechaza: la validación no es la misma | Prueba O1 |
| E-06 | M | `Puente.cs`, `IAlmacenDatos` | CRUD incompleto: no se pueden editar ni eliminar proveedores, ni cancelar o rechazar órdenes | Prueba V3 |
| E-07 | M | `crear_base_datos.sql` (Proveedor.Cumplimiento) | El RF10 (cumplimiento) es un número fijo de los datos de prueba; las órdenes nuevas no lo calculan, y no hay fecha de entrega ni de recepción | Revisión de código |
| E-08 | M | Modelo de datos | No existen `Usuario`, `Bodega`, `CategoriaProducto` ni `DetalleOrdenCompra` del documento maestro. Cada orden lleva un solo producto. Las categorías y unidades están fijas en C#, JS y en la restricción `CK_Producto_Unidad` (3 lugares que hay que mantener iguales) | `InventarioServicio.cs:12-14`, `data.js`, `crear_base_datos.sql:43` |
| E-09 | M | Recepción de órdenes | Pagar una orden no suma stock y no hay acción de "recibir"; la entrada se registra aparte, a mano | Revisión de código |
| E-10 | B | `InventarioServicio.cs:45` | Para mostrar 20 movimientos se traen los de 10 años (`ListarMovimientos(3650)`) y se recortan en memoria | Revisión de código |
| E-11 | B | `AsistenteInventario.cs:93` | "Fecha de hoy" se calcula en UTC: después de las 7 p. m. en Bogotá ya muestra el día siguiente | Revisión de código |
| E-12 | B | `GeneradorRecordatorios`, `OrdenServicio` | El plazo de pago de 15 días es fijo para todos los proveedores | Revisión de código |
| E-13 | B | Duplicación | Las listas de unidades, categorías y motivos están en C# y en `data.js`; las pluralizaciones están en `AsistenteInventario.Cantidad` y en `util.cantidad` | Revisión de código |
| E-14 | B | Comentarios obsoletos | "microservicio" en `Contratos.cs` y en `AsistenteInventario.cs:245`; "el asistente espera 30 s" en `api.js:9`, cuando son 75 s | `grep` |
| E-15 | B | `VentanaPrincipal.cs:103` | `async void` para los mensajes. Hoy no falla porque `Puente` captura todo, pero cualquier excepción futura cerraría el programa | Revisión de código |

## 5. Autenticación, permisos y seguridad

| ID | Gravedad | Hallazgo | Evidencia |
|---|---|---|---|
| S-01 | A | **No hay autenticación.** `login.js:22` guarda en `sessionStorage` el usuario y el rol que se escriban; la clave nunca se revisa. No hay tabla de usuarios ni hash de contraseñas | Recorrido E2 |
| S-02 | A | **No hay autorización en el backend.** El `Puente` ejecuta cualquier acción sin saber quién la pide: aprobar órdenes (RF09, solo el dueño), eliminar productos y `datos.restablecer`, que **borra toda la base** | Prueba S3, recorrido E13 |
| S-03 | A | **Ninguna página exige sesión.** Se entra a `inventario.html` sin ingresar | Recorrido E1 |
| S-04 | A | **La trazabilidad no es confiable.** El usuario de cada movimiento lo envía la pantalla (`inventarioService.js:62`) y el backend lo acepta tal cual | Pruebas S1, E7 |
| S-05 | M | **WebView2 sin restricciones** (`VentanaPrincipal.cs:97-99`): no se controla la navegación (`NavigationStarting`), no se revisa el origen de los mensajes (`e.Source`) y las herramientas de desarrollo (F12) quedan activas. Cualquiera puede ejecutar acciones del backend desde la consola, incluida `datos.restablecer` | Revisión de código |
| S-06 | M | El botón "Restaurar datos de prueba" está visible para todos (`inventario.html:80`) | Revisión de código |
| S-07 | M | La clave de Gemini se guarda en texto plano en `%LocalAppData%\FerroSuite\clave-gemini.txt` (`Configuracion.cs:40`). Se recomienda DPAPI | Revisión de código |
| S-08 | M | Datos enviados a terceros: el asistente manda a Google productos, precios, proveedores y órdenes. Contradice la decisión del documento maestro (sección 17: no enviar datos a terceros) | `AsistenteInventario.cs` |
| S-09 | B | Los mensajes de error genéricos muestran `ex.Message` al usuario (`Puente.cs:68`) | Revisión de código |
| S-10 | B | El backend guarda HTML en campos de texto sin limpiarlo. No hay XSS porque la pantalla escapa todo (`util.esc`), pero la defensa depende de un solo lado | Prueba S2 |
| — | OK | No hay inyección SQL (todo usa parámetros), no hay claves en el repositorio ni en el historial, y la clave de Gemini viaja en el encabezado y no en la URL | Revisión de código y prueba G2 |

## 6. Persistencia y conexión con SQL Server

| ID | Gravedad | Hallazgo |
|---|---|---|
| D-01 | A | **Se mezclan datos sintéticos con datos reales.** `datos_prueba.sql:10` inserta proveedores, productos y ventas inventadas **cada vez que la tabla Proveedor esté vacía**, también en la primera ejecución de una ferretería real. Además usa nombres y NIT con apariencia real ("Cementos Andinos SAS 900123456-7") sin decir que son inventados |
| D-02 | A | **No hay control de versiones del esquema.** El script solo crea lo que falta: si se agrega o cambia una columna, las bases ya creadas no se actualizan. El nombre `FerroSuite` es fijo, así que una versión nueva chocaría con la base de la versión vieja |
| D-03 | M | E-01, E-02, E-03 y E-04 (sección 4) son errores de persistencia |
| D-04 | M | `AddWithValue` envía los textos como `nvarchar` aunque las columnas sean `varchar` (`Sku`, `Nit`). Eso obliga a una conversión implícita y SQL Server no puede usar bien los índices |
| D-05 | M | Cada método abre su propia conexión. `ListarProductos` hace 4 consultas y `Resumen`, 6; al abrir Inventario se hacen unas 12. Funciona con poco volumen, pero no escala |
| D-06 | B | `Connect Timeout=30` y `Preparar()` en cada inicio: si LocalDB no está instalado, la ventana espera hasta 30 s antes de mostrar el error |
| D-07 | B | La cadena de conexión está fija en el código (LocalDB e Integrated Security, sin contraseñas, lo cual está bien). No se puede apuntar a otro servidor sin recompilar |
| D-08 | B | `DBCC CHECKIDENT` en `Restablecer()` necesita permisos altos. En LocalDB no es problema, pero sí en un SQL Server compartido |
| — | OK | Hay restricciones `CHECK`, claves foráneas e índices para las consultas frecuentes, y el script se puede ejecutar varias veces |

## 7. Dependencias innecesarias

| Dependencia | Estado | Recomendación |
|---|---|---|
| `FerroSuite.web/` (ASP.NET Core, Bootstrap 5, jQuery, jQuery Validation) | No se usa; son unos 60 archivos de librerías | Eliminar en el repositorio nuevo |
| `Microsoft.Web.WebView2 1.0.3967.48` | Necesaria. Esa versión existe (mayo de 2026) y ya hay versiones estables más nuevas (1.0.4314.35) | Conservar y actualizar a la estable más reciente |
| `Microsoft.Data.SqlClient 6.0.2` | Necesaria. Versión estable; ya salieron 6.1.x (la 6.1.0 fue retirada) | Conservar y evaluar 6.1.x más adelante |
| `ClienteGemini` y la integración con Gemini | La regla 6 pide que la predicción no dependa de Gemini | Quitar de la predicción; como mucho, explicación opcional y apagada por defecto |
| Hangfire, Razor Pages, GitHub Actions | Se mencionan en el README, pero **no existen en el código** | No agregar si no hacen falta; corregir el README |

## 8. Estado real de la integración de IA

| Aspecto | Estado real |
|---|---|
| Predicción | **Hoy no hay IA de predicción.** `CalculadoraReposicion` usa una regla fija: promedio de ventas de 30 días, días restantes = stock ÷ consumo, cantidad sugerida = consumo × (entrega + 15) + mínimo − stock |
| Gemini | Solo **redacta** textos (reposición, recordatorios, preguntas) a partir de cifras calculadas por las reglas. Si no hay clave, falla o tarda más de 60 s, responde con reglas (`RespuestasSinIA`) |
| Funcionamiento verificado | Con un servidor falso, `ClienteGemini` lee bien las respuestas, ignora las partes de "pensamiento" y maneja el error 400 y el tiempo agotado (G1 a G5). **Con Gemini real nunca se verificó:** el equipo reportó que tardaba más de 60 s en su red |
| Riesgos | Envío de datos del negocio a Google (S-08); la defensa contra instrucciones escondidas en las preguntas es mínima (solo delimitadores `<<<>>>`); depende de una API comercial (choca con la regla 6) |
| Datos para entrenar | **No sirven.** Los datos de prueba son ventas constantes cada 3 días durante 28 días, una línea plana sin estacionalidad ni ruido. No hay historial real |
| Red neuronal, PyTorch, ONNX | No existe nada |

Conclusión: la red neuronal hay que construirla desde cero. `CalculadoraReposicion` se conserva como punto de comparación y como respaldo cuando el modelo no esté disponible o falte historial.

## 9. Inconsistencias entre código, README y documentación

| Documento | Dice | Realidad |
|---|---|---|
| `README.md` (Arquitectura) | Presentación con Razor Pages (ASP.NET Core); Hangfire; GitHub Actions; "ERP en la nube" | Es WinForms + WebView2 de escritorio; no hay Hangfire, Razor Pages en uso ni workflows |
| `README.md` (Prototipo web) | "usa HTML, CSS y JavaScript sin instalar nada" | Las pantallas solo funcionan dentro del programa (`api.js` rechaza las solicitudes fuera de él) |
| `README.md` (Estructura) | `FerroSuite.web/`: "Proyecto base ASP.NET Core para el backend" | Es una plantilla vacía que no se usa |
| `docs/relacion-requerimientos-prototipo.md` | Endpoints `POST /api/ia/...` y `GET /api/salud` | No hay HTTP; son las acciones `ia.*` del Puente |
| Mismo documento, RF04 | "no deja eliminar productos con órdenes **pendientes**" | Bloquea con **cualquier** orden |
| Mismo documento, RF06 | La alerta aparece en `index.html` | Inicio solo muestra un contador; la alerta está en Inventario |
| Mismo documento, RF10 | "Columna de cumplimiento" | Es un valor fijo de los datos de prueba (E-07) |
| `index.html` | "control de existencias por bodega", "historial de cumplimiento" | No hay bodegas ni historial de cumplimiento |
| `docs/README.md` | Lista 3 documentos | No menciona `arquitectura-integracion.md`, `pruebas-integracion.md` ni el PDF |
| `docs/arquitectura-integracion.md` | Carpeta raíz `FerroSuite-erp/` | El repositorio se llama `AR_SOFT_FerroSuite` |
| `docs/pruebas-integracion.md` | Tabla de 4 pruebas | Las columnas "Resultado obtenido" y "Estado" están **vacías**. La "prueba previa" se hizo en memoria, no con SQL Server |
| `docs/FerroSuite_Documento_Maestro.docx` | Versión con las secciones 13 a 18 | Es anterior al documento maestro más reciente del equipo (secciones 18 a 28, con presupuesto y cronograma) |
| Comentarios del código | "microservicio", "30 s" | Ver E-14 |

## 10. Riesgos de compatibilidad al reorganizar la solución

| Riesgo | Por qué | Cómo evitarlo |
|---|---|---|
| Rutas del frontend | El `.csproj` copia `..\frontend\**` con una ruta relativa, y `VentanaPrincipal` busca `frontend` junto al `.exe` | Mantener la copia al directorio de salida y probar que la carpeta llegue |
| Scripts SQL | `BaseDatos.Preparar()` lee `BaseDatos\*.sql` junto al `.exe` | Mantener `CopyToOutputDirectory` o incrustarlos como recursos |
| Base de datos existente | Las máquinas del equipo ya tienen la base `FerroSuite` con el esquema viejo | Usar migraciones versionadas, o un nombre nuevo de base con un script de migración |
| Contrato de mensajes | Los nombres de las acciones y de las propiedades (camelCase por `JsonSerializerDefaults.Web`) los comparten `api.js` y `Puente` | Congelarlos y cubrirlos con pruebas antes de mover archivos |
| Espacios de nombres | Todo está bajo `FerroSuite.IA.*`. Al renombrar proyectos cambian los `using` | Renombrar de una sola vez y compilar; el JSON no se ve afectado |
| Separar proyectos | Hoy todo es `net10.0-windows`. Las bibliotecas nuevas deben ser `net10.0` (sin WinForms) para poder probarlas | Mover a `Core` solo lo que no use WinForms ni WebView2; ya está así salvo `VentanaPrincipal` |
| Carpeta de datos de WebView2 | Por defecto se crea junto al `.exe` (`FerroSuite.exe.WebView2`). En `Program Files` fallaría por permisos | Fijar `userDataFolder` en `%LocalAppData%\FerroSuite` al crear el entorno |
| Requisitos del equipo | .NET 10, Visual Studio 2026, LocalDB y WebView2 Runtime | Documentarlos en el README nuevo y mostrar un mensaje claro si falta algo (ya lo hace parcialmente) |
| ONNX Runtime en C# | Agregará un paquete nativo por plataforma (x64 o ARM64) | Fijar `RuntimeIdentifier`, o probar el empaquetado en Windows |
| Datos sintéticos | Si se mueven tal cual, se repite D-01 | Separar `datos_demo.sql` (solo en modo demostración) del esquema |

---

## 11. Clasificación de archivos

| Archivo o carpeta | Decisión | Motivo |
|---|---|---|
| `frontend/js/comunicacion/api.js` | **Conservar** | Canal sólido; solo corregir el comentario de 30 s |
| `frontend/js/components/*.js` (10) | **Conservar** | Reutilizables y con escape de texto |
| `frontend/css/*.css` (4) | **Conservar** | Diseño coherente |
| `frontend/js/main.js` | **Conservar** | Arranque común |
| `frontend/js/controllers/inventario.js`, `ordenes.js`, `proveedores.js` | **Modificar** | Mostrar u ocultar acciones según el rol, quitar "Restaurar datos", agregar editar y eliminar proveedor y cancelar orden |
| `frontend/js/controllers/inicio.js`, `asistente.js` | **Modificar** | Inicio según el rol; el asistente debe mostrar la predicción de la red neuronal y no depender de Gemini |
| `frontend/js/controllers/login.js` | **Reemplazar** | Debe autenticar contra el backend; no puede elegirse el rol |
| `frontend/js/services/*.js` (4) | **Modificar** | Quitar el envío del usuario (`inventarioService.js:62`), quitar `restablecerDatos` y agregar las acciones nuevas |
| `frontend/js/models/data.js` | **Reemplazar** | Las listas deben venir del backend o de la base, no estar repetidas |
| `frontend/*.html` (5) | **Modificar** | Cargar solo los scripts necesarios, textos fieles a lo que existe, quitar el botón de restaurar y agregar las vistas por rol |
| `FerroSuite.IA/Program.cs` | **Conservar** | Igual |
| `FerroSuite.IA/VentanaPrincipal.cs` | **Modificar** | Restringir la navegación, revisar el origen de los mensajes, desactivar F12 en Release, fijar la carpeta de datos de WebView2 y quitar `async void` |
| `FerroSuite.IA/Configuracion.cs` | **Modificar** | Cifrar con DPAPI lo que se guarde; configurar la cadena de conexión |
| `FerroSuite.IA/Comunicacion/Puente.cs` | **Modificar** | Agregar sesión y autorización por acción, no mostrar `ex.Message`, y nuevas acciones (usuarios, proveedores, predicción) |
| `FerroSuite.IA/Modelos/Entidades.cs` | **Modificar** | Agregar `Usuario` y `Rol`, el precio unitario en la orden y las fechas de entrega y recepción |
| `FerroSuite.IA/Modelos/Contratos.cs` | **Modificar** | Quitar los comentarios de "microservicio" y separar los contratos de la predicción |
| `FerroSuite.IA/Servicios/InventarioServicio.cs` | **Modificar** | El usuario sale de la sesión y no del mensaje; borrado lógico en lugar de cascada; no traer 10 años de movimientos |
| `FerroSuite.IA/Servicios/ProveedorServicio.cs` | **Modificar** | Agregar editar, desactivar y calcular el cumplimiento |
| `FerroSuite.IA/Servicios/OrdenServicio.cs` | **Modificar** | Validar enteros según la unidad, rechazar o cancelar, recibir y precio fijo |
| `FerroSuite.IA/Servicios/CalculadoraReposicion.cs` | **Conservar** | Punto de comparación y respaldo de la red neuronal |
| `FerroSuite.IA/Servicios/GeneradorRecordatorios.cs` | **Conservar** (con un ajuste menor) | Funciona; el plazo de pago debe ser configurable |
| `FerroSuite.IA/Servicios/AsistenteInventario.cs` | **Modificar** | Quitar Gemini de la predicción; conservar las respuestas con reglas; integrar la predicción ONNX |
| `FerroSuite.IA/Servicios/ClienteGemini.cs` | **Eliminar** de la ruta principal (como mucho, módulo opcional apagado) | Regla 6; el envío de datos a terceros choca con el documento maestro |
| `FerroSuite.IA/Datos/IAlmacenDatos.cs` | **Modificar** | Agregar métodos (usuarios, editar proveedor, órdenes) |
| `FerroSuite.IA/Datos/AlmacenSqlServer.cs` | **Modificar** | Actualizar el stock dentro de la misma operación, tipos de parámetro explícitos, `TOP` en las consultas y una sola conexión por operación |
| `FerroSuite.IA/Datos/BaseDatos.cs` | **Modificar** | Migraciones versionadas, cargar los datos demo solo en modo demostración, conexión configurable |
| `FerroSuite.IA/BaseDatos/crear_base_datos.sql` | **Reemplazar** (por migraciones `V001…`) | Corregir el código de orden, la cascada, agregar Usuario, PrecioUnitario y fechas |
| `FerroSuite.IA/BaseDatos/datos_prueba.sql` | **Reemplazar** | Marcarlos como SINTÉTICOS, usar NIT imposibles y cargarlos solo bajo demanda |
| `FerroSuite.IA/FerroSuite.IA.csproj` | **Reemplazar** | Partir en varios proyectos y actualizar WebView2 |
| `FerroSuite.IA/FerroSuite.IA.csproj.user` | **Eliminar** | Archivo personal del IDE |
| `FerroSuite.IA/README.md` | **Reemplazar** | Se reescribe según la nueva estructura |
| `FerroSuite.slnx` | **Reemplazar** | Nueva solución con todos los proyectos y las pruebas |
| `FerroSuite.web/` (todo) | **Eliminar** | Plantilla sin usar; los avisos de licencia de las librerías se van con ella porque no se redistribuyen |
| `README.md` | **Reemplazar** | Varias afirmaciones falsas (sección 9) |
| `docs/arquitectura-integracion.md` | **Modificar** | Base útil; actualizar las rutas y la estructura |
| `docs/pruebas-integracion.md` | **Reemplazar** | Pruebas automatizadas y manuales, con los resultados reales registrados |
| `docs/relacion-requerimientos-prototipo.md` | **Reemplazar** | Endpoints obsoletos y estados incorrectos |
| `docs/README.md` | **Modificar** | Índice completo |
| `docs/img/*` (6) | **Conservar** | Figuras del documento maestro |
| `docs/ERP.docx` | **Conservar** como histórico | Documento de las fases 1 y 2 |
| `docs/FerroSuite_Documento_Maestro.docx` | **Reemplazar** | Por la versión más reciente del documento maestro |
| `docs/Frontend enviando solicitud de registro.pdf` | **Conservar** como evidencia histórica | Capturas reales del equipo; dejarlas identificadas como evidencia de la versión anterior |
| `diagrams/*.png` | **Conservar** | Diagramas de las sesiones 3 y 4 |
| `diagrams/README.md` | **Modificar** | Está vacío (1 byte) |
| `.gitignore` | **Conservar** y agregar entradas | Agregar `*.onnx` grandes según la política, `ml/.venv/`, `ml/data/raw/` |
| `.gitattributes` | **Conservar** | Normalización de saltos de línea |

## 12. Plan de migración al repositorio nuevo (no ejecutado)

### 12.1 Estructura de destino

```text
FerroSuite/
├── FerroSuite.slnx
├── README.md, LICENSE (por decidir), .gitignore, .gitattributes
├── src/
│   ├── FerroSuite.Core/          net10.0 — Modelos, Servicios, validaciones, contratos, autorización
│   ├── FerroSuite.Datos/         net10.0 — IAlmacenDatos → AlmacenSqlServer, migraciones SQL
│   ├── FerroSuite.Prediccion/    net10.0 — carga y ejecución del modelo ONNX (Microsoft.ML.OnnxRuntime)
│   └── FerroSuite.Escritorio/    net10.0-windows — Program, VentanaPrincipal, Puente, frontend/
├── tests/
│   ├── FerroSuite.Core.Tests/    pruebas de servicios con almacenamiento en memoria (base: las 35 de esta auditoría)
│   └── FerroSuite.Datos.Tests/   pruebas contra LocalDB (solo en Windows)
├── ml/                           PyTorch: generación o carga de datos, entrenamiento, exportación ONNX, evaluación
└── docs/
```

### 12.2 Mapa de archivos

| Origen | Destino | Acción |
|---|---|---|
| `FerroSuite.IA/Modelos/*.cs` | `src/FerroSuite.Core/Modelos/` | Mover y modificar |
| `FerroSuite.IA/Servicios/{Inventario,Proveedor,Orden}Servicio.cs` | `src/FerroSuite.Core/Servicios/` | Mover y modificar |
| `FerroSuite.IA/Servicios/{CalculadoraReposicion,GeneradorRecordatorios}.cs` | `src/FerroSuite.Core/Reposicion/` | Mover (sin cambios lógicos) |
| `FerroSuite.IA/Servicios/AsistenteInventario.cs` | `src/FerroSuite.Core/Asistente/` | Mover y modificar (sin Gemini) |
| `FerroSuite.IA/Servicios/ClienteGemini.cs` | — | No se migra (o va a un módulo opcional, si ustedes lo deciden) |
| `FerroSuite.IA/Datos/IAlmacenDatos.cs` | `src/FerroSuite.Core/Datos/` | Mover (la interfaz pertenece al núcleo) |
| `FerroSuite.IA/Datos/{AlmacenSqlServer,BaseDatos}.cs` | `src/FerroSuite.Datos/` | Mover y modificar |
| `FerroSuite.IA/BaseDatos/*.sql` | `src/FerroSuite.Datos/Migraciones/V001__esquema.sql`, `Demo/datos_sinteticos.sql` | Reemplazar |
| `FerroSuite.IA/Comunicacion/Puente.cs` | `src/FerroSuite.Escritorio/Comunicacion/` | Mover y modificar |
| `FerroSuite.IA/{Program,VentanaPrincipal,Configuracion}.cs` | `src/FerroSuite.Escritorio/` | Mover y modificar |
| `frontend/**` | `src/FerroSuite.Escritorio/frontend/**` | Mover y modificar según la sección 11 |
| `docs/img/*`, `docs/ERP.docx`, PDF, `diagrams/*.png` | `docs/` (histórico en `docs/historico/`) | Copiar |
| `FerroSuite.web/`, `.csproj.user` | — | No se migran |

### 12.3 Orden propuesto (cada paso es una etapa que ustedes autorizan)

1. **Esqueleto:** solución, 4 proyectos, proyecto de pruebas y `.gitignore`. Copiar el código sin cambiar la lógica, solo los espacios de nombres. Comprobar que compila y que pasan las 35 pruebas de esta auditoría convertidas a xUnit.
2. **Correcciones de datos:** migraciones V001 y V002 (código de orden único, sin cascada, PrecioUnitario, fechas), stock actualizado dentro de la misma operación, datos demo separados y marcados.
3. **Seguridad:** tabla Usuario con hash PBKDF2, inicio de sesión en el backend, sesión en el Puente, autorización por acción y rol, y el endurecimiento de WebView2.
4. **Interfaces por rol:** administrador con CRUD completo; vendedor, bodega y contador con vistas operativas.
5. **Red neuronal:** datos (reales o sintéticos marcados), entrenamiento en PyTorch, exportación a ONNX, ejecución desde C#, comparación contra `CalculadoraReposicion` y respaldo.
6. **Pruebas y documentación:** pruebas contra LocalDB en Windows, README y documentos actualizados, evidencias reales.

### 12.4 Decisiones pendientes

- Enlace o nombre del repositorio nuevo.
- Licencia del proyecto (no hay `LICENSE` en el original).
- Si Gemini se elimina por completo o queda como explicación opcional y apagada.
- Fuente de datos para entrenar: historial real o datos sintéticos identificados como tales.
- Nombre de la base nueva (`FerroSuite` con migraciones o un nombre distinto para no chocar con la anterior).

## Anexo: archivos de verificación

Las pruebas usadas en esta auditoría están en `auditoria_pruebas.zip`:
- `Harness/`: proyecto net10.0 con el sustituto de SqlClient, el almacenamiento en memoria, 35 pruebas y el puente HTTP de prueba;
- `Ui/`: comprobación de compilación de la ventana;
- `e2e.js`: recorrido en navegador.

Ninguno de estos archivos forma parte del repositorio original. Todos los datos que usan son sintéticos.
