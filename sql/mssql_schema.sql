-- ============================================================
-- Script for Azure SQL (Microsoft SQL Server)
-- Table and stored procedures for the voting application
-- ============================================================

IF OBJECT_ID('dbo.Payload', 'U') IS NOT NULL
    DROP TABLE dbo.Payload;
GO

IF OBJECT_ID('dbo.Vote', 'U') IS NOT NULL
    DROP TABLE dbo.Vote;
GO

CREATE TABLE dbo.Vote
(
    IDVote      BIGINT IDENTITY(1,1) PRIMARY KEY,
    [Option]    VARCHAR(10) NOT NULL,
    DateCreated DATETIME2   NOT NULL DEFAULT SYSUTCDATETIME(),

    CONSTRAINT CK_Vote_Option CHECK ([Option] IN ('yes', 'no'))
);
GO

-- ------------------------------------------------------------
-- Optional per-vote payload, used to benchmark write throughput
-- with a variable-size blob attached to the vote. Only populated
-- when Load:PayloadBytesPerVote resolves to a value above 0 - see
-- VoteAdd below.
-- ------------------------------------------------------------
CREATE TABLE dbo.Payload
(
    IDPayload   BIGINT IDENTITY(1,1) PRIMARY KEY,
    IDVote      BIGINT         NOT NULL,
    Data        VARBINARY(MAX) NOT NULL,
    DateCreated DATETIME2      NOT NULL DEFAULT SYSUTCDATETIME(),

    CONSTRAINT FK_Payload_Vote FOREIGN KEY (IDVote) REFERENCES dbo.Vote(IDVote)
);
GO

-- ------------------------------------------------------------
-- Stored procedure for inserting a vote, with an optional payload
-- blob inserted alongside it in the same call
-- ------------------------------------------------------------
IF OBJECT_ID('dbo.VoteAdd', 'P') IS NOT NULL
    DROP PROCEDURE dbo.VoteAdd;
GO

CREATE PROCEDURE dbo.VoteAdd
    @Option  VARCHAR(10),
    @Payload VARBINARY(MAX) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO dbo.Vote ([Option], DateCreated)
    VALUES (@Option, SYSUTCDATETIME());

    IF @Payload IS NOT NULL
    BEGIN
        INSERT INTO dbo.Payload (IDVote, Data, DateCreated)
        VALUES (SCOPE_IDENTITY(), @Payload, SYSUTCDATETIME());
    END
END
GO

-- ------------------------------------------------------------
-- Stored procedure for retrieving the summed results with
-- percentages, used by the public dashboard
-- ------------------------------------------------------------
IF OBJECT_ID('dbo.VoteReportGet', 'P') IS NOT NULL
    DROP PROCEDURE dbo.VoteReportGet;
GO

CREATE PROCEDURE dbo.VoteReportGet
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        SUM(CASE WHEN [Option] = 'yes' THEN 1 ELSE 0 END) AS [Yes],
        SUM(CASE WHEN [Option] = 'no'  THEN 1 ELSE 0 END) AS [No],
        COUNT(*) AS [Total],
        CASE WHEN COUNT(*) = 0 THEN 0
             ELSE ROUND(100.0 * SUM(CASE WHEN [Option] = 'yes' THEN 1 ELSE 0 END) / COUNT(*), 2)
        END AS [YesPercent],
        CASE WHEN COUNT(*) = 0 THEN 0
             ELSE ROUND(100.0 * SUM(CASE WHEN [Option] = 'no' THEN 1 ELSE 0 END) / COUNT(*), 2)
        END AS [NoPercent],
        (SELECT COUNT(*) FROM dbo.Payload) AS [PayloadCount],
        (SELECT ISNULL(SUM(DATALENGTH(Data)), 0) FROM dbo.Payload) AS [PayloadTotalBytes]
    FROM dbo.Vote;
END
GO

-- ------------------------------------------------------------
-- Stored procedure that wipes all data (Vote and Payload), resets
-- identity counters, and reclaims the freed disk space. Destructive -
-- exposed via the dashboard's "cleanup" button, always behind login.
-- ------------------------------------------------------------
IF OBJECT_ID('dbo.DatabaseCleanup', 'P') IS NOT NULL
    DROP PROCEDURE dbo.DatabaseCleanup;
GO

CREATE PROCEDURE dbo.DatabaseCleanup
AS
BEGIN
    SET NOCOUNT ON;

    TRUNCATE TABLE dbo.Payload;
    TRUNCATE TABLE dbo.Vote;
END
GO
