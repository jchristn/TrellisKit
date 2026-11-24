namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// How a forum thread came to be (docs/PHASE3_CONTRACT.md section 2.2).
    /// </summary>
    public enum ThreadKindEnum
    {
        /// <summary>
        /// Authored in the forum.
        /// </summary>
        Original,

        /// <summary>
        /// A discussion thread opened from a feed post; its conversation is the feed post's.
        /// </summary>
        LinkedFeedPost
    }
}
