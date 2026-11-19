# Informe de pruebas — revisión integral de FerroSuite

> **Actualización (cierre, 18-nov-2026):** después de esta revisión se corrigieron D-01, D-03, D-04 y D-06; la revisión integral pasó a 29/29 y las verificaciones estáticas a 18/19. Estado vigente en [INFORME_FINAL_FERROSUITE.md](../INFORME_FINAL_FERROSUITE.md).


**Fecha de ejecución:** 18 de noviembre de 2026 · **Versión revisada:** 0.5.0 (Etapas 1-6) · **Datos:** todos SINTÉTICOS

Este informe registra qué se probó, cómo y qué pasó. Una prueba solo figura como **APROBADA** si se ejecutó en esta
revisión y su registro está en `evidencias/revision/`. Lo que este entorno no permite ejecutar figura como
**PENDIENTE DE VERIFICACIÓN**, con los pasos para completarlo.

## 1. Resumen

| Conjunto | Ejecutadas | Pasan | Fallan | Registro |
|---|---:|---:|---:|---|
| Compilación (5 proyectos + código de la ventana en Debug y Release) | 7 | 7 | 0 | `01_compilacion.txt` |
| Compilación de la solución completa con los paquetes reales | — | — | — | Pendiente (`01b_compilacion_solucion_completa.txt`) |
| Pruebas automáticas en memoria (backend real, repositorio en memoria) | 143 | 143 | 0 | `02_pruebas_en_memoria.txt` |
| Recorrido general en navegador (pantallas reales + backend real) | 28 | 28 | 0 | `03_recorrido_general.txt` |
| Pantalla de Inteligencia Artificial (con modelo / sin modelo) | 20 | 20 | 0 | `04_…`, `05_…` |
| **Revisión integral nueva** (CRUD, inventario, órdenes, reportes, fallas, duplicados, red) | 29 | 26 | **3** | `06_revision_integral.txt` |
| Predictor que falla | 4 | 4 | 0 | `07_predictor_con_falla.txt` |
| Sin acceso a internet (pruebas en memoria, pantalla de IA, revisión integral) | 189 | 186 | 3* | `sin_red/` |
| Verificaciones estáticas (referencias, rutas, claves, duplicados, dependencias) | 19 | 17 | **2** | `11_verificaciones_estaticas.txt` |
| Suite SQL Server (`-- --bd`) | 0 de 51 | — | — | **Pendiente** (no hay SQL Server en este entorno) |

\* Son las mismas 3 fallas de la revisión integral, que no dependen de la red.

**Resultado:** sin internet, el programa funciona igual (inventario, órdenes, modelo ONNX, pronóstico y asistente
con reglas). Se encontraron **8 defectos** (sección 5): 1 alto, 4 medios y 3 bajos. No se corrigió ninguno en esta
revisión, porque se pidió solo la revisión. **Quedan pendientes** la compilación con los paquetes reales,
la persistencia en SQL Server y el arranque de la ventana en Windows (sección 6).

## 2. Entorno y alcance

| Elemento | En esta revisión | En el programa real |
|---|---|---|
| Sistema | Linux (contenedor), .NET SDK 10.0.115 | Windows 10/11 |
| Ventana | No se puede abrir (WinForms y WebView2 solo existen en Windows). El código de la ventana se **compila** con WinForms de Mono | WinForms + WebView2 |
| Pantallas | Las mismas, en Chromium (Playwright), con la herramienta `FerroSuite.Pruebas servidor`, que pasa cada mensaje al `Puente` real | WebView2 con `https://ferrosuite.example` |
| Backend | `Puente`, servicios, autorización, auditoría y pronóstico **reales** | Igual |
| Persistencia | Repositorio en memoria (`AlmacenMemoria`), con la misma interfaz que el de SQL | SQL Server LocalDB |
| Modelo | `demanda_mlp.onnx` real, con ONNX Runtime nativo 1.29.0 (biblioteca administrada de una versión anterior: el paquete NuGet no se puede descargar aquí) | Paquete `Microsoft.ML.OnnxRuntime` 1.29.0 |
| Paquetes NuGet | Sin acceso a nuget.org: `Microsoft.Data.SqlClient` se sustituye por una versión mínima (stub) y Identity se toma del marco ASP.NET Core | Paquetes reales |
| Red | Normal, y un **espacio de red aislado** (`unshare -rn`) sin ninguna salida a internet | — |

Fallas simuladas, solo en la herramienta de prueba:
- **Base de datos caída:** el repositorio en memoria lanza una `DbException`, la misma clase base de la que hereda `SqlException`.
- **Backend sin respuesta:** el `Puente` no contesta.
- **Predictor que falla:** un doble de prueba que lanza una excepción al calcular.

Para estas pruebas se agregaron:
- `--modelo-falla` y `POST /__control` en el servidor de prueba;
- la prueba G6;
- los scripts `tests/pantallas/revision.js`, `tests/pantallas/revision_predictor.js`, `tests/revision/verificaciones_estaticas.py` y `tests/revision/ejecutar_revision.sh`.

**No se modificó código del programa.**

## 3. Cómo reproducir

```bash
# Linux (como en esta revisión; requiere el entorno de verificación descrito en docs/compilacion.md)
tests/revision/ejecutar_revision.sh evidencias/revision          # todo
tests/revision/ejecutar_revision.sh evidencias/revision estatica # una parte: compilacion | memoria | navegador | sin-red | estatica
# Windows
dotnet build FerroSuite.slnx
dotnet run --project tests/FerroSuite.Pruebas            # 143 pruebas
dotnet run --project tests/FerroSuite.Pruebas -- --bd    # 51 pruebas con SQL Server LocalDB
```

Cada registro empieza con la fecha, el sistema y la versión de .NET. Cada línea de prueba dice
`PASA | ID | qué se comprueba — detalle medido` o `FALLA | …`. Los registros del servidor de prueba (traen las claves
aleatorias de la ejecución) se borran al terminar.

## 4. Resultados por área

Estados: **APROBADA** = ejecutada y correcta · **FALLIDA** = ejecutada e incorrecta · **PENDIENTE** = no ejecutable aquí.
La columna *Pruebas* remite a los identificadores de los registros, donde está cada comprobación individual con su detalle.

### 4.1 Compilación de la solución .NET

| ID | Propósito | Entradas | Resultado esperado | Resultado real | Estado |
|---|---|---|---|---|---|
| RV-01 | Compilar las capas y las pruebas | `dotnet build` de Dominio, Aplicación, Infraestructura, Comunicación y Pruebas (con los sustitutos de la sección 2) | 0 errores, 0 advertencias | 0 errores y 0 advertencias en los 5 proyectos (`01_compilacion.txt`) | APROBADA |
| RV-02 | Compilar el código de la ventana | `VentanaPrincipal.cs` y el resto del proyecto de escritorio, con WinForms de Mono, Debug y Release | 0 errores, 0 advertencias | 0 y 0 en Debug y Release. Solo prueba que compila; no que funcione | APROBADA |
| RV-03 | Compilar la solución completa con los paquetes reales | `dotnet build FerroSuite.slnx` | Compila | No ejecutable aquí: `NETSDK1100` (proyecto para Windows) y `NU1101` (sin acceso a nuget.org para WindowsDesktop, Identity y OnnxRuntime) (`01b_…`) | PENDIENTE |
| RV-04 | Archivos que se copian junto al ejecutable | Evaluación de MSBuild del proyecto de escritorio | Pantallas, migraciones, configuración y modelo | 48 archivos: 41 de `frontend/`, 4 scripts SQL, `appsettings.json`, `demanda_mlp.onnx` y sus parámetros; todos los orígenes existen | APROBADA |

### 4.2 Inicio del programa

| ID | Propósito | Entradas | Resultado esperado | Resultado real | Estado |
|---|---|---|---|---|---|
| RV-05 | Arranque completo | Abrir `FerroSuite.exe` | Carga WebView2, prepara la base, pide crear el administrador si no existe y muestra el ingreso | No ejecutable (requiere Windows) | PENDIENTE |
| RV-06 | Configuración al arrancar | Archivo ausente, mal escrito, valores fuera de rango, nombres de base inválidos | Valores por defecto y aviso, sin cerrarse | K1-K8 pasan | APROBADA |
| RV-07 | Arranque sin LocalDB | LocalDB detenido | Página «No se pudo conectar con la base de datos» | Solo revisión de código (`VentanaPrincipal.cs` 165-176): el texto se codifica en HTML, pero muestra `ex.Message` (ver D-08) | PENDIENTE |

### 4.3 Inicio y cierre de sesión

| ID | Propósito | Entradas | Resultado esperado | Resultado real | Estado |
|---|---|---|---|---|---|
| RV-08 | Ingreso y protección de la cuenta | Clave correcta e incorrecta, usuario inexistente o inactivo, 5 fallos, 30 min sin actividad, 12 h, datos desmedidos | Mismo mensaje genérico; bloqueo; vencimiento; permisos del rol en la respuesta | A1-A9 (seguridad), E1, E2, E21, E22, R1 pasan | APROBADA |
| RV-09 | Cierre de sesión | Botón «Salir» | Vuelve al ingreso; el backend ya no tiene sesión; abrir una pantalla pide ingresar | R2: `sesion.actual` vacío y `productos.listar` → «Tu sesión no está iniciada o venció» | APROBADA |

### 4.4 Autorización por roles

| ID | Propósito | Entradas | Resultado esperado | Resultado real | Estado |
|---|---|---|---|---|---|
| RV-10 | Matriz rol × acción en el backend | 37 acciones × 4 roles y sin sesión, como mensajes directos al `Puente` | Cada rol ejecuta solo lo suyo; lo demás se rechaza y se audita | MZ0-MZ-SIN pasan. ADMIN 37; BODEGA 17 (rechaza 20); VENDEDOR 7 (rechaza 30); CONTADOR 11 (rechaza 26) | APROBADA |
| RV-11 | Pantallas por rol | Menús, botones y direcciones abiertas a mano | Solo lo permitido; «Sin acceso» en lo demás | E3-E13, E19, E28 e IA16 pasan | APROBADA |
| RV-12 | Servicios llamados sin pasar por el `Puente` | Eliminar productos, aprobar compras, crear usuarios y cambiar la configuración | Rechazo | D1 y D2 pasan | APROBADA |

### 4.5 CRUD administrativo y registro de productos y proveedores

| ID | Propósito | Entradas | Resultado esperado | Resultado real | Estado |
|---|---|---|---|---|---|
| RV-13 | Usuarios | Crear (BODEGA), desactivar, activar, restablecer la clave, desbloquear | Cada operación queda guardada; clave temporal mostrada una vez | U1-U8, E14, R20-R22 pasan | APROBADA |
| RV-14 | Productos | Crear `rev-001` (stock 12, mínimo 4), editar nombre y mínimo, intentar editar el stock, eliminar | SKU en mayúsculas; el stock solo cambia con movimientos; un producto con historial se desactiva | P1-P9, E17, R6, R8 pasan (el campo de stock queda bloqueado al editar) | APROBADA |
| RV-15 | Proveedores | Crear con NIT `000.000.003-2`, NIT inválido, desactivar y activar | NIT normalizado; DV validado; el historial se conserva | V1-V6, E18, R3, R4 pasan | APROBADA |
| RV-16 | Editar un proveedor desde la pantalla | Tabla de proveedores | Botón «Editar» (el backend tiene `proveedores.actualizar`) | R5: no hay botón para editar | **FALLIDA** (D-01) |
| RV-17 | Bodegas y categorías desde la pantalla | Inventario y Usuarios | Altas y cambios para el administrador | S7: `bodegas.crear` no tiene pantalla; categorías y edición de usuarios no tienen acción | **FALLIDA** (D-02) |

### 4.6 Entradas y salidas de inventario

| ID | Propósito | Entradas | Resultado esperado | Resultado real | Estado |
|---|---|---|---|---|---|
| RV-18 | Entradas y salidas con validación | Compra +8 y venta −5 desde la pantalla; salida de 500; decimales en varilla; bodega inexistente | 12 → 20 → 15; rechazo de lo inválido sin cambiar el stock | R9 (12 → 20 → 15, 3 movimientos con responsable), R10, M1-M7, E6, E10 pasan | APROBADA |
| RV-19 | Concurrencia | 20 salidas simultáneas con stock 10; 5 recepciones simultáneas | 10 salidas; 1 recepción; nunca stock negativo | X1 y X2 pasan (en memoria; la versión con SQL está en RV-20) | APROBADA |

### 4.7 Persistencia en SQL Server

| ID | Propósito | Entradas | Resultado esperado | Resultado real | Estado |
|---|---|---|---|---|---|
| RV-20 | Suite de base de datos | `-- --bd`: creación y migraciones (B1-B6), restricciones (I1-I12), transacciones (T1-T5), concurrencia (X1-X4), seguridad (Z1-Z8), pronóstico (F1-F4), reapertura (P1-P2), demostración (D1-D4), migración desde la versión 1 (L1-L6) | 51 pasan | No ejecutable: no hay SQL Server ni el paquete real `Microsoft.Data.SqlClient` | PENDIENTE |
| RV-21 | Falla de base de datos informada sin cerrar el programa | `DbException` en el repositorio (CM5) y base «caída» desde el navegador (R23) | Mensaje claro con referencia, sin detalles internos; se recupera | C5 y R23 pasan: «Revisa que SQL Server LocalDB esté disponible… Referencia: …»; al volver la base, todo responde | APROBADA (falla simulada) |

### 4.8 Generación y aprobación de órdenes de compra

| ID | Propósito | Entradas | Resultado esperado | Resultado real | Estado |
|---|---|---|---|---|---|
| RV-22 | Ciclo completo | Crear (10 u.), aprobar, recibir, pagar; crear y rechazar otra | Pendiente → Aprobada (con entrega esperada) → Recibida (stock 15 → 25) → Pagada; rechazada | R12-R16, O1-O12, E11, E12, E16, E19, E20 pasan | APROBADA |
| RV-23 | Doble clic al crear y al aprobar | Doble clic en «Crear orden» y en «Aprobar» | Una orden; una aprobación | R12: 1 orden. R13: se enviaron 2 solicitudes y el backend rechazó la segunda (ver D-07) | APROBADA |
| RV-24 | Borrador desde Inteligencia Artificial | «Crear borrador» y, ya creado, otro clic en el mismo botón | Una orden | R26: **2 órdenes** | **FALLIDA** (D-03) |

### 4.9 Consulta de reportes

FerroSuite no tiene un módulo de reportes exportables. Lo que existe son pantallas de consulta, y esas son las que se probaron.

| ID | Propósito | Entradas | Resultado esperado | Resultado real | Estado |
|---|---|---|---|---|---|
| RV-25 | Resúmenes | Inicio y Órdenes | Cifras iguales a las del backend | R17 (pantalla 5\|1\|3\|0 = backend) y R19 pasan | APROBADA |
| RV-26 | Historial de movimientos | Inventario | Movimientos con bodega y responsable | R11 y M6 pasan | APROBADA |
| RV-27 | Auditoría | Usuarios → auditoría (administrador) | Operaciones sensibles registradas, sin claves | R18, E15, AU1-AU4 pasan (ninguna de las 22 claves usadas aparece) | APROBADA |
| RV-28 | Pronóstico y reposición | Tabla de Pronósticos y pantalla de IA | Datos del backend | E24-E25 e IA2-IA6 pasan | APROBADA |

### 4.10 Carga del modelo ONNX

| ID | Propósito | Entradas | Resultado esperado | Resultado real | Estado |
|---|---|---|---|---|---|
| RV-29 | Carga verificada | `modelos/demanda_mlp.onnx` y sus parámetros | Huella SHA-256, parámetros y grafo correctos | PI1 pasa; la huella `08eb91cf…5cce` coincide con la registrada (`01_compilacion.txt`) | APROBADA |
| RV-30 | Modelo dañado, de otra versión o ausente | Byte alterado, `ventana_dias` 21, carpeta vacía | No disponible con motivo; el programa sigue con la regla de respaldo | PI5, PD7, IN1-IN3 pasan | APROBADA |
| RV-31 | Carga con el paquete NuGet real en Windows | `Microsoft.ML.OnnxRuntime` 1.29.0 | Igual que RV-29 | No ejecutable aquí (sección 2) | PENDIENTE |

### 4.11 Predicción de demanda

| ID | Propósito | Entradas | Resultado esperado | Resultado real | Estado |
|---|---|---|---|---|---|
| RV-32 | Equivalencia con el entrenamiento | 35 casos de entradas, 4 series y el recorrido SINT-001 exportados desde Python | Mismas entradas y salida | PF1-PF5, PI2, PI3 pasan: diferencia 0; 3,61/día (Python 3,6096) | APROBADA |
| RV-33 | Detalle por producto | Con historial, con días agotados, sin historial, SKU inválido | Valores del backend; respaldo informado | PD1-PD10 e IA3-IA6 pasan | APROBADA |

### 4.12 Generación de recomendaciones de reposición

| ID | Propósito | Entradas | Resultado esperado | Resultado real | Estado |
|---|---|---|---|---|---|
| RV-34 | Reglas de la recomendación | Consumo, stock, entrega, lo ya pedido, historial insuficiente | Días, urgencia, riesgo y cantidad; nunca crea ni aprueba órdenes sola | PU1-PU8, PD9, PI4, E26-E27, IA12-IA14 pasan | APROBADA |

### 4.13 Funcionamiento sin acceso a internet

| ID | Propósito | Entradas | Resultado esperado | Resultado real | Estado |
|---|---|---|---|---|---|
| RV-35 | Backend sin red | Las 143 pruebas en un espacio de red aislado (comprobado: «Network is unreachable») | Igual que con red | 143 de 143 (`sin_red/08_…`) | APROBADA |
| RV-36 | Pantallas sin red | Pantalla de IA y revisión integral sin red | Igual que con red; ninguna solicitud fuera del equipo | IA 17 de 17. Revisión 26 de 29, con las mismas 3 fallas, que no dependen de la red. R27: 0 solicitudes externas. S3: ningún recurso de internet | APROBADA |
| RV-37 | Asistente Gemini sin red | Clave configurada y servidor inalcanzable | Responde con reglas y dice el motivo, sin bloquearse | G6 pasa (0,0 s sin red; 0,3 s con red), G4 y AS2 pasan | APROBADA |
| RV-38 | Windows sin red | Equipo desconectado con WebView2 Runtime y LocalDB instalados | Funciona igual | No ejecutable aquí. Instalar WebView2 Runtime y LocalDB necesita internet una vez | PENDIENTE |

### 4.14 Manejo de errores de base de datos y del predictor

| ID | Propósito | Entradas | Resultado esperado | Resultado real | Estado |
|---|---|---|---|---|---|
| RV-39 | Base de datos caída | Ver RV-21 | Ver RV-21 | Ver RV-21 | APROBADA (falla simulada) |
| RV-40 | Errores reales de SQL | Transacción que falla a mitad, migración que falla, LocalDB detenido | Se deshace todo; mensaje claro | T1-T5 de `--bd`: no ejecutables | PENDIENTE |
| RV-41 | Backend sin respuesta al abrir una pantalla | `Puente` sin respuesta; abrir Inventario e Inicio | Tras la espera, aviso de que no hay comunicación | R24: Inventario muestra **«Sin acceso — Tu rol no tiene acceso…»** e Inicio no muestra ningún aviso. R25: al reconectar, se recupera | **FALLIDA** (D-04) |
| RV-42 | Error inesperado del backend | Excepción no prevista; respuesta de error en la pantalla de IA | Solo número de referencia; botón «Reintentar» | W4 e IA11 pasan | APROBADA |
| RV-43 | Predictor que falla al calcular | `--modelo-falla` | Regla de respaldo con motivo; falla auditada; el inventario sigue | PU8, PD6, RP1-RP4 pasan | APROBADA |

### 4.15 Verificaciones adicionales

| ID | Propósito | Entradas | Resultado esperado | Resultado real | Estado |
|---|---|---|---|---|---|
| RV-44 | Operaciones duplicadas | Acciones del `Puente`, envíos dobles, concurrencia | Sin duplicados | S4 (sin acciones repetidas), R7, R12, R13, X1, X2 pasan. Duplica: R26 (D-03) | APROBADA, salvo RV-24 |
| RV-45 | Referencias y rutas | Scripts, estilos y enlaces de las pantallas; acciones que piden las pantallas; archivos huérfanos; rutas de ejecución; enlaces de la documentación | Todo existe | S1, S2, S6, S8, S17, S18 y R28 pasan (0 recursos con 404); 0 advertencias de compilación; R29: sin errores de JavaScript | APROBADA |
| RV-46 | Acciones sin pantalla | Acciones del backend frente a lo que llaman las pantallas | Toda acción tiene uso | S7: `bodegas.crear` y `proveedores.actualizar` sin pantalla | **FALLIDA** (D-01, D-02) |
| RV-47 | Claves expuestas | Búsqueda de claves de Google, OpenAI y GitHub, claves privadas, contraseñas en cadenas de conexión y literales en todo el repositorio; configuración; auditoría | Ninguna | S10: 0 en código, configuración, SQL, `ml/` y documentación. S10b: en las pruebas solo hay 8 claves deliberadamente inválidas o centinelas. S11, S12, K8, K11, AU4 pasan. Ver D-05 sobre la clave de Gemini guardada en el equipo | APROBADA |
| RV-48 | Dependencias innecesarias | 4 paquetes NuGet y `ml/requirements.txt` | Cada dependencia se usa; capas sin dependencias indebidas | S13-S16 pasan. Los 4 paquetes se usan; el dominio no tiene dependencias; la aplicación no depende de SQL, ONNX ni WebView2; `onnxscript` lo exige el exportador `dynamo=True` | APROBADA |
| RV-49 | Trazabilidad de las pruebas | Identificadores de las suites | Únicos | S9: A1-A5 y C1-C3 repetidos en la suite en memoria; E26 dos veces en `recorrido.js`; 14 identificadores compartidos con la suite SQL | **FALLIDA** (D-06) |

## 5. Defectos pendientes

| ID | Severidad | Defecto | Cómo se reproduce | Evidencia | Corrección sugerida |
|---|---|---|---|---|---|
| D-03 | **Alta** | El borrador de orden de la pantalla de IA puede crear **órdenes duplicadas** | Generar borrador → «Crear borrador» → sin cerrar el panel, otro clic en «Crear borrador»: hay 2 órdenes pendientes del mismo producto | R26 (con y sin red) | Cerrar el panel y deshabilitar el botón tras crear; mostrar el enlace a la orden; avisar si ya hay una orden pendiente del mismo producto |
| D-04 | Media | Si el backend no responde al abrir una pantalla, se muestra **«Sin acceso — Tu rol no tiene acceso»** (Inventario y demás) o nada (Inicio), en vez de «No hay comunicación» | Backend sin respuesta → abrir `inventario.html` y esperar 10 s | R24 | En `main.js`, que `mostrarSesion` distinga «sin sesión» de «sin comunicación» y muestre `mostrarErrorPagina` con reintento; que los controladores no llamen a `sinAcceso()` en ese caso |
| D-01 | Media | No se puede **editar un proveedor** desde la pantalla, aunque el backend lo permite (`proveedores.actualizar`, prueba V5) | Proveedores → la tabla no tiene «Editar» | R5, S7 | Botón «Editar» que llene el formulario, como en Inventario |
| D-02 | Media | **CRUD administrativo incompleto:** bodegas sin pantalla (solo `bodegas.crear` en el backend, sin editar ni desactivar); categorías sin altas ni cambios; usuarios sin editar nombre ni rol | Buscar esas funciones en las pantallas del administrador | S7; lista de acciones del `Puente` | Agregar acciones y pantallas (ya figura en el README como pendiente) |
| D-05 | Media (seguridad) | La **clave de Gemini se guarda en texto plano** en `%LocalAppData%\FerroSuite\clave-gemini.txt` | Guardar la clave desde Opciones y abrir el archivo | Revisión de `ClaveGemini.cs` | Cifrarla con DPAPI (`ProtectedData`, ámbito del usuario) |
| D-06 | Baja | **Identificadores de prueba repetidos** (A1-A5, C1-C3; E26; 14 compartidos con la suite SQL): un informe por identificador es ambiguo | `python3 tests/revision/verificaciones_estaticas.py` | S9 | Renombrar (por ejemplo AS1-AS5 para el asistente, CM1-CM6 para la comunicación) y poner prefijo a la suite SQL |
| D-07 | Baja | Los botones de las filas (Aprobar, Recibir, Pagar, Activar…) no se deshabilitan mientras responde el backend: un doble clic envía dos solicitudes. El backend rechaza la segunda, así que no hay duplicado | Doble clic en «Aprobar» | R13 («2 solicitudes enviadas») | Deshabilitar el botón hasta la respuesta, como ya hace el formulario |
| D-08 | Baja | Si la base no se puede preparar al arrancar, la página muestra el **mensaje técnico de la excepción**. El resto del programa solo muestra un número de referencia | Revisión de código (`VentanaPrincipal.cs` 172-174); no ejecutado | — | Mostrar un texto fijo con la referencia y guardar el detalle en el registro |

**Observaciones (no son defectos):**
- No hay reportes exportables ni imprimibles (PDF o Excel). Si el curso los exige, es una funcionalidad nueva.
- Cuando el predictor falla al calcular, el aviso general dice «Modelo no disponible», aunque el detalle aclara «El modelo falló al calcular» (RP1).

## 6. Pruebas pendientes de verificación y cómo completarlas

En un equipo con Windows 10/11, Visual Studio 2026 (carga «Desarrollo de escritorio de .NET» y el componente «SQL Server Express LocalDB») y WebView2 Runtime:

1. **RV-03, compilación completa:** `dotnet build FerroSuite.slnx -c Release` → esperado: 0 errores. Anotar las advertencias.
2. **RV-20, RV-40, persistencia y errores reales de SQL:**
   1. `sqllocaldb start MSSQLLocalDB`.
   2. `dotnet run --project tests/FerroSuite.Pruebas -- --bd` → esperado `Total: 51 pasan, 0 fallan`.
   3. Guardar la salida en `evidencias/revision/windows_bd.txt`.
3. **RV-05, arranque:**
   1. F5 sobre `FerroSuite.Presentacion.Escritorio`.
   2. Verificar: «Preparando la base de datos…», creación del administrador (primera vez) y pantalla de ingreso.
   3. Ingresar y cerrar sesión; repetir los pasos de R1-R29 a mano en las pantallas clave; capturar pantallas.
4. **RV-07, arranque sin base:** `sqllocaldb stop MSSQLLocalDB`, abrir el programa → esperado: la página de error. Revisar D-08.
5. **RV-31, modelo con el paquete real:** en la misma suite (`dotnet run --project tests/FerroSuite.Pruebas`), PI1-PI3 deben pasar con `Microsoft.ML.OnnxRuntime` 1.29.0 de NuGet.
6. **RV-38, sin internet:**
   1. Desconectar la red.
   2. Abrir el programa y repetir ingreso, movimiento, orden y la pantalla de IA.
   3. Esperado: todo funciona; el asistente responde «con reglas».

## 7. Archivos de esta revisión

| Archivo | Tipo |
|---|---|
| `docs/informe_pruebas.md` | Este informe |
| `evidencias/revision/*.txt`, `sin_red/*` | Registros de ejecución |
| `evidencias/*.png` | Capturas regeneradas en esta revisión |
| `tests/pantallas/revision.js`, `tests/pantallas/revision_predictor.js` | Pruebas en navegador nuevas |
| `tests/revision/verificaciones_estaticas.py`, `ejecutar_revision.sh`, `interfaz_local.py` | Verificaciones estáticas y ejecución completa |
| `tests/FerroSuite.Pruebas/Program.cs` | Prueba G6 (sin internet); `--modelo-falla` y control de fallas del servidor de prueba |
| `tests/FerroSuite.Pruebas/Herramientas/ServidorPruebaFrontend.cs` | `POST /__control` (solo pruebas) |
