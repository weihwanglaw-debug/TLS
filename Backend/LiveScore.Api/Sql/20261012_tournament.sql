
IF DB_NAME() <> 'TLS' AND DB_NAME() NOT LIKE 'TLS_Test[_]%'
    THROW 50000, 'Wrong database for TLS upgrade.', 1;
IF COL_LENGTH('dbo.SystemSettings','CourtCount') IS NULL
    ALTER TABLE dbo.SystemSettings ADD CourtCount int NOT NULL CONSTRAINT DF_TLS_Courts DEFAULT 4;
IF OBJECT_ID('dbo.EventOperations','U') IS NULL
    CREATE TABLE dbo.EventOperations(EventId nvarchar(160) COLLATE Latin1_General_100_BIN2 NOT NULL PRIMARY KEY, Json nvarchar(max) NOT NULL,
    CONSTRAINT FK_TLS_EventOperations FOREIGN KEY(EventId) REFERENCES dbo.Events(Id));
IF OBJECT_ID('dbo.ProgramOperations','U') IS NULL
    CREATE TABLE dbo.ProgramOperations(ProgramId nvarchar(160) COLLATE Latin1_General_100_BIN2 NOT NULL PRIMARY KEY, Json nvarchar(max) NOT NULL,
    CONSTRAINT FK_TLS_ProgramOperations FOREIGN KEY(ProgramId) REFERENCES dbo.Programs(Id));
