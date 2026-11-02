IF DB_NAME() <> 'TLS' AND DB_NAME() NOT LIKE 'TLS_Test[_]%'
    THROW 50000, 'Wrong database for TLS upgrade.', 1;
IF OBJECT_ID(N'dbo.EventImports', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.EventImports (
        EventId nvarchar(160) COLLATE Latin1_General_100_BIN2 NOT NULL PRIMARY KEY,
        Source nvarchar(max) NOT NULL,
        SourceSystemId nvarchar(160) COLLATE Latin1_General_100_BIN2 NOT NULL,
        SourceEventId nvarchar(160) COLLATE Latin1_General_100_BIN2 NOT NULL,
        SourceTimeZone nvarchar(max) NOT NULL,
        ExportedAt datetimeoffset NOT NULL,
        ImportedAt datetimeoffset NOT NULL,
        Stage nvarchar(8) NOT NULL,
        PackageJson nvarchar(max) NOT NULL,
        CONSTRAINT FK_EventImports_Events_EventId FOREIGN KEY (EventId) REFERENCES dbo.Events(Id)
    );
END;

IF COL_LENGTH('dbo.SystemSettings','AppName') IS NULL
    ALTER TABLE dbo.SystemSettings ADD AppName nvarchar(max) NOT NULL CONSTRAINT DF_TLS_AppName DEFAULT N'TLS';
IF COL_LENGTH('dbo.SystemSettings','LogoLightUrl') IS NULL
    ALTER TABLE dbo.SystemSettings ADD LogoLightUrl nvarchar(max) NOT NULL CONSTRAINT DF_TLS_LogoLight DEFAULT N'';
IF COL_LENGTH('dbo.SystemSettings','LogoDarkUrl') IS NULL
    ALTER TABLE dbo.SystemSettings ADD LogoDarkUrl nvarchar(max) NOT NULL CONSTRAINT DF_TLS_LogoDark DEFAULT N'';
