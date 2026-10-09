/*
  Base de MENTIRA que imita las tablas de World Office que lee el Sincronizador (mismos nombres de
  tabla y columna reales, solo las que se usan). Sirve para probar el Sincronizador de punta a
  punta sin tocar World Office. Crea la base [WoMock] desde cero cada vez.

  Uso (con el contenedor de Remisiones levantado):
    docker exec -i jimaco-remisiones-db /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P "<SA_PASSWORD>" -f 65001 < wo-mock.sql
*/
IF DB_ID('WoMock') IS NOT NULL
BEGIN
    ALTER DATABASE WoMock SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    DROP DATABASE WoMock;
END
GO
CREATE DATABASE WoMock;
GO
USE WoMock;
GO

CREATE TABLE dbo.Inventarios (
    IdInventario int IDENTITY(1,1) PRIMARY KEY,
    [CódigoInventario] nvarchar(100) NOT NULL,
    [Descripción] nvarchar(1000) NOT NULL,
    UnidadDeMedida nvarchar(30) NOT NULL,
    Iva numeric(13,6) NULL,
    Activo smallint NOT NULL,
    VersionCol rowversion
);

CREATE TABLE dbo.[Inventarios - Bodegas] (
    IdBodegaInventario nvarchar(30) PRIMARY KEY,
    Nombre nvarchar(100) NOT NULL
);

CREATE TABLE dbo.[Inventarios - Por Bodega] (
    IdInventario int NOT NULL,
    IdBodegaInventario nvarchar(30) NOT NULL,
    [Ubicación] nvarchar(100) NULL
);

CREATE TABLE dbo.[CuentasContables - Documentos] (
    IdEmpresa int NOT NULL,
    IdCuentaContableDocumento nvarchar(4) NOT NULL,
    EfectoSobreElInventario smallint NOT NULL,
    PRIMARY KEY (IdEmpresa, IdCuentaContableDocumento)
);

CREATE TABLE dbo.[CuentasContables - Asientos] (
    IdAsientoContable int IDENTITY(1,1) PRIMARY KEY,
    IdEmpresa int NOT NULL,
    IdCuentaContableDocumento nvarchar(4) NOT NULL,
    prefijo nvarchar(5) NULL,
    [DocumentoNúmero] numeric(20,0) NOT NULL
);

CREATE TABLE dbo.CCA_M_Inventarios (
    [Renglón] int IDENTITY(1,1) PRIMARY KEY,
    IdAsientoContable int NOT NULL,
    IdInventario int NOT NULL,
    IdBodegaInventario nvarchar(30) NOT NULL,
    Cantidad numeric(13,6) NOT NULL
);
GO

-- Misma definición que la vista real de World Office.
CREATE VIEW dbo.Vista_ExistenciasPorBodegas AS
SELECT dbo.CCA_M_Inventarios.IdInventario, SUM(dbo.CCA_M_Inventarios.Cantidad * dbo.[CuentasContables - Documentos].EfectoSobreElInventario) AS Existencia,
 dbo.Inventarios.[CódigoInventario], dbo.Inventarios.[CódigoInventario] as CodigoInventario, dbo.Inventarios.[Descripción], dbo.Inventarios.[Descripción] as Descripcion, dbo.[Inventarios - Bodegas].IdBodegaInventario AS Codigo_Bodega,
 dbo.[Inventarios - Bodegas].Nombre AS Nombre_Bodega, dbo.[Inventarios - Bodegas].IdBodegaInventario, dbo.[Inventarios - Bodegas].Nombre, IB2.[Ubicación], IB2.[Ubicación] as Ubicacion
FROM dbo.CCA_M_Inventarios INNER JOIN
 dbo.[CuentasContables - Asientos] INNER JOIN
 dbo.[CuentasContables - Documentos] ON dbo.[CuentasContables - Asientos].IdEmpresa = dbo.[CuentasContables - Documentos].IdEmpresa AND
 dbo.[CuentasContables - Asientos].IdCuentaContableDocumento = dbo.[CuentasContables - Documentos].IdCuentaContableDocumento ON
 dbo.CCA_M_Inventarios.IdAsientoContable = dbo.[CuentasContables - Asientos].IdAsientoContable INNER JOIN
 dbo.Inventarios ON dbo.CCA_M_Inventarios.IdInventario = dbo.Inventarios.IdInventario INNER JOIN
 dbo.[Inventarios - Bodegas] ON dbo.CCA_M_Inventarios.IdBodegaInventario = dbo.[Inventarios - Bodegas].IdBodegaInventario
 LEFT JOIN dbo.[Inventarios - Por Bodega] IB2 ON dbo.CCA_M_Inventarios.IdInventario = IB2.IdInventario AND dbo.CCA_M_Inventarios.IdBodegaInventario = IB2.IdBodegaInventario
WHERE (dbo.[CuentasContables - Documentos].EfectoSobreElInventario <> 0)
GROUP BY dbo.CCA_M_Inventarios.IdInventario, dbo.Inventarios.[CódigoInventario], dbo.Inventarios.[Descripción], dbo.[Inventarios - Bodegas].IdBodegaInventario,
 dbo.[Inventarios - Bodegas].Nombre, IB2.[Ubicación];
GO

CREATE TABLE dbo.[Terceros - Tipo de Identificación] (
    IdTipoIdentificacion int PRIMARY KEY,
    TipoDeIdentificacion nvarchar(100) NOT NULL,
    Abreviatura nvarchar(10) NULL
);

CREATE TABLE dbo.Ciudades (IdCiudad int PRIMARY KEY, Ciudad nvarchar(100) NOT NULL);

CREATE TABLE dbo.Terceros (
    IdTercero int IDENTITY(1,1) PRIMARY KEY,
    IdTipoIdentificacion int NULL,
    Identificacion nvarchar(50) NULL,
    IdentificacionCiudad int NULL,
    Nombre nvarchar(240) NULL,
    Apellidos nvarchar(240) NULL,
    Propiedades nvarchar(510) NULL,
    Activo smallint NOT NULL,
    Primer_Nombre nvarchar(240) NULL,
    Segundo_Nombre nvarchar(100) NULL,
    Primer_Apellido nvarchar(100) NULL,
    Segundo_Apellido nvarchar(100) NULL,
    VersionCol rowversion
);

CREATE TABLE dbo.[Terceros - Direcciones] (
    IdTerceroDireccion int IDENTITY(1,1) PRIMARY KEY,
    IdTercero int NOT NULL,
    [IdDirección] int NOT NULL,
    [Dirección] nvarchar(300) NULL,
    [Teléfonos] nvarchar(100) NULL,
    EMail nvarchar(200) NULL,
    IdCiudad int NULL,
    VersionCol rowversion,
    senDirecPpal smallint NULL,
    Telefono2 numeric(18,0) NULL
);
GO

-- ===== Datos =====
INSERT dbo.[Inventarios - Bodegas] VALUES (N'Uno', N'Principal'), (N'Dos', N'Secundaria');

INSERT dbo.Inventarios ([CódigoInventario], [Descripción], UnidadDeMedida, Iva, Activo) VALUES
 (N'PJ-4137',  N'PINTURA ESMALTE AMARILLO X GALON',        N'gal',  0.19, -1),
 (N'PJ-28211', N'MOSQUETON ACERO 50 KN DOBLE SEGURO',      N'Und.', 0.19, -1),
 (N'TR-15007', N'COMPRESOR LUBRICADO 50 LTS 3 1/2 HP',     N'Und.', 0.19, -1),
 (N'PJ-9001',  N'TUBO PVC PRESION 1/2 X 6 M',              N'Und.', 0.05, -1),
 (N'PJ-1111',  N'ARENA LAVADA M3',                         N'm3',   0.00, -1),
 (N'PJ-0001',  N'PRODUCTO DESCONTINUADO',                  N'Und.', 0.19,  0);

INSERT dbo.[CuentasContables - Documentos] VALUES (1, N'EA', 1), (1, N'REM', -1), (1, N'DREM', 1), (1, N'COT', 0);

-- Entradas de almacén (+) y remisiones de venta (-)
INSERT dbo.[CuentasContables - Asientos] (IdEmpresa, IdCuentaContableDocumento, prefijo, [DocumentoNúmero]) VALUES
 (1, N'EA', N'EA', 1), (1, N'REM', N'REM', 29294), (1, N'COT', N'COT', 16204), (1, N'EA', N'EA', 2);

INSERT dbo.CCA_M_Inventarios (IdAsientoContable, IdInventario, IdBodegaInventario, Cantidad) VALUES
 (1, 1, N'Uno', 100), (1, 2, N'Uno', 20), (1, 3, N'Uno', 5), (1, 4, N'Uno', 60),
 (4, 1, N'Dos', 12),
 (2, 1, N'Uno', 4), (2, 3, N'Uno', 1),
 (3, 2, N'Uno', 50);  -- una cotización: efecto 0, no cuenta

-- Tipos de identificación (los reales de Jimaco)
INSERT dbo.[Terceros - Tipo de Identificación] VALUES
 (13, N'CC', N'CC'), (31, N'NIT', N'NIT'), (22, N'Cédula de extranjería', N'CE'), (41, N'PASAPORTE', N'PA'), (7, N'CONSUMIDOR FINAL', NULL);

INSERT dbo.Ciudades VALUES (11001, N'Bogotá D.C.'), (15097, N'Boavita'), (5001, N'Medellín');

INSERT dbo.Terceros (IdTipoIdentificacion, Identificacion, IdentificacionCiudad, Nombre, Apellidos, Propiedades, Activo, Primer_Nombre, Segundo_Nombre, Primer_Apellido, Segundo_Apellido) VALUES
 (31, N'902020510', 15097, N'CONSORCIO CONSTRUCTOR ANDES', N'', N'Cliente;', -1, N'CONSORCIO CONSTRUCTOR ANDES', NULL, NULL, NULL),     -- 1 empresa
 (13, N'1015424228', 11001, N'CAMILO', N'PIRAGAUTA', N'Cliente', -1, N'CAMILO', N'ANDRES', N'PIRAGAUTA', N'PAEZ'),                        -- 2 persona natural, sin ';'
 (31, N'800123456', 5001, N'FERRETERIA EL PROVEEDOR SAS', N'', N'Proveedor;', -1, N'FERRETERIA EL PROVEEDOR SAS', NULL, NULL, NULL),      -- 3 solo proveedor: NO debe entrar
 (22, N'123456', 11001, N'JOHN', N'SMITH', N'Cliente;Proveedor;', -1, NULL, NULL, NULL, NULL),                                            -- 4 extranjero, nombre solo en Nombre/Apellidos
 (7,  N'222222222222', NULL, N'CONSUMIDOR FINAL', N'', N'Cliente;', -1, NULL, NULL, NULL, NULL);                                          -- 5 consumidor final: se omite

INSERT dbo.[Terceros - Direcciones] (IdTercero, [IdDirección], [Dirección], [Teléfonos], EMail, IdCiudad, senDirecPpal, Telefono2) VALUES
 (1, 1, N'CL 104 # 18 A - 52 OF 501', N'3218614753', N'facturacionconsorcio@andes.erco.energy', 11001, -1, 3108582223),
 (2, 1, N'CL 69 SUR 95A 41', N'0', N'kmilo2811@hotmail.com; otro@correo.com', 11001, -1, 32143137),
 (4, 1, N'CRA 7 # 10-20', N'', N'', 11001, -1, 0);
GO
