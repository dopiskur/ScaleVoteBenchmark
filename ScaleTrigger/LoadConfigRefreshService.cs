namespace ScaleTrigger
{
    /// <summary>Polls LoadConfig into LoadConfigCache; the interval is itself the "ConfigRefresh" row, re-read every tick.</summary>
    public class LoadConfigRefreshService(
        IServiceScopeFactory scopeFactory,
        LoadConfigCache cache,
        ILogger<LoadConfigRefreshService> logger) : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var (min, max) = cache.Get("ConfigRefresh");
                int intervalSeconds = max > min ? Random.Shared.Next(min, max + 1) : min;
                if (intervalSeconds <= 0)
                {
                    intervalSeconds = 1;
                }

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                try
                {
                    using var scope = scopeFactory.CreateScope();
                    var repoFactory = scope.ServiceProvider.GetRequiredService<RepoFactory>();
                    var settings = await repoFactory.GetRepo().LoadConfigGetAsync();
                    cache.Set(settings);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to refresh LoadConfig from the database.");
                }
            }
        }
    }
}
