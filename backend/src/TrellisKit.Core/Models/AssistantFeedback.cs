namespace TrellisKit.Core.Models
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Feedback on a reply, with the rated pair copied (docs/AI_CONTRACT.md section 14.3).
    /// </summary>
    public class AssistantFeedback
    {
        #region Public-Members

        /// <summary>
        /// Owning property id.
        /// </summary>
        public string PropertyId { get; set; } = String.Empty;

        /// <summary>
        /// Identifier (afb_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Rated reply, or null after it is deleted.
        /// </summary>
        public string? MessageId { get; set; } = null;

        /// <summary>
        /// Conversation, or null after it is deleted.
        /// </summary>
        public string? ConversationId { get; set; } = null;

        /// <summary>
        /// Assistant id.
        /// </summary>
        public string AssistantId { get; set; } = String.Empty;

        /// <summary>
        /// Member, or null.
        /// </summary>
        public string? UserId { get; set; } = null;

        /// <summary>
        /// Visitor, or null.
        /// </summary>
        public string? VisitorId { get; set; } = null;

        /// <summary>
        /// Up or Down.
        /// </summary>
        public AssistantFeedbackRatingEnum Rating { get; set; } = AssistantFeedbackRatingEnum.Up;

        /// <summary>
        /// Optional note.
        /// </summary>
        public string? Note { get; set; } = null;

        /// <summary>
        /// Reasons (Incorrect, Unhelpful, Unsafe, Other).
        /// </summary>
        public List<string> Reasons { get; set; } = new List<string>();

        /// <summary>
        /// Copy of the member message answered.
        /// </summary>
        public string PromptCopy { get; set; } = String.Empty;

        /// <summary>
        /// Copy of the rated reply.
        /// </summary>
        public string ReplyCopy { get; set; } = String.Empty;

        /// <summary>
        /// Reviewed by an administrator.
        /// </summary>
        public bool Reviewed { get; set; } = false;

        /// <summary>
        /// Reviewer.
        /// </summary>
        public string? ReviewedByUserId { get; set; } = null;

        /// <summary>
        /// Review time.
        /// </summary>
        public DateTime? ReviewedUtc { get; set; } = null;

        /// <summary>
        /// Created time.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Updated time.
        /// </summary>
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

        #endregion
    }
}
