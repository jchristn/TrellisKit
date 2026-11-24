namespace TrellisKit.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// A moderation copy of an assistant conversation up to and including a reported or withheld message
    /// (docs/AI_CONTRACT.md section 12.3). It outlives the conversation until <see cref="ExpiresUtc"/>
    /// (moderation.retainRemovedDays), so a report can be reviewed after the member deletes the conversation.
    /// </summary>
    public class AiModerationSnapshot
    {
        #region Public-Members

        /// <summary>
        /// Property identifier.
        /// </summary>
        public string PropertyId { get; set; } = String.Empty;

        /// <summary>
        /// Snapshot identifier (ams_ prefix).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// The report that created the snapshot, or null.
        /// </summary>
        public string? ReportId { get; set; } = null;

        /// <summary>
        /// The reported or withheld message (one snapshot per message).
        /// </summary>
        public string MessageId { get; set; } = String.Empty;

        /// <summary>
        /// Conversation identifier.
        /// </summary>
        public string ConversationId { get; set; } = String.Empty;

        /// <summary>
        /// Assistant identifier.
        /// </summary>
        public string AssistantId { get; set; } = String.Empty;

        /// <summary>
        /// The member who owned the conversation, or null for a visitor.
        /// </summary>
        public string? OwnerUserId { get; set; } = null;

        /// <summary>
        /// Why the snapshot exists: Report (a member report), OutputBlocked (output moderation withheld the reply),
        /// OutputFlagged (output moderation flagged the reply), or InputFlagged (the word filter or the moderation model
        /// flagged a member message).
        /// </summary>
        public string Reason { get; set; } = "Report";

        /// <summary>
        /// Endpoint of the message, or null.
        /// </summary>
        public string? EndpointId { get; set; } = null;

        /// <summary>
        /// Endpoint name at the time of the snapshot, or null.
        /// </summary>
        public string? EndpointName { get; set; } = null;

        /// <summary>
        /// Model of the message, or null.
        /// </summary>
        public string? Model { get; set; } = null;

        /// <summary>
        /// Messages copied (bounded by the assistant's limits.maxMessagesPerConversation).
        /// </summary>
        public int MessageCount { get; set; } = 0;

        /// <summary>
        /// Knowledge items cited by the message (bounded by knowledge.maxContextItems).
        /// </summary>
        public List<AssistantCitation> Citations
        {
            get { return _Citations; }
            set { _Citations = value ?? new List<AssistantCitation>(); }
        }

        /// <summary>
        /// Tool calls of the message (at most AiConstants.MaxToolCallsPerReply), copied with their result links.
        /// </summary>
        public List<AssistantToolCall> ToolCalls
        {
            get { return _ToolCalls; }
            set { _ToolCalls = value ?? new List<AssistantToolCall>(); }
        }

        /// <summary>
        /// Created.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// When the snapshot is deleted.
        /// </summary>
        public DateTime ExpiresUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Private-Members

        private List<AssistantCitation> _Citations = new List<AssistantCitation>();
        private List<AssistantToolCall> _ToolCalls = new List<AssistantToolCall>();

        #endregion
    }
}
