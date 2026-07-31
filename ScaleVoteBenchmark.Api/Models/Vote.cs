namespace ScaleVoteBenchmark.Api.Models
{
    /// <summary>
    /// Model of a single vote.
    /// </summary>
    public class Vote
    {
        public int? IDVote { get; set; }

        /// <summary>
        /// Value "yes" or "no".
        /// </summary>
        public string? Option { get; set; }

        public DateTime? DateCreated { get; set; }
    }

    /// <summary>
    /// Summed voting results per option.
    /// </summary>
    public class VoteCounts
    {
        public int Yes { get; set; }
        public int No { get; set; }
    }
}
