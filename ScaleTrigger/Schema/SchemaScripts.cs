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

            @"CREATE PROCEDURE dbo.VoteAdd
    @Option         VARCHAR(10),
    @Payload        VARBINARY(MAX) = NULL,
    @HashIterations INT            = 0
AS
BEGIN
    SET NOCOUNT ON;

    -- Database-side CPU load (vs. app-side CpuIterationsPerVote). 0 = skip.
    IF @HashIterations > 0
    BEGIN
        DECLARE @HashState VARBINARY(32) = HASHBYTES('SHA2_256', CAST(NEWID() AS VARBINARY(16)));
        DECLARE @i INT = 0;
        WHILE @i < @HashIterations
        BEGIN
            SET @HashState = HASHBYTES('SHA2_256', @HashState);
            SET @i += 1;
        END
    END

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
    IN pPayload LONGBLOB,
    IN pHashIterations INT
)
BEGIN
    DECLARE hashState CHAR(64);
    DECLARE i INT DEFAULT 0;

    -- Database-side CPU load (vs. app-side CpuIterationsPerVote). 0 = skip.
    IF pHashIterations > 0 THEN
        SET hashState = SHA2(UUID(), 256);
        WHILE i < pHashIterations DO
            SET hashState = SHA2(hashState, 256);
            SET i = i + 1;
        END WHILE;
    END IF;

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

            @"CREATE PROCEDURE vote_add(p_option VARCHAR(10), p_payload BYTEA DEFAULT NULL, p_hash_iterations INT DEFAULT 0)
LANGUAGE plpgsql
AS $$
DECLARE
    new_id_vote BIGINT;
    hash_state  BYTEA;
    i           INT;
BEGIN
    -- Database-side CPU load (vs. app-side CpuIterationsPerVote). 0 = skip.
    IF p_hash_iterations > 0 THEN
        hash_state := sha256(convert_to(p_option || clock_timestamp()::TEXT, 'UTF8'));
        FOR i IN 1..p_hash_iterations LOOP
            hash_state := sha256(hash_state);
        END LOOP;
    END IF;

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
            @"IF OBJECT_ID('dbo.DatabaseCleanup', 'P') IS NOT NULL DROP PROCEDURE dbo.DatabaseCleanup;",
            @"IF OBJECT_ID('dbo.VoteReportGet', 'P') IS NOT NULL DROP PROCEDURE dbo.VoteReportGet;",
            @"IF OBJECT_ID('dbo.VoteAdd', 'P') IS NOT NULL DROP PROCEDURE dbo.VoteAdd;",
            @"IF OBJECT_ID('dbo.Payload', 'U') IS NOT NULL DROP TABLE dbo.Payload;",
            @"IF OBJECT_ID('dbo.Vote', 'U') IS NOT NULL DROP TABLE dbo.Vote;",
        };

        public static readonly string[] MySqlDrop =
        {
            @"DROP PROCEDURE IF EXISTS `LoadConfigSet`;",
            @"DROP PROCEDURE IF EXISTS `LoadConfigGet`;",
            @"DROP TABLE IF EXISTS `LoadConfig`;",
            @"DROP PROCEDURE IF EXISTS `DatabaseCleanup`;",
            @"DROP PROCEDURE IF EXISTS `VoteReportGet`;",
            @"DROP PROCEDURE IF EXISTS `VoteAdd`;",
            @"DROP TABLE IF EXISTS `Payload`;",
            @"DROP TABLE IF EXISTS `Vote`;",
        };

        public static readonly string[] PostgreSqlDrop =
        {
            @"DROP PROCEDURE IF EXISTS load_config_set(VARCHAR, INT, INT);",
            @"DROP FUNCTION IF EXISTS load_config_get();",
            @"DROP TABLE IF EXISTS load_config;",
            @"DROP PROCEDURE IF EXISTS database_cleanup();",
            @"DROP FUNCTION IF EXISTS vote_report_get();",
            @"DROP PROCEDURE IF EXISTS vote_add(VARCHAR, BYTEA, INT);",
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
