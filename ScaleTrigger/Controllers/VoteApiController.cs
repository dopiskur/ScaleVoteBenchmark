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

        // Prevents a cache stampede: concurrent requests that miss at the same time
        // piggy-back on one fetch instead of each hitting the database.
        private static readonly SemaphoreSlim ReportFetchLock = new(1, 1);

        public VoteApiController(RepoFactory repoFactory, IConfiguration configuration, LoadConfigCache loadConfigCache)
        {
            this.repoFactory = repoFactory;
            this.configuration = configuration;
            this.loadConfigCache = loadConfigCache;
        }

        /// <summary>
        /// CPU/memory/disk/network load runs here, in the app; PayloadBytesPerVote and
        /// DbCpuIterationsPerVote run inside the database instead (see IRepository.VoteAddAsync).
        /// dbCpuBurnOnly=true isolates the database-side CPU cost without inserting a row.
        /// LoadEnabled=false short-circuits to a no-op before any of that, so the dashboard's
        /// "Discard backlog" can drain already-queued calls near-instantly instead of each
        /// running its full configured cost.
        /// </summary>
        [HttpPost("add")]
        [Authorize(Policy = "OptionalJwt")]
        public async Task<ActionResult> VoteAdd([FromQuery] string option, [FromQuery] bool dbCpuBurnOnly = false)
        {
            if (option != "yes" && option != "no")
            {
                return BadRequest("Allowed values are only 'yes' or 'no'.");
            }

            if (loadConfigCache.Get("LoadEnabled").Min <= 0)
            {
                return Ok();
            }

            int cpuIterations = RandomizedLoadValue("CpuIterationsPerVote");
            int memoryKilobytes = RandomizedLoadValue("MemoryKilobytesPerVote");
            int diskWriteKilobytes = RandomizedLoadValue("DiskWriteKilobytesPerVote");
            int networkLatencyMilliseconds = RandomizedLoadValue("NetworkLatencyMillisecondsPerVote");
            int dbHashIterations = RandomizedLoadValue("DbCpuIterationsPerVote");

            // Offloaded so this CPU/disk-bound work doesn't starve the thread pool
            // under concurrent load - same reasoning as NodeBenchmarkApiController.Run.
            await Task.Run(async () =>
            {
                LoadSimulator.SimulateCpuLoad(cpuIterations);
                LoadSimulator.SimulateMemoryLoad(memoryKilobytes);
                await LoadSimulator.SimulateDiskLoad(diskWriteKilobytes);
            });
            await LoadSimulator.SimulateNetworkLatencyAsync(networkLatencyMilliseconds);

            if (dbCpuBurnOnly)
            {
                await repoFactory.GetRepo().DbCpuBurnAsync(dbHashIterations);
                return Ok();
            }

            int payloadBytes = RandomizedLoadValue("PayloadBytesPerVote");
            byte[]? payload = null;
            if (payloadBytes > 0)
            {
                payload = new byte[payloadBytes];
                Random.Shared.NextBytes(payload);
            }

            await repoFactory.GetRepo().VoteAddAsync(option, payload, dbHashIterations);
            repoFactory.GetCache().RemoveItem(ReportCacheKey);

            return Ok();
        }

        [HttpGet("report")]
        [AllowAnonymous]
        public async Task<ActionResult<VoteReport>> VoteReportGet()
        {
            var cached = repoFactory.GetCache().GetItem<VoteReport>(ReportCacheKey);
            if (cached != null)
            {
                return Ok(cached);
            }

            await ReportFetchLock.WaitAsync();
            try
            {
                // Re-check: another request may have already refilled the cache while this one was waiting.
                cached = repoFactory.GetCache().GetItem<VoteReport>(ReportCacheKey);
                if (cached != null)
                {
                    return Ok(cached);
                }

                int slidingExpiration = int.Parse(configuration["Cache:SlidingExpirationMinutes"] ?? "5");

                VoteReport report;
                try
                {
                    report = await repoFactory.GetRepo().VoteReportGetAsync();
                }
                catch (Exception)
                {
                    return StatusCode(StatusCodes.Status503ServiceUnavailable, "Database unavailable.");
                }

                repoFactory.GetCache().SetItem(ReportCacheKey, report, slidingExpiration);

                return Ok(report);
            }
            finally
            {
                ReportFetchLock.Release();
            }
        }

        /// <summary>Drops and recreates the schema, so the database ends up looking brand new.</summary>
        [HttpPost("reset")]
        [Authorize(Policy = "OptionalJwt")]
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

        private int RandomizedLoadValue(string settingName)
        {
            var (min, max) = loadConfigCache.Get(settingName);

            return max > min ? Random.Shared.Next(min, max + 1) : min;
        }
    }
}
