using Microsoft.Data.SqlClient;
using ScaleVoteBenchmark.Api.Azure;
using ScaleVoteBenchmark.Api.Interfaces;
using ScaleVoteBenchmark.Api.Models;
using ScaleVoteBenchmark.Api.Schema;

namespace ScaleVoteBenchmark.Api.Repositories
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

        private SqlConnection CreateConnection()
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
                connection.AccessToken = AzureSqlAuthProvider.GetAccessToken();
            }

            return connection;
        }

        public void VoteAdd(string option)
        {
            using var connection = CreateConnection();
            connection.Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "VoteAdd";
            cmd.CommandType = System.Data.CommandType.StoredProcedure;
            cmd.Parameters.AddWithValue("@Option", option);
            cmd.ExecuteNonQuery();
        }

        public VoteReport VoteReportGet()
        {
            using var connection = CreateConnection();
            connection.Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "VoteReportGet";
            cmd.CommandType = System.Data.CommandType.StoredProcedure;

            using var dr = cmd.ExecuteReader();
            var report = new VoteReport();

            if (dr.Read())
            {
                report.Yes = dr["Yes"] != DBNull.Value ? Convert.ToInt32(dr["Yes"]) : 0;
                report.No = dr["No"] != DBNull.Value ? Convert.ToInt32(dr["No"]) : 0;
                report.Total = dr["Total"] != DBNull.Value ? Convert.ToInt32(dr["Total"]) : 0;
                report.YesPercent = dr["YesPercent"] != DBNull.Value ? Convert.ToDecimal(dr["YesPercent"]) : 0;
                report.NoPercent = dr["NoPercent"] != DBNull.Value ? Convert.ToDecimal(dr["NoPercent"]) : 0;
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
        public void TestConnection()
        {
            using var connection = CreateConnection();
            connection.Open();
        }

        public void EnsureSchema()
        {
            using var connection = CreateConnection();
            connection.Open();

            using (var checkCmd = connection.CreateCommand())
            {
                checkCmd.CommandText = "SELECT OBJECT_ID('dbo.Vote', 'U')";
                if (checkCmd.ExecuteScalar() is not DBNull and not null)
                {
                    return;
                }
            }

            foreach (var batch in SchemaScripts.MsSql)
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = batch;
                cmd.ExecuteNonQuery();
            }
        }
    }
}
