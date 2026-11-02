IF OBJECT_ID(N'dbo.IdSequences',N'U') IS NULL
BEGIN
    CREATE TABLE dbo.IdSequences (
        [Key] nvarchar(160) COLLATE Latin1_General_100_BIN2 NOT NULL CONSTRAINT PK_IdSequences PRIMARY KEY,
        [Value] bigint NOT NULL,
        CONSTRAINT CK_IdSequences_Value CHECK ([Value]>=0)
    );
END;
IF OBJECT_ID(N'dbo.ImportIdMappings',N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ImportIdMappings (
        Fingerprint nvarchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL CONSTRAINT PK_ImportIdMappings PRIMARY KEY,
        EventId nvarchar(160) COLLATE Latin1_General_100_BIN2 NOT NULL,
        Kind nvarchar(24) COLLATE Latin1_General_100_BIN2 NOT NULL,
        SourceId nvarchar(160) COLLATE Latin1_General_100_BIN2 NOT NULL,
        LocalId nvarchar(160) COLLATE Latin1_General_100_BIN2 NOT NULL
    );
    CREATE UNIQUE INDEX IX_ImportIdMappings_LocalId ON dbo.ImportIdMappings(LocalId);
END;
