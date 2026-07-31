-- ============================================================
-- Skripta za Azure Database for MySQL
-- Tablica i pohranjene procedure za voting aplikaciju
-- ============================================================

DROP TABLE IF EXISTS `Vote`;

CREATE TABLE `Vote`
(
    `IDVote`      INT AUTO_INCREMENT PRIMARY KEY,
    `Option`      VARCHAR(10) NOT NULL,
    `DateCreated` DATETIME    NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT `CK_Vote_Option` CHECK (`Option` IN ('yes', 'no'))
);

-- ------------------------------------------------------------
-- Pohranjena procedura za unos glasa
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
-- Pohranjena procedura za dohvaćanje zbrojenih rezultata
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
