-- ============================================================
-- Script for Azure Database for PostgreSQL
-- Table and stored routines for the voting application
-- ============================================================

DROP TABLE IF EXISTS vote;

CREATE TABLE vote
(
    id_vote      SERIAL PRIMARY KEY,
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
-- Stored function for retrieving the summed results
-- ------------------------------------------------------------
DROP FUNCTION IF EXISTS vote_counts_get();

CREATE FUNCTION vote_counts_get()
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
$$;
