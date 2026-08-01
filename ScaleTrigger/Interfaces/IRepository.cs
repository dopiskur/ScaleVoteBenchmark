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
        /// array) to skip the payload insert entirely.
        /// </summary>
        Task VoteAddAsync(string option, byte[]? payload);

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
        /// Deletes all rows from Vote and Payload, resets their
        /// auto-increment counters, and reclaims the freed disk space at
        /// the storage engine level (TRUNCATE followed by a
        /// provider-specific shrink/VACUUM/OPTIMIZE). Destructive -
        /// wipes all benchmark data irreversibly.
        /// </summary>
        Task CleanupAsync();
    }
}
