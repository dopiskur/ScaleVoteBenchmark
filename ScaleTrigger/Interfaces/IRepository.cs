using ScaleTrigger.Models;

namespace ScaleTrigger.Interfaces
{
    /// <summary>
    /// Common interface for accessing the data layer, regardless of
    /// whether it's the MSSQL (Azure SQL) or MySQL (Azure Database for
    /// MySQL) implementation. All functions communicate exclusively
    /// through stored procedures.
    /// </summary>
    public interface IRepository
    {
        /// <summary>
        /// Records a vote. If payload is not null, it is also inserted
        /// into the Payload table (a BLOB column) linked to the new vote,
        /// used to benchmark write throughput with a variable-size blob
        /// attached to the vote. Callers should pass null (not an empty
        /// array) to skip the payload insert entirely. hashIterations, if
        /// greater than 0, runs that many chained hash computations
        /// inside the database engine itself before the insert, to
        /// simulate CPU load on the database (as opposed to
        /// LoadSimulator.SimulateCpuLoad, which simulates it in the
        /// application) - 0 skips this entirely and just inserts.
        /// </summary>
        Task VoteAddAsync(string option, byte[]? payload, int hashIterations);

        /// <summary>
        /// Returns the vote report (counts and percentages per option),
        /// fully summed and computed by a stored procedure/function in
        /// the database.
        /// </summary>
        Task<VoteReport> VoteReportGetAsync();

        /// <summary>
        /// Checks whether a connection to the database can be established
        /// using the currently configured connection string. Does not
        /// execute any query against the tables, only opens and closes
        /// the connection. Intended for checking at application startup,
        /// so that an incorrect configuration (wrong password,
        /// unreachable server, closed firewall on Azure) is reported
        /// immediately, rather than only on the first real user request.
        /// </summary>
        Task TestConnectionAsync();

        /// <summary>
        /// Creates the Vote table and its stored procedures/functions if
        /// they do not already exist, using the same connection details
        /// configured in appsettings.json. Does nothing if the schema is
        /// already present, so existing data is never touched.
        /// </summary>
        Task EnsureSchemaAsync();

        /// <summary>
        /// Drops Vote, Payload and LoadConfig (and, for MSSQL/MySQL/
        /// PostgreSQL, their stored procedures/functions) entirely, then
        /// shrinks the freed space at the storage engine level
        /// (provider-specific: MSSQL shrinks the database and its log
        /// file with DBCC SHRINKDATABASE; SQLite truncates the WAL file
        /// and VACUUMs the main file; MySQL/PostgreSQL reclaim space
        /// automatically as part of DROP TABLE, since both use
        /// file-per-table storage by default). Destructive and
        /// irreversible - wipes all benchmark data and every LoadConfig
        /// edit. Callers are expected to follow this with
        /// EnsureSchemaAsync() and LoadConfigEnsureSeededAsync() to
        /// recreate a fresh schema (see VoteApiController.Reset()).
        /// </summary>
        Task DropSchemaAsync();

        /// <summary>
        /// Creates the LoadConfig table (and, for MSSQL/MySQL/PostgreSQL,
        /// its stored procedures/function) if it does not already exist,
        /// then - only if the table is still empty - inserts one row per
        /// setting in <paramref name="defaults"/>. Does nothing beyond
        /// that on subsequent calls, so values already edited via the
        /// dashboard are never overwritten by appsettings.json again.
        /// </summary>
        Task LoadConfigEnsureSeededAsync(IEnumerable<LoadConfigSetting> defaults);

        /// <summary>
        /// Returns all rows currently in LoadConfig.
        /// </summary>
        Task<List<LoadConfigSetting>> LoadConfigGetAsync();

        /// <summary>
        /// Updates the Min/Max of each given setting by SettingName.
        /// Settings not already present in the table (i.e. not one of
        /// the names LoadConfigEnsureSeededAsync originally inserted)
        /// are silently ignored, since the update statement/procedure
        /// matches by SettingName.
        /// </summary>
        Task LoadConfigUpdateAsync(IEnumerable<LoadConfigSetting> settings);
    }
}
