namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Body of PUT /api/v1/assistant-conversations/{id}/messages/{messageId}/feedback.
    /// </summary>
    public class MessageFeedbackRequest
    {
        #region Public-Members

        /// <summary>
        /// Up or Down.
        /// </summary>
        public AssistantFeedbackRatingEnum? Rating { get; set; } = null;

        /// <summary>
        /// Up to 1000 characters.
        /// </summary>
        public string? Note { get; set; } = null;

        /// <summary>
        /// Incorrect, Unhelpful, Unsafe, Other.
        /// </summary>
        public List<string>? Reasons { get; set; } = null;

        #endregion
    }
}
