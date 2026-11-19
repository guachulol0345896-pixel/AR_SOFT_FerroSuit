# Seguridad (etapa 3): autenticación, autorización y auditoría

## 1. Qué había y qué se corrigió

| Antes (etapa 2) | Ahora |
|---|---|
| Hash PBKDF2-SHA256 escrito a mano | `PasswordHasher` de **ASP.NET Core Identity** (biblioteca probada, MIT) |
| Permisos solo en el `Puente` | Matriz única (`Aplicacion/Seguridad/Permisos.cs`) que exigen el `Puente` **y** los servicios |
| El vendedor podía hacer ajustes de inventario | Cada motivo exige su permiso: el vendedor solo registra ventas y devoluciones |
| Menús de la ventana (clave de Gemini, simulaciones, reiniciar demo) sin control | Requieren sesión de administrador y quedan en la auditoría |
| Sin auditoría | Tabla `AuditoriaEvento`, solo inserciones, sin secretos |
| Sin bloqueo, sin vencimiento de sesión, sin cambio de clave | Bloqueo 15 min tras 5 fallos; sesión de 30 min sin actividad / 12 h máximo; cambio de clave obligatorio y restablecimiento |
| Un error inesperado mostraba el mensaje interno | Solo un número de referencia; el detalle técnico va a la depuración y la referencia a la auditoría |
| WebView2 aceptaba mensajes de cualquier página | Solo de `https://ferrosuite.example`; navegación externa bloqueada; sin herramientas de desarrollo en la versión final |

## 2. Claves

- `Infraestructura/Seguridad/HasherIdentity.cs` usa `PasswordHasher<TUser>` en modo **IdentityV3**: PBKDF2-HMAC-SHA512,
  sal aleatoria de 128 bits, subclave de 256 bits y **210.000 iteraciones** (recomendación de OWASP para PBKDF2-HMAC-SHA512).
  El hash incluye formato, sal y parámetros; si se suben las iteraciones, los hash antiguos se recalculan al ingresar.
- Las claves creadas con la versión 2 (columnas `ClaveHash/ClaveSal/ClaveIteraciones`) se verifican **una sola vez** y se
  convierten al formato de Identity; la base impide tener los dos formatos a la vez (`CK_Usuario_Clave`).
- Política: 10 a 128 caracteres, con letras y números, sin contener el nombre de usuario.
- Mismo mensaje para usuario inexistente, inactivo, bloqueado o clave incorrecta, y el mismo trabajo de cálculo en todos
  los casos (no revela por el tiempo si el usuario existe).
- Las claves que crea el administrador (usuario nuevo) o que se restablecen (clave temporal aleatoria de 16 caracteres)
  obligan a cambiarla en el primer ingreso; mientras tanto el backend solo acepta "cambiar clave" y "cerrar sesión".

## 3. Roles y permisos (mínimo privilegio)

| Permiso | Administrador | Encargado de bodega | Vendedor de mostrador | Contador externo |
|---|:-:|:-:|:-:|:-:|
| Ver inventario y stock | ✓ | ✓ | ✓ | ✓ |
| Ver historial de movimientos | ✓ | ✓ | | ✓ |
| Registrar ventas y devoluciones | ✓ | | ✓ | |
| Registrar ajustes, daños y entradas manuales | ✓ | ✓ | | |
| Crear y editar productos | ✓ | | | |
| **Eliminar productos** | ✓ | | | |
| Crear bodegas | ✓ | | | |
| Ver proveedores | ✓ | ✓ | | ✓ |
| Crear, editar y desactivar proveedores | ✓ | | | |
| Ver órdenes de compra | ✓ | ✓ | | ✓ |
| Crear órdenes | ✓ | ✓ | | |
| **Aprobar o rechazar compras** | ✓ | | | |
| Recibir mercancía | ✓ | ✓ | | |
| Marcar órdenes como pagadas | ✓ | | | ✓ |
| Ver valores totales en pesos (valor en bodega, cuentas por pagar) | ✓ | | | ✓ |
| Ver el precio unitario de compra de cada producto (para crear órdenes) | ✓ | ✓ | | ✓ |
| Asistente de reposición y preguntas | ✓ | ✓ | | |
| Recordatorios | ✓ | ✓ | | ✓ |
| Pronóstico de demanda y recomendaciones de reposición (tabla *Pronósticos* y pantalla *Inteligencia artificial*: `pronosticos.reposicion`, `pronosticos.producto`) | ✓ | ✓ | | |
| **Crear usuarios**, editar nombre y rol, activar, desactivar, desbloquear, restablecer clave | ✓ | | | |
| Crear, editar, activar y desactivar bodegas y categorías | ✓ | | | |
| Ver auditoría | ✓ | | | |
| **Modificar configuración** (clave de Gemini, simulaciones) | ✓ | | | |
| Reconstruir la base de demostración (solo modo Demo) | ✓ | | | |

El vendedor no recibe el precio de compra de los productos. El encargado de bodega sí lo recibe (lo necesita para crear
órdenes; el precio de la orden lo toma igual el servidor), pero no ve el valor total del inventario ni las cuentas por pagar.
El administrador no puede desactivarse a sí mismo ni cambiar su propio rol, y siempre debe quedar al menos un
administrador activo. No se puede desactivar una bodega con mercancía ni una categoría con productos activos.

## 4. Dónde se comprueba

1. **Ventana (WebView2)**: solo navega por `https://ferrosuite.example/`; los enlaces externos se abren en el navegador del
   sistema; no se abren ventanas nuevas; en la versión final (Release) no hay herramientas de desarrollo, menú contextual ni
   teclas del navegador; se desactivan el autocompletado y el guardado de claves.
2. **Puente**: rechaza mensajes de otro origen (y lo audita), de más de 64 KB o con anidamiento excesivo, y acciones que no
   estén en su tabla (`PermisoPorAccion`, denegar por defecto). Luego exige sesión vigente y el permiso de la acción.
3. **Servicios**: las operaciones sensibles reciben la sesión y vuelven a exigir el permiso (`Autorizador`), aunque alguien
   las llame sin pasar por el `Puente`.
4. **Pantallas**: ocultan menús, botones y formularios según los permisos que devuelve el backend. Es solo comodidad: el
   backend rechaza igual cualquier mensaje enviado a mano.

## 5. Auditoría

Tabla `AuditoriaEvento` (`V003`): fecha, usuario, rol, identificador aleatorio de la sesión, operación, resultado
(Exitoso / Fallido / Denegado), objeto, detalle y equipo. Un disparador impide modificarla o borrarla. Se registran:
ingresos (correctos, fallidos, bloqueados), bloqueos, cierres y vencimientos de sesión, cambios de clave, accesos denegados,
mensajes de otro origen, creación/activación/desactivación/desbloqueo de usuarios y restablecimientos de clave, productos
(crear, editar, eliminar o desactivar), proveedores, órdenes (crear, aprobar, rechazar, recibir, pagar), bodegas,
cambios de configuración, reconstrucción de la demo y errores inesperados (solo con su número de referencia).

**Nunca** se registran claves, claves temporales, hash ni la clave de Gemini. Los textos se limpian de caracteres de control
(nadie puede falsificar líneas) y se recortan. Si el registro falla, la operación no se interrumpe (decisión documentada:
el programa es de un solo equipo y bloquear la venta por un fallo del registro sería peor); queda el aviso en la depuración.

## 6. Cuentas de demostración (procedimiento local documentado)

No hay usuarios con clave conocida en ninguna base ni en el código. Para mostrar el programa con varios roles:

1. En `appsettings.json` (junto al ejecutable) poner `"Modo": "Demo"` y abrir FerroSuite. Se crea la base `FerroSuiteDemo`
   con datos sintéticos y los usuarios `demo.bodega`, `demo.vendedor` y `demo.contador` **sin clave**.
2. Crear el administrador cuando el programa lo pida e ingresar con él.
3. *Opciones → Generar claves de las cuentas de demostración…*: el programa asigna claves aleatorias a las cuentas `demo.*`
   y las muestra **una sola vez** para copiarlas. No se guardan en texto ni en la auditoría.
4. Repetir el paso 3 cuando se necesiten claves nuevas. En modo Real esta opción no existe y el backend la rechaza.

## 7. Pruebas

| Suite | Cómo | Qué comprueba |
|---|---|---|
| En memoria (154 pruebas en total) | `dotnet run --project tests/FerroSuite.Pruebas` | Hash de Identity y sus parámetros; matriz **rol × acción** (43 acciones × 4 roles + sin sesión) enviando mensajes directos al Puente; servicios llamados sin el Puente; bloqueo, vencimiento de sesión, cambio obligatorio y restablecimiento de clave; mínimo privilegio en datos y motivos; origen, tamaño y acciones inventadas; errores sin detalles internos; auditoría de operaciones sensibles y **ninguna clave en la auditoría** |
| Pantallas (23 pasos) | `tests/pantallas/recorrido.js` | Cada rol ve solo lo suyo; mensajes directos por el canal de WebView2 rechazados; creación de usuario y cambio obligatorio de clave |
| SQL Server (54 pruebas en total) | `dotnet run --project tests/FerroSuite.Pruebas -- --bd` (solo Windows) | Además de la persistencia: auditoría en la base y su disparador, conversión de claves de la versión 2, bloqueo guardado, restricciones de usuario, roles, ninguna clave en la auditoría, cuentas de demostración |

## 8. Limitaciones conocidas

- La sesión vive en la memoria del programa (una ventana, un usuario). Es adecuado para un programa de escritorio; no es un
  servicio multiusuario en red.
- Quien tenga permisos de administrador de Windows sobre el equipo puede leer la base LocalDB directamente: la protección de
  la base frente al propio sistema operativo depende de las cuentas de Windows (fuera del alcance del programa).
- La clave de Gemini se guarda en `%LocalAppData%\FerroSuite` (sin cifrar con DPAPI todavía).
