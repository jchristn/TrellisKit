namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// Thread list orderings (docs/PHASE3_CONTRACT.md section 4.2); a forum's default ordering is one of them.
    /// </summary>
    public enum ThreadOrderingEnum
    {
        /// <summary>
        /// Last activity, newest first.
        /// </summary>
        Active,

        /// <summary>
        /// Trending score, highest first.
        /// </summary>
        Hot,

        /// <summary>
        /// Published time, newest first.
        /// </summary>
        CreatedDescending,

        /// <summary>
        /// Published time, oldest first.
        /// </summary>
        CreatedAscending,

        /// <summary>
        /// Reactions plus twice the comments, highest first.
        /// </summary>
        Top,

        /// <summary>
        /// Search rank (only with search).
        /// </summary>
        Relevance
    }
}
