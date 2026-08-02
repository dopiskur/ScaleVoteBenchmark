namespace ScaleTrigger.Schema
{
    /// <summary>SQL batches used to bootstrap the schema. Each repository only runs these when its tables don't exist yet.</summary>
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

            @"CREATE PROCEDURE dbo.DbCpuBurn
    @MaxPrime INT
AS
BEGIN
    SET NOCOUNT ON;

    -- Set-based CPU burn: no procedural loop, no touch of Vote/Payload at
    -- all - just a large row set for the query engine itself to churn
    -- through. MAXDOP 1 keeps the cost on a single scheduler, matching a
    -- single request's share of CPU rather than fanning out across cores.
    IF @MaxPrime > 0
    BEGIN
        DECLARE @RowCount BIGINT;
        SELECT @RowCount = COUNT(*)
        FROM (
            SELECT TOP (@MaxPrime) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS n
            FROM sys.all_objects a CROSS JOIN sys.all_objects b
        ) x
        OPTION (MAXDOP 1);
    END
END",

            @"CREATE PROCEDURE dbo.VoteAdd
    @Option    VARCHAR(10),
    @Payload   VARBINARY(MAX) = NULL,
    @MaxPrime  INT            = 0
AS
BEGIN
    SET NOCOUNT ON;

    EXEC dbo.DbCpuBurn @MaxPrime;

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

    -- COUNT_BIG (rather than COUNT/plain int, which caps at ~2.1 billion)
    -- since this is a load-testing tool expected to accumulate a very large
    -- number of rows. WITH (NOLOCK) so this read-only report doesn't wait
    -- behind whatever row/page lock a concurrent VoteAdd insert is holding
    -- under the default READ COMMITTED isolation level - a stale-by-a-row
    -- count is fine here, blocking on every insert isn't.
    SELECT
        COUNT_BIG(*) AS [Total],
        (SELECT COUNT_BIG(*) FROM dbo.Payload WITH (NOLOCK)) AS [PayloadCount],
        (SELECT ISNULL(SUM(DATALENGTH(Data)), 0) FROM dbo.Payload WITH (NOLOCK)) AS [PayloadTotalBytes]
    FROM dbo.Vote WITH (NOLOCK);
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

            @"CREATE PROCEDURE `DbCpuBurn`(
    IN pMaxPrime INT
)
BEGIN
    IF pMaxPrime > 0 THEN
        -- Set-based CPU burn: a recursive CTE generating pMaxPrime rows, no
        -- procedural loop and no touch of Vote/Payload at all. MySQL caps
        -- recursive CTE depth at 1000 by default - raised per-session so a
        -- realistic pMaxPrime doesn't abort with 'Recursive query aborted'.
        SET SESSION cte_max_recursion_depth = 4294967295;

        WITH RECURSIVE seq(n) AS (
            SELECT 1
            UNION ALL
            SELECT n + 1 FROM seq WHERE n < pMaxPrime
        )
        SELECT COUNT(*) FROM seq;
    END IF;
END",

            @"CREATE PROCEDURE `VoteAdd`(
    IN pOption VARCHAR(10),
    IN pPayload LONGBLOB,
    IN pMaxPrime INT
)
BEGIN
    CALL `DbCpuBurn`(pMaxPrime);

    INSERT INTO `Vote` (`Option`, `DateCreated`)
    VALUES (pOption, UTC_TIMESTAMP());

    IF pPayload IS NOT NULL THEN
        INSERT INTO `Payload` (`IDVote`, `Data`, `DateCreated`)
        VALUES (LAST_INSERT_ID(), pPayload, UTC_TIMESTAMP());
    END IF;
END",

            @"CREATE PROCEDURE `VoteReportGet`()
BEGIN
    -- InnoDB's plain SELECT is a non-locking consistent (MVCC) read, so this
    -- never waits behind a concurrent VoteAdd insert regardless of isolation level.
    SELECT
        COUNT(*) AS `Total`,
        (SELECT COUNT(*) FROM `Payload`) AS `PayloadCount`,
        (SELECT IFNULL(SUM(LENGTH(`Data`)), 0) FROM `Payload`) AS `PayloadTotalBytes`
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

            @"CREATE TABLE payload
(
    id_payload   BIGSERIAL PRIMARY KEY,
    id_vote      BIGINT    NOT NULL REFERENCES vote(id_vote),
    data         BYTEA     NOT NULL,
    date_created TIMESTAMP NOT NULL DEFAULT (now() AT TIME ZONE 'utc')
);",

            @"CREATE PROCEDURE db_cpu_burn(p_max_prime INT)
LANGUAGE plpgsql
AS $$
DECLARE
    row_count BIGINT;
BEGIN
    -- Set-based CPU burn: generate_series aggregation, no procedural loop
    -- and no touch of vote/payload at all.
    IF p_max_prime > 0 THEN
        SELECT COUNT(*) INTO row_count FROM generate_series(1, p_max_prime);
    END IF;
END;
$$;",

            @"CREATE PROCEDURE vote_add(p_option VARCHAR(10), p_payload BYTEA DEFAULT NULL, p_max_prime INT DEFAULT 0)
LANGUAGE plpgsql
AS $$
DECLARE
    new_id_vote BIGINT;
BEGIN
    CALL db_cpu_burn(p_max_prime);

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
RETURNS TABLE(total BIGINT, payload_count BIGINT, payload_total_bytes BIGINT)
LANGUAGE plpgsql
AS $$
BEGIN
    -- COUNT(*) is BIGINT natively in Postgres. Plain SELECT is an MVCC
    -- snapshot read here too, so it never waits behind a concurrent
    -- vote_add insert regardless of isolation level.
    RETURN QUERY
    SELECT
        COUNT(*) AS total,
        (SELECT COUNT(*) FROM payload) AS payload_count,
        (SELECT COALESCE(SUM(LENGTH(data)), 0) FROM payload)::BIGINT AS payload_total_bytes
    FROM vote;
END;
$$;",
        };

        // No stored routines - SqliteRepository runs plain SQL directly.
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

        // LoadConfig: live, editable Min/Max ranges, seeded once from
        // appsettings.json's "Load" section (see LoadConfigEnsureSeededAsync).
        public static readonly string[] MsSqlLoadConfig =
        {
            @"CREATE TABLE dbo.LoadConfig
(
    SettingName VARCHAR(50) NOT NULL PRIMARY KEY,
    MinValue    INT         NOT NULL,
    MaxValue    INT         NOT NULL,
    DateUpdated DATETIME2   NOT NULL DEFAULT SYSUTCDATETIME()
);",

            @"CREATE PROCEDURE dbo.LoadConfigGet
AS
BEGIN
    SET NOCOUNT ON;

    SELECT SettingName, MinValue, MaxValue
    FROM dbo.LoadConfig
    ORDER BY SettingName;
END",

            @"CREATE PROCEDURE dbo.LoadConfigSet
    @SettingName VARCHAR(50),
    @MinValue    INT,
    @MaxValue    INT
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.LoadConfig
    SET MinValue = @MinValue, MaxValue = @MaxValue, DateUpdated = SYSUTCDATETIME()
    WHERE SettingName = @SettingName;
END",
        };

        public static readonly string[] MySqlLoadConfig =
        {
            @"CREATE TABLE `LoadConfig`
(
    `SettingName` VARCHAR(50) NOT NULL PRIMARY KEY,
    `MinValue`    INT NOT NULL,
    `MaxValue`    INT NOT NULL,
    `DateUpdated` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP
);",

            @"CREATE PROCEDURE `LoadConfigGet`()
BEGIN
    SELECT `SettingName`, `MinValue`, `MaxValue`
    FROM `LoadConfig`
    ORDER BY `SettingName`;
END",

            @"CREATE PROCEDURE `LoadConfigSet`(
    IN pSettingName VARCHAR(50),
    IN pMinValue INT,
    IN pMaxValue INT
)
BEGIN
    UPDATE `LoadConfig`
    SET `MinValue` = pMinValue, `MaxValue` = pMaxValue
    WHERE `SettingName` = pSettingName;
END",
        };

        public static readonly string[] PostgreSqlLoadConfig =
        {
            @"CREATE TABLE load_config
(
    setting_name VARCHAR(50) PRIMARY KEY,
    min_value    INT       NOT NULL,
    max_value    INT       NOT NULL,
    date_updated TIMESTAMP NOT NULL DEFAULT (now() AT TIME ZONE 'utc')
);",

            @"CREATE FUNCTION load_config_get()
RETURNS TABLE(setting_name VARCHAR(50), min_value INT, max_value INT)
LANGUAGE plpgsql
AS $$
BEGIN
    RETURN QUERY
    SELECT lc.setting_name, lc.min_value, lc.max_value
    FROM load_config lc
    ORDER BY lc.setting_name;
END;
$$;",

            @"CREATE PROCEDURE load_config_set(p_setting_name VARCHAR(50), p_min_value INT, p_max_value INT)
LANGUAGE plpgsql
AS $$
BEGIN
    UPDATE load_config
    SET min_value = p_min_value, max_value = p_max_value, date_updated = now() AT TIME ZONE 'utc'
    WHERE setting_name = p_setting_name;
END;
$$;",
        };

        public static readonly string[] SqliteLoadConfig =
        {
            @"CREATE TABLE LoadConfig
(
    SettingName TEXT    PRIMARY KEY,
    MinValue    INTEGER NOT NULL,
    MaxValue    INTEGER NOT NULL,
    DateUpdated TEXT    NOT NULL DEFAULT CURRENT_TIMESTAMP
);",
        };

        // Used by DropSchemaAsync(); IF EXISTS makes each statement safe
        // to rerun even if a prior reset already removed some of these.
        public static readonly string[] MsSqlDrop =
        {
            @"IF OBJECT_ID('dbo.LoadConfigSet', 'P') IS NOT NULL DROP PROCEDURE dbo.LoadConfigSet;",
            @"IF OBJECT_ID('dbo.LoadConfigGet', 'P') IS NOT NULL DROP PROCEDURE dbo.LoadConfigGet;",
            @"IF OBJECT_ID('dbo.LoadConfig', 'U') IS NOT NULL DROP TABLE dbo.LoadConfig;",
            @"IF OBJECT_ID('dbo.VoteReportGet', 'P') IS NOT NULL DROP PROCEDURE dbo.VoteReportGet;",
            @"IF OBJECT_ID('dbo.VoteAdd', 'P') IS NOT NULL DROP PROCEDURE dbo.VoteAdd;",
            @"IF OBJECT_ID('dbo.DbCpuBurn', 'P') IS NOT NULL DROP PROCEDURE dbo.DbCpuBurn;",
            @"IF OBJECT_ID('dbo.Payload', 'U') IS NOT NULL DROP TABLE dbo.Payload;",
            @"IF OBJECT_ID('dbo.Vote', 'U') IS NOT NULL DROP TABLE dbo.Vote;",
        };

        public static readonly string[] MySqlDrop =
        {
            @"DROP PROCEDURE IF EXISTS `LoadConfigSet`;",
            @"DROP PROCEDURE IF EXISTS `LoadConfigGet`;",
            @"DROP TABLE IF EXISTS `LoadConfig`;",
            @"DROP PROCEDURE IF EXISTS `VoteReportGet`;",
            @"DROP PROCEDURE IF EXISTS `VoteAdd`;",
            @"DROP PROCEDURE IF EXISTS `DbCpuBurn`;",
            @"DROP TABLE IF EXISTS `Payload`;",
            @"DROP TABLE IF EXISTS `Vote`;",
        };

        public static readonly string[] PostgreSqlDrop =
        {
            @"DROP PROCEDURE IF EXISTS load_config_set(VARCHAR, INT, INT);",
            @"DROP FUNCTION IF EXISTS load_config_get();",
            @"DROP TABLE IF EXISTS load_config;",
            @"DROP FUNCTION IF EXISTS vote_report_get();",
            @"DROP PROCEDURE IF EXISTS vote_add(VARCHAR, BYTEA, INT);",
            @"DROP PROCEDURE IF EXISTS db_cpu_burn(INT);",
            @"DROP TABLE IF EXISTS payload;",
            @"DROP TABLE IF EXISTS vote;",
        };

        public static readonly string[] SqliteDrop =
        {
            @"DROP TABLE IF EXISTS LoadConfig;",
            @"DROP TABLE IF EXISTS Payload;",
            @"DROP TABLE IF EXISTS Vote;",
        };
    }
}
