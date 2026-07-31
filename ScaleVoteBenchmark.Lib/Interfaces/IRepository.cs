using ScaleVoteBenchmark.Lib.Models;

namespace ScaleVoteBenchmark.Lib.Interfaces
{
    /// <summary>
    /// Zajedničko sučelje za pristup podatkovnom sloju, neovisno o tome
    /// radi li se o MSSQL (Azure SQL) ili MySQL (Azure Database for MySQL)
    /// implementaciji. Sve funkcije komuniciraju isključivo putem
    /// pohranjenih procedura.
    /// </summary>
    public interface IRepository
    {
        void VoteAdd(string option);

        VoteCounts VoteCountsGet();
    }
}
