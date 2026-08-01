namespace ScaleTrigger.Schema
{
    /// <summary>
    /// SQL batches used to bootstrap the database schema on first run
    /// (see RepoFactory-created repositories' EnsureSchema()). Each
    /// repository only executes these when the Vote table does not yet
    /// exist, so an already-provisioned database (and its data) is never
    /// touched. This is the only definition of the schema - there is no
    /// separate copy to keep in sync.
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

            @"CREATE TABLE dbo.Payload
(
    IDPayload   BIGINT IDENTITY(1,1) PRIMARY KEY,
    IDVote      BIGINT         NOT NULL,
    Data        VARBINARY(MAX) NOT NULL,
    DateCreated DATETIME2      NOT NULL DEFAULT SYSUTCDATETIME(),

    CONSTRAINT FK_Payload_Vote FOREIGN KEY (IDVote) REFERENCES dbo.Vote(IDVote)
);",

            @"CREATE PROCEDURE dbo.VoteAdd
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
        END AS [NoPercent],
        (SELECT COUNT(*) FROM dbo.Payload) AS [PayloadCount],
        (SELECT ISNULL(SUM(DATALENGTH(Data)), 0) FROM dbo.Payload) AS [PayloadTotalBytes]
    FROM dbo.Vote;
END",

            @"CREATE PROCEDURE dbo.DatabaseCleanup
AS
BEGIN
    SET NOCOUNT ON;

    TRUNCATE TABLE dbo.Payload;
    TRUNCATE TABLE dbo.Vote;
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

            @"CREATE TABLE `Payload`
(
    `IDPayload`   BIGINT AUTO_INCREMENT PRIMARY KEY,
    `IDVote`      BIGINT   NOT NULL,
    `Data`        LONGBLOB NOT NULL,
    `DateCreated` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT `FK_Payload_Vote` FOREIGN KEY (`IDVote`) REFERENCES `Vote`(`IDVote`)
);",

            @"CREATE PROCEDURE `VoteAdd`(
    IN pOption VARCHAR(10),
    IN pPayload LONGBLOB
)
BEGIN
    INSERT INTO `Vote` (`Option`, `DateCreated`)
    VALUES (pOption, UTC_TIMESTAMP());

    IF pPayload IS NOT NULL THEN
        INSERT INTO `Payload` (`IDVote`, `Data`, `DateCreated`)
        VALUES (LAST_INSERT_ID(), pPayload, UTC_TIMESTAMP());
    END IF;
END",

            @"CREATE PROCEDURE `VoteReportGet`()
BEGIN
    SELECT
        SUM(CASE WHEN `Option` = 'yes' THEN 1 ELSE 0 END) AS `Yes`,
        SUM(CASE WHEN `Option` = 'no'  THEN 1 ELSE 0 END) AS `No`,
        COUNT(*) AS `Total`,
        IF(COUNT(*) = 0, 0, ROUND(100.0 * SUM(CASE WHEN `Option` = 'yes' THEN 1 ELSE 0 END) / COUNT(*), 2)) AS `YesPercent`,
        IF(COUNT(*) = 0, 0, ROUND(100.0 * SUM(CASE WHEN `Option` = 'no' THEN 1 ELSE 0 END) / COUNT(*), 2)) AS `NoPercent`,
        (SELECT COUNT(*) FROM `Payload`) AS `PayloadCount`,
        (SELECT IFNULL(SUM(LENGTH(`Data`)), 0) FROM `Payload`) AS `PayloadTotalBytes`
    FROM `Vote`;
END",

            @"CREATE PROCEDURE `DatabaseCleanup`()
BEGIN
    SET FOREIGN_KEY_CHECKS = 0;
    TRUNCATE TABLE `Payload`;
    TRUNCATE TABLE `Vote`;
    SET FOREIGN_KEY_CHECKS = 1;
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

            @"CREATE TABLE payload
(
    id_payload   BIGSERIAL PRIMARY KEY,
    id_vote      BIGINT    NOT NULL REFERENCES vote(id_vote),
    data         BYTEA     NOT NULL,
    date_created TIMESTAMP NOT NULL DEFAULT (now() AT TIME ZONE 'utc')
);",

            @"CREATE PROCEDURE vote_add(p_option VARCHAR(10), p_payload BYTEA DEFAULT NULL)
LANGUAGE plpgsql
AS $$
DECLARE
    new_id_vote BIGINT;
BEGIN
    INSERT INTO vote (option, date_created)
    VALUES (p_option, now() AT TIME ZONE 'utc')
    RETURNING id_vote INTO new_id_vote;

    IF p_payload IS NOT NULL THEN
        INSERT INTO payload (id_vote, data, date_created)
        VALUES (new_id_vote, p_payload, now() AT TIME ZONE 'utc');
    END IF;
END;
$$;",

            @"CREATE FUNCTION vote_report_get()
RETURNS TABLE(yes_count INT, no_count INT, total INT, yes_percent NUMERIC, no_percent NUMERIC, payload_count INT, payload_total_bytes BIGINT)
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
        END AS no_percent,
        (SELECT COUNT(*) FROM payload)::INT AS payload_count,
        (SELECT COALESCE(SUM(LENGTH(data)), 0) FROM payload)::BIGINT AS payload_total_bytes
    FROM vote;
END;
$$;",

            @"CREATE PROCEDURE database_cleanup()
LANGUAGE plpgsql
AS $$
BEGIN
    TRUNCATE TABLE payload, vote RESTART IDENTITY CASCADE;
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

            @"CREATE TABLE Payload
(
    IDPayload   INTEGER PRIMARY KEY AUTOINCREMENT,
    IDVote      INTEGER NOT NULL REFERENCES Vote(IDVote),
    Data        BLOB    NOT NULL,
    DateCreated TEXT    NOT NULL DEFAULT CURRENT_TIMESTAMP
);",
        };
    }
}
