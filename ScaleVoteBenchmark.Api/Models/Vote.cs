namespace ScaleVoteBenchmark.Api.Models
{
    /// <summary>
    /// Model of a single vote.
    /// </summary>
    public class Vote
    {
        public long? IDVote { get; set; }

        /// <summary>
        /// Value "yes" or "no".
        /// </summary>
        public string? Option { get; set; }

        public DateTime? DateCreated { get; set; }
    }

    /// <summary>
    /// Vote report (counts and percentages per option), fully computed by
    /// the database. The API only maps the columns returned by the
    /// stored procedure/function into this model - no percentage math
    /// happens in application code.
    /// </summary>
    public class VoteReport
    {
        public int Yes { get; set; }
        public int No { get; set; }
        public int Total { get; set; }
        public decimal YesPercent { get; set; }
        public decimal NoPercent { get; set; }
    }
}
