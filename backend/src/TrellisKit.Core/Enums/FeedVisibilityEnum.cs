namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// Who can read a feed post.
    /// </summary>
    public enum FeedVisibilityEnum
    {
        /// <summary>
        /// Every active member.
        /// </summary>
        Members,

        /// <summary>
        /// Every active member, and anonymous visitors through the public feed.
        /// </summary>
        Public
    }
}
