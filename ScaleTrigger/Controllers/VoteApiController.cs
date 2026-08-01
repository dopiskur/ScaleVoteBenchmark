using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ScaleTrigger.Models;

namespace ScaleTrigger.Controllers
{
    [ApiController]
    [Route("api/vote")]
    public class VoteApiController : ControllerBase
    {
        private readonly RepoFactory repoFactory;
        private readonly IConfiguration configuration;
        private readonly LoadConfigCache loadConfigCache;

        private const string ReportCacheKey = "VoteReportCache";

        public VoteApiController(RepoFactory repoFactory, IConfiguration configuration, LoadConfigCache loadConfigCache)
        {
            this.repoFactory = repoFactory;
            this.configuration = configuration;
            this.loadConfigCache = loadConfigCache;
        }

        /// <summary>
        /// Accepts a vote for the "yes" or "no" option. Protected by JWT
        /// authorization (see "Auth:Enabled" in appsettings.json) so the
        /// load-generation script exercises token validation as part of
        /// the benchmark, not just the vote write itself. Before writing
        /// to the database, simulated CPU, memory, disk write and network
        /// latency load is executed in the application itself - each
        /// intensity is picked fresh, at random, from the Min/Max range
        /// currently in LoadConfigCache (a fixed value is just a range
        /// where Min equals Max). PayloadBytesPerVote and
        /// DbHashIterationsPerVote instead simulate load inside the
        /// database: a non-zero PayloadBytesPerVote generates that many
        /// random bytes and inserts them into the Payload table alongside
        /// the vote (blob write throughput); a non-zero
        /// DbHashIterationsPerVote makes VoteAdd itself run that many
        /// chained hash computations before the insert (database CPU
        /// load) - see IRepository.VoteAddAsync.
        /// </summary>
        [HttpPost("add")]
        [Authorize(Policy = "OptionalJwt")]
        public async Task<ActionResult> VoteAdd([FromQuery] string option)
        {
            if (option != "yes" && option != "no")
            {
                return BadRequest("Allowed values are only 'yes' or 'no'.");
            }

            int cpuIterations = RandomizedLoadValue("CpuIterationsPerVote");
            int memoryMegabytes = RandomizedLoadValue("MemoryMegabytesPerVote");
            int diskWriteKilobytes = RandomizedLoadValue("DiskWriteKilobytesPerVote");
            int networkLatencyMilliseconds = RandomizedLoadValue("NetworkLatencyMillisecondsPerVote");
            int payloadBytes = RandomizedLoadValue("PayloadBytesPerVote");
            int dbHashIterations = RandomizedLoadValue("DbHashIterationsPerVote");

            LoadSimulator.SimulateCpuLoad(cpuIterations);
            LoadSimulator.SimulateMemoryLoad(memoryMegabytes);
            LoadSimulator.SimulateDiskLoad(diskWriteKilobytes);
            await LoadSimulator.SimulateNetworkLatencyAsync(networkLatencyMilliseconds);

            byte[]? payload = null;
            if (payloadBytes > 0)
            {
                payload = new byte[payloadBytes];
                Random.Shared.NextBytes(payload);
            }

            await repoFactory.GetRepo().VoteAddAsync(option, payload, dbHashIterations);

            // Invalidate the cached report after a new vote
            repoFactory.GetCache().RemoveItem(ReportCacheKey);

            return Ok();
        }

        /// <summary>
        /// Returns the voting results with percentages, summed and
        /// computed entirely by a stored procedure/function in the
        /// database. Access is anonymous so the public dashboard on the
        /// application's root URL can display it without a login.
        /// </summary>
        [HttpGet("report")]
        [AllowAnonymous]
        public async Task<ActionResult<VoteReport>> VoteReportGet()
        {
            var cached = repoFactory.GetCache().GetItem<VoteReport>(ReportCacheKey);
            if (cached != null)
            {
                return Ok(cached);
            }

            int slidingExpiration = int.Parse(configuration["Cache:SlidingExpirationMinutes"] ?? "5");

            var report = await repoFactory.GetRepo().VoteReportGetAsync();
            repoFactory.GetCache().SetItem(ReportCacheKey, report, slidingExpiration);

            return Ok(report);
        }

        /// <summary>
        /// Fully resets the database: drops Vote, Payload and LoadConfig
        /// (and their stored procedures/functions) via
        /// IRepository.DropSchemaAsync(), which also shrinks the freed
        /// disk space, then recreates the schema from scratch
        /// (EnsureSchemaAsync()) and reseeds LoadConfig from
        /// appsettings.json's "Load" section (LoadConfigEnsureSeededAsync,
        /// via the same LoadConfigDefaults helper Program.cs uses at
        /// startup) - so afterwards the database looks exactly like a
        /// brand new one. Always requires a valid JWT via plain
        /// [Authorize] (the default policy), regardless of "Auth:Enabled"
        /// - this is a destructive admin action, not part of the
        /// load-testing surface that setting controls.
        /// </summary>
        [HttpPost("reset")]
        [Authorize]
        public async Task<ActionResult> Reset()
        {
            var repo = repoFactory.GetRepo();

            await repo.DropSchemaAsync();
            await repo.EnsureSchemaAsync();

            var defaults = LoadConfigDefaults.ReadFrom(configuration);
            await repo.LoadConfigEnsureSeededAsync(defaults);
            loadConfigCache.Set(await repo.LoadConfigGetAsync());

            repoFactory.GetCache().RemoveItem(ReportCacheKey);

            return Ok();
        }

        /// <summary>
        /// Returns a value picked at random (inclusive) from the setting's
        /// current Min/Max range in LoadConfigCache. If Max is not greater
        /// than Min, Min is returned as-is, so a fixed intensity is just a
        /// range where Min equals Max.
        /// </summary>
        private int RandomizedLoadValue(string settingName)
        {
            var (min, max) = loadConfigCache.Get(settingName);

            return max > min ? Random.Shared.Next(min, max + 1) : min;
        }
    }
}
