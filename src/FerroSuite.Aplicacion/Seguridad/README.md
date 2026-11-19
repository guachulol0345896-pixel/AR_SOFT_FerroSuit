# Aplicación · Seguridad

- `Permisos`: los permisos del programa y la matriz por rol (mínimo privilegio). Única fuente de verdad.
- `Autorizador`: exige sesión, cambio de clave pendiente resuelto y permiso; si no, audita el intento y lanza `ErrorAutorizacion`.
  Lo usan el `Puente` (por acción) y los servicios (por operación sensible).
- `GestorSesion`: sesión del programa con vencimiento por inactividad (30 min) y duración máxima (12 h).
- `SesionUsuario`: quién ingresó; la pantalla no puede cambiarla. Lleva un identificador aleatorio para la auditoría.
- `AuditoriaServicio`: registra operaciones sensibles sin secretos (limpia y recorta los textos).
- `AdministracionServicio`: cambios de configuración desde la ventana y reconstrucción de la demo, con permiso y auditoría.
- `GeneradorClaves`: claves temporales aleatorias (generador criptográfico).

Detalle en [docs/seguridad.md](../../../docs/seguridad.md).
