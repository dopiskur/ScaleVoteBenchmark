using Azure.Core;
using Azure.Identity;

namespace ScaleTrigger.Azure
{
    /// <summary>
    /// Enables authentication against Azure SQL via the Managed Identity
    /// mechanism, instead of a classic username and password in the
    /// connection string. Used exclusively when the "UseManagedIdentity"
    /// setting in appsettings.json is set to true, and the application is
    /// running inside an Azure environment (App Service, VM) that has an
    /// assigned identity.
    /// </summary>
    public static class AzureSqlAuthProvider
    {
        private const string SqlResourceScope = "https://database.windows.net/.default";

        /// <summary>
        /// Retrieves an access token via the DefaultAzureCredential chain
        /// (in order: Managed Identity, Azure CLI, Visual Studio, etc.),
        /// which is then set as the AccessToken on the SqlConnection object.
        /// </summary>
        public static async Task<string> GetAccessTokenAsync()
        {
            var credential = new DefaultAzureCredential();
            var tokenRequestContext = new TokenRequestContext(new[] { SqlResourceScope });
            AccessToken token = await credential.GetTokenAsync(tokenRequestContext);
            return token.Token;
        }
    }
}
