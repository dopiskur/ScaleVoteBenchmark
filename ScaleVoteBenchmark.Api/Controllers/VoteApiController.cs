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

        private const string ResultsCacheKey = "VoteCountsCache";

        public VoteApiController(RepoFactory repoFactory, IConfiguration configuration)
        {
            this.repoFactory = repoFactory;
            this.configuration = configuration;
        }

        /// <summary>
        /// Accepts a vote for the "yes" or "no" option. Access is anonymous
        /// so the load-generation script can call the endpoint directly,
        /// without authentication. Before writing to the database, a
        /// simulated CPU and memory load is executed, whose intensity is
        /// defined in the appsettings.json file.
        /// </summary>
        [HttpPost("add")]
        [AllowAnonymous]
        public ActionResult VoteAdd([FromQuery] string option)
        {
            if (option != "yes" && option != "no")
            {
                return BadRequest("Allowed values are only 'yes' or 'no'.");
            }

            int cpuIterations = int.Parse(configuration["Load:CpuIterationsPerVote"] ?? "0");
            int memoryMegabytes = int.Parse(configuration["Load:MemoryMegabytesPerVote"] ?? "0");

            LoadSimulator.SimulateCpuLoad(cpuIterations);
            LoadSimulator.SimulateMemoryLoad(memoryMegabytes);

            repoFactory.GetRepo().VoteAdd(option);

            // Invalidate the cached results after a new vote
            repoFactory.GetCache().RemoveItem(ResultsCacheKey);

            return Ok();
        }

        /// <summary>
        /// Returns the current summed voting results. Protected by JWT
        /// authorization so only an administrative user can access
        /// real-time results. The result is cached briefly to relieve the
        /// data layer in case of frequent retrieval (e.g. real-time page
        /// polling).
        /// </summary>
        [HttpGet("counts")]
        [Authorize]
        public ActionResult<VoteCounts> VoteCountsGet()
        {
            var cached = repoFactory.GetCache().GetItem<VoteCounts>(ResultsCacheKey);
            if (cached != null)
            {
                return Ok(cached);
            }

            int slidingExpiration = int.Parse(configuration["Cache:SlidingExpirationMinutes"] ?? "5");

            var counts = repoFactory.GetRepo().VoteCountsGet();
            repoFactory.GetCache().SetItem(ResultsCacheKey, counts, slidingExpiration);

            return Ok(counts);
        }
    }
}
