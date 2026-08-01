using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ScaleTrigger.Models;

namespace ScaleTrigger.Controllers
{
    [ApiController]
    [Route("api/nodebenchmark")]
    public class NodeBenchmarkApiController : ControllerBase
    {
        private readonly IConfiguration configuration;

        public NodeBenchmarkApiController(IConfiguration configuration)
        {
            this.configuration = configuration;
        }

        [HttpGet("hardware")]
        [AllowAnonymous]
        public async Task<ActionResult<NodeHardwareInfo>> GetHardware()
        {
            return Ok(await NodeBenchmark.GetHardwareInfoAsync());
        }

        /// <summary>
        /// Runs CPU, then memory, then disk, in that order, and blocks
        /// until all three finish (~NodeBenchmark:CpuDurationSeconds
        /// alone, 20s by default) - offloaded to a background thread so
        /// the CPU-bound work doesn't run directly on an async
        /// continuation. Requires a JWT only when "Auth:Enabled" is true,
        /// same as the other admin actions.
        /// </summary>
        [HttpPost("run")]
        [Authorize(Policy = "OptionalJwt")]
        public async Task<ActionResult<NodeBenchmarkResult>> Run()
        {
            int cpuDurationSeconds = int.Parse(configuration["NodeBenchmark:CpuDurationSeconds"] ?? "20");
            int memoryBlockMegabytes = int.Parse(configuration["NodeBenchmark:MemoryBlockMegabytes"] ?? "64");
            int memoryRepetitions = int.Parse(configuration["NodeBenchmark:MemoryRepetitions"] ?? "5");
            int diskSizeMegabytes = int.Parse(configuration["NodeBenchmark:DiskSizeMegabytes"] ?? "20");
            int diskRepetitions = int.Parse(configuration["NodeBenchmark:DiskRepetitions"] ?? "5");

            var hardware = await NodeBenchmark.GetHardwareInfoAsync();

            double cpuScore = 0, memoryScore = 0, diskScore = 0;
            await Task.Run(() =>
            {
                cpuScore = NodeBenchmark.RunCpuBenchmark(cpuDurationSeconds);
                memoryScore = NodeBenchmark.RunMemoryBenchmark(memoryBlockMegabytes, memoryRepetitions);
                diskScore = NodeBenchmark.RunDiskBenchmark(diskSizeMegabytes, diskRepetitions);
            });

            return Ok(new NodeBenchmarkResult
            {
                Hardware = hardware,
                CpuNumbersPerSecond = cpuScore,
                MemoryMbPerSecond = memoryScore,
                DiskMbPerSecond = diskScore
            });
        }
    }
}
