using Azure.Core;
using Azure.Identity;

namespace ScaleTrigger.Azure
{
    public static class AzureSqlAuthProvider
    {
        private const string SqlResourceScope = "https://database.windows.net/.default";

        public static async Task<string> GetAccessTokenAsync()
        {
            var credential = new DefaultAzureCredential();
            var tokenRequestContext = new TokenRequestContext(new[] { SqlResourceScope });
            AccessToken token = await credential.GetTokenAsync(tokenRequestContext);
            return token.Token;
        }
    }
}
