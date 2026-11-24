namespace TrellisKit.Core.Responses
{
    using System;

    /// <summary>
    /// The result of an administrator's action on a content changelog row.
    /// </summary>
    public class ContentReviewActionResult
    {
        #region Public-Members

        /// <summary>
        /// The row after the action (Dismissed or Actioned).
        /// </summary>
        public ContentChangeView Change { get; set; } = new ContentChangeView();

        /// <summary>
        /// Dismiss, Hide, Remove, Warn, Mute, or Ban.
        /// </summary>
        public string Action { get; set; } = String.Empty;

        /// <summary>
        /// The moderation action recorded (Hide, Remove, Warn), or null.
        /// </summary>
        public string? ModerationActionId { get; set; } = null;

        #endregion
    }
}
