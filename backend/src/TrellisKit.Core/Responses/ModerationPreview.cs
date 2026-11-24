namespace TrellisKit.Core.Responses
{
    using System;

    /// <summary>
    /// Queue row preview of the target: { title, excerpt, hasMedia }.
    /// </summary>
    public class ModerationPreview
    {
        #region Public-Members

        /// <summary>
        /// Post title or member display name.
        /// </summary>
        public string? Title { get; set; } = null;

        /// <summary>
        /// Excerpt of the content (or the member's bio), or null.
        /// </summary>
        public string? Excerpt { get; set; } = null;

        /// <summary>
        /// Whether the target has attachments.
        /// </summary>
        public bool HasMedia { get; set; } = false;

        #endregion
    }
}
