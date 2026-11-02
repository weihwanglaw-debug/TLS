-- Additive SBA master storage. Existing event and participant data is untouched.
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID(N'[dbo].[BadmintonClub]', N'U') IS NULL
BEGIN
CREATE TABLE [BadmintonClub] (
    [ClubId] int NOT NULL IDENTITY,
    [Name] nvarchar(255) NOT NULL,
    [ContactNumber] nvarchar(50) NULL,
    [Email] nvarchar(255) NULL,
    [Address] nvarchar(500) NULL,
    [Country] nvarchar(100) NULL,
    [IsActive] bit NOT NULL DEFAULT CAST(1 AS bit),
    [CreatedAt] datetime2 NOT NULL DEFAULT ((getdate())),
    [UpdatedAt] datetime2 NULL,
    CONSTRAINT [PK_BadmintonClub] PRIMARY KEY ([ClubId])
);
END;
IF OBJECT_ID(N'[dbo].[SbaRankings]', N'U') IS NULL
BEGIN
CREATE TABLE [SbaRankings] (
    [SbaRankingID] int NOT NULL IDENTITY,
    [RankingType] nvarchar(80) NOT NULL,
    [Player1SbaID] varchar(20) COLLATE Latin1_General_100_BIN2 NOT NULL,
    [Player1Name] nvarchar(200) NOT NULL,
    [Player1Club] nvarchar(200) NULL,
    [Player1DateOfBirth] date NULL,
    [Player2SbaID] varchar(20) COLLATE Latin1_General_100_BIN2 NULL,
    [Player2Name] nvarchar(200) NULL,
    [Player2Club] nvarchar(200) NULL,
    [Player2DateOfBirth] date NULL,
    [YearOfBirth] int NULL,
    [AccumulatedScore] int NOT NULL,
    [Ranking] int NOT NULL,
    [Tournaments] int NOT NULL,
    [UpdatedAt] datetime2 NOT NULL DEFAULT ((sysutcdatetime())),
    CONSTRAINT [PK_SbaRankings] PRIMARY KEY ([SbaRankingID])
);
CREATE UNIQUE INDEX [UX_SbaRankings_Type_Players] ON [SbaRankings] ([RankingType], [Player1SbaID], [Player2SbaID]) WHERE [Player2SbaID] IS NOT NULL;
CREATE UNIQUE INDEX [UX_SbaRankings_Type_Singles] ON [SbaRankings] ([RankingType], [Player1SbaID]) WHERE [Player2SbaID] IS NULL;
END;
COMMIT TRANSACTION;
