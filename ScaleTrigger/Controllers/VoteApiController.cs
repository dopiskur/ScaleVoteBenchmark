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
        /// CPU/memory/disk/network load runs here, in the application.
        /// PayloadBytesPerVote and DbCpuIterationsPerVote instead
        /// simulate load inside the database itself - see
        /// IRepository.VoteAddAsync.
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
            int dbMaxPrime = RandomizedLoadValue("DbCpuIterationsPerVote");

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

            await repoFactory.GetRepo().VoteAddAsync(option, payload, dbMaxPrime);
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

            int slidingExpiration = int.Parse(configuration["Cache:SlidingExpirationMinutes"] ?? "5");

            var report = await repoFactory.GetRepo().VoteReportGetAsync();
            repoFactory.GetCache().SetItem(ReportCacheKey, report, slidingExpiration);

            return Ok(report);
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
