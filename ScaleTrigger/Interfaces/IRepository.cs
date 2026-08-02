using ScaleTrigger.Models;

namespace ScaleTrigger.Interfaces
{
    public interface IRepository
    {
        /// <summary>
        /// Pass null, not an empty array, to skip the payload insert.
        /// maxPrime &gt; 0 runs sysbench's CPU algorithm (counts primes up
        /// to maxPrime by trial division) inside the database engine
        /// itself before the insert (0 skips it) - unlike
        /// LoadSimulator.SimulateCpuLoad, which runs the same algorithm
        /// in the app.
        /// </summary>
        Task VoteAddAsync(string option, byte[]? payload, int maxPrime);

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
        /// First makes a best-effort attempt to force out every other
        /// connection/transaction against the database (rolling them back
        /// immediately rather than waiting on whatever lock they hold),
        /// so a reset can't hang behind someone else's open transaction -
        /// silently skipped if the configured account lacks the
        /// permission to do so, in which case the drop proceeds normally
        /// and may itself block on an existing lock.
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
