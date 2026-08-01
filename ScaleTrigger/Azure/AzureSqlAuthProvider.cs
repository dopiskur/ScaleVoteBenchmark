using Azure.Core;
using Azure.Identity;

namespace ScaleTrigger.Azure
{
    public static class AzureSqlAuthProvider
    {
        private const string SqlResourceScope = "https://database.windows.net/.default";

        // DefaultAzureCredential probes multiple credential sources on
        // construction, and token acquisition is itself a network round
        // trip - both too expensive to redo on every connection open, so
        // the credential and its token are shared and refreshed only when
        // close to expiry.
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
