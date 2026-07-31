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
        /// to the database, simulated CPU, memory and disk write load is
        /// executed, whose intensity is defined in the appsettings.json
        /// file.
        /// </summary>
        [HttpPost("add")]
        [Authorize]
        public ActionResult VoteAdd([FromQuery] string option)
        {
            if (option != "yes" && option != "no")
            {
                return BadRequest("Allowed values are only 'yes' or 'no'.");
            }

            int cpuIterations = int.Parse(configuration["Load:CpuIterationsPerVote"] ?? "0");
            int memoryMegabytes = int.Parse(configuration["Load:MemoryMegabytesPerVote"] ?? "0");
            int diskWriteKilobytes = int.Parse(configuration["Load:DiskWriteKilobytesPerVote"] ?? "0");

            LoadSimulator.SimulateCpuLoad(cpuIterations);
            LoadSimulator.SimulateMemoryLoad(memoryMegabytes);
            LoadSimulator.SimulateDiskLoad(diskWriteKilobytes);

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
    }
}
