-- ============================================================
-- Script for Azure SQL (Microsoft SQL Server)
-- Table and stored procedures for the voting application
-- ============================================================

IF OBJECT_ID('dbo.Vote', 'U') IS NOT NULL
    DROP TABLE dbo.Vote;
GO

CREATE TABLE dbo.Vote
(
    IDVote      INT IDENTITY(1,1) PRIMARY KEY,
    [Option]    VARCHAR(10) NOT NULL,
    DateCreated DATETIME2   NOT NULL DEFAULT SYSUTCDATETIME(),

    CONSTRAINT CK_Vote_Option CHECK ([Option] IN ('yes', 'no'))
);
GO

-- ------------------------------------------------------------
-- Stored procedure for inserting a vote
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
-- Stored procedure for retrieving the summed results
-- ------------------------------------------------------------
IF OBJECT_ID('dbo.VoteCountsGet', 'P') IS NOT NULL
    DROP PROCEDURE dbo.VoteCountsGet;
GO

CREATE PROCEDURE dbo.VoteCountsGet
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        SUM(CASE WHEN [Option] = 'yes' THEN 1 ELSE 0 END) AS [Yes],
        SUM(CASE WHEN [Option] = 'no'  THEN 1 ELSE 0 END) AS [No]
    FROM dbo.Vote;
END
GO
