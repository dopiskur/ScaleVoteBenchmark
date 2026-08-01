namespace ScaleTrigger
{
    /// <summary>
    /// Polls the LoadConfig table on a fixed interval and refreshes
    /// LoadConfigCache, so edits made via the dashboard's "Save" button
    /// take effect on the next tick without restarting the app. The
    /// interval is the "ConfigRefresh" row in LoadConfig itself (seconds,
    /// default 1 - seeded from appsettings.json's Load:ConfigRefresh the
    /// first time the table is created, same as every other setting),
    /// re-read from the cache on every tick so an edit to it takes effect
    /// on the following cycle.
    /// </summary>
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
