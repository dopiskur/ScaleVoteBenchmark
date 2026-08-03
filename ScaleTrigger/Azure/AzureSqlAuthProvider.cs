using Azure.Core;
using Azure.Identity;

namespace ScaleTrigger.Azure
{
    public static class AzureSqlAuthProvider
    {
        private const string SqlResourceScope = "https://database.windows.net/.default";

        // Credential construction and token acquisition are both too expensive to
        // redo per connection, so both are shared and refreshed only near expiry.
        private static readonly DefaultAzureCredential Credential = new();
        private static readonly SemaphoreSlim RefreshLock = new(1, 1);
        private static AccessToken? cachedToken;

        public static async Task<string> GetAccessTokenAsync()
        {
            if (cachedToken is { } token && token.ExpiresOn > DateTimeOffset.UtcNow.AddMinutes(5))
            {
                return token.Token;
            }

            await RefreshLock.WaitAsync();
            try
            {
                // Re-check: another caller may have already refreshed while this one was waiting.
                if (cachedToken is { } recheck && recheck.ExpiresOn > DateTimeOffset.UtcNow.AddMinutes(5))
                {
                    return recheck.Token;
                }

                var tokenRequestContext = new TokenRequestContext(new[] { SqlResourceScope });
                AccessToken newToken = await Credential.GetTokenAsync(tokenRequestContext);
                cachedToken = newToken;
                return newToken.Token;
            }
            finally
            {
                RefreshLock.Release();
            }
        }
    }
}
