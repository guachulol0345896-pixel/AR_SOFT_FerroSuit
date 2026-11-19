/* =====================================================================
   FerroSuite — Migración V003: autenticación, autorización y auditoría

   - Usuario.ClaveIdentity: hash de la clave en el formato de ASP.NET Core
     Identity (PasswordHasher, PBKDF2-HMAC-SHA512 con sal). Las claves creadas
     con la versión 2 (columnas ClaveHash/ClaveSal/ClaveIteraciones) se
     conservan: el programa las verifica una vez y, si son correctas, las
     convierte al formato nuevo y borra las anteriores. Nunca hay dos formatos
     a la vez para un mismo usuario.
   - Bloqueo temporal por intentos fallidos, cambio obligatorio de clave y
     fecha del último ingreso.
   - Tabla AuditoriaEvento: registro de operaciones sensibles. Solo se
     insertan filas (no se modifican ni se borran). Nunca guarda claves,
     hash ni otros secretos.
   - Nombres de los roles según el Documento Maestro.

   NO BORRA DATOS. Se ejecuta dentro de una transacción.
   ===================================================================== */

ALTER TABLE dbo.Usuario ADD
    ClaveIdentity     NVARCHAR(256)  NULL,
    DebeCambiarClave  BIT            NOT NULL CONSTRAINT DF_Usuario_DebeCambiar DEFAULT (0),
    IntentosFallidos  INT            NOT NULL CONSTRAINT DF_Usuario_Intentos DEFAULT (0),
    BloqueadoHasta    DATETIME2(0)   NULL,
    UltimoIngreso     DATETIME2(0)   NULL;
GO

ALTER TABLE dbo.Usuario DROP CONSTRAINT CK_Usuario_Clave;
ALTER TABLE dbo.Usuario ADD CONSTRAINT CK_Usuario_Clave CHECK (
    /* Formato anterior (versión 2): los tres datos o ninguno, con al menos 100.000 iteraciones. */
    ((ClaveHash IS NULL AND ClaveSal IS NULL AND ClaveIteraciones IS NULL)
     OR (ClaveHash IS NOT NULL AND ClaveSal IS NOT NULL AND ClaveIteraciones >= 100000))
    /* Nunca los dos formatos a la vez. */
    AND (ClaveIdentity IS NULL OR ClaveHash IS NULL)
    AND (ClaveIdentity IS NULL OR LEN(ClaveIdentity) >= 40));
ALTER TABLE dbo.Usuario ADD CONSTRAINT CK_Usuario_Intentos CHECK (IntentosFallidos >= 0);
GO

UPDATE dbo.Rol SET Nombre = N'Administrador'          WHERE Codigo = 'ADMIN';
UPDATE dbo.Rol SET Nombre = N'Encargado de bodega'    WHERE Codigo = 'BODEGA';
UPDATE dbo.Rol SET Nombre = N'Vendedor de mostrador'  WHERE Codigo = 'VENDEDOR';
UPDATE dbo.Rol SET Nombre = N'Contador externo'       WHERE Codigo = 'CONTADOR';
GO

CREATE TABLE dbo.AuditoriaEvento (
    EventoId       BIGINT          IDENTITY(1,1) NOT NULL,
    Fecha          DATETIME2(0)    NOT NULL CONSTRAINT DF_Auditoria_Fecha DEFAULT (SYSDATETIME()),
    UsuarioId      INT             NULL,
    NombreUsuario  NVARCHAR(60)    NOT NULL,
    RolCodigo      VARCHAR(20)     NULL,
    SesionId       CHAR(32)        NULL,
    Accion         VARCHAR(60)     NOT NULL,
    Resultado      VARCHAR(10)     NOT NULL,
    Objeto         NVARCHAR(120)   NULL,
    Detalle        NVARCHAR(400)   NULL,
    Equipo         NVARCHAR(64)    NULL,
    CONSTRAINT PK_AuditoriaEvento PRIMARY KEY (EventoId),
    CONSTRAINT FK_Auditoria_Usuario FOREIGN KEY (UsuarioId) REFERENCES dbo.Usuario (UsuarioId),
    CONSTRAINT CK_Auditoria_Resultado CHECK (Resultado IN ('Exitoso', 'Fallido', 'Denegado')),
    CONSTRAINT CK_Auditoria_Accion CHECK (LEN(Accion) > 0)
);
CREATE INDEX IX_Auditoria_Fecha ON dbo.AuditoriaEvento (Fecha DESC);
CREATE INDEX IX_Auditoria_Usuario ON dbo.AuditoriaEvento (UsuarioId);
GO

/* La auditoría es historial: no se modifica ni se borra. */
CREATE TRIGGER dbo.TR_AuditoriaEvento_SoloInsertar
ON dbo.AuditoriaEvento
INSTEAD OF UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    THROW 51020, N'El registro de auditoría no se modifica ni se borra.', 1;
END;
GO
