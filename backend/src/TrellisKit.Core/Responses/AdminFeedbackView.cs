namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A feedback queue item (docs/AI_CONTRACT.md section 14.3).
    /// </summary>
    public class AdminFeedbackView
    {
        #region Public-Members

        /// <summary>
        /// Identifier.
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Assistant.
        /// </summary>
        public AiAssistantRef Assistant { get; set; } = new AiAssistantRef();

        /// <summary>
        /// Rating.
        /// </summary>
        public AssistantFeedbackRatingEnum Rating { get; set; } = AssistantFeedbackRatingEnum.Up;

        /// <summary>
        /// Reasons.
        /// </summary>
        public List<string> Reasons { get; set; } = new List<string>();

        /// <summary>
        /// Note.
        /// </summary>
        public string? Note { get; set; } = null;

        /// <summary>
        /// Reviewed.
        /// </summary>
        public bool Reviewed { get; set; } = false;

        /// <summary>
        /// Reviewer id.
        /// </summary>
        public string? ReviewedBy { get; set; } = null;

        /// <summary>
        /// Review time.
        /// </summary>
        public DateTime? ReviewedUtc { get; set; } = null;

        /// <summary>
        /// Conversation (null after deletion).
        /// </summary>
        public string? ConversationId { get; set; } = null;

        /// <summary>
        /// Reply (null after deletion).
        /// </summary>
        public string? MessageId { get; set; } = null;

        /// <summary>
        /// Copy of the rated reply.
        /// </summary>
        public string ReplyContent { get; set; } = String.Empty;

        /// <summary>
        /// Copy of the member message.
        /// </summary>
        public string PromptContent { get; set; } = String.Empty;

        /// <summary>
        /// Member who rated.
        /// </summary>
        public UserSummary? Member { get; set; } = null;

        /// <summary>
        /// Labels.
        /// </summary>
        public List<string> Labels { get; set; } = new List<string>();

        /// <summary>
        /// Tags.
        /// </summary>
        public Dictionary<string, string> Tags { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// Created.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        #endregion
    }
}
