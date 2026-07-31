namespace ScaleVoteBenchmark.Lib.Models
{
    /// <summary>
    /// Model pojedinačnog glasa.
    /// </summary>
    public class Vote
    {
        public int? IDVote { get; set; }

        /// <summary>
        /// Vrijednost "yes" ili "no".
        /// </summary>
        public string? Option { get; set; }

        public DateTime? DateCreated { get; set; }
    }

    /// <summary>
    /// Zbrojeni rezultati glasovanja po opciji.
    /// </summary>
    public class VoteCounts
    {
        public int Yes { get; set; }
        public int No { get; set; }
    }
}
