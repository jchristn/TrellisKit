namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// When poll results are shown to members (docs/PHASE3_CONTRACT.md section 6.1).
    /// </summary>
    public enum PollResultsVisibilityEnum
    {
        /// <summary>
        /// Results are always visible.
        /// </summary>
        Always,

        /// <summary>
        /// After the caller voted or the poll closed.
        /// </summary>
        AfterVote,

        /// <summary>
        /// Only after the poll closed.
        /// </summary>
        AfterClose
    }
}
