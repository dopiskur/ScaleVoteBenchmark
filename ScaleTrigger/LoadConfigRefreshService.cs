namespace ScaleTrigger
{
    /// <summary>
    /// Polls the LoadConfig table on a fixed interval and refreshes
    /// LoadConfigCache, so edits made via the dashboard's "Save" button
    /// take effect on the next tick without restarting the app. The
    /// interval is "ConfigRefresh" in appsettings.json (seconds, default
    /// 10), re-read on every tick so a change to it takes effect on the
    /// following cycle.
    /// </summary>
    public class LoadConfigRefreshService(
        IServiceScopeFactory scopeFactory,
        LoadConfigCache cache,
        IConfiguration configuration,
        ILogger<LoadConfigRefreshService> logger) : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                int intervalSeconds = int.TryParse(configuration["ConfigRefresh"], out int s) && s > 0 ? s : 10;

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
