-- ============================================================
-- Script for Azure Database for PostgreSQL
-- Table and stored routines for the voting application
-- ============================================================

DROP TABLE IF EXISTS payload;
DROP TABLE IF EXISTS vote;

CREATE TABLE vote
(
    id_vote      BIGSERIAL PRIMARY KEY,
    option       VARCHAR(10) NOT NULL CHECK (option IN ('yes', 'no')),
    date_created TIMESTAMP   NOT NULL DEFAULT (now() AT TIME ZONE 'utc')
);

-- ------------------------------------------------------------
-- Optional per-vote payload, used to benchmark write throughput
-- with a variable-size blob attached to the vote. Only populated
-- when Load:PayloadBytesPerVote resolves to a value above 0 - see
-- vote_add below.
-- ------------------------------------------------------------
CREATE TABLE payload
(
    id_payload   BIGSERIAL PRIMARY KEY,
    id_vote      BIGINT    NOT NULL REFERENCES vote(id_vote),
    data         BYTEA     NOT NULL,
    date_created TIMESTAMP NOT NULL DEFAULT (now() AT TIME ZONE 'utc')
);

-- ------------------------------------------------------------
-- Stored procedure for inserting a vote, with an optional payload
-- blob inserted alongside it in the same call
-- ------------------------------------------------------------
DROP PROCEDURE IF EXISTS vote_add(VARCHAR, BYTEA);

CREATE PROCEDURE vote_add(p_option VARCHAR(10), p_payload BYTEA DEFAULT NULL)
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
$$;

-- ------------------------------------------------------------
-- Stored function for retrieving the summed results with
-- percentages, used by the public dashboard
-- ------------------------------------------------------------
DROP FUNCTION IF EXISTS vote_report_get();

CREATE FUNCTION vote_report_get()
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
$$;

-- ------------------------------------------------------------
-- Stored procedure that wipes all data (Vote and Payload), resets
-- identity sequences, and reclaims the freed disk space. Destructive -
-- exposed via the dashboard's "cleanup" button, always behind login.
-- ------------------------------------------------------------
DROP PROCEDURE IF EXISTS database_cleanup();

CREATE PROCEDURE database_cleanup()
LANGUAGE plpgsql
AS $$
BEGIN
    TRUNCATE TABLE payload, vote RESTART IDENTITY CASCADE;
END;
$$;
