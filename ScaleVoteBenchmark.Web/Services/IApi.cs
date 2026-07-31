using ScaleVoteBenchmark.Lib.Models;

namespace ScaleVoteBenchmark.Web.Services
{
    /// <summary>
    /// Sučelje za komunikaciju prezentacijskog sloja sa slojem poslovne
    /// logike (ScaleVoteBenchmark.Api). Prezentacijski sloj nikada ne pristupa bazi
    /// podataka izravno, već isključivo putem REST API poziva.
    /// </summary>
    public interface IApi
    {
        Task VoteAdd(string option);

        Task<VoteCounts?> VoteCountsGet(string jwtToken);

        Task<string?> Login(string username, string password);
    }
}
