using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ScaleVoteBenchmark.Api.Models;

namespace ScaleVoteBenchmark.Api.Controllers
{
    [ApiController]
    [Route("api/vote")]
    public class VoteApiController : ControllerBase
    {
        private readonly RepoFactory repoFactory;
        private readonly IConfiguration configuration;

        private const string ReportCacheKey = "VoteReportCache";

        public VoteApiController(RepoFactory repoFactory, IConfiguration configuration)
        {
            this.repoFactory = repoFactory;
            this.configuration = configuration;
        }

        /// <summary>
        /// Accepts a vote for the "yes" or "no" option. Protected by JWT
        /// authorization (see "Auth:Enabled" in appsettings.json) so the
        /// load-generation script exercises token validation as part of
        /// the benchmark, not just the vote write itself. Before writing
        /// to the database, simulated CPU, memory, disk write and network
        /// latency load is executed - each intensity is picked fresh, at
        /// random, from the Min/Max range defined in the appsettings.json
        /// file (a fixed value is just a range where Min equals Max).
        /// </summary>
        [HttpPost("add")]
        [Authorize]
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

            LoadSimulator.SimulateCpuLoad(cpuIterations);
            LoadSimulator.SimulateMemoryLoad(memoryMegabytes);
            LoadSimulator.SimulateDiskLoad(diskWriteKilobytes);
            await LoadSimulator.SimulateNetworkLatencyAsync(networkLatencyMilliseconds);

            repoFactory.GetRepo().VoteAdd(option);

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
        public ActionResult<VoteReport> VoteReportGet()
        {
            var cached = repoFactory.GetCache().GetItem<VoteReport>(ReportCacheKey);
            if (cached != null)
            {
                return Ok(cached);
            }

            int slidingExpiration = int.Parse(configuration["Cache:SlidingExpirationMinutes"] ?? "5");

            var report = repoFactory.GetRepo().VoteReportGet();
            repoFactory.GetCache().SetItem(ReportCacheKey, report, slidingExpiration);

            return Ok(report);
        }

        /// <summary>
        /// Reads "Load:{settingName}:Min" and "Load:{settingName}:Max"
        /// from appsettings.json and returns a value picked at random
        /// (inclusive) from that range. If Max is not greater than Min,
        /// Min is returned as-is, so a fixed intensity is just a range
        /// where Min equals Max.
        /// </summary>
        private int RandomizedLoadValue(string settingName)
        {
            int min = int.Parse(configuration[$"Load:{settingName}:Min"] ?? "0");
            int max = int.Parse(configuration[$"Load:{settingName}:Max"] ?? "0");

            return max > min ? Random.Shared.Next(min, max + 1) : min;
        }
    }
}
