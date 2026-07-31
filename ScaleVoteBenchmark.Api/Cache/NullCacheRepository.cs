using ScaleVoteBenchmark.Api.Interfaces;

namespace ScaleVoteBenchmark.Api.Cache
{
    /// <summary>
    /// Implementacija predmemorije koja ne sprema ništa - koristi se kada
    /// je "Cache:Enabled" u appsettings.json postavljen na false, kako bi
    /// svaki zahtjev za rezultatima uvijek išao izravno na bazu podataka.
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
