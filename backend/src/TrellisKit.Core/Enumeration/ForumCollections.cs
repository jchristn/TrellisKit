namespace TrellisKit.Core.Enumeration
{
    using TrellisKit.Core.Enums;

    /// <summary>
    /// The Phase 3 enumeration collections (docs/PHASE3_CONTRACT.md): forums, threads, revisions, follows, mutes, poll
    /// voters, trending, and search. Thread orderings Hot, ScoreDescending, and Relevance are composite or computed per
    /// request; the thread data methods page them with their own keyset and the expressions here are placeholders.
    /// </summary>
    public static class ForumCollections
    {
        #region Public-Members

        /// <summary>
        /// Forum orderings.
        /// </summary>
        public const string ForumOrderings = "SortOrderAscending, NameAscending, ActivityDescending, CreatedDescending, CreatedAscending";

        /// <summary>
        /// Member thread orderings.
        /// </summary>
        public const string ThreadOrderings = "Active, Hot, CreatedDescending, CreatedAscending, Top, Relevance";

        /// <summary>
        /// Admin thread orderings.
        /// </summary>
        public const string AdminThreadOrderings = "CreatedDescending, CreatedAscending, LastActivityDescending, LastActivityAscending";

        /// <summary>
        /// Thread list filters (forum and cross-forum lists).
        /// </summary>
        public const string ThreadFilters = "type, answered, unanswered, feedbackStatus, pollOpen, authorUserId, followedOnly, unreadOnly, hasMedia, hasVideo, audience, includePinned, includeComments";

        /// <summary>
        /// Member forum list.
        /// </summary>
        public static readonly EnumerationCollection Forums = new EnumerationCollection("forums", TargetTypeEnum.Forum, "fo.id", "fo.createdutc", false)
            .Order("SortOrderAscending", "fo.sortorder", false, EnumerationKeyKindEnum.Number)
            .Order("NameAscending", "lower(fo.name)", false, EnumerationKeyKindEnum.Text)
            .Order("ActivityDescending", "COALESCE(fo.lastactivityutc, fo.createdutc)", true, EnumerationKeyKindEnum.Time)
            .Search("fo.name", "fo.description")
            .Filters("section", "followedOnly", "includeArchived", "audience")
            .DefaultTo("SortOrderAscending");

        /// <summary>
        /// Admin forum list.
        /// </summary>
        public static readonly EnumerationCollection AdminForums = new EnumerationCollection("admin-forums", TargetTypeEnum.Forum, "fo.id", "fo.createdutc", true)
            .Order("SortOrderAscending", "fo.sortorder", false, EnumerationKeyKindEnum.Number)
            .Order("NameAscending", "lower(fo.name)", false, EnumerationKeyKindEnum.Text)
            .Order("ActivityDescending", "COALESCE(fo.lastactivityutc, fo.createdutc)", true, EnumerationKeyKindEnum.Time)
            .Search("fo.name", "fo.description")
            .Filters("section", "followedOnly", "includeArchived", "isArchived", "audience")
            .DefaultTo("SortOrderAscending");

        /// <summary>
        /// Threads of one forum.
        /// </summary>
        public static readonly EnumerationCollection ForumThreads = ThreadCollection("forum-threads", false)
            .Filters("type", "answered", "unanswered", "feedbackStatus", "pollOpen", "authorUserId", "followedOnly", "unreadOnly", "hasMedia", "hasVideo", "audience", "includePinned", "includeComments")
            .DefaultTo("Active");

        /// <summary>
        /// Threads across forums (All forums, own threads, Following, and search).
        /// </summary>
        public static readonly EnumerationCollection Threads = ThreadCollection("threads", false)
            .Filters("type", "answered", "unanswered", "feedbackStatus", "pollOpen", "authorUserId", "followedOnly", "unreadOnly", "hasMedia", "hasVideo", "audience", "includePinned",
                "includeComments", "forumIds", "state")
            .DefaultTo("Active");

        /// <summary>
        /// Admin thread list (every state in the caller's scope).
        /// </summary>
        public static readonly EnumerationCollection AdminThreads = new EnumerationCollection("admin-threads", TargetTypeEnum.Thread, "t.id", "t.createdutc", true)
            .Order("LastActivityDescending", "t.lastactivityutc", true, EnumerationKeyKindEnum.Time)
            .Order("LastActivityAscending", "t.lastactivityutc", false, EnumerationKeyKindEnum.Time)
            .Search("t.title", "t.body")
            .Filters("forumIds", "type", "state", "authorUserId", "pinned", "locked", "promoted", "feedbackStatus", "audience")
            .DefaultTo("CreatedDescending");

        /// <summary>
        /// Trending queue (admin console).
        /// </summary>
        public static readonly EnumerationCollection Trending = new EnumerationCollection("admin-trending", TargetTypeEnum.Thread, "t.id", "t.createdutc", true)
            .Order("ScoreDescending", "t.trendingscore", true, EnumerationKeyKindEnum.Text)
            .Search("t.title")
            .Filters("forumIds", "type", "promoted", "minScore")
            .DefaultTo("ScoreDescending");

        /// <summary>
        /// Public sample threads (anonymous).
        /// </summary>
        public static readonly EnumerationCollection PublicThreads = new EnumerationCollection("public-threads", TargetTypeEnum.Thread, "t.id", "t.createdutc", false)
            .Search("t.title", "t.body")
            .DefaultTo("CreatedDescending");

        /// <summary>
        /// Thread revisions (newest first).
        /// </summary>
        public static readonly EnumerationCollection ThreadRevisions = new EnumerationCollection("thread-revisions", TargetTypeEnum.ThreadRevision, "tv.id", "tv.createdutc", false)
            .Search("tv.title", "tv.body");

        /// <summary>
        /// A member's follows (forums and threads).
        /// </summary>
        public static readonly EnumerationCollection Follows = new EnumerationCollection("me-follows", TargetTypeEnum.ThreadFollow, "x.id", "x.createdutc", false)
            .Filters("targetType");

        /// <summary>
        /// Forum mutes (moderation console).
        /// </summary>
        public static readonly EnumerationCollection ForumMutes = new EnumerationCollection("forum-mutes", TargetTypeEnum.ForumMute, "m.id", "m.createdutc", true)
            .Search("m.reason")
            .Filters("forumId", "userId", "activeOnly");

        /// <summary>
        /// Poll voters (polls with voter visibility Visible). Labels and tags filter the voters' member metadata.
        /// </summary>
        public static readonly EnumerationCollection PollVoters = new EnumerationCollection("poll-voters", TargetTypeEnum.User, "pv.userid", "pv.createdutc", false)
            .Filters("optionId");

        /// <summary>
        /// Moderators whose assignments cover one forum.
        /// </summary>
        public static readonly EnumerationCollection ForumModerators = new EnumerationCollection("forum-moderators", TargetTypeEnum.User, "u.id", "u.createdutc", true)
            .Order("DisplayNameAscending", "lower(u.displayname)", false, EnumerationKeyKindEnum.Text)
            .Search("u.displayname", "u.handle")
            .DefaultTo("DisplayNameAscending");

        /// <summary>
        /// Global search (resolved decision 11). Ordering Relevance (default) or the created orderings; filter types.
        /// </summary>
        public static readonly EnumerationCollection Search = new EnumerationCollection("search", TargetTypeEnum.Thread, "s.id", "s.createdutc", false)
            .Order("Relevance", "s.rank", false, EnumerationKeyKindEnum.Number)
            .Filters("types")
            .DefaultTo("Relevance");

        #endregion

        #region Private-Methods

        private static EnumerationCollection ThreadCollection(string name, bool isAdmin)
        {
            return new EnumerationCollection(name, TargetTypeEnum.Thread, "t.id", "t.createdutc", isAdmin)
                .Order("Active", "t.lastactivityutc", true, EnumerationKeyKindEnum.Time)
                .Order("Hot", "t.trendingscore", true, EnumerationKeyKindEnum.Text)
                .Order("CreatedDescending", "COALESCE(t.publishedutc, t.createdutc)", true, EnumerationKeyKindEnum.Time)
                .Order("CreatedAscending", "COALESCE(t.publishedutc, t.createdutc)", false, EnumerationKeyKindEnum.Time)
                .Order("Top", "(t.reactiontotal + 2 * t.commentcount)", true, EnumerationKeyKindEnum.Number)
                .Order("Relevance", "t.id", false, EnumerationKeyKindEnum.Number)
                .Search("t.title", "t.body");
        }

        #endregion
    }
}
