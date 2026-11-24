namespace TrellisKit.Core.Enumeration
{
    using TrellisKit.Core.Enums;

    /// <summary>
    /// The Phase 5 ideas board enumeration collections (docs/PHASE5_CONTRACT.md section 7). The Relevance ordering is
    /// built per request (title matches first, then votes); the expression here is a placeholder.
    /// </summary>
    public static class IdeaCollections
    {
        #region Public-Members

        /// <summary>
        /// Member idea orderings.
        /// </summary>
        public const string IdeaOrderings = "Top, Trending, CreatedDescending, CreatedAscending, UpdatedDescending, Relevance";

        /// <summary>
        /// Member board (and the public board).
        /// </summary>
        public static readonly EnumerationCollection Ideas = IdeaCollection("ideas", false)
            .Filters("status", "open", "votedByMe", "authorUserId", "audience", "includeMerged")
            .DefaultTo("Top");

        /// <summary>
        /// Public board (anonymous, Public ideas only).
        /// </summary>
        public static readonly EnumerationCollection PublicIdeas = IdeaCollection("public-ideas", false)
            .Filters("status", "open")
            .DefaultTo("Top");

        /// <summary>
        /// Staff board (every state; adds hidden and merged).
        /// </summary>
        public static readonly EnumerationCollection AdminIdeas = IdeaCollection("admin-ideas", true)
            .Filters("status", "open", "authorUserId", "audience", "includeMerged", "hidden", "merged")
            .DefaultTo("CreatedDescending");

        /// <summary>
        /// Idea statuses in display order.
        /// </summary>
        public static readonly EnumerationCollection Statuses = new EnumerationCollection("idea-statuses", TargetTypeEnum.IdeaStatus, "s.id", "s.createdutc", false)
            .Order("SortOrderAscending", "s.sortorder", false, EnumerationKeyKindEnum.Number)
            .Search("s.key", "s.label")
            .DefaultTo("SortOrderAscending");

        /// <summary>
        /// Idea statuses for staff (adds includeRetired).
        /// </summary>
        public static readonly EnumerationCollection AdminStatuses = new EnumerationCollection("admin-idea-statuses", TargetTypeEnum.IdeaStatus, "s.id", "s.createdutc", true)
            .Order("SortOrderAscending", "s.sortorder", false, EnumerationKeyKindEnum.Number)
            .Search("s.key", "s.label")
            .Filters("includeRetired")
            .DefaultTo("SortOrderAscending");

        #endregion

        #region Private-Methods

        private static EnumerationCollection IdeaCollection(string name, bool isAdmin)
        {
            return new EnumerationCollection(name, TargetTypeEnum.Idea, "i.id", "i.createdutc", isAdmin)
                .Order("Top", "i.votecount", true, EnumerationKeyKindEnum.Number)
                .Order("Trending", "i.recentvotecount", true, EnumerationKeyKindEnum.Number)
                .Order("UpdatedDescending", "i.updatedutc", true, EnumerationKeyKindEnum.Time)
                .Order("Relevance", "i.votecount", true, EnumerationKeyKindEnum.Number)
                .Search("i.title", "i.body");
        }

        #endregion
    }
}
