namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Feedback on a reply.
    /// </summary>
    public class MessageFeedbackView
    {
        #region Public-Members

        /// <summary>
        /// Up or Down.
        /// </summary>
        public AssistantFeedbackRatingEnum Rating { get; set; } = AssistantFeedbackRatingEnum.Up;

        /// <summary>
        /// Note.
        /// </summary>
        public string? Note { get; set; } = null;

        /// <summary>
        /// Reasons.
        /// </summary>
        public List<string> Reasons { get; set; } = new List<string>();

        /// <summary>
        /// Created.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        #endregion
    }
}
