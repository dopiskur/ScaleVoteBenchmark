using ScaleTrigger.Models;

namespace ScaleTrigger.Interfaces
{
    public interface IRepository
    {
        /// <summary>
        /// Pass null, not an empty array, to skip the payload insert.
        /// hashIterations &gt; 0 runs that many chained hashes inside the
        /// database engine itself before the insert (0 skips it) -
        /// unlike LoadSimulator.SimulateCpuLoad, which runs in the app.
        /// </summary>
        Task VoteAddAsync(string option, byte[]? payload, int hashIterations);

        Task<VoteReport> VoteReportGetAsync();

        /// <summary>
        /// Opens and closes a connection without querying, so a bad
        /// config surfaces at startup instead of on the first vote.
        /// </summary>
        Task TestConnectionAsync();

        /// <summary>No-op if the schema already exists.</summary>
        Task EnsureSchemaAsync();

        /// <summary>
        /// Drops Vote, Payload and LoadConfig and shrinks the freed
        /// space. Destructive and irreversible - callers must follow up
        /// with EnsureSchemaAsync() and LoadConfigEnsureSeededAsync() to
        /// get a working schema back (see VoteApiController.Reset()).
        /// </summary>
        Task DropSchemaAsync();

        /// <summary>
        /// Creates LoadConfig if missing and seeds <paramref name="defaults"/>
        /// only if the table is still empty, so dashboard edits are never
        /// overwritten by appsettings.json on a later startup.
        /// </summary>
        Task LoadConfigEnsureSeededAsync(IEnumerable<LoadConfigSetting> defaults);

        Task<List<LoadConfigSetting>> LoadConfigGetAsync();

        /// <summary>Settings not already in the table are silently ignored.</summary>
        Task LoadConfigUpdateAsync(IEnumerable<LoadConfigSetting> settings);
    }
}
