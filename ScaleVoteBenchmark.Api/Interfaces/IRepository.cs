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
        void VoteAdd(string option);

        VoteCounts VoteCountsGet();

        /// <summary>
        /// Returns the vote report (counts and percentages per option),
        /// fully summed and computed by a stored procedure/function in
        /// the database - the same pattern as VoteCountsGet, just with
        /// percentage columns added.
        /// </summary>
        VoteReport VoteReportGet();

        /// <summary>
        /// Checks whether a connection to the database can be established
        /// using the currently configured connection string. Does not
        /// execute any query against the tables, only opens and closes
        /// the connection. Intended for checking at application startup,
        /// so that an incorrect configuration (wrong password,
        /// unreachable server, closed firewall on Azure) is reported
        /// immediately, rather than only on the first real user request.
        /// </summary>
        void TestConnection();
    }
}
