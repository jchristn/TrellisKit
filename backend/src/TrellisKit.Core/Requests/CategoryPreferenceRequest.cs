namespace TrellisKit.Core.Requests
{
    /// <summary>
    /// One category change; omitted cells are unchanged.
    /// </summary>
    public class CategoryPreferenceRequest
    {
        /// <summary>
        /// Category name (Replies, Mentions, Reactions, FeedPosts, Moderation, Account).
        /// </summary>
        public string? Category { get; set; } = null;

        /// <summary>
        /// In-app, or null.
        /// </summary>
        public bool? InApp { get; set; } = null;

        /// <summary>
        /// Push, or null.
        /// </summary>
        public bool? Push { get; set; } = null;

        /// <summary>
        /// Email, or null.
        /// </summary>
        public bool? Email { get; set; } = null;
    }
}
