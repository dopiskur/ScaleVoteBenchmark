using System.Net.Http.Headers;
using System.Net.Http.Json;
using ScaleVoteBenchmark.Lib.Models;

namespace ScaleVoteBenchmark.Web.Services
{
    /// <summary>
    /// Implementacija komunikacije s ScaleVoteBenchmark.Api aplikacijom putem
    /// HttpClient biblioteke koda. Ovo je jedina klasa u prezentacijskom
    /// sloju koja zna za postojanje mrežnog sučelja prema poslovnoj
    /// logici.
    /// </summary>
    public class ApiRepository : IApi
    {
        private readonly HttpClient httpClient;

        public ApiRepository(HttpClient httpClient)
        {
            this.httpClient = httpClient;
        }

        public async Task VoteAdd(string option)
        {
            var response = await httpClient.PostAsync($"api/vote/add?option={option}", content: null);
            response.EnsureSuccessStatusCode();
        }

        public async Task<VoteCounts?> VoteCountsGet(string jwtToken)
        {
            httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", jwtToken);

            var response = await httpClient.GetAsync("api/vote/counts");

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            return await response.Content.ReadFromJsonAsync<VoteCounts>();
        }

        public async Task<string?> Login(string username, string password)
        {
            var response = await httpClient.PostAsJsonAsync("api/auth/login", new { username, password });

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var result = await response.Content.ReadFromJsonAsync<LoginResponse>();
            return result?.Token;
        }

        private class LoginResponse
        {
            public string? Token { get; set; }
        }
    }
}
