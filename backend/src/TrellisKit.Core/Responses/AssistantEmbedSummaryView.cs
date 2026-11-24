namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Widget presentation of an embeddable assistant.
    /// </summary>
    public class AssistantEmbedSummaryView
    {
        #region Public-Members

        /// <summary>
        /// Launcher label.
        /// </summary>
        public string? LauncherLabel { get; set; } = null;

        /// <summary>
        /// Position.
        /// </summary>
        public string Position { get; set; } = "BottomRight";

        /// <summary>
        /// The exact origins the widget may be embedded in and exchange postMessage events with (docs/AI_CONTRACT.md
        /// section 13.3): the assistant's embed.allowedOrigins plus the property's own web origins. The embed page posts
        /// to the embedding page's origin only when it is in this list (never to "*"); the loader checks event.origin.
        /// </summary>
        public List<string> AllowedOrigins
        {
            get { return _AllowedOrigins; }
            set { _AllowedOrigins = value ?? new List<string>(); }
        }

        #endregion

        #region Private-Members

        private List<string> _AllowedOrigins = new List<string>();

        #endregion
    }
}
