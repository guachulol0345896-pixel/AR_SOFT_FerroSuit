/* =====================================================================
   FerroSuite — DATOS DE DEMOSTRACIÓN (SINTÉTICOS)

   Se cargan SOLO en la base de demostración (por defecto "FerroSuiteDemo"),
   nunca en la base real. El programa crea esa base desde cero, aplica las
   migraciones y ejecuta este script: el resultado es siempre el mismo
   (las fechas son relativas al día en que se inicializa).

   Todo es INVENTADO para probar el programa: proveedores (marcados DEMO,
   NIT que empiezan por 0000 con dígito de verificación válido), productos,
   precios, existencias, ventas, usuarios y órdenes. No son datos de una
   ferretería ni de empresas reales, no sirven como evidencia de
   funcionamiento real ni para entrenar la red neuronal.

   Los usuarios de demostración NO tienen clave (no pueden iniciar sesión):
   existen solo para figurar como responsables de los movimientos. El
   administrador se crea desde el programa la primera vez que se abre.
   ===================================================================== */

IF EXISTS (SELECT 1 FROM dbo.Proveedor) OR EXISTS (SELECT 1 FROM dbo.Producto)
    THROW 52001, N'La base de demostración ya tiene datos: el programa la vuelve a crear vacía antes de ejecutar este script.', 1;
GO

/* Bodega adicional de demostración (la "Bodega principal" la crea la migración V002). */
INSERT INTO dbo.Bodega (Nombre, Direccion) VALUES (N'Bodega mostrador (DEMO)', N'Dirección de demostración');

INSERT INTO dbo.Proveedor (Nit, Nombre, Telefono, DiasEntrega, Cumplimiento) VALUES
    ('000001001-2', N'DEMO Cementos y Agregados', '000 000 0001', 3, NULL),
    ('000001002-1', N'DEMO Aceros y Varillas',    '000 000 0002', 5, NULL),
    ('000001003-7', N'DEMO Eléctricos',           '000 000 0003', 2, NULL);

INSERT INTO dbo.Usuario (NombreUsuario, NombreCompleto, RolId) VALUES
    ('demo.bodega',   N'Encargado de bodega (DEMO)',   2),
    ('demo.vendedor', N'Vendedor de mostrador (DEMO)', 3),
    ('demo.contador', N'Contador externo (DEMO)',      4);
GO

/* Productos: StockActual es el total; las existencias por bodega se reparten abajo. */
INSERT INTO dbo.Producto (Sku, Nombre, CategoriaId, Unidad, StockActual, StockMinimo, PrecioCompra, NitProveedor)
SELECT v.Sku, v.Nombre, c.CategoriaId, v.Unidad, v.Stock, v.Minimo, v.Precio, v.Nit
FROM (VALUES
    ('DEMO-0245', N'Cemento gris 50 kg',          N'Construcción', N'Bulto',     120,  30, 29500, '000001001-2'),
    ('DEMO-0110', N'Varilla corrugada 1/2"',      N'Construcción', N'Varilla',    18,  20, 21800, '000001002-1'),
    ('DEMO-0087', N'Cable eléctrico THHN 12',     N'Eléctricos',   N'Metro',     340,  50,  2100, '000001003-7'),
    ('DEMO-0033', N'Tornillo autorroscante 8x1',  N'Tornillería',  N'Unidad',    900, 200,    90, '000001002-1'),
    ('DEMO-0199', N'Pintura blanca tipo 1',       N'Pinturas',     N'Galón',      12,  15, 48000, '000001001-2'),
    ('DEMO-0301', N'Tubo PVC presión 1/2"',       N'Plomería',     N'Unidad',     26,  15,  9800, '000001001-2'),
    ('DEMO-0302', N'Codo PVC 1/2"',               N'Plomería',     N'Unidad',     40,  30,   650, '000001001-2'),
    ('DEMO-0303', N'Soldadura PVC 1/4 gal',       N'Plomería',     N'Unidad',      4,   6, 23500, '000001001-2'),
    ('DEMO-0410', N'Alambre negro calibre 18',    N'Construcción', N'Kilogramo',  22.5, 10,  7600, '000001002-1'),
    ('DEMO-0520', N'Interruptor sencillo',        N'Eléctricos',   N'Unidad',     35,  12,  5200, '000001003-7'),
    ('DEMO-0611', N'Martillo de uña 16 oz',       N'Herramientas', N'Unidad',      9,   4, 26000, '000001002-1'),
    ('DEMO-0612', N'Disco de corte metal 4 1/2"', N'Herramientas', N'Unidad',      0,  10,  4300, '000001002-1')
) AS v (Sku, Nombre, Categoria, Unidad, Stock, Minimo, Precio, Nit)
JOIN dbo.CategoriaProducto c ON c.Nombre = v.Categoria;
GO

/* Existencias: 80 % (redondeado hacia abajo a la unidad o a 0,5 en metros y kilos) en la bodega principal y el resto en mostrador. */
INSERT INTO dbo.ExistenciaBodega (Sku, BodegaId, Cantidad)
SELECT p.Sku, b.BodegaId,
       CASE WHEN b.Nombre = N'Bodega principal'
            THEN CASE WHEN p.Unidad IN (N'Metro', N'Kilogramo') THEN FLOOR(p.StockActual * 0.8 * 2) / 2 ELSE FLOOR(p.StockActual * 0.8) END
            ELSE p.StockActual - CASE WHEN p.Unidad IN (N'Metro', N'Kilogramo') THEN FLOOR(p.StockActual * 0.8 * 2) / 2 ELSE FLOOR(p.StockActual * 0.8) END
       END
FROM dbo.Producto p
CROSS JOIN dbo.Bodega b;
GO

/* Ventas de los últimos 30 días con variación por día (patrón SINTÉTICO, no observado en una ferretería real).
   Enteros para unidades, varillas, bultos y galones; un decimal para metros y kilos. */
INSERT INTO dbo.MovimientoInventario (Sku, Tipo, Cantidad, Motivo, Usuario, Fecha, BodegaId, UsuarioId)
SELECT x.Sku, 'Salida', x.Cantidad, N'Venta', u.NombreCompleto, x.Fecha, b.BodegaId, u.UsuarioId
FROM (
    SELECT v.Sku,
           CASE WHEN p.Unidad IN (N'Metro', N'Kilogramo') THEN ROUND(v.PorDia * f.Factor, 1) ELSE CEILING(v.PorDia * f.Factor) END AS Cantidad,
           DATEADD(HOUR, 10 + d.Dia % 8, DATEADD(DAY, -d.Dia, CAST(CAST(SYSDATETIME() AS DATE) AS DATETIME2(0)))) AS Fecha
    FROM (VALUES ('DEMO-0245', 2.0), ('DEMO-0110', 1.0), ('DEMO-0087', 4.7), ('DEMO-0033', 15.0),
                 ('DEMO-0199', 0.4), ('DEMO-0301', 1.0), ('DEMO-0302', 0.7), ('DEMO-0303', 0.2),
                 ('DEMO-0410', 0.5), ('DEMO-0520', 0.4), ('DEMO-0612', 0.7)) AS v (Sku, PorDia)
    JOIN dbo.Producto p ON p.Sku = v.Sku
    CROSS JOIN (VALUES (1),(2),(3),(4),(5),(6),(7),(8),(9),(10),(11),(12),(13),(14),(15),
                       (16),(17),(18),(19),(20),(21),(22),(23),(24),(25),(26),(27),(28),(29),(30)) AS d (Dia)
    CROSS APPLY (SELECT CASE d.Dia % 7 WHEN 0 THEN 0.6 WHEN 1 THEN 0.8 WHEN 2 THEN 1.0 WHEN 3 THEN 1.0
                                      WHEN 4 THEN 1.2 WHEN 5 THEN 1.5 ELSE 0.9 END AS Factor) AS f
) AS x
JOIN dbo.Usuario u ON u.NombreUsuario = 'demo.vendedor'
JOIN dbo.Bodega b ON b.Nombre = N'Bodega mostrador (DEMO)'
WHERE x.Cantidad > 0;
GO

/* Órdenes: una aprobada hace 12 días sin pagar (el pago vence pronto) y una pendiente de aprobación. */
INSERT INTO dbo.OrdenCompra (NitProveedor, Estado, FechaCreacion, FechaAprobacion, FechaEntregaEsperada, Pagada)
VALUES ('000001002-1', N'Aprobada', DATEADD(DAY, -14, SYSDATETIME()), DATEADD(DAY, -12, SYSDATETIME()), DATEADD(DAY, -7, SYSDATETIME()), 0),
       ('000001002-1', N'Pendiente de aprobación', DATEADD(DAY, -3, SYSDATETIME()), NULL, NULL, 0);

INSERT INTO dbo.DetalleOrdenCompra (OrdenId, Sku, Cantidad, PrecioUnitario)
SELECT o.OrdenId, v.Sku, v.Cantidad, p.PrecioCompra
FROM (VALUES (N'Aprobada', 'DEMO-0110', 100), (N'Pendiente de aprobación', 'DEMO-0612', 50), (N'Pendiente de aprobación', 'DEMO-0611', 6)) AS v (Estado, Sku, Cantidad)
JOIN dbo.OrdenCompra o ON o.Estado = v.Estado
JOIN dbo.Producto p ON p.Sku = v.Sku;
GO
