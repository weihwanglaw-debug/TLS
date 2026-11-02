IF DB_NAME() <> 'TLS' AND DB_NAME() NOT LIKE 'TLS_Test[_]%'
    THROW 50000, 'Wrong database for TLS live upgrade.', 1;
IF COL_LENGTH('dbo.SystemSettings','LiveVenueJson') IS NULL
    ALTER TABLE dbo.SystemSettings ADD LiveVenueJson nvarchar(max) NOT NULL CONSTRAINT DF_TLS_LiveVenue DEFAULT N'{}';
IF OBJECT_ID('dbo.MatchLiveRecords','U') IS NULL
    CREATE TABLE dbo.MatchLiveRecords(MatchId nvarchar(160) COLLATE Latin1_General_100_BIN2 NOT NULL PRIMARY KEY,
    ProgramId nvarchar(160) COLLATE Latin1_General_100_BIN2 NOT NULL,
    Status nvarchar(8) NOT NULL, Json nvarchar(max) NOT NULL,
    CONSTRAINT FK_TLS_MatchLiveProgram FOREIGN KEY(ProgramId) REFERENCES dbo.Programs(Id),
    CONSTRAINT CK_MatchLiveRecords_Status CHECK(Status IN ('SC','IP','RC','C','WO')));
