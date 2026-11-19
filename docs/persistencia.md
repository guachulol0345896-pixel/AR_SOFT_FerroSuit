# Persistencia (etapa 2): SQL Server LocalDB

> Etapa 3: la migración `V003__seguridad_y_auditoria.sql` agrega el hash de Identity, el bloqueo, el cambio obligatorio de
> clave y la tabla `AuditoriaEvento`; ver [seguridad.md](seguridad.md). Lo que sigue describe la etapa 2.

## 1. Punto de partida

**Esquema real que había** (`database/esquema/crear_base_datos.sql` de la versión 1, ahora `migraciones/V001`):

| Tabla | Clave | Problemas encontrados |
|---|---|---|
| `Proveedor` | `Nit` | Sin validación de formato ni dígito de verificación; no se podía editar ni desactivar. |
| `Producto` | `Sku` | Categoría como texto libre; un solo stock (sin bodegas); sin formato de SKU en la base. |
| `MovimientoInventario` | `MovimientoId` | El responsable era el texto que mandaba la pantalla (S-04); `ON DELETE CASCADE` borraba el historial al borrar el producto (E-02); el stock se calculaba en C# y se guardaba aparte (dos salidas simultáneas podían dejarlo mal, E-03). |
| `OrdenCompra` | `OrdenId` | Un solo producto por orden; el valor se calculaba con el precio **actual** del producto (E-04); el código `'OC-' + últimos 3 dígitos` se repetía desde la orden 1000 (E-01); cualquier pantalla podía aprobar o pagar. |

**Modelo del Documento Maestro**: Producto, Categoría, Bodega, Usuario, Rol, Proveedor, Movimiento de inventario,
Orden de compra y Detalle de orden, con claves foráneas entre ellos.

## 2. Análisis de compatibilidad y decisiones

Los equipos del grupo ya tienen una base `FerroSuite` creada por la versión 1. Por eso **no se reemplazó ninguna tabla**:
la migración `V002` agrega lo que falta, copia los datos a su nuevo lugar, comprueba que todo cuadre y solo entonces quita
las columnas que sobran.

| Tema | Decisión | Por qué |
|---|---|---|
| Claves de Producto y Proveedor | Se conservan `Sku` y `Nit` (claves naturales) en vez de `id_producto` / `id_proveedor` del Maestro. | Las bases existentes y todo el programa ya las usan; cambiarlas obligaría a reescribir todas las relaciones sin ganar integridad. **Diferencia consciente con el Maestro**, documentada en el propio script. |
| Categoría | Nueva tabla `CategoriaProducto`; `Producto.Categoria` (texto) pasa a `CategoriaId`. | Se cargan las 6 categorías de referencia y **además** cualquier categoría escrita en productos existentes, para no perder ninguna. |
| Bodegas | Nuevas tablas `Bodega` y `ExistenciaBodega` (stock por producto y bodega). | El stock existente pasa completo a "Bodega principal". `Producto.StockActual` se mantiene como total (lo usan el asistente y los reportes). |
| Usuarios y roles | Nuevas tablas `Rol` (4 roles del Maestro) y `Usuario` (clave como hash PBKDF2). | Sin usuarios ni claves por defecto: el primer administrador se crea en el programa la primera vez. |
| Responsable de cada movimiento | `MovimientoInventario.UsuarioId` (FK) obligatorio. Se conserva la columna de texto `Usuario` como copia histórica del nombre. | Los movimientos anteriores quedan asignados al usuario `registro.anterior` (inactivo, sin clave) y **conservan el texto original**. |
| Historial | Se quita el borrado en cascada y un disparador impide modificar o borrar movimientos. | Un producto con historial se **desactiva** (`Activo = 0`) en vez de borrarse. Los errores se corrigen con un movimiento de ajuste. |
| Detalle de orden | Nueva tabla `DetalleOrdenCompra` con `PrecioUnitario` guardado. | Cada orden antigua pasa a tener una línea con su producto y cantidad. Como esas órdenes no guardaban precio, se toma el precio de compra del momento de migrar (único dato disponible). |
| Código de orden | `OC-001 … OC-999` y número completo desde la 1000 (`OC-1000`), con índice único. | Las órdenes existentes conservan exactamente su código. |
| Estados de orden | `Pendiente de aprobación`, `Aprobada`, `Rechazada`, `Recibida`; fechas de aprobación, entrega esperada, recepción y pago. | Restricciones que impiden combinaciones incoherentes (pagar una orden pendiente, "Recibida" sin fecha ni bodega…). |
| Formato de SKU y NIT | `CHECK` en la base (formato) y validación completa en C# (incluido el dígito de verificación de la DIAN). | Si una base vieja tuviera valores fuera de formato, la regla se crea `WITH NOCHECK`: no toca esas filas pero se aplica a todo lo nuevo. |

## 3. Modelo resultante

```text
CategoriaProducto 1───* Producto *───0..1 Proveedor
                          │  │
            ExistenciaBodega  └──* DetalleOrdenCompra *───1 OrdenCompra *───1 Proveedor
              │                                                 │
           Bodega 1───* MovimientoInventario *───1 Usuario *───1 Rol
              └──────────────────────────── (bodega de recepción) ┘
MovimientoInventario *───1 Producto
VersionEsquema (control de migraciones)
```

| Tabla | Restricciones principales |
|---|---|
| `Producto` | PK `Sku`; FK categoría y proveedor; `CHECK` formato SKU; stock mínimo ≥ 0; precio ≥ 0 |
| `Proveedor` | PK `Nit`; `CHECK` formato NIT, días de entrega, nombre no vacío |
| `ExistenciaBodega` | PK (`Sku`, `BodegaId`); FK producto y bodega; `CHECK Cantidad >= 0` |
| `MovimientoInventario` | FK producto (sin cascada), bodega y usuario; tipo Entrada/Salida; cantidad > 0; disparador "solo insertar" |
| `Usuario` | Nombre de usuario único en minúsculas; clave: o no tiene, o tiene hash + sal + ≥ 100.000 iteraciones |
| `OrdenCompra` | FK proveedor y bodega de recepción; estados válidos; coherencia de pago y recepción; código único |
| `DetalleOrdenCompra` | FK orden y producto; (orden, producto) único; cantidad > 0; precio ≥ 0 |

## 4. Creación, inicialización y migración

- `BaseDatosSql.Preparar()` crea la base si no existe, aplica las migraciones pendientes (`Migrador`) y, solo en modo Demo,
  carga la demostración.
- `Migrador`: registra cada migración aplicada en `dbo.VersionEsquema` (versión, nombre, huella SHA-256, fecha). Cada
  migración corre en **una transacción con `XACT_ABORT ON`**; si falla, se deshace completa y el programa muestra el error.
  Si la base fue actualizada por una versión más nueva del programa, se detiene en lugar de dañarla.
- **Real y Demo separados**: `Modo` en `appsettings.json`. La base real (`FerroSuite`) nunca se borra ni recibe datos
  sintéticos; el programa se niega a borrarla aunque se le pida (comprobado antes de conectarse). La base de demostración
  (`FerroSuiteDemo`) se reconstruye desde cero con *Opciones → Reiniciar base de demostración…* (menú visible solo en modo
  Demo) y el resultado es siempre el mismo. El título de la ventana indica "DEMOSTRACIÓN (datos sintéticos)".
- Los datos de demostración están marcados (`DEMO-` en SKU, "DEMO" en proveedores y usuarios) y sus usuarios no tienen clave.

## 5. Transacciones y concurrencia

Toda entrada o salida sigue el mismo orden dentro de **una transacción** (`OperacionesStockSql`):

1. **Existencia de la bodega**. Salida: `UPDATE … SET Cantidad = Cantidad - @c WHERE … AND Cantidad >= @c` (una sola
   sentencia: si otra salida se llevó el stock un instante antes, esta no afecta filas y se informa "no hay stock suficiente").
   Entrada: `UPDATE` con `UPDLOCK, HOLDLOCK` y, si no había fila, `INSERT`.
2. **Total del producto** con la diferencia (`OUTPUT INSERTED.StockActual`).
3. **Movimiento**, con bodega, usuario de la sesión y referencia.

Si cualquier paso falla, se deshace todo. Siempre en el mismo orden para evitar bloqueos cruzados. Lo mismo para: crear un
producto con stock inicial (producto + existencia + movimiento "Inventario inicial"), crear una orden (encabezado + líneas)
y recibir una orden (cambio de estado condicional + una entrada por línea con el código de la orden).
Los cambios de estado de las órdenes son condicionales (`WHERE Estado = 'esperado'`): si dos personas aprueban o reciben la
misma orden a la vez, solo una lo logra.

Todas las consultas usan parámetros con tipo explícito (`VarChar`, `NVarChar`, `Decimal(12,2)`…); no se concatena ningún
dato del usuario en el SQL. El único SQL dinámico (`CREATE/DROP DATABASE`) usa `QUOTENAME` con el nombre como parámetro y
el nombre se valida antes (letras, números y `_`).

## 6. Usuario responsable y sesión

- El backend guarda la sesión (`SesionUsuario`) tras `sesion.iniciar`; la pantalla ya no envía quién hizo el movimiento.
- Claves (etapa 3): `PasswordHasher` de ASP.NET Core Identity; ver [seguridad.md](seguridad.md).
- Cada acción del `Puente` se comprueba contra el rol (`Aplicacion/Seguridad/Permisos.cs`): administrador todo; bodega
  movimientos, crear y recibir órdenes; vendedor movimientos; contador marcar pagos; todos pueden consultar.

## 7. Pruebas

| Suite | Cómo | Dónde corre |
|---|---|---|
| Unitarias e integración en memoria (84) | `dotnet run --project tests/FerroSuite.Pruebas` | Cualquier sistema |
| Pantallas en navegador (22) | `tests/pantallas/recorrido.js` con la herramienta `servidor` | Cualquier sistema con Node y Playwright |
| **SQL Server LocalDB (38)** | `dotnet run --project tests/FerroSuite.Pruebas -- --bd` | **Solo Windows con LocalDB** |

La suite `--bd` crea bases temporales `FerroSuite_Pruebas_<id>`, las borra al final y guarda el resultado en
`tests/resultados/resultados_bd_<fecha>.txt`. Comprueba:

- **Creación y migraciones**: base desde cero, 11 tablas, idempotencia, modo Real sin datos de demostración.
- **Integridad**: `CHECK` de SKU, NIT, existencias, iteraciones de clave, nombre de usuario y estado; FK sin cascada; el
  disparador impide modificar o borrar movimientos; detalle único por orden; índice único del código; un SKU con comillas no
  se ejecuta como SQL.
- **Transacciones fallidas**: un movimiento que falla después de descontar (usuario inexistente) no deja rastro; una salida
  sin stock no cambia nada; recibir una orden que falla a mitad deja la orden Aprobada y el stock igual; una migración que
  falla se deshace completa.
- **Concurrencia**: 20 salidas simultáneas con stock 10 → exactamente 10; 30 entradas/salidas mezcladas → stock = suma de
  existencias = suma de movimientos; 5 recepciones simultáneas de una orden → una sola; 20 órdenes en paralelo → 20 códigos.
- **Persistencia al cerrar y abrir**: nuevas conexiones e instancia; y **otro proceso** (como volver a abrir el programa)
  lee los mismos datos y lo que escribe queda guardado.
- **Migración de una base de la versión 1** (`database/pruebas/legado_v1.sql`): no se pierde ninguna fila, el stock queda en
  la bodega principal, las categorías se conservan, los movimientos conservan el texto del usuario, las órdenes conservan
  sus códigos y tienen su detalle.
- **Demostración**: separada de la real, marcada como DEMO y reproducible (dos reconstrucciones dan lo mismo).

## 8. Limitaciones conocidas

- La suite `--bd` y la ventana real **no se ejecutaron** en el entorno donde se preparó este código (Linux sin SQL Server).
  Deben ejecutarse en Windows; el archivo de resultados es la evidencia.
- Si la base `FerroSuite` de un equipo tiene los **datos sintéticos antiguos** (los que cargaba la versión 1), la migración
  los conserva, porque no borra datos. Si esa base solo se usó para pruebas, el equipo puede decidir eliminarla a mano y
  dejar que el programa cree una vacía, o usar el modo Demo.
- Las órdenes antiguas toman como precio el de compra del momento de migrar (no existía otro dato).
- Las pantallas aún son las mismas para todos los roles (el backend sí bloquea lo que cada rol no puede hacer); las
  interfaces por rol y el CRUD completo de usuarios, bodegas y categorías corresponden a la etapa siguiente.
