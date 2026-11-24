namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// What a word filter match does.
    /// </summary>
    public enum WordFilterActionEnum
    {
        /// <summary>
        /// Reject the write with 400 ContentFiltered.
        /// </summary>
        Block,

        /// <summary>
        /// Accept the write and file a system report.
        /// </summary>
        Flag
    }
}
