namespace ScaleVoteBenchmark.Lib.Models
{
    /// <summary>
    /// Objekt koji se sprema u MemoryCache za potrebe brzog dohvata
    /// zadnjih poznatih rezultata glasovanja, bez dodatnog upita na bazu
    /// prilikom svakog administrativnog dohvata.
    /// </summary>
    public class VoteCountsCache
    {
        public VoteCounts? Counts { get; set; }
        public DateTime DateCached { get; set; }
    }
}
