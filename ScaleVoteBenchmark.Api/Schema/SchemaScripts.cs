namespace ScaleVoteBenchmark.Api.Schema
{
    /// <summary>
    /// SQL batches used to bootstrap the database schema on first run
    /// (see RepoFactory-created repositories' EnsureSchema()). Each
    /// repository only executes these when the Vote table does not yet
    /// exist, so an already-provisioned database (and its data) is never
    /// touched. Mirrors the sql/*.sql scripts in the repository root,
    /// which remain available for manual provisioning or an explicit
    /// drop-and-recreate reset - update both places together if the
    /// schema changes.
    /// </summary>
    public static class SchemaScripts
    {
        public static readonly string[] MsSql =
        {
            @"CREATE TABLE dbo.Vote
(
    IDVote      BIGINT IDENTITY(1,1) PRIMARY KEY,
    [Option]    VARCHAR(10) NOT NULL,
    DateCreated DATETIME2   NOT NULL DEFAULT SYSUTCDATETIME(),

    CONSTRAINT CK_Vote_Option CHECK ([Option] IN ('yes', 'no'))
);",

            @"CREATE PROCEDURE dbo.VoteAdd
    @Option VARCHAR(10)
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO dbo.Vote ([Option], DateCreated)
    VALUES (@Option, SYSUTCDATETIME());
END",

            @"CREATE PROCEDURE dbo.VoteCountsGet
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        SUM(CASE WHEN [Option] = 'yes' THEN 1 ELSE 0 END) AS [Yes],
        SUM(CASE WHEN [Option] = 'no'  THEN 1 ELSE 0 END) AS [No]
    FROM dbo.Vote;
END",

            @"CREATE PROCEDURE dbo.VoteReportGet
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
        END AS [NoPercent]
    FROM dbo.Vote;
END",
        };

        public static readonly string[] MySql =
        {
            @"CREATE TABLE `Vote`
(
    `IDVote`      BIGINT AUTO_INCREMENT PRIMARY KEY,
    `Option`      VARCHAR(10) NOT NULL,
    `DateCreated` DATETIME    NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT `CK_Vote_Option` CHECK (`Option` IN ('yes', 'no'))
);",

            @"CREATE PROCEDURE `VoteAdd`(
    IN pOption VARCHAR(10)
)
BEGIN
    INSERT INTO `Vote` (`Option`, `DateCreated`)
    VALUES (pOption, UTC_TIMESTAMP());
END",

            @"CREATE PROCEDURE `VoteCountsGet`()
BEGIN
    SELECT
        SUM(CASE WHEN `Option` = 'yes' THEN 1 ELSE 0 END) AS `Yes`,
        SUM(CASE WHEN `Option` = 'no'  THEN 1 ELSE 0 END) AS `No`
    FROM `Vote`;
END",

            @"CREATE PROCEDURE `VoteReportGet`()
BEGIN
    SELECT
        SUM(CASE WHEN `Option` = 'yes' THEN 1 ELSE 0 END) AS `Yes`,
        SUM(CASE WHEN `Option` = 'no'  THEN 1 ELSE 0 END) AS `No`,
        COUNT(*) AS `Total`,
        IF(COUNT(*) = 0, 0, ROUND(100.0 * SUM(CASE WHEN `Option` = 'yes' THEN 1 ELSE 0 END) / COUNT(*), 2)) AS `YesPercent`,
        IF(COUNT(*) = 0, 0, ROUND(100.0 * SUM(CASE WHEN `Option` = 'no' THEN 1 ELSE 0 END) / COUNT(*), 2)) AS `NoPercent`
    FROM `Vote`;
END",
        };

        public static readonly string[] PostgreSql =
        {
            @"CREATE TABLE vote
(
    id_vote      BIGSERIAL PRIMARY KEY,
    option       VARCHAR(10) NOT NULL CHECK (option IN ('yes', 'no')),
    date_created TIMESTAMP   NOT NULL DEFAULT (now() AT TIME ZONE 'utc')
);",

            @"CREATE PROCEDURE vote_add(p_option VARCHAR(10))
LANGUAGE plpgsql
AS $$
BEGIN
    INSERT INTO vote (option, date_created)
    VALUES (p_option, now() AT TIME ZONE 'utc');
END;
$$;",

            @"CREATE FUNCTION vote_counts_get()
RETURNS TABLE(yes_count INT, no_count INT)
LANGUAGE plpgsql
AS $$
BEGIN
    RETURN QUERY
    SELECT
        SUM(CASE WHEN option = 'yes' THEN 1 ELSE 0 END)::INT AS yes_count,
        SUM(CASE WHEN option = 'no'  THEN 1 ELSE 0 END)::INT AS no_count
    FROM vote;
END;
$$;",

            @"CREATE FUNCTION vote_report_get()
RETURNS TABLE(yes_count INT, no_count INT, total INT, yes_percent NUMERIC, no_percent NUMERIC)
LANGUAGE plpgsql
AS $$
BEGIN
    RETURN QUERY
    SELECT
        SUM(CASE WHEN option = 'yes' THEN 1 ELSE 0 END)::INT AS yes_count,
        SUM(CASE WHEN option = 'no'  THEN 1 ELSE 0 END)::INT AS no_count,
        COUNT(*)::INT AS total,
        CASE WHEN COUNT(*) = 0 THEN 0
             ELSE ROUND(100.0 * SUM(CASE WHEN option = 'yes' THEN 1 ELSE 0 END) / COUNT(*), 2)
        END AS yes_percent,
        CASE WHEN COUNT(*) = 0 THEN 0
             ELSE ROUND(100.0 * SUM(CASE WHEN option = 'no' THEN 1 ELSE 0 END) / COUNT(*), 2)
        END AS no_percent
    FROM vote;
END;
$$;",
        };

        // SQLite has no stored procedure/function support, so
        // SqliteRepository runs plain SQL directly - this batch only
        // creates the table. DateCreated is filled by SQLite's built-in
        // CURRENT_TIMESTAMP default (always UTC), mirroring the other
        // providers' behavior of stamping the vote's insert time.
        public static readonly string[] Sqlite =
        {
            @"CREATE TABLE Vote
(
    IDVote      INTEGER PRIMARY KEY AUTOINCREMENT,
    ""Option""    TEXT NOT NULL CHECK (""Option"" IN ('yes', 'no')),
    DateCreated TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
);",
        };
    }
}
