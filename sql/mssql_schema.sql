-- ============================================================
-- Skripta za Azure SQL (Microsoft SQL Server)
-- Tablica i pohranjene procedure za voting aplikaciju
-- ============================================================

IF OBJECT_ID('dbo.Vote', 'U') IS NOT NULL
    DROP TABLE dbo.Vote;
GO

CREATE TABLE dbo.Vote
(
    IDVote      INT IDENTITY(1,1) PRIMARY KEY,
    [Option]    VARCHAR(10) NOT NULL,
    DateCreated DATETIME2   NOT NULL DEFAULT SYSUTCDATETIME(),

    CONSTRAINT CK_Vote_Option CHECK ([Option] IN ('pas', 'macka'))
);
GO

-- ------------------------------------------------------------
-- Pohranjena procedura za unos glasa
-- ------------------------------------------------------------
IF OBJECT_ID('dbo.VoteAdd', 'P') IS NOT NULL
    DROP PROCEDURE dbo.VoteAdd;
GO

CREATE PROCEDURE dbo.VoteAdd
    @Option VARCHAR(10)
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO dbo.Vote ([Option], DateCreated)
    VALUES (@Option, SYSUTCDATETIME());
END
GO

-- ------------------------------------------------------------
-- Pohranjena procedura za dohvaćanje zbrojenih rezultata
-- ------------------------------------------------------------
IF OBJECT_ID('dbo.VoteCountsGet', 'P') IS NOT NULL
    DROP PROCEDURE dbo.VoteCountsGet;
GO

CREATE PROCEDURE dbo.VoteCountsGet
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        SUM(CASE WHEN [Option] = 'pas'   THEN 1 ELSE 0 END) AS Pas,
        SUM(CASE WHEN [Option] = 'macka' THEN 1 ELSE 0 END) AS Macka
    FROM dbo.Vote;
END
GO
