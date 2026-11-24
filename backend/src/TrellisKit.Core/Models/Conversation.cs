namespace TrellisKit.Core.Models
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A direct messaging conversation row (docs/PHASE5_CONTRACT.md section 16).
    /// </summary>
    public class Conversation
    {
        #region Public-Members

        /// <summary>
        /// Identifier (cnv_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Kind.
        /// </summary>
        public ConversationKindEnum Kind { get; set; } = ConversationKindEnum.Direct;

        /// <summary>
        /// The two member ids sorted and joined with a colon (Direct), or null.
        /// </summary>
        public string? DirectKey { get; set; } = null;

        /// <summary>
        /// Highest message sequence number.
        /// </summary>
        public long LastSeq { get; set; } = 0;

        /// <summary>
        /// Identifier of the latest message, or null.
        /// </summary>
        public string? LastMessageId { get; set; } = null;

        /// <summary>
        /// Time of the latest message (UTC), or null.
        /// </summary>
        public DateTime? LastMessageUtc { get; set; } = null;

        /// <summary>
        /// Member who started the conversation, or null after account deletion.
        /// </summary>
        public string? CreatedByUserId { get; set; } = null;

        /// <summary>
        /// When every member deleted it: purge time (UTC), else null.
        /// </summary>
        public DateTime? PurgeAfterUtc { get; set; } = null;

        /// <summary>
        /// Created (UTC).
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Updated (UTC).
        /// </summary>
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

        #endregion
    }
}
