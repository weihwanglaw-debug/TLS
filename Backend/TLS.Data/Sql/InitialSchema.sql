CREATE TABLE [AuditLogs] (
    [Id] bigint NOT NULL,
    [At] nvarchar(max) NOT NULL,
    [Action] nvarchar(max) NOT NULL,
    [BeforeJson] nvarchar(max) NOT NULL,
    [AfterJson] nvarchar(max) NOT NULL,
    CONSTRAINT [PK_AuditLogs] PRIMARY KEY ([Id])
);
GO


CREATE TABLE [Events] (
    [Id] nvarchar(160) COLLATE Latin1_General_100_BIN2 NOT NULL,
    [SortOrder] int NOT NULL,
    [Name] nvarchar(max) NOT NULL,
    [Venue] nvarchar(max) NOT NULL,
    [StartDate] date NOT NULL,
    [EndDate] date NOT NULL,
    [MaxProgramsPerParticipant] int NULL,
    [Status] nvarchar(8) NOT NULL,
    CONSTRAINT [PK_Events] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_Events_Status] CHECK ([Status] IN ('D','A','C','X'))
);
GO


CREATE TABLE [SystemSettings] (
    [Id] int NOT NULL,
    [LiveVenueJson] nvarchar(max) NOT NULL DEFAULT N'{}',
    [CourtCount] int NOT NULL,
    [AppName] nvarchar(max) NOT NULL,
    [LogoLightUrl] nvarchar(max) NOT NULL,
    [LogoDarkUrl] nvarchar(max) NOT NULL,
    [Revision] bigint NOT NULL,
    [TimeZone] nvarchar(max) NOT NULL,
    CONSTRAINT [PK_SystemSettings] PRIMARY KEY ([Id])
);
GO


CREATE TABLE [EventImports] (
    [EventId] nvarchar(160) COLLATE Latin1_General_100_BIN2 NOT NULL,
    [Source] nvarchar(max) NOT NULL,
    [SourceSystemId] nvarchar(160) COLLATE Latin1_General_100_BIN2 NOT NULL,
    [SourceEventId] nvarchar(160) COLLATE Latin1_General_100_BIN2 NOT NULL,
    [SourceTimeZone] nvarchar(max) NOT NULL,
    [ExportedAt] datetimeoffset NOT NULL,
    [ImportedAt] datetimeoffset NOT NULL,
    [Stage] nvarchar(8) NOT NULL,
    [PackageJson] nvarchar(max) NOT NULL,
    CONSTRAINT [PK_EventImports] PRIMARY KEY ([EventId]),
    CONSTRAINT [FK_EventImports_Events_EventId] FOREIGN KEY ([EventId]) REFERENCES [Events] ([Id]) ON DELETE NO ACTION
);
GO


CREATE TABLE [EventOperations] (
    [EventId] nvarchar(160) COLLATE Latin1_General_100_BIN2 NOT NULL,
    [Json] nvarchar(max) NOT NULL,
    CONSTRAINT [PK_EventOperations] PRIMARY KEY ([EventId]),
    CONSTRAINT [FK_EventOperations_Events_EventId] FOREIGN KEY ([EventId]) REFERENCES [Events] ([Id]) ON DELETE NO ACTION
);
GO


CREATE TABLE [Participants] (
    [Id] nvarchar(160) COLLATE Latin1_General_100_BIN2 NOT NULL,
    [SortOrder] int NOT NULL,
    [EventId] nvarchar(160) COLLATE Latin1_General_100_BIN2 NOT NULL,
    [Name] nvarchar(max) NOT NULL,
    [Dob] date NOT NULL,
    [Gender] nvarchar(max) NOT NULL,
    [Email] nvarchar(max) NOT NULL,
    [Mobile] nvarchar(max) NOT NULL,
    [Club] nvarchar(max) NOT NULL,
    [Nationality] nvarchar(max) NOT NULL,
    [SbaId] nvarchar(160) COLLATE Latin1_General_100_BIN2 NOT NULL,
    CONSTRAINT [PK_Participants] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_Participants_Events_EventId] FOREIGN KEY ([EventId]) REFERENCES [Events] ([Id]) ON DELETE NO ACTION
);
GO


CREATE TABLE [Programs] (
    [Id] nvarchar(160) COLLATE Latin1_General_100_BIN2 NOT NULL,
    [SortOrder] int NOT NULL,
    [EventId] nvarchar(160) COLLATE Latin1_General_100_BIN2 NOT NULL,
    [Name] nvarchar(max) NOT NULL,
    [Sport] nvarchar(max) NOT NULL,
    [EntryType] nvarchar(max) NOT NULL,
    [MinPlayers] int NOT NULL,
    [MaxPlayers] int NOT NULL,
    [MinAge] int NOT NULL,
    [MaxAge] int NOT NULL,
    [Gender] nvarchar(max) NOT NULL,
    [MinCapacity] int NOT NULL,
    [MaxCapacity] int NOT NULL,
    [CapacityUnit] nvarchar(max) NOT NULL,
    [Status] nvarchar(8) NOT NULL,
    CONSTRAINT [PK_Programs] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_Programs_Status] CHECK ([Status] IN ('A','X')),
    CONSTRAINT [FK_Programs_Events_EventId] FOREIGN KEY ([EventId]) REFERENCES [Events] ([Id]) ON DELETE NO ACTION
);
GO


CREATE TABLE [MatchLiveRecords] (
    [MatchId] nvarchar(160) COLLATE Latin1_General_100_BIN2 NOT NULL,
    [ProgramId] nvarchar(160) COLLATE Latin1_General_100_BIN2 NOT NULL,
    [Status] nvarchar(8) NOT NULL,
    [Json] nvarchar(max) NOT NULL,
    CONSTRAINT [PK_MatchLiveRecords] PRIMARY KEY ([MatchId]),
    CONSTRAINT [CK_MatchLiveRecords_Status] CHECK ([Status] IN ('SC','IP','RC','C','WO')),
    CONSTRAINT [FK_MatchLiveRecords_Programs_ProgramId] FOREIGN KEY ([ProgramId]) REFERENCES [Programs] ([Id]) ON DELETE NO ACTION
);
GO


CREATE TABLE [ProgramEntries] (
    [Id] nvarchar(160) COLLATE Latin1_General_100_BIN2 NOT NULL,
    [SortOrder] int NOT NULL,
    [EventId] nvarchar(160) COLLATE Latin1_General_100_BIN2 NOT NULL,
    [ProgramId] nvarchar(160) COLLATE Latin1_General_100_BIN2 NOT NULL,
    [Name] nvarchar(max) NOT NULL,
    [Seed] int NULL,
    [Status] nvarchar(8) NOT NULL,
    CONSTRAINT [PK_ProgramEntries] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_ProgramEntries_Status] CHECK ([Status] IN ('A','X')),
    CONSTRAINT [FK_ProgramEntries_Events_EventId] FOREIGN KEY ([EventId]) REFERENCES [Events] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_ProgramEntries_Programs_ProgramId] FOREIGN KEY ([ProgramId]) REFERENCES [Programs] ([Id]) ON DELETE NO ACTION
);
GO


CREATE TABLE [ProgramOperations] (
    [ProgramId] nvarchar(160) COLLATE Latin1_General_100_BIN2 NOT NULL,
    [Json] nvarchar(max) NOT NULL,
    CONSTRAINT [PK_ProgramOperations] PRIMARY KEY ([ProgramId]),
    CONSTRAINT [FK_ProgramOperations_Programs_ProgramId] FOREIGN KEY ([ProgramId]) REFERENCES [Programs] ([Id]) ON DELETE NO ACTION
);
GO


CREATE TABLE [EntryMembers] (
    [EntryId] nvarchar(160) COLLATE Latin1_General_100_BIN2 NOT NULL,
    [Position] int NOT NULL,
    [ParticipantId] nvarchar(160) COLLATE Latin1_General_100_BIN2 NOT NULL,
    CONSTRAINT [PK_EntryMembers] PRIMARY KEY ([EntryId], [Position]),
    CONSTRAINT [FK_EntryMembers_Participants_ParticipantId] FOREIGN KEY ([ParticipantId]) REFERENCES [Participants] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_EntryMembers_ProgramEntries_EntryId] FOREIGN KEY ([EntryId]) REFERENCES [ProgramEntries] ([Id]) ON DELETE NO ACTION
);
GO


IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'AppName', N'CourtCount', N'LiveVenueJson', N'LogoDarkUrl', N'LogoLightUrl', N'Revision', N'TimeZone') AND [object_id] = OBJECT_ID(N'[SystemSettings]'))
    SET IDENTITY_INSERT [SystemSettings] ON;
INSERT INTO [SystemSettings] ([Id], [AppName], [CourtCount], [LiveVenueJson], [LogoDarkUrl], [LogoLightUrl], [Revision], [TimeZone])
VALUES (1, N'TLS', 4, N'{}', N'', N'', CAST(0 AS bigint), N'Asia/Singapore');
IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'AppName', N'CourtCount', N'LiveVenueJson', N'LogoDarkUrl', N'LogoLightUrl', N'Revision', N'TimeZone') AND [object_id] = OBJECT_ID(N'[SystemSettings]'))
    SET IDENTITY_INSERT [SystemSettings] OFF;
GO


CREATE INDEX [IX_EntryMembers_ParticipantId] ON [EntryMembers] ([ParticipantId]);
GO


CREATE INDEX [IX_MatchLiveRecords_ProgramId] ON [MatchLiveRecords] ([ProgramId]);
GO


CREATE INDEX [IX_Participants_EventId] ON [Participants] ([EventId]);
GO


CREATE INDEX [IX_ProgramEntries_EventId] ON [ProgramEntries] ([EventId]);
GO


CREATE INDEX [IX_ProgramEntries_ProgramId] ON [ProgramEntries] ([ProgramId]);
GO


CREATE INDEX [IX_Programs_EventId] ON [Programs] ([EventId]);
GO




-- Readable business IDs: persistent counters and source mapping.
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
