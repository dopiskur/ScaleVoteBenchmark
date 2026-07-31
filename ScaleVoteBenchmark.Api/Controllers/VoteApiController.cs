using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ScaleVoteBenchmark.Lib;
using ScaleVoteBenchmark.Lib.Models;

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
        /// Zaprima glas za opciju "pas" ili "macka". Pristup je anoniman
        /// jer glasovanje mora biti dostupno svim korisnicima landing
        /// stranice. Prije upisa u bazu izvršava se simulirano
        /// procesorsko i memorijsko opterećenje, čiji je intenzitet
        /// definiran u appsettings.json datoteci.
        /// </summary>
        [HttpPost("add")]
        [AllowAnonymous]
        public ActionResult VoteAdd([FromQuery] string option)
        {
            if (option != "pas" && option != "macka")
            {
                return BadRequest("Dozvoljene vrijednosti su isključivo 'pas' ili 'macka'.");
            }

            int cpuIterations = int.Parse(configuration["Load:CpuIterationsPerVote"] ?? "0");
            int memoryMegabytes = int.Parse(configuration["Load:MemoryMegabytesPerVote"] ?? "0");

            LoadSimulator.SimulateCpuLoad(cpuIterations);
            LoadSimulator.SimulateMemoryLoad(memoryMegabytes);

            repoFactory.GetRepo().VoteAdd(option);

            // Poništavanje predmemorije rezultata nakon novog glasa
            repoFactory.GetCache().RemoveItem(ResultsCacheKey);

            return Ok();
        }

        /// <summary>
        /// Vraća trenutne zbrojene rezultate glasovanja. Zaštićeno JWT
        /// autorizacijom kako bi rezultatima u stvarnom vremenu mogao
        /// pristupiti isključivo administrativni korisnik. Rezultat se
        /// kratkotrajno sprema u predmemoriju kako bi se rasteretio
        /// podatkovni sloj u slučaju čestog dohvaćanja (npr. polling
        /// stranice u stvarnom vremenu).
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
