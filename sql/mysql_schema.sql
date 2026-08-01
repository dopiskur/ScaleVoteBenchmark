-- ============================================================
-- Script for Azure Database for MySQL
-- Table and stored procedures for the voting application
-- ============================================================

DROP TABLE IF EXISTS `Payload`;
DROP TABLE IF EXISTS `Vote`;

CREATE TABLE `Vote`
(
    `IDVote`      BIGINT AUTO_INCREMENT PRIMARY KEY,
    `Option`      VARCHAR(10) NOT NULL,
    `DateCreated` DATETIME    NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT `CK_Vote_Option` CHECK (`Option` IN ('yes', 'no'))
);

-- ------------------------------------------------------------
-- Optional per-vote payload, used to benchmark write throughput
-- with a variable-size blob attached to the vote. Only populated
-- when Load:PayloadBytesPerVote resolves to a value above 0 - see
-- VoteAdd below.
-- ------------------------------------------------------------
CREATE TABLE `Payload`
(
    `IDPayload`   BIGINT AUTO_INCREMENT PRIMARY KEY,
    `IDVote`      BIGINT   NOT NULL,
    `Data`        LONGBLOB NOT NULL,
    `DateCreated` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT `FK_Payload_Vote` FOREIGN KEY (`IDVote`) REFERENCES `Vote`(`IDVote`)
);

-- ------------------------------------------------------------
-- Stored procedure for inserting a vote, with an optional payload
-- blob inserted alongside it in the same call
-- ------------------------------------------------------------
DROP PROCEDURE IF EXISTS `VoteAdd`;

DELIMITER $$

CREATE PROCEDURE `VoteAdd`(
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
END $$

DELIMITER ;

-- ------------------------------------------------------------
-- Stored procedure for retrieving the summed results with
-- percentages, used by the public dashboard
-- ------------------------------------------------------------
DROP PROCEDURE IF EXISTS `VoteReportGet`;

DELIMITER $$

CREATE PROCEDURE `VoteReportGet`()
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
END $$

DELIMITER ;

-- ------------------------------------------------------------
-- Stored procedure that wipes all data (Vote and Payload) and
-- reclaims the freed disk space. Destructive - exposed via the
-- dashboard's "cleanup" button, always behind login.
-- ------------------------------------------------------------
DROP PROCEDURE IF EXISTS `DatabaseCleanup`;

DELIMITER $$

CREATE PROCEDURE `DatabaseCleanup`()
BEGIN
    SET FOREIGN_KEY_CHECKS = 0;
    TRUNCATE TABLE `Payload`;
    TRUNCATE TABLE `Vote`;
    SET FOREIGN_KEY_CHECKS = 1;
END $$

DELIMITER ;
