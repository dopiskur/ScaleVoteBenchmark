-- ============================================================
-- Script for Azure Database for PostgreSQL
-- Table and stored routines for the voting application
-- ============================================================

DROP TABLE IF EXISTS vote;

CREATE TABLE vote
(
    id_vote      BIGSERIAL PRIMARY KEY,
    option       VARCHAR(10) NOT NULL CHECK (option IN ('yes', 'no')),
    date_created TIMESTAMP   NOT NULL DEFAULT (now() AT TIME ZONE 'utc')
);

-- ------------------------------------------------------------
-- Stored procedure for inserting a vote
-- ------------------------------------------------------------
DROP PROCEDURE IF EXISTS vote_add(VARCHAR);

CREATE PROCEDURE vote_add(p_option VARCHAR(10))
LANGUAGE plpgsql
AS $$
BEGIN
    INSERT INTO vote (option, date_created)
    VALUES (p_option, now() AT TIME ZONE 'utc');
END;
$$;

-- ------------------------------------------------------------
-- Stored function for retrieving the summed results with
-- percentages, used by the public dashboard
-- ------------------------------------------------------------
DROP FUNCTION IF EXISTS vote_report_get();

CREATE FUNCTION vote_report_get()
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
$$;
