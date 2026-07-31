using Azure.Core;
using Azure.Identity;

namespace ScaleVoteBenchmark.Api.Azure
{
    /// <summary>
    /// Omogućava autentikaciju prema Azure SQL bazi putem Managed Identity
    /// mehanizma, umjesto klasičnog korisničkog imena i lozinke u
    /// connection stringu. Koristi se isključivo kada je u appsettings.json
    /// postavka "UseManagedIdentity" postavljena na true, a aplikacija se
    /// izvršava unutar Azure okruženja (App Service, VM) koje ima
    /// dodijeljen identitet.
    /// </summary>
    public static class AzureSqlAuthProvider
    {
        private const string SqlResourceScope = "https://database.windows.net/.default";

        /// <summary>
        /// Dohvaća access token putem DefaultAzureCredential lanca
        /// (redoslijedom: Managed Identity, Azure CLI, Visual Studio, itd.),
        /// koji se potom postavlja kao AccessToken na SqlConnection objekt.
        /// </summary>
        public static string GetAccessToken()
        {
            var credential = new DefaultAzureCredential();
            var tokenRequestContext = new TokenRequestContext(new[] { SqlResourceScope });
            AccessToken token = credential.GetToken(tokenRequestContext);
            return token.Token;
        }
    }
}
