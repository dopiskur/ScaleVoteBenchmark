using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ScaleTrigger.Models;

namespace ScaleTrigger.Controllers
{
    [ApiController]
    [Route("api/loadconfig")]
    public class LoadConfigApiController : ControllerBase
    {
        private static readonly string[] KnownSettingNames =
        {
            "CpuIterationsPerVote",
            "MemoryMegabytesPerVote",
            "DiskWriteKilobytesPerVote",
            "NetworkLatencyMillisecondsPerVote",
            "PayloadBytesPerVote",
            "DbHashIterationsPerVote",
            "ConfigRefresh"
        };

        private readonly RepoFactory repoFactory;
        private readonly LoadConfigCache loadConfigCache;

        public LoadConfigApiController(RepoFactory repoFactory, LoadConfigCache loadConfigCache)
        {
            this.repoFactory = repoFactory;
            this.loadConfigCache = loadConfigCache;
        }

        /// <summary>Reads straight from the database, not the cache, so it's never a tick stale.</summary>
        [HttpGet]
        [AllowAnonymous]
        public async Task<ActionResult<List<LoadConfigSetting>>> Get()
        {
            return Ok(await repoFactory.GetRepo().LoadConfigGetAsync());
        }

        /// <summary>Refreshes LoadConfigCache immediately so the update applies to the very next vote.</summary>
        [HttpPost]
        [Authorize(Policy = "OptionalJwt")]
        public async Task<ActionResult> Update([FromBody] List<LoadConfigSetting> settings)
        {
            if (settings == null || settings.Count == 0)
            {
                return BadRequest("At least one setting is required.");
            }

            foreach (var setting in settings)
            {
                if (!KnownSettingNames.Contains(setting.SettingName))
                {
                    return BadRequest($"Unknown setting name: '{setting.SettingName}'.");
                }

                if (setting.Min < 0 || setting.Max < 0)
                {
                    return BadRequest($"'{setting.SettingName}': Min and Max must not be negative.");
                }

                if (setting.Min > setting.Max)
                {
                    return BadRequest($"'{setting.SettingName}': Min must not be greater than Max.");
                }
            }

            await repoFactory.GetRepo().LoadConfigUpdateAsync(settings);
            loadConfigCache.Set(await repoFactory.GetRepo().LoadConfigGetAsync());

            return Ok();
        }
    }
}
