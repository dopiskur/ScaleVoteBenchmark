using System.Data;
using Microsoft.Data.SqlClient;
using ScaleTrigger.Azure;
using ScaleTrigger.Interfaces;
using ScaleTrigger.Models;
using ScaleTrigger.Schema;

namespace ScaleTrigger.Repositories
{
    /// <summary>
    /// Repository implementation for working with a Microsoft SQL Server
    /// / Azure SQL database. All functions communicate exclusively
    /// through stored procedures, without direct SELECT, INSERT, UPDATE
    /// or DELETE queries.
    /// </summary>
    public class MsSqlRepository : IRepository
    {
        private readonly string connectionString;
        private readonly bool useManagedIdentity;

        public MsSqlRepository(string connectionString, bool useManagedIdentity = false)
        {
            this.connectionString = connectionString;
            this.useManagedIdentity = useManagedIdentity;
        }

        private async Task<SqlConnection> CreateConnectionAsync()
        {
            var connection = new SqlConnection(connectionString);

            // If Managed Identity mode is enabled, the password is not
            // used from the connection string; instead, a temporary
            // access token is obtained via the application's Azure AD
            // identity.
            //
            // SECURITY NOTE: when useManagedIdentity=false (default), the
            // application connects the classic way, using the username
            // and password contained in the connection string
            // (appsettings.json). This mode is intentionally kept for
            // simplicity of local development and scenarios outside
            // Azure, but it is less secure because the database password
            // remains in the configuration file in plain text. For a
            // production environment in Azure, Managed Identity mode is
            // recommended.
            if (useManagedIdentity)
            {
                connection.AccessToken = await AzureSqlAuthProvider.GetAccessTokenAsync();
            }

            await connection.OpenAsync();
            return connection;
        }

        public async Task VoteAddAsync(string option, byte[]? payload)
        {
            using var connection = await CreateConnectionAsync();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "VoteAdd";
            cmd.CommandType = System.Data.CommandType.StoredProcedure;
            cmd.Parameters.AddWithValue("@Option", option);

            // AddWithValue can't infer a SQL type from a bare
            // DBNull.Value, so the parameter type is set explicitly.
            cmd.Parameters.Add(new SqlParameter("@Payload", SqlDbType.VarBinary, -1)
            {
                Value = (object?)payload ?? DBNull.Value
            });

            await cmd.ExecuteNonQueryAsync();
        }

        public async Task<VoteReport> VoteReportGetAsync()
        {
            using var connection = await CreateConnectionAsync();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "VoteReportGet";
            cmd.CommandType = System.Data.CommandType.StoredProcedure;

            using var dr = await cmd.ExecuteReaderAsync();
            var report = new VoteReport();

            if (await dr.ReadAsync())
            {
                report.Yes = dr["Yes"] != DBNull.Value ? Convert.ToInt32(dr["Yes"]) : 0;
                report.No = dr["No"] != DBNull.Value ? Convert.ToInt32(dr["No"]) : 0;
                report.Total = dr["Total"] != DBNull.Value ? Convert.ToInt32(dr["Total"]) : 0;
                report.YesPercent = dr["YesPercent"] != DBNull.Value ? Convert.ToDecimal(dr["YesPercent"]) : 0;
                report.NoPercent = dr["NoPercent"] != DBNull.Value ? Convert.ToDecimal(dr["NoPercent"]) : 0;
                report.PayloadCount = dr["PayloadCount"] != DBNull.Value ? Convert.ToInt32(dr["PayloadCount"]) : 0;
                report.PayloadTotalBytes = dr["PayloadTotalBytes"] != DBNull.Value ? Convert.ToInt64(dr["PayloadTotalBytes"]) : 0;
            }

            return report;
        }

        /// <summary>
        /// Opens and immediately closes a connection to the Azure SQL
        /// database, without executing any query. The exception is
        /// intentionally not caught here; it is passed to the caller so
        /// the application's startup logic can print a clear error
        /// message.
        /// </summary>
        public async Task TestConnectionAsync()
        {
            using var connection = await CreateConnectionAsync();
        }

        public async Task EnsureSchemaAsync()
        {
            using var connection = await CreateConnectionAsync();

            using (var checkCmd = connection.CreateCommand())
            {
                checkCmd.CommandText = "SELECT OBJECT_ID('dbo.Vote', 'U')";
                if (await checkCmd.ExecuteScalarAsync() is not DBNull and not null)
                {
                    return;
                }
            }

            foreach (var batch in SchemaScripts.MsSql)
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = batch;
                await cmd.ExecuteNonQueryAsync();
            }
        }

        public async Task CleanupAsync()
        {
            using var connection = await CreateConnectionAsync();

            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = "DatabaseCleanup";
                cmd.CommandType = System.Data.CommandType.StoredProcedure;
                await cmd.ExecuteNonQueryAsync();
            }

            // Best-effort: reclaim the space TRUNCATE freed. Not fatal if
            // this fails (e.g. insufficient permission on a shared/managed
            // instance) - the destructive part already succeeded above.
            try
            {
                using var shrinkCmd = connection.CreateCommand();
                shrinkCmd.CommandText = "DBCC SHRINKDATABASE (0);";
                await shrinkCmd.ExecuteNonQueryAsync();
            }
            catch
            {
                // Ignored - shrinking is a nice-to-have, not required for
                // the cleanup itself to be considered successful.
            }
        }
    }
}
