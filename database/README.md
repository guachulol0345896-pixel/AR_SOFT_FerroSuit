# Base de datos de FerroSuite (SQL Server LocalDB)

| Carpeta | Archivo | Qué hace |
|---|---|---|
| `migraciones/` | `V001__esquema_inicial.sql` | Esquema de la versión 1 (4 tablas). En una base nueva lo crea; en una base creada por la versión anterior no cambia nada (todo va con `IF OBJECT_ID … IS NULL`). |
| | `V002__modelo_completo.sql` | Completa el modelo del Documento Maestro **sin borrar datos**: categorías, bodegas y existencias por bodega, roles y usuarios, usuario responsable en cada movimiento, detalle de órdenes, fechas y estados de orden, códigos de orden únicos y restricciones de SKU y NIT. |
| | `V003__seguridad_y_auditoria.sql` | Hash de claves en formato de ASP.NET Core Identity (conserva y convierte las de la versión 2), bloqueo por intentos fallidos, cambio obligatorio de clave, tabla `AuditoriaEvento` (solo inserciones) y nombres de roles del Documento Maestro. |
| `demo/` | `datos_demo.sql` | Datos de **demostración** (sintéticos). Solo se cargan en la base de demostración. |
| `pruebas/` | `legado_v1.sql` | Solo para pruebas: reproduce una base de la versión 1 con sus datos sintéticos antiguos para comprobar que la migración no pierde nada. El programa no lo usa. |

## Cómo se usa

El programa copia `migraciones/` y `demo/` a `<salida>\BaseDatos\` y, al iniciar (`Infraestructura/Persistencia/BaseDatosSql.Preparar`):

1. Crea la base si no existe (nunca la borra).
2. Aplica, en orden, las migraciones que aún no estén registradas en la tabla `dbo.VersionEsquema`. Cada migración se
   ejecuta completa en **una transacción**: si algo falla, se deshace y la base queda como estaba.
3. Solo en modo **Demo**: si la base de demostración está vacía, carga `demo/datos_demo.sql`.

`config/appsettings.json` decide el modo:

```jsonc
"BaseDatos": {
  "Servidor": "(localdb)\\MSSQLLocalDB",
  "Modo": "Real",                 // "Real" (por defecto) o "Demo"
  "NombreBaseReal": "FerroSuite",  // datos de la ferretería: nunca se borra ni recibe datos ficticios
  "NombreBaseDemo": "FerroSuiteDemo" // datos sintéticos: se puede reconstruir desde Opciones → Reiniciar base de demostración
}
```

## Reglas para cambiar el esquema

- No se editan migraciones ya publicadas: cada cambio va en un archivo nuevo `V003__descripcion.sql`, `V004__…`.
- Una migración nunca borra datos para "arreglar" el esquema: copia, verifica (con `THROW` si algo no cuadra) y solo
  entonces quita lo que sobra.
- Los lotes se separan con una línea que solo diga `GO`. No usar `USE` ni `CREATE DATABASE` dentro de una migración.

Más detalle (modelo, decisiones de compatibilidad, transacciones y pruebas) en [docs/persistencia.md](../docs/persistencia.md).
