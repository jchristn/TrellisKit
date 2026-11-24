namespace TrellisKit.Core.Requests
{
    using System;

    /// <summary>
    /// POST /api/v1/admin/content-review/{id}/action. Not thread safe.
    /// </summary>
    public class ContentReviewActionRequest
    {
        #region Public-Members

        /// <summary>
        /// Dismiss, Hide, Remove, Warn, Mute, or Ban.
        /// </summary>
        public string? Action { get; set; } = null;

        /// <summary>
        /// Reason shown to the author where the action notifies them (3 to 500 characters; required except for
        /// Dismiss).
        /// </summary>
        public string? Reason { get; set; } = null;

        /// <summary>
        /// Internal note kept on the row, or null (at most 2000 characters).
        /// </summary>
        public string? Note { get; set; } = null;

        /// <summary>
        /// Mute: end of the mute, or null for indefinitely.
        /// </summary>
        public DateTime? UntilUtc { get; set; } = null;

        /// <summary>
        /// Hide and Remove: notify the author (default true for Remove).
        /// </summary>
        public bool? NotifyAuthor { get; set; } = null;

        #endregion
    }
}
