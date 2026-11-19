/* =====================================================================
   FerroSuite — Migración V001: esquema inicial (versión 1 del programa)
   Es el mismo esquema que creaba la versión anterior (tablas Proveedor,
   Producto, MovimientoInventario y OrdenCompra). Solo crea lo que no existe,
   así que en una base creada por la versión anterior no cambia nada.
   La base de datos la crea el programa: este script no crea la base ni
   cambia de base, para poder aplicarse a la real, la demo o la de pruebas.
   ===================================================================== */

/* ---------- Proveedor: empresas a las que la ferretería les compra ---------- */
IF OBJECT_ID(N'dbo.Proveedor', N'U') IS NULL
CREATE TABLE dbo.Proveedor (
    Nit            VARCHAR(20)    NOT NULL,
    Nombre         NVARCHAR(120)  NOT NULL,
    Telefono       VARCHAR(30)    NULL,
    DiasEntrega    INT            NOT NULL,
    Cumplimiento   DECIMAL(5,2)   NULL,          -- porcentaje de entregas a tiempo
    CONSTRAINT PK_Proveedor PRIMARY KEY (Nit),
    CONSTRAINT CK_Proveedor_DiasEntrega CHECK (DiasEntrega >= 0),
    CONSTRAINT CK_Proveedor_Cumplimiento CHECK (Cumplimiento IS NULL OR Cumplimiento BETWEEN 0 AND 100)
);
GO

/* ---------- Producto: catálogo con unidades mixtas ---------- */
IF OBJECT_ID(N'dbo.Producto', N'U') IS NULL
CREATE TABLE dbo.Producto (
    Sku            VARCHAR(20)    NOT NULL,
    Nombre         NVARCHAR(120)  NOT NULL,
    Categoria      NVARCHAR(50)   NOT NULL,
    Unidad         NVARCHAR(20)   NOT NULL,
    StockActual    DECIMAL(12,2)  NOT NULL CONSTRAINT DF_Producto_Stock DEFAULT (0),
    StockMinimo    DECIMAL(12,2)  NOT NULL,
    PrecioCompra   DECIMAL(12,2)  NULL,
    NitProveedor   VARCHAR(20)    NULL,
    CONSTRAINT PK_Producto PRIMARY KEY (Sku),
    CONSTRAINT FK_Producto_Proveedor FOREIGN KEY (NitProveedor) REFERENCES dbo.Proveedor (Nit),
    CONSTRAINT CK_Producto_Unidad CHECK (Unidad IN (N'Unidad', N'Metro', N'Kilogramo', N'Varilla', N'Bulto', N'Galón')),
    CONSTRAINT CK_Producto_Stock CHECK (StockActual >= 0 AND StockMinimo >= 0),
    CONSTRAINT CK_Producto_Precio CHECK (PrecioCompra IS NULL OR PrecioCompra >= 0)
);
GO

/* ---------- MovimientoInventario: entradas y salidas (trazabilidad) ---------- */
IF OBJECT_ID(N'dbo.MovimientoInventario', N'U') IS NULL
CREATE TABLE dbo.MovimientoInventario (
    MovimientoId   INT            IDENTITY(1,1) NOT NULL,
    Sku            VARCHAR(20)    NOT NULL,
    Tipo           VARCHAR(10)    NOT NULL,
    Cantidad       DECIMAL(12,2)  NOT NULL,
    Motivo         NVARCHAR(40)   NOT NULL,
    Usuario        NVARCHAR(80)   NOT NULL,
    Fecha          DATETIME2(0)   NOT NULL CONSTRAINT DF_Movimiento_Fecha DEFAULT (SYSDATETIME()),
    CONSTRAINT PK_MovimientoInventario PRIMARY KEY (MovimientoId),
    CONSTRAINT FK_Movimiento_Producto FOREIGN KEY (Sku) REFERENCES dbo.Producto (Sku) ON DELETE CASCADE,
    CONSTRAINT CK_Movimiento_Tipo CHECK (Tipo IN ('Entrada', 'Salida')),
    CONSTRAINT CK_Movimiento_Cantidad CHECK (Cantidad > 0)
);
GO

/* ---------- OrdenCompra: pedidos a proveedores y su pago ---------- */
IF OBJECT_ID(N'dbo.OrdenCompra', N'U') IS NULL
CREATE TABLE dbo.OrdenCompra (
    OrdenId          INT            IDENTITY(1,1) NOT NULL,
    Codigo           AS ('OC-' + RIGHT('000' + CAST(OrdenId AS VARCHAR(10)), 3)) PERSISTED,
    Sku              VARCHAR(20)    NOT NULL,
    NitProveedor     VARCHAR(20)    NOT NULL,
    Cantidad         DECIMAL(12,2)  NOT NULL,
    Estado           NVARCHAR(30)   NOT NULL CONSTRAINT DF_Orden_Estado DEFAULT (N'Pendiente de aprobación'),
    FechaCreacion    DATETIME2(0)   NOT NULL CONSTRAINT DF_Orden_Fecha DEFAULT (SYSDATETIME()),
    FechaAprobacion  DATETIME2(0)   NULL,
    Pagada           BIT            NOT NULL CONSTRAINT DF_Orden_Pagada DEFAULT (0),
    CONSTRAINT PK_OrdenCompra PRIMARY KEY (OrdenId),
    CONSTRAINT FK_Orden_Producto FOREIGN KEY (Sku) REFERENCES dbo.Producto (Sku),
    CONSTRAINT FK_Orden_Proveedor FOREIGN KEY (NitProveedor) REFERENCES dbo.Proveedor (Nit),
    CONSTRAINT CK_Orden_Cantidad CHECK (Cantidad > 0),
    CONSTRAINT CK_Orden_Estado CHECK (Estado IN (N'Pendiente de aprobación', N'Aprobada'))
);
GO

/* Índices para las consultas más usadas */
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Movimiento_Sku_Fecha')
    CREATE INDEX IX_Movimiento_Sku_Fecha ON dbo.MovimientoInventario (Sku, Fecha);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Orden_Sku')
    CREATE INDEX IX_Orden_Sku ON dbo.OrdenCompra (Sku);
GO
