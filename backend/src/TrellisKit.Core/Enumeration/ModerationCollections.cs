namespace TrellisKit.Core.Enumeration
{
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Enumeration collections of the moderation slice (all admin collections: skip, exact totals, at most
    /// enumeration.maxResultsAdmin). Column names use the aliases of the queries in ReportMethodsBase (q for the
    /// aggregated queue, r for reports) and ModerationActionMethodsBase (a for actions, x for the content history union,
    /// u for moderators). The queue and the content history are polymorphic: their label and tag filters apply to the
    /// metadata of each row's own target (FeedPost, Comment, or User), not to one fixed type.
    /// </summary>
    public static class ModerationCollections
    {
        #region Public-Members

        /// <summary>
        /// Orderings of the moderation queue.
        /// </summary>
        public const string QueueOrderings = "DueAscending, LastReportedDescending, FirstReportedAscending, ReportCountDescending, CreatedDescending, CreatedAscending";

        /// <summary>
        /// Orderings of the actions log and report lists.
        /// </summary>
        public const string TimeOrderings = "CreatedDescending, CreatedAscending";

        /// <summary>
        /// Orderings of the content history.
        /// </summary>
        public const string ContentOrderings = "CreatedDescending, CreatedAscending, ReactionTotalDescending, ReportCountDescending";

        /// <summary>
        /// Orderings of the moderators list.
        /// </summary>
        public const string ModeratorOrderings = "DisplayNameAscending, CreatedDescending, CreatedAscending";

        /// <summary>
        /// GET /api/v1/admin/moderation/queue: one row per reported target (aggregated reports), filtered to the
        /// caller's scope. Orderings DueAscending (default: earliest review target first, so overdue rows lead),
        /// LastReportedDescending, FirstReportedAscending, ReportCountDescending, and the Created orderings (on the
        /// first report). Filters status (Open, the default, or Resolved), targetType, reason, overdue. The created
        /// window applies to the first report. Labels and tags filter the target's own metadata.
        /// </summary>
        public static readonly EnumerationCollection Queue = new EnumerationCollection("moderation-queue", TargetTypeEnum.Report, "q.qid", "q.firstreportedutc", true)
            .Order("DueAscending", "q.dueutc", false, EnumerationKeyKindEnum.Time)
            .Order("LastReportedDescending", "q.lastreportedutc", true, EnumerationKeyKindEnum.Time)
            .Order("FirstReportedAscending", "q.firstreportedutc", false, EnumerationKeyKindEnum.Time)
            .Order("ReportCountDescending", "q.reportcount", true, EnumerationKeyKindEnum.Number)
            .Filters("status", "targetType", "reason", "overdue", "scopeType", "forumIds")
            .DefaultTo("DueAscending");

        /// <summary>
        /// Reports of one target (POST /api/v1/admin/moderation/queue/{targetType}/{targetId}/reports/enumerate), newest
        /// first, in the caller's scope. Metadata type Report. Search over details. Filters status and reason.
        /// </summary>
        public static readonly EnumerationCollection Reports = new EnumerationCollection("moderation-reports", TargetTypeEnum.Report, "r.id", "r.createdutc", true)
            .Search("r.details")
            .Filters("status", "reason");

        /// <summary>
        /// GET /api/v1/admin/moderation/actions: the moderation actions log, newest first. Metadata type
        /// ModerationAction. Search over reason and note. Filters moderatorUserId (or "system" for actions without a
        /// moderator), targetType, targetId, targetAuthorUserId, action, and fromUtc and toUtc (aliases of the created
        /// window).
        /// </summary>
        public static readonly EnumerationCollection Actions = new EnumerationCollection("moderation-actions", TargetTypeEnum.ModerationAction, "a.id", "a.createdutc", true)
            .Search("a.reason", "a.note")
            .Filters("moderatorUserId", "targetType", "targetId", "targetAuthorUserId", "action", "fromUtc", "toUtc");

        /// <summary>
        /// GET /api/v1/admin/moderation/content: a member's feed posts and comments in any state, newest first.
        /// Filters authorUserId (required), targetType (FeedPost or Comment), state (Visible, Hidden, Removed,
        /// Deleted). Search over title and body. Labels and tags filter each item's own metadata.
        /// </summary>
        public static readonly EnumerationCollection Content = new EnumerationCollection("moderation-content", TargetTypeEnum.FeedPost, "x.id", "x.createdutc", true)
            .Order("ReactionTotalDescending", "x.reactiontotal", true, EnumerationKeyKindEnum.Number)
            .Order("ReportCountDescending", "x.reportcount", true, EnumerationKeyKindEnum.Number)
            .Search("x.title", "x.body")
            .Filters("authorUserId", "targetType", "state");

        /// <summary>
        /// GET /api/v1/admin/moderators: members holding Moderator or FeedModerator (not deleted). Orderings
        /// DisplayNameAscending (default) and the Created orderings (account creation). Search over display name and
        /// handle. Filters role (Moderator or FeedModerator) and resourceType. Metadata type User.
        /// </summary>
        public static readonly EnumerationCollection Moderators = new EnumerationCollection("admin-moderators", TargetTypeEnum.User, "u.id", "u.createdutc", true)
            .Order("DisplayNameAscending", "lower(u.displayname)", false, EnumerationKeyKindEnum.Text)
            .Search("u.displayname", "u.handle")
            .Filters("role", "resourceType")
            .DefaultTo("DisplayNameAscending");

        #endregion
    }
}
