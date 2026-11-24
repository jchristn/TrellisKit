namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// A broadcast as its recipient sees it (docs/PHASE5_CONTRACT.md section 8.2).
    /// </summary>
    public class AnnouncementView : IMetadataPayload
    {
        #region Public-Members

        /// <summary>
        /// Broadcast identifier (bct_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Title.
        /// </summary>
        public string Title { get; set; } = String.Empty;

        /// <summary>
        /// Body (Markdown subset).
        /// </summary>
        public string Body { get; set; } = String.Empty;

        /// <summary>
        /// When it was sent (sending started), or null.
        /// </summary>
        public DateTime? SentUtc { get; set; } = null;

        /// <inheritdoc />
        public List<string> Labels { get; set; } = new List<string>();

        /// <inheritdoc />
        public Dictionary<string, string> Tags { get; set; } = new Dictionary<string, string>();

        #endregion
    }
}
