-- ============================================================
-- Script for a local SQLite database
-- Table for the voting application
--
-- SQLite has no stored procedure/function support, so unlike the
-- MSSQL/MySQL/PostgreSQL schemas there are no stored routines here -
-- ScaleTrigger's SqliteRepository runs plain parameterized
-- SQL directly against this table instead.
-- ============================================================

DROP TABLE IF EXISTS Payload;
DROP TABLE IF EXISTS Vote;

CREATE TABLE Vote
(
    IDVote      INTEGER PRIMARY KEY AUTOINCREMENT,
    "Option"    TEXT NOT NULL CHECK ("Option" IN ('yes', 'no')),
    DateCreated TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
);

-- Optional per-vote payload, used to benchmark write throughput with
-- a variable-size blob attached to the vote. Only populated when
-- Load:PayloadBytesPerVote resolves to a value above 0 - see
-- SqliteRepository.VoteAddAsync.
CREATE TABLE Payload
(
    IDPayload   INTEGER PRIMARY KEY AUTOINCREMENT,
    IDVote      INTEGER NOT NULL REFERENCES Vote(IDVote),
    Data        BLOB    NOT NULL,
    DateCreated TEXT    NOT NULL DEFAULT CURRENT_TIMESTAMP
);

-- No cleanup routine here either - SqliteRepository.CleanupAsync()
-- runs DELETE + VACUUM directly (SQLite has no stored procedures).
