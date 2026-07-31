-- ============================================================
-- Script for a local SQLite database
-- Table for the voting application
--
-- SQLite has no stored procedure/function support, so unlike the
-- MSSQL/MySQL/PostgreSQL schemas there are no stored routines here -
-- ScaleVoteBenchmark.Api's SqliteRepository runs plain parameterized
-- SQL directly against this table instead.
-- ============================================================

DROP TABLE IF EXISTS Vote;

CREATE TABLE Vote
(
    IDVote      INTEGER PRIMARY KEY AUTOINCREMENT,
    "Option"    TEXT NOT NULL CHECK ("Option" IN ('yes', 'no')),
    DateCreated TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
);
