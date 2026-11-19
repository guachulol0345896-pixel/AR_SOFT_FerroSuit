/* =====================================================================
   SOLO PARA PRUEBAS DE MIGRACIÓN. Reproduce una base creada por la versión 1
   del programa: el esquema anterior más sus datos SINTÉTICOS antiguos.
   La prueba de integración lo aplica a una base temporal, ejecuta las
   migraciones y comprueba que no se pierde ningún dato.
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

IF NOT EXISTS (SELECT 1 FROM dbo.Proveedor)
BEGIN
    INSERT INTO dbo.Proveedor (Nit, Nombre, Telefono, DiasEntrega, Cumplimiento) VALUES
    ('900123456-7', N'Cementos Andinos SAS', '601 555 0110', 3, 95),
    ('800987654-1', N'Aceros del Norte',     '601 555 0221', 5, 88),
    ('901222333-9', N'Eléctricos JR',        '601 555 0330', 2, 97);

    INSERT INTO dbo.Producto (Sku, Nombre, Categoria, Unidad, StockActual, StockMinimo, PrecioCompra, NitProveedor) VALUES
    ('FER-0245', N'Cemento gris 50 kg',          N'Construcción', N'Bulto',     120,  30, 29500, '900123456-7'),
    ('FER-0110', N'Varilla corrugada 1/2"',      N'Construcción', N'Varilla',    18,  20, 21800, '800987654-1'),
    ('FER-0087', N'Cable eléctrico THHN 12',     N'Eléctricos',   N'Metro',     340,  50,  2100, '901222333-9'),
    ('FER-0033', N'Tornillo autorroscante 8x1',  N'Tornillería',  N'Unidad',    900, 200,    90, '800987654-1'),
    ('FER-0199', N'Pintura blanca tipo 1',       N'Pinturas',     N'Galón',      12,  15, 48000, '900123456-7'),
    ('FER-0301', N'Tubo PVC presión 1/2"',       N'Plomería',     N'Unidad',     26,  15,  9800, '900123456-7'),
    ('FER-0302', N'Codo PVC 1/2"',               N'Plomería',     N'Unidad',     40,  30,   650, '900123456-7'),
    ('FER-0303', N'Soldadura PVC 1/4 gal',       N'Plomería',     N'Unidad',      4,   6, 23500, '900123456-7'),
    ('FER-0410', N'Alambre negro calibre 18',    N'Construcción', N'Kilogramo',  22.5, 10,  7600, '800987654-1'),
    ('FER-0520', N'Interruptor sencillo',        N'Eléctricos',   N'Unidad',     35,  12,  5200, '901222333-9'),
    ('FER-0611', N'Martillo de uña 16 oz',       N'Herramientas', N'Unidad',      9,   4, 26000, '800987654-1'),
    ('FER-0612', N'Disco de corte metal 4 1/2"', N'Herramientas', N'Unidad',      0,  10,  4300, '800987654-1');

    /* Ventas del último mes (cada 3 días), para calcular cuánto se vende por día */
    INSERT INTO dbo.MovimientoInventario (Sku, Tipo, Cantidad, Motivo, Usuario, Fecha)
    SELECT v.Sku, 'Salida', v.PorDia * 3, N'Venta', N'Datos de prueba',
           DATEADD(DAY, -d.Dia, CAST(CAST(SYSDATETIME() AS DATE) AS DATETIME2(0)))
    FROM (VALUES ('FER-0245', 6), ('FER-0110', 3), ('FER-0087', 14), ('FER-0033', 45),
                 ('FER-0199', 1), ('FER-0301', 3), ('FER-0302', 2), ('FER-0303', 0.5),
                 ('FER-0410', 1.5), ('FER-0520', 1), ('FER-0612', 2)) AS v (Sku, PorDia)
    CROSS JOIN (VALUES (28), (25), (22), (19), (16), (13), (10), (7), (4), (1)) AS d (Dia);

    /* Una orden aprobada sin pagar (su pago vence pronto) y una por aprobar */
    INSERT INTO dbo.OrdenCompra (Sku, NitProveedor, Cantidad, Estado, FechaCreacion, FechaAprobacion, Pagada) VALUES
    ('FER-0110', '800987654-1', 100, N'Aprobada',                DATEADD(DAY, -14, SYSDATETIME()), DATEADD(DAY, -12, SYSDATETIME()), 0),
    ('FER-0612', '800987654-1',  50, N'Pendiente de aprobación', DATEADD(DAY, -3,  SYSDATETIME()), NULL, 0);
END
GO
