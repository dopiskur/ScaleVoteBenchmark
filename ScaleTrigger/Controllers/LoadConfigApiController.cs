using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ScaleTrigger.Models;

namespace ScaleTrigger.Controllers
{
    [ApiController]
    [Route("api/loadconfig")]
    public class LoadConfigApiController : ControllerBase
    {
        /// <summary>
        /// The only SettingName values LoadConfig is seeded with (see
        /// Program.cs) - anything else in an update request is rejected.
        /// </summary>
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

        /// <summary>
        /// Returns the current LoadConfig rows straight from the
        /// database (not the cache), so the dashboard always shows what
        /// is actually persisted, even a moment before
        /// LoadConfigRefreshService's next tick. Anonymous, same as
        /// GET /api/vote/report - these are benchmark-tuning values, not
        /// sensitive data.
        /// </summary>
        [HttpGet]
        [AllowAnonymous]
        public async Task<ActionResult<List<LoadConfigSetting>>> Get()
        {
            return Ok(await repoFactory.GetRepo().LoadConfigGetAsync());
        }

        /// <summary>
        /// Updates one or more LoadConfig rows by SettingName. Requires a
        /// JWT only when "Auth:Enabled" is true (same "OptionalJwt" policy
        /// as POST /api/vote/add), so the dashboard's "Save changes"
        /// button needs no login while auth is off - unlike
        /// POST /api/vote/cleanup, this only tunes benchmark intensity,
        /// it doesn't destroy data. Refreshes LoadConfigCache immediately
        /// after a successful update, so the new values take effect on
        /// the very next vote rather than waiting for
        /// LoadConfigRefreshService's next tick.
        /// </summary>
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
