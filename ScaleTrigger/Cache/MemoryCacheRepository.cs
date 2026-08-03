using Microsoft.Extensions.Caching.Memory;
using ScaleTrigger.Interfaces;

namespace ScaleTrigger.Cache
{
    /// <summary>Enabled/disabled live via the "CacheEnabled" LoadConfig setting, not appsettings.json, so a node picks up the current value from the shared database on scale-out.</summary>
    public class MemoryCacheRepository : ICache
    {
        private readonly IMemoryCache cache;
        private readonly LoadConfigCache loadConfigCache;

        public MemoryCacheRepository(IMemoryCache cache, LoadConfigCache loadConfigCache)
        {
            this.cache = cache;
            this.loadConfigCache = loadConfigCache;
        }

        private bool Enabled => loadConfigCache.Get("CacheEnabled").Min > 0;

        public T? GetItem<T>(string key) where T : class
        {
            if (!Enabled)
            {
                return null;
            }

            cache.TryGetValue(key, out T? value);
            return value;
        }

        public void SetItem<T>(string key, T value, int slidingExpirationMinutes) where T : class
        {
            if (!Enabled)
            {
                return;
            }

            var options = new MemoryCacheEntryOptions
            {
                SlidingExpiration = TimeSpan.FromMinutes(slidingExpirationMinutes)
            };

            cache.Set(key, value, options);
        }

        public void RemoveItem(string key)
        {
            cache.Remove(key);
        }
    }
}
