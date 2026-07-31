-- ============================================================
-- Script for Azure Database for MySQL
-- Table and stored procedures for the voting application
-- ============================================================

DROP TABLE IF EXISTS `Vote`;

CREATE TABLE `Vote`
(
    `IDVote`      BIGINT AUTO_INCREMENT PRIMARY KEY,
    `Option`      VARCHAR(10) NOT NULL,
    `DateCreated` DATETIME    NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT `CK_Vote_Option` CHECK (`Option` IN ('yes', 'no'))
);

-- ------------------------------------------------------------
-- Stored procedure for inserting a vote
-- ------------------------------------------------------------
DROP PROCEDURE IF EXISTS `VoteAdd`;

DELIMITER $$

CREATE PROCEDURE `VoteAdd`(
    IN pOption VARCHAR(10)
)
BEGIN
    INSERT INTO `Vote` (`Option`, `DateCreated`)
    VALUES (pOption, UTC_TIMESTAMP());
END $$

DELIMITER ;

-- ------------------------------------------------------------
-- Stored procedure for retrieving the summed results
-- ------------------------------------------------------------
DROP PROCEDURE IF EXISTS `VoteCountsGet`;

DELIMITER $$

CREATE PROCEDURE `VoteCountsGet`()
BEGIN
    SELECT
        SUM(CASE WHEN `Option` = 'yes' THEN 1 ELSE 0 END) AS `Yes`,
        SUM(CASE WHEN `Option` = 'no'  THEN 1 ELSE 0 END) AS `No`
    FROM `Vote`;
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
        IF(COUNT(*) = 0, 0, ROUND(100.0 * SUM(CASE WHEN `Option` = 'no' THEN 1 ELSE 0 END) / COUNT(*), 2)) AS `NoPercent`
    FROM `Vote`;
END $$

DELIMITER ;
