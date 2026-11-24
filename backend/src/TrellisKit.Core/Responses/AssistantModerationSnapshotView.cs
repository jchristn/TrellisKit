namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// The moderation queue target of an AssistantMessage report (docs/AI_CONTRACT.md section 12.3): the retained
    /// copy of the conversation up to and including the reported or withheld message, with the endpoint, model, and
    /// knowledge items of that message. Messages are a bounded preview (the last 5, ending with the reported one) with
    /// the enumerate path for the rest.
    /// </summary>
    public class AssistantModerationSnapshotView
    {
        #region Public-Members

        /// <summary>
        /// Snapshot identifier.
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// The reported or withheld message identifier.
        /// </summary>
        public string MessageId { get; set; } = String.Empty;

        /// <summary>
        /// The report that created the snapshot, or null.
        /// </summary>
        public string? ReportId { get; set; } = null;

        /// <summary>
        /// Report (a member report), OutputBlocked, OutputFlagged, or InputFlagged.
        /// </summary>
        public string Reason { get; set; } = "Report";

        /// <summary>
        /// Conversation identifier (the conversation itself may since have been deleted).
        /// </summary>
        public string ConversationId { get; set; } = String.Empty;

        /// <summary>
        /// Whether the member has since deleted the conversation (only this copy remains).
        /// </summary>
        public bool ConversationDeleted { get; set; } = false;

        /// <summary>
        /// The assistant (Deleted true when it no longer exists).
        /// </summary>
        public AiAssistantRef Assistant { get; set; } = new AiAssistantRef();

        /// <summary>
        /// The member who owned the conversation, or null for a visitor.
        /// </summary>
        public UserSummary? Owner { get; set; } = null;

        /// <summary>
        /// The reported or withheld message.
        /// </summary>
        public AssistantSnapshotMessageView ReportedMessage { get; set; } = new AssistantSnapshotMessageView();

        /// <summary>
        /// Endpoint and model of the reported message (null fields for a member message).
        /// </summary>
        public MessageModelView Model { get; set; } = new MessageModelView();

        /// <summary>
        /// Knowledge items the reported message cited (bounded by the assistant's knowledge.maxContextItems).
        /// </summary>
        public List<CitationView> Citations { get; set; } = new List<CitationView>();

        /// <summary>
        /// Tool calls of the reported reply as copied into the snapshot (administrator fields included), so they stay
        /// readable after the member deletes the conversation.
        /// </summary>
        public ToolCallPageView ToolCalls { get; set; } = new ToolCallPageView();

        /// <summary>
        /// The last messages of the copy with totalRecords and the enumerate path.
        /// </summary>
        public BoundedPreview<AssistantSnapshotMessageView> Messages { get; set; } = new BoundedPreview<AssistantSnapshotMessageView>();

        /// <summary>
        /// Created.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// When the copy is deleted (moderation.retainRemovedDays after it was made).
        /// </summary>
        public DateTime ExpiresUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Labels (always empty).
        /// </summary>
        public List<string> Labels { get; set; } = new List<string>();

        /// <summary>
        /// Tags (always empty).
        /// </summary>
        public Dictionary<string, string> Tags { get; set; } = new Dictionary<string, string>();

        #endregion
    }
}
