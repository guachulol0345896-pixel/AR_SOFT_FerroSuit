/* =====================================================================
   FerroSuite — Migración V002: modelo relacional completo (Documento Maestro)

   Agrega: CategoriaProducto, Bodega, ExistenciaBodega, Rol, Usuario y
   DetalleOrdenCompra; bodega y usuario responsable en cada movimiento;
   reglas de formato de SKU y NIT; fechas de aprobación, entrega, recepción
   y pago en las órdenes; códigos de orden únicos.

   NO BORRA DATOS. Lo que cambia de lugar se copia antes y se comprueba:
   - Producto.Categoria (texto)      -> CategoriaProducto + Producto.CategoriaId
   - Producto.StockActual            -> se copia a ExistenciaBodega ("Bodega principal");
                                        Producto.StockActual queda como total
   - OrdenCompra.Sku / Cantidad      -> DetalleOrdenCompra (una línea por orden)
   - MovimientoInventario.Usuario    -> se conserva como texto; los movimientos
                                        anteriores quedan asignados al usuario
                                        "registro.anterior" (inactivo, sin clave)
   Si alguna comprobación falla, el script lanza un error y el programa
   deshace TODA la migración (se ejecuta dentro de una transacción).

   Diferencia consciente con el Documento Maestro: se conservan las claves
   naturales Sku (Producto) y Nit (Proveedor) en lugar de id_producto e
   id_proveedor, porque las bases existentes y el programa ya las usan y
   cambiarlas obligaría a reescribir todas las relaciones.

   Cada lote termina en GO (el programa ejecuta los lotes en orden).
   ===================================================================== */

/* ---------- 1. Categorías de producto ---------- */
CREATE TABLE dbo.CategoriaProducto (
    CategoriaId  INT            IDENTITY(1,1) NOT NULL,
    Nombre       NVARCHAR(50)   NOT NULL,
    Activa       BIT            NOT NULL CONSTRAINT DF_Categoria_Activa DEFAULT (1),
    CONSTRAINT PK_CategoriaProducto PRIMARY KEY (CategoriaId),
    CONSTRAINT UQ_CategoriaProducto_Nombre UNIQUE (Nombre),
    CONSTRAINT CK_CategoriaProducto_Nombre CHECK (LEN(Nombre) > 0)
);
GO

/* Categorías de referencia (no son datos de demostración: las usa el formulario de productos). */
INSERT INTO dbo.CategoriaProducto (Nombre)
SELECT v.Nombre
FROM (VALUES (N'Construcción'), (N'Plomería'), (N'Eléctricos'), (N'Tornillería'), (N'Pinturas'), (N'Herramientas')) AS v (Nombre)
WHERE NOT EXISTS (SELECT 1 FROM dbo.CategoriaProducto c WHERE c.Nombre = v.Nombre);

/* Categorías que ya usan productos existentes y no están en la lista: se conservan. */
INSERT INTO dbo.CategoriaProducto (Nombre)
SELECT DISTINCT p.Categoria
FROM dbo.Producto p
WHERE LEN(p.Categoria) > 0
  AND NOT EXISTS (SELECT 1 FROM dbo.CategoriaProducto c WHERE c.Nombre = p.Categoria);

IF EXISTS (SELECT 1 FROM dbo.Producto WHERE LEN(Categoria) = 0)
   AND NOT EXISTS (SELECT 1 FROM dbo.CategoriaProducto WHERE Nombre = N'Sin categoría')
    INSERT INTO dbo.CategoriaProducto (Nombre) VALUES (N'Sin categoría');
GO

ALTER TABLE dbo.Producto ADD CategoriaId INT NULL;
GO

UPDATE p
SET CategoriaId = c.CategoriaId
FROM dbo.Producto p
JOIN dbo.CategoriaProducto c
  ON c.Nombre = CASE WHEN LEN(p.Categoria) = 0 THEN N'Sin categoría' ELSE p.Categoria END;

IF EXISTS (SELECT 1 FROM dbo.Producto WHERE CategoriaId IS NULL)
    THROW 51001, N'Migración V002: hay productos cuya categoría no se pudo asignar. No se aplicó ningún cambio.', 1;
GO

ALTER TABLE dbo.Producto ALTER COLUMN CategoriaId INT NOT NULL;
ALTER TABLE dbo.Producto ADD CONSTRAINT FK_Producto_Categoria
    FOREIGN KEY (CategoriaId) REFERENCES dbo.CategoriaProducto (CategoriaId);
/* El texto ya quedó copiado en CategoriaProducto y enlazado por CategoriaId. */
ALTER TABLE dbo.Producto DROP COLUMN Categoria;
ALTER TABLE dbo.Producto ADD Activo BIT NOT NULL CONSTRAINT DF_Producto_Activo DEFAULT (1);
GO

/* ---------- 2. Formato de SKU y NIT ----------
   SKU: 3 a 20 caracteres, solo A-Z mayúsculas, dígitos y guion; empieza por letra o dígito y no termina en guion.
   NIT: 6 a 12 dígitos, opcionalmente seguidos de "-" y un dígito de verificación.
   (El dígito de verificación de la DIAN se comprueba en C#; aquí solo el formato.)
   Si una base existente ya tuviera valores fuera de formato, la regla se crea WITH NOCHECK:
   no toca esas filas, pero se aplica a todo lo nuevo o modificado. */
IF NOT EXISTS (SELECT 1 FROM dbo.Producto
               WHERE NOT (LEN(Sku) BETWEEN 3 AND 20
                          AND Sku COLLATE Latin1_General_BIN NOT LIKE '%[^A-Z0-9-]%'
                          AND Sku COLLATE Latin1_General_BIN LIKE '[A-Z0-9]%'
                          AND Sku NOT LIKE '%-'))
    ALTER TABLE dbo.Producto WITH CHECK ADD CONSTRAINT CK_Producto_SkuFormato CHECK (
        LEN(Sku) BETWEEN 3 AND 20
        AND Sku COLLATE Latin1_General_BIN NOT LIKE '%[^A-Z0-9-]%'
        AND Sku COLLATE Latin1_General_BIN LIKE '[A-Z0-9]%'
        AND Sku NOT LIKE '%-');
ELSE
    ALTER TABLE dbo.Producto WITH NOCHECK ADD CONSTRAINT CK_Producto_SkuFormato CHECK (
        LEN(Sku) BETWEEN 3 AND 20
        AND Sku COLLATE Latin1_General_BIN NOT LIKE '%[^A-Z0-9-]%'
        AND Sku COLLATE Latin1_General_BIN LIKE '[A-Z0-9]%'
        AND Sku NOT LIKE '%-');
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Proveedor
               WHERE NOT (Nit NOT LIKE '%[^0-9-]%'
                          AND Nit NOT LIKE '%-%-%'
                          AND ((CHARINDEX('-', Nit) = 0 AND LEN(Nit) BETWEEN 6 AND 12)
                               OR (CHARINDEX('-', Nit) = LEN(Nit) - 1 AND LEN(Nit) BETWEEN 8 AND 14))))
    ALTER TABLE dbo.Proveedor WITH CHECK ADD CONSTRAINT CK_Proveedor_NitFormato CHECK (
        Nit NOT LIKE '%[^0-9-]%'
        AND Nit NOT LIKE '%-%-%'
        AND ((CHARINDEX('-', Nit) = 0 AND LEN(Nit) BETWEEN 6 AND 12)
             OR (CHARINDEX('-', Nit) = LEN(Nit) - 1 AND LEN(Nit) BETWEEN 8 AND 14)));
ELSE
    ALTER TABLE dbo.Proveedor WITH NOCHECK ADD CONSTRAINT CK_Proveedor_NitFormato CHECK (
        Nit NOT LIKE '%[^0-9-]%'
        AND Nit NOT LIKE '%-%-%'
        AND ((CHARINDEX('-', Nit) = 0 AND LEN(Nit) BETWEEN 6 AND 12)
             OR (CHARINDEX('-', Nit) = LEN(Nit) - 1 AND LEN(Nit) BETWEEN 8 AND 14)));

ALTER TABLE dbo.Proveedor ADD Activo BIT NOT NULL CONSTRAINT DF_Proveedor_Activo DEFAULT (1);
ALTER TABLE dbo.Proveedor ADD CONSTRAINT CK_Proveedor_Nombre CHECK (LEN(Nombre) > 0);
GO

/* ---------- 3. Bodegas y existencias por bodega ---------- */
CREATE TABLE dbo.Bodega (
    BodegaId   INT            IDENTITY(1,1) NOT NULL,
    Nombre     NVARCHAR(100)  NOT NULL,
    Direccion  NVARCHAR(200)  NULL,
    Activa     BIT            NOT NULL CONSTRAINT DF_Bodega_Activa DEFAULT (1),
    CONSTRAINT PK_Bodega PRIMARY KEY (BodegaId),
    CONSTRAINT UQ_Bodega_Nombre UNIQUE (Nombre),
    CONSTRAINT CK_Bodega_Nombre CHECK (LEN(Nombre) > 0)
);
GO

/* Bodega de referencia: recibe el stock que ya existía. */
INSERT INTO dbo.Bodega (Nombre) VALUES (N'Bodega principal');

CREATE TABLE dbo.ExistenciaBodega (
    Sku        VARCHAR(20)    NOT NULL,
    BodegaId   INT            NOT NULL,
    Cantidad   DECIMAL(12,2)  NOT NULL CONSTRAINT DF_Existencia_Cantidad DEFAULT (0),
    CONSTRAINT PK_ExistenciaBodega PRIMARY KEY (Sku, BodegaId),
    CONSTRAINT FK_Existencia_Producto FOREIGN KEY (Sku) REFERENCES dbo.Producto (Sku),
    CONSTRAINT FK_Existencia_Bodega FOREIGN KEY (BodegaId) REFERENCES dbo.Bodega (BodegaId),
    CONSTRAINT CK_Existencia_Cantidad CHECK (Cantidad >= 0)
);
GO

INSERT INTO dbo.ExistenciaBodega (Sku, BodegaId, Cantidad)
SELECT p.Sku, b.BodegaId, p.StockActual
FROM dbo.Producto p
CROSS JOIN (SELECT BodegaId FROM dbo.Bodega WHERE Nombre = N'Bodega principal') AS b;

IF EXISTS (SELECT 1 FROM dbo.Producto p
           WHERE p.StockActual <> (SELECT ISNULL(SUM(e.Cantidad), 0) FROM dbo.ExistenciaBodega e WHERE e.Sku = p.Sku))
    THROW 51002, N'Migración V002: las existencias por bodega no coinciden con el stock. No se aplicó ningún cambio.', 1;
GO

/* ---------- 4. Roles y usuarios ---------- */
CREATE TABLE dbo.Rol (
    RolId   INT           NOT NULL,
    Codigo  VARCHAR(20)   NOT NULL,
    Nombre  NVARCHAR(50)  NOT NULL,
    CONSTRAINT PK_Rol PRIMARY KEY (RolId),
    CONSTRAINT UQ_Rol_Codigo UNIQUE (Codigo),
    CONSTRAINT UQ_Rol_Nombre UNIQUE (Nombre)
);
GO

INSERT INTO dbo.Rol (RolId, Codigo, Nombre) VALUES
    (1, 'ADMIN',    N'Dueño o administrador'),
    (2, 'BODEGA',   N'Encargado de bodega'),
    (3, 'VENDEDOR', N'Vendedor de mostrador'),
    (4, 'CONTADOR', N'Contador externo');

/* La clave se guarda como hash PBKDF2 (nunca en texto plano). Un usuario sin clave no puede iniciar sesión
   (por ejemplo, el usuario de registros anteriores o usuarios de demostración). */
CREATE TABLE dbo.Usuario (
    UsuarioId         INT             IDENTITY(1,1) NOT NULL,
    NombreUsuario     VARCHAR(40)     NOT NULL,
    NombreCompleto    NVARCHAR(100)   NOT NULL,
    RolId             INT             NOT NULL,
    ClaveHash         VARBINARY(32)   NULL,
    ClaveSal          VARBINARY(16)   NULL,
    ClaveIteraciones  INT             NULL,
    Activo            BIT             NOT NULL CONSTRAINT DF_Usuario_Activo DEFAULT (1),
    FechaCreacion     DATETIME2(0)    NOT NULL CONSTRAINT DF_Usuario_Fecha DEFAULT (SYSDATETIME()),
    CONSTRAINT PK_Usuario PRIMARY KEY (UsuarioId),
    CONSTRAINT UQ_Usuario_NombreUsuario UNIQUE (NombreUsuario),
    CONSTRAINT FK_Usuario_Rol FOREIGN KEY (RolId) REFERENCES dbo.Rol (RolId),
    CONSTRAINT CK_Usuario_NombreUsuario CHECK (
        LEN(NombreUsuario) BETWEEN 3 AND 40
        AND NombreUsuario COLLATE Latin1_General_BIN NOT LIKE '%[^a-z0-9._-]%'),
    CONSTRAINT CK_Usuario_NombreCompleto CHECK (LEN(NombreCompleto) > 0),
    CONSTRAINT CK_Usuario_Clave CHECK (
        (ClaveHash IS NULL AND ClaveSal IS NULL AND ClaveIteraciones IS NULL)
        OR (ClaveHash IS NOT NULL AND ClaveSal IS NOT NULL AND ClaveIteraciones >= 100000))
);
GO

/* Usuario al que quedan asignados los movimientos registrados antes de esta versión (el texto original se conserva). */
INSERT INTO dbo.Usuario (NombreUsuario, NombreCompleto, RolId, Activo)
VALUES ('registro.anterior', N'Registros anteriores a la versión 2', 2, 0);
GO

/* ---------- 5. Movimientos: bodega, usuario responsable, referencia y sin borrado en cascada ---------- */
ALTER TABLE dbo.MovimientoInventario ADD BodegaId INT NULL, UsuarioId INT NULL, Referencia NVARCHAR(30) NULL;
GO

UPDATE dbo.MovimientoInventario
SET BodegaId  = (SELECT BodegaId FROM dbo.Bodega WHERE Nombre = N'Bodega principal'),
    UsuarioId = (SELECT UsuarioId FROM dbo.Usuario WHERE NombreUsuario = 'registro.anterior');
GO

ALTER TABLE dbo.MovimientoInventario ALTER COLUMN BodegaId INT NOT NULL;
ALTER TABLE dbo.MovimientoInventario ALTER COLUMN UsuarioId INT NOT NULL;
ALTER TABLE dbo.MovimientoInventario ADD CONSTRAINT FK_Movimiento_Bodega
    FOREIGN KEY (BodegaId) REFERENCES dbo.Bodega (BodegaId);
ALTER TABLE dbo.MovimientoInventario ADD CONSTRAINT FK_Movimiento_Usuario
    FOREIGN KEY (UsuarioId) REFERENCES dbo.Usuario (UsuarioId);
/* Antes, borrar un producto borraba su historial (ON DELETE CASCADE). Ahora el historial lo impide. */
ALTER TABLE dbo.MovimientoInventario DROP CONSTRAINT FK_Movimiento_Producto;
ALTER TABLE dbo.MovimientoInventario ADD CONSTRAINT FK_Movimiento_Producto
    FOREIGN KEY (Sku) REFERENCES dbo.Producto (Sku);
CREATE INDEX IX_Movimiento_Usuario ON dbo.MovimientoInventario (UsuarioId);
CREATE INDEX IX_Movimiento_Bodega ON dbo.MovimientoInventario (BodegaId);
GO

/* Los movimientos son historial: no se modifican ni se borran (RNF08). Se corrigen con un movimiento nuevo. */
CREATE TRIGGER dbo.TR_MovimientoInventario_SoloInsertar
ON dbo.MovimientoInventario
INSTEAD OF UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    THROW 51010, N'Los movimientos de inventario no se modifican ni se borran: registra un movimiento de ajuste.', 1;
END;
GO

/* ---------- 6. Órdenes de compra: detalle, fechas, estados y códigos únicos ---------- */
CREATE TABLE dbo.DetalleOrdenCompra (
    DetalleId       INT            IDENTITY(1,1) NOT NULL,
    OrdenId         INT            NOT NULL,
    Sku             VARCHAR(20)    NOT NULL,
    Cantidad        DECIMAL(12,2)  NOT NULL,
    PrecioUnitario  DECIMAL(12,2)  NULL,
    CONSTRAINT PK_DetalleOrdenCompra PRIMARY KEY (DetalleId),
    CONSTRAINT FK_Detalle_Orden FOREIGN KEY (OrdenId) REFERENCES dbo.OrdenCompra (OrdenId),
    CONSTRAINT FK_Detalle_Producto FOREIGN KEY (Sku) REFERENCES dbo.Producto (Sku),
    CONSTRAINT UQ_Detalle_OrdenSku UNIQUE (OrdenId, Sku),
    CONSTRAINT CK_Detalle_Cantidad CHECK (Cantidad > 0),
    CONSTRAINT CK_Detalle_Precio CHECK (PrecioUnitario IS NULL OR PrecioUnitario >= 0)
);
CREATE INDEX IX_Detalle_Sku ON dbo.DetalleOrdenCompra (Sku);
GO

/* Cada orden anterior tenía un solo producto: pasa a ser su única línea de detalle. Esas órdenes no guardaban
   el precio, así que se toma el precio de compra que tiene el producto al momento de migrar. */
INSERT INTO dbo.DetalleOrdenCompra (OrdenId, Sku, Cantidad, PrecioUnitario)
SELECT o.OrdenId, o.Sku, o.Cantidad, p.PrecioCompra
FROM dbo.OrdenCompra o
JOIN dbo.Producto p ON p.Sku = o.Sku;

IF (SELECT COUNT(*) FROM dbo.OrdenCompra) <> (SELECT COUNT(DISTINCT OrdenId) FROM dbo.DetalleOrdenCompra)
    THROW 51003, N'Migración V002: no todas las órdenes pasaron al detalle. No se aplicó ningún cambio.', 1;
GO

/* Los datos de Sku y Cantidad ya están en DetalleOrdenCompra. */
DROP INDEX IX_Orden_Sku ON dbo.OrdenCompra;
ALTER TABLE dbo.OrdenCompra DROP CONSTRAINT FK_Orden_Producto;
ALTER TABLE dbo.OrdenCompra DROP CONSTRAINT CK_Orden_Cantidad;
ALTER TABLE dbo.OrdenCompra DROP COLUMN Sku, Cantidad;

ALTER TABLE dbo.OrdenCompra ADD
    FechaEntregaEsperada  DATETIME2(0)  NULL,
    FechaRecepcion        DATETIME2(0)  NULL,
    FechaPago             DATETIME2(0)  NULL,
    BodegaRecepcionId     INT           NULL;
GO

ALTER TABLE dbo.OrdenCompra ADD CONSTRAINT FK_Orden_BodegaRecepcion
    FOREIGN KEY (BodegaRecepcionId) REFERENCES dbo.Bodega (BodegaId);

ALTER TABLE dbo.OrdenCompra DROP CONSTRAINT CK_Orden_Estado;
ALTER TABLE dbo.OrdenCompra ADD CONSTRAINT CK_Orden_Estado
    CHECK (Estado IN (N'Pendiente de aprobación', N'Aprobada', N'Rechazada', N'Recibida'));

/* Reglas de coherencia entre estado y fechas. */
IF NOT EXISTS (SELECT 1 FROM dbo.OrdenCompra WHERE Pagada = 1 AND Estado NOT IN (N'Aprobada', N'Recibida'))
    ALTER TABLE dbo.OrdenCompra WITH CHECK ADD CONSTRAINT CK_Orden_Pago
        CHECK (Pagada = 0 OR Estado IN (N'Aprobada', N'Recibida'));
ELSE
    ALTER TABLE dbo.OrdenCompra WITH NOCHECK ADD CONSTRAINT CK_Orden_Pago
        CHECK (Pagada = 0 OR Estado IN (N'Aprobada', N'Recibida'));

ALTER TABLE dbo.OrdenCompra ADD CONSTRAINT CK_Orden_FechaPago CHECK (FechaPago IS NULL OR Pagada = 1);
ALTER TABLE dbo.OrdenCompra ADD CONSTRAINT CK_Orden_Recepcion CHECK (
    (Estado = N'Recibida' AND FechaRecepcion IS NOT NULL AND BodegaRecepcionId IS NOT NULL)
    OR (Estado <> N'Recibida' AND FechaRecepcion IS NULL));
GO

/* El código anterior ('OC-' + últimos 3 dígitos) se repetía desde la orden 1000 (OC-1000 -> OC-000).
   El nuevo conserva OC-001 ... OC-999 para las órdenes existentes y usa el número completo desde la 1000. */
ALTER TABLE dbo.OrdenCompra DROP COLUMN Codigo;
GO

ALTER TABLE dbo.OrdenCompra ADD Codigo AS (
    'OC-' + CASE WHEN OrdenId < 1000
                 THEN RIGHT('000' + CAST(OrdenId AS VARCHAR(10)), 3)
                 ELSE CAST(OrdenId AS VARCHAR(10)) END) PERSISTED;
GO

CREATE UNIQUE INDEX UX_OrdenCompra_Codigo ON dbo.OrdenCompra (Codigo);
CREATE INDEX IX_Orden_Proveedor ON dbo.OrdenCompra (NitProveedor);
GO
