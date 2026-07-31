using ScaleVoteBenchmark.Api.Interfaces;

namespace ScaleVoteBenchmark.Api.Cache
{
    /// <summary>
    /// Cache implementation that stores nothing - used when
    /// "Cache:Enabled" in appsettings.json is set to false, so every
    /// request for results always goes straight to the database.
    /// </summary>
    public class NullCacheRepository : ICache
    {
        public T? GetItem<T>(string key) where T : class => null;

        public void SetItem<T>(string key, T value, int slidingExpirationMinutes) where T : class
        {
        }

        public void RemoveItem(string key)
        {
        }
    }
}
