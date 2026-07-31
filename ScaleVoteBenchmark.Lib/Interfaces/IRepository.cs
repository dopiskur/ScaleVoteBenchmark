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

        /// <summary>
        /// Provjerava je li moguće uspostaviti konekciju s bazom podataka
        /// koristeći trenutno konfigurirani connection string. Ne izvršava
        /// nikakav upit nad tablicama, isključivo otvara i zatvara
        /// konekciju. Namijenjeno provjeri pri pokretanju aplikacije, kako
        /// bi se pogrešna konfiguracija (kriva lozinka, nedostupan
        /// poslužitelj, zatvoren firewall na Azureu) prijavila odmah, a ne
        /// tek kod prvog stvarnog zahtjeva korisnika.
        /// </summary>
        void TestConnection();
    }
}
