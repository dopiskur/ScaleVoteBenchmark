using ScaleTrigger.Models;

namespace ScaleTrigger.Interfaces
{
    public interface IRepository
    {
        /// <summary>Pass null, not an empty array, to skip the payload insert. hashIterations &gt; 0 runs a chained SHA-512 burn inside the database before the insert.</summary>
        Task VoteAddAsync(string option, byte[]? payload, int hashIterations);

        Task<VoteReport> VoteReportGetAsync();

        /// <summary>Same CPU burn as VoteAdd, without touching Vote/Payload. 0 is a no-op.</summary>
        Task DbCpuBurnAsync(int hashIterations);

        /// <summary>Opens and closes a connection without querying, so a bad config surfaces at startup, not on the first vote.</summary>
        Task TestConnectionAsync();

        /// <summary>No-op if the schema already exists.</summary>
        Task EnsureSchemaAsync();

        /// <summary>
        /// Drops Vote, Payload and LoadConfig and shrinks the freed space.
        /// Best-effort force-disconnects other connections first so this
        /// can't hang behind an open transaction. Callers must follow up
        /// with EnsureSchemaAsync() and LoadConfigEnsureSeededAsync() to
        /// get a working schema back.
        /// </summary>
        Task DropSchemaAsync();

        /// <summary>Creates LoadConfig if missing and seeds <paramref name="defaults"/> only if the table is still empty, so dashboard edits survive a later startup.</summary>
        Task LoadConfigEnsureSeededAsync(IEnumerable<LoadConfigSetting> defaults);

        Task<List<LoadConfigSetting>> LoadConfigGetAsync();

        /// <summary>Settings not already in the table are silently ignored.</summary>
        Task LoadConfigUpdateAsync(IEnumerable<LoadConfigSetting> settings);
    }
}
