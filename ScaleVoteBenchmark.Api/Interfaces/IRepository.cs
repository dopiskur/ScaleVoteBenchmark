using ScaleVoteBenchmark.Api.Models;

namespace ScaleVoteBenchmark.Api.Interfaces
{
    /// <summary>
    /// Common interface for accessing the data layer, regardless of
    /// whether it's the MSSQL (Azure SQL) or MySQL (Azure Database for
    /// MySQL) implementation. All functions communicate exclusively
    /// through stored procedures.
    /// </summary>
    public interface IRepository
    {
        Task VoteAddAsync(string option);

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
    }
}
