namespace TrellisKit.Core.Enumeration
{
    using TrellisKit.Core.Enums;

    /// <summary>
    /// The Phase 5 direct messaging enumeration collections (docs/PHASE5_CONTRACT.md sections 3.1, 3.4, 5.2 and as-built
    /// note B). Column names use the aliases of the queries in ConversationMethodsBase (cm, c, om, ou) and
    /// MessageMethodsBase (m, r). Conversation labels and tags are the caller's own metadata on the ConversationMember row
    /// (cvm_); message labels and tags are the author's.
    /// </summary>
    public static class MessagingCollections
    {
        #region Public-Members

        /// <summary>
        /// Conversation list orderings.
        /// </summary>
        public const string ConversationOrderings = "LastMessageDescending, CreatedDescending, CreatedAscending";

        /// <summary>
        /// Conversation list filters.
        /// </summary>
        public const string ConversationFilters = "folder, unreadOnly, withUserId";

        /// <summary>
        /// History orderings.
        /// </summary>
        public const string HistoryOrderings = "SeqDescending, SeqAscending";

        /// <summary>
        /// History filters.
        /// </summary>
        public const string HistoryFilters = "beforeSeq, afterSeq, aroundSeq";

        /// <summary>
        /// Search orderings.
        /// </summary>
        public const string SearchOrderings = "Relevance, CreatedDescending, CreatedAscending";

        /// <summary>
        /// Search filters.
        /// </summary>
        public const string SearchFilters = "conversationId, withUserId";

        /// <summary>
        /// POST /api/v1/conversations/enumerate: the caller's conversations. Search covers the other member's display
        /// name and handle; label and tag filters apply to the caller's ConversationMember metadata. LastMessageDescending
        /// (default) orders by the caller's last visible activity; the created orderings use the conversation's creation.
        /// </summary>
        public static readonly EnumerationCollection Conversations = new EnumerationCollection("conversations", TargetTypeEnum.ConversationMember, "cm.id", "c.createdutc", false)
            .Order("LastMessageDescending", "cm.lastactivityutc", true, EnumerationKeyKindEnum.Time)
            .Search("ou.displayname", "ou.handle")
            .Filters("folder", "unreadOnly", "withUserId")
            .DefaultTo("LastMessageDescending");

        /// <summary>
        /// GET /api/v1/me/messaging/declined: requests the caller declined (for undo), newest decline first.
        /// </summary>
        public static readonly EnumerationCollection Declined = new EnumerationCollection("messaging-declined", TargetTypeEnum.ConversationMember, "cm.id", "c.createdutc", false)
            .Order("DeclinedDescending", "cm.declinedutc", true, EnumerationKeyKindEnum.Time)
            .Search("ou.displayname", "ou.handle")
            .DefaultTo("DeclinedDescending");

        /// <summary>
        /// POST /api/v1/conversations/changes/enumerate: the caller's conversation changes ordered by change time then
        /// member row id; terminal sync watermarks are separate from page continuation tokens.
        /// </summary>
        public static readonly EnumerationCollection Changes = new EnumerationCollection("conversation-changes", TargetTypeEnum.ConversationMember, "cm.id", "c.createdutc", false)
            .Order("ChangedAscending", "cm.updatedutc", false, EnumerationKeyKindEnum.Time)
            .DefaultTo("ChangedAscending");

        /// <summary>
        /// POST /api/v1/conversations/{id}/messages/enumerate: history of one conversation (keyset over seq; maximum
        /// 100, default 50). Search uses the PostgreSQL full text vector (the data methods add it).
        /// </summary>
        public static readonly EnumerationCollection History = new EnumerationCollection("messages", TargetTypeEnum.Message, "m.id", "m.createdutc", false)
            .Order("SeqDescending", "m.seq", true, EnumerationKeyKindEnum.Number)
            .Order("SeqAscending", "m.seq", false, EnumerationKeyKindEnum.Number)
            .Filters("beforeSeq", "afterSeq", "aroundSeq")
            .DefaultTo("SeqDescending");

        /// <summary>
        /// POST /api/v1/messages/search/enumerate: full text search over the caller's own conversations (at most
        /// messaging.searchMaxResults per page). Relevance is ts_rank scaled to an integer; the data methods build the
        /// expression for the query text (the expression here is a placeholder).
        /// </summary>
        public static readonly EnumerationCollection Search = new EnumerationCollection("message-search", TargetTypeEnum.Message, "m.id", "m.createdutc", false)
            .Order("Relevance", "m.seq", true, EnumerationKeyKindEnum.Number)
            .Filters("conversationId", "withUserId")
            .DefaultTo("Relevance");

        /// <summary>
        /// POST /api/v1/admin/moderation/messaging/reports/enumerate: Message and Conversation reports (scope Messaging).
        /// Filters status (Open default, Resolved) and targetType.
        /// </summary>
        public static readonly EnumerationCollection Reports = new EnumerationCollection("messaging-reports", TargetTypeEnum.Report, "r.id", "r.createdutc", true)
            .Filters("status", "targetType");

        #endregion
    }
}
