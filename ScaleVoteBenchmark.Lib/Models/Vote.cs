namespace ScaleVoteBenchmark.Lib.Models
{
    /// <summary>
    /// Model pojedinačnog glasa.
    /// </summary>
    public class Vote
    {
        public int? IDVote { get; set; }

        /// <summary>
        /// Vrijednost "pas" ili "macka".
        /// </summary>
        public string? Option { get; set; }

        public DateTime? DateCreated { get; set; }
    }

    /// <summary>
    /// Zbrojeni rezultati glasovanja po opciji.
    /// </summary>
    public class VoteCounts
    {
        public int Pas { get; set; }
        public int Macka { get; set; }
    }
}
